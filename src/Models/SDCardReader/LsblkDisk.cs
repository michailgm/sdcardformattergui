using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SDCardFormatterApp;

public enum DeviceType
{
    Unknown,
    Disk,
    Partition
}

public class LsblkResult
{
    [JsonPropertyName("blockdevices")]
    public List<LsblkDisk> Blockdevices { get; set; } = new();
}

public class LsblkBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetInt64() == 1,
            JsonTokenType.String => reader.GetString() == "1" || reader.GetString()?.ToLower() == "true",
            _ => false
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

public sealed class LsblkDisk
{
    private static readonly ConcurrentDictionary<string, (LsblkResult Result, DateTime Expiry)> simpleCache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(200);

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("rm")]
    [JsonConverter(typeof(LsblkBoolConverter))] // lsblk понякога връща "1"/"0" или true/false в JSON
    public bool Removable { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("tran")]
    public string Transport { get; set; } = string.Empty;

    [JsonPropertyName("mountpoint")]
    public string Mountpoint { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<LsblkDisk> Children { get; set; } = new();

    public bool IsMounted => !string.IsNullOrEmpty(Mountpoint) || Children.Any(c => !string.IsNullOrEmpty(c.Mountpoint));

    public static DeviceType GetDeviceType(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return DeviceType.Unknown;

        var nameOnly = Path.GetFileName(deviceName);
        var sysPath = $"/sys/class/block/{nameOnly}";

        if (!Directory.Exists(sysPath))
            return DeviceType.Unknown;

        return File.Exists(Path.Combine(sysPath, "partition")) ? DeviceType.Partition : DeviceType.Disk;
    }

    public static string GetBaseDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return string.Empty;

        var nameOnly = Path.GetFileName(deviceName);
        var sysPath = $"/sys/class/block/{nameOnly}";

        if (!Directory.Exists(sysPath)) return nameOnly;

        var di = new DirectoryInfo(sysPath);
        var target = di.ResolveLinkTarget(true);

        if (target == null) return nameOnly;

        if (File.Exists(Path.Combine(target.FullName, "partition")))
        {
            var parent = Directory.GetParent(target.FullName);
            return parent?.Name ?? nameOnly;
        }

        return nameOnly;
    }

    public static string GetDeviceShortName(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) return string.Empty;
        return Path.GetFileName(device) ?? string.Empty;
    }

    public static string GetDeviceFullName(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) return string.Empty;

        var specifiedDevice = $"/dev/{GetDeviceShortName(device)}";
        return File.Exists(specifiedDevice) ? specifiedDevice : string.Empty;
    }

    private static (string cacheKey, string deviceArg) GetCacheKeyAndDeviceArg(string specifiedDeviceName)
    {
        if (string.IsNullOrWhiteSpace(specifiedDeviceName))
            return ("lsblk_all", null);

        var fullName = GetDeviceFullName(specifiedDeviceName);
        return string.IsNullOrEmpty(fullName)
            ? ("lsblk_all", null)
            : ($"lsblk_{GetDeviceShortName(fullName)}", fullName);
    }

    public static async Task<LsblkResult> InternalLoadDrives(string specifiedDeviceName = null, CancellationToken token = default)
    {
        var (cacheKey, deviceArg) = GetCacheKeyAndDeviceArg(specifiedDeviceName);

        if (simpleCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            return cached.Result;

        var args = "-o NAME,SIZE,TYPE,MODEL,TRAN,RM,MOUNTPOINT -J -b";

        if (!string.IsNullOrEmpty(deviceArg))
            args += $" {deviceArg}";

        if (token == default)
            token = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;

        var psi = new ProcessStartInfo
        {
            FileName = "lsblk",
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return null;

            var json = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            var result = JsonSerializer.Deserialize<LsblkResult>(json);
            simpleCache[cacheKey] = (result, DateTime.UtcNow.Add(CacheDuration));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"lsblk error: {ex.Message}");
            return null;
        }
    }

    // Метод за извличане на конкретно устройство по име (напр. "sdb")
    public static async Task<LsblkDisk> GetDeviceByName(string shortName, CancellationToken token = default)
    {
        for (var i = 0; i < 3; i++)
        {
            var result = await InternalLoadDrives(shortName, token).ConfigureAwait(false);
            var device = result?.Blockdevices?.FirstOrDefault(d => d.Name == shortName);

            if (device != null) return device;

            await Task.Delay(100 * (i + 1), token).ConfigureAwait(false);
        }

        return null;
    }

    public static async Task<List<LsblkDisk>> GetAllDrives(CancellationToken token = default)
    {
        for (var i = 0; i < 3; i++)
        {
            var result = await InternalLoadDrives(null, token).ConfigureAwait(false);

            if (result?.Blockdevices != null && result.Blockdevices.Count > 0)
                return result.Blockdevices;

            await Task.Delay(200 * (i + 1), token).ConfigureAwait(false);
        }

        return new List<LsblkDisk>();
    }

    public async Task<bool> UnmountAllAsync(CancellationToken token = default)
    {
        var mountPoints = new List<string>();

        if (!string.IsNullOrEmpty(Mountpoint))
            mountPoints.Add(Mountpoint);

        mountPoints.AddRange(Children
            ?.Where(c => !string.IsNullOrEmpty(c.Mountpoint))
            ?.Select(c => c.Mountpoint) ?? Enumerable.Empty<string>());

        if (mountPoints.Count == 0) return true;

        var allSuccess = true;

        foreach (var path in mountPoints)
        {
            // "umount -l" (lazy unmount) за по-сигурно разкачване
            var psi = new ProcessStartInfo
            {
                FileName = "umount",
                Arguments = $"-l \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);

                if (process != null)
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    if (process.ExitCode != 0) allSuccess = false;
                }
            }
            catch
            {
                allSuccess = false;
            }
        }

        return allSuccess;
    }

    public static string GetSdCardType(LsblkDisk disk)
    {
        if (disk == null) return string.Empty;

        var gb = disk.Size / (1024.0 * 1024.0 * 1024.0);

        if (gb <= 2.1) return "SD";
        if (gb <= 32.5) return "SDHC";
        if (gb <= 2048) return "SDXC";
        return "SDUC";
    }
}