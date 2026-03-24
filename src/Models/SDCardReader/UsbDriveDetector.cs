using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SDCardFormatterApp;

public enum DeviceAction
{
    Inserted,
    Removed
}

public class DevicesListChangedEventArgs : EventArgs
{
    public List<LsblkDisk> Devices { get; set; }
    public DeviceAction Action { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DeviceEventArgs : EventArgs
{
    public LsblkDisk Device { get; set; }
    public string DeviceName => Device?.Name ?? string.Empty;
    public DeviceAction Action { get; set; }
    public DateTime Timestamp { get; set; }
}

public class UsbDriveDetector : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly object syncDevicesListLock = new();
    private CancellationTokenSource cts;

    private readonly List<LsblkDisk> devices = new();
    private bool disposed;
    private bool isMonitoring;

    private Task monitorTask;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public event EventHandler<DeviceEventArgs> DeviceInserted;
    public event EventHandler<DeviceEventArgs> DeviceRemoved;
    public event EventHandler<DevicesListChangedEventArgs> DeviceListChanged;

    ~UsbDriveDetector()
    {
        Dispose(false);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (disposed) return;

        isMonitoring = false;

        if (cts != null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
            cts = null;
        }

        if (monitorTask != null)
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

        semaphore?.Dispose();
        disposed = true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing)
        {
            StopMonitoring();
            semaphore?.Dispose();
        }

        disposed = true;
    }

    public void StartMonitoring()
    {
        if (disposed) throw new ObjectDisposedException(nameof(UsbDriveDetector));
        if (isMonitoring) return;

        try
        {
            cts?.Cancel();
            cts?.Dispose();

            cts = new CancellationTokenSource();
            isMonitoring = true;

            _ = LoadCurrentDrivesAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Debug.WriteLine($"Грешка при първоначално зареждане: {t.Exception}");
            }, TaskContinuationOptions.OnlyOnFaulted);

            monitorTask = Task.Run(() => MonitorUdevAsync(cts.Token));
        }
        catch (Exception ex)
        {
            isMonitoring = false;
            Debug.WriteLine($"Failed to start monitoring: {ex.Message}");
            throw;
        }
    }

    public void StopMonitoring()
    {
        if (disposed) throw new ObjectDisposedException(nameof(UsbDriveDetector));
        if (!isMonitoring) return;

        isMonitoring = false;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private async Task MonitorUdevAsync(CancellationToken token)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "udevadm",
            Arguments = "monitor --subsystem-match=block --udev",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        int retryDelay = 1000;
        const int maxRetryDelay = 30000;

        while (!token.IsCancellationRequested)
        {
            Process process = null;

            try
            {
                process = Process.Start(startInfo);
                if (process == null)
                {
                    Debug.WriteLine("Не може да стартира udevadm. Повторен опит след 1 сек.");
                    await Task.Delay(retryDelay, token).ConfigureAwait(false);
                    retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
                    continue;
                }

                retryDelay = 1000;

                using (process)
                {
                    while (!token.IsCancellationRequested && !process.StandardOutput.EndOfStream)
                    {
                        string line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                        
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (line.Contains(" add ") || line.Contains(" remove "))
                            try
                            {
                                await HandleUdevLineAsync(line, token).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Грешка при обработка на udev ред: {ex.Message}");
                            }
                    }
                }

                if (token.IsCancellationRequested)
                    break;

                Debug.WriteLine("udevadm процесът приключи. Рестартиране след 1 сек.");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Грешка в udev монитор: {ex.Message}");

                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(retryDelay, token).ConfigureAwait(false);
                    retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
                }
            }
        }
    }

    private void InvokeDeviceInsertedEvent(LsblkDisk device)
    {
        if (DeviceInserted == null) return;

        DeviceEventArgs insertArgs = new DeviceEventArgs
        {
            Device = device,
            Action = DeviceAction.Inserted,
            Timestamp = DateTime.Now
        };

        Task.Run(() => DeviceInserted.Invoke(this, insertArgs));
    }

    private void InvokeDeviceRemovedEvent(LsblkDisk device)
    {
        if (DeviceRemoved == null) return;

        DeviceEventArgs removeArgs = new DeviceEventArgs
        {
            Device = device,
            Action = DeviceAction.Removed,
            Timestamp = DateTime.Now
        };

        Task.Run(() => DeviceRemoved.Invoke(this, removeArgs));
    }

    private void InvokeDeviceListChangedEvent(List<LsblkDisk> currentDevices, DeviceAction action)
    {
        if (DeviceListChanged == null) return;

        DevicesListChangedEventArgs changeArgs = new DevicesListChangedEventArgs
        {
            Devices = currentDevices,
            Action = action,
            Timestamp = DateTime.Now
        };

        Task.Run(() => DeviceListChanged.Invoke(this, changeArgs));
    }

    private async Task HandleUdevLineAsync(string line, CancellationToken token)
    {
        try
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return;

            string action = parts[2];
            string devPath = parts[3];
            string shortName = LsblkDisk.GetBaseDevice(devPath); // взема "sdb"

            await semaphore.WaitAsync(token).ConfigureAwait(false);

            switch (action)
            {
                case "add":
                    await Task.Delay(500, token).ConfigureAwait(false);
                    LsblkDisk device = await LsblkDisk.GetDeviceByName(shortName, token).ConfigureAwait(false);

                    if (device != null && device.Removable && device.Type == "disk")
                    {
                        lock (syncDevicesListLock)
                        {
                            if (!devices.Any(d => d.Name == device.Name))
                                devices.Add(device);
                            else
                                return; // вече го има
                        }

                        InvokeDeviceInsertedEvent(device);
                        InvokeDeviceListChangedEvent(GetDevicesSnapshot(), DeviceAction.Inserted);
                    }

                    break;
                case "remove":
                    LsblkDisk diskToRemove = null;

                    lock (syncDevicesListLock)
                    {
                        diskToRemove = devices.FirstOrDefault(d => d.Name == shortName);

                        if (diskToRemove != null)
                            devices.Remove(diskToRemove);
                    }

                    if (diskToRemove != null)
                    {
                        InvokeDeviceRemovedEvent(diskToRemove);
                        InvokeDeviceListChangedEvent(GetDevicesSnapshot(), DeviceAction.Removed);
                    }

                    break;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private List<LsblkDisk> GetDevicesSnapshot()
    {
        lock (syncDevicesListLock)
        {
            return new List<LsblkDisk>(devices);
        }
    }

    private async Task<List<LsblkDisk>> GetCurrentDevicesAsync()
    {
        try
        {
            List<LsblkDisk> all = await LsblkDisk.GetAllDrives().ConfigureAwait(false);
            return all?.Where(d => d.Removable && d.Type == "disk")?.ToList() ?? new List<LsblkDisk>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Грешка при получаване на устройства: {ex.Message}");
            return new List<LsblkDisk>();
        }
    }

    private async Task LoadCurrentDrivesAsync()
    {
        try
        {
            List<LsblkDisk> fetched = await GetCurrentDevicesAsync().ConfigureAwait(false);
            lock (syncDevicesListLock)
            {
                devices.Clear();
                devices.AddRange(fetched);
            }

            InvokeDeviceListChangedEvent(GetDevicesSnapshot(), DeviceAction.Inserted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Грешка при зареждане на устройства: {ex.Message}");
        }
    }
}