using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SDCardFormatterApp;

public enum FormatType
{
    Quick, // бързо форматиране (без --overwrite)
    Overwrite // пълно форматиране с презаписване (--overwrite)
}

public partial class MainWindow : Window
{
    private readonly UsbDriveDetector usbDetector;
    private List<LsblkDisk> availableDevices = new();

    private string password;

    public MainWindow()
    {
        InitializeComponent();

        usbDetector = new UsbDriveDetector();
        usbDetector.DeviceInserted += OnDeviceInserted;
        usbDetector.DeviceRemoved += OnDeviceRemoved;
        usbDetector.DeviceListChanged += OnDeviceListChanged;

        Loaded += async (_, _) => await InitializeAsync();
        Closed += async (_, _) =>
        {
            try
            {
                await usbDetector.DisposeAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"Грешка при освобождаване: {ex.Message}");
            }
        };

        KeyDown += OnWindowKeyDown;
    }

    private async Task InitializeAsync()
    {
        await LoadDrivesAsync();
        usbDetector.StartMonitoring();
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => FormatProgress.Value = 0);

            var allDisks = await LsblkDisk.GetAllDrives();
            var filtered = allDisks?
                .Where(d => d.Type == "disk" && d.Transport == "usb" && d.Removable).ToList() ?? new List<LsblkDisk>();

            await Dispatcher.UIThread.InvokeAsync(() => UpdateDriveSelectorAsync(filtered));
        }
        catch (Exception ex)
        {
            await ShowError($"Грешка при зареждане на устройства: {ex.Message}");
        }
    }

    private void UpdateDriveSelectorAsync(List<LsblkDisk> disks)
    {
        availableDevices = disks;
        DriveSelector.Items.Clear();

        if (disks.Any())
        {
            foreach (var disk in disks)
            {
                var size = FormatSize(disk.Size);
                var model = string.IsNullOrWhiteSpace(disk.Model) ? "USB Drive" : disk.Model.Trim();
                var display = $"/dev/{disk.Name} - {size} {model}";

                DriveSelector.Items.Add(new ComboBoxItem
                {
                    Content = display,
                    Tag = disk
                });
            }

            DriveSelector.SelectedIndex = 0;
            FormatBtn.IsEnabled = true;
            FormatBtn.Focus();
        }
        else
        {
            DriveSelector.Items.Add(new ComboBoxItem
            {
                Content = "💾 Поставете SD карта",
                IsEnabled = false,
                Foreground = Brushes.Gray,
                FontStyle = FontStyle.Italic
            });

            FormatBtn.IsEnabled = false;
        }

        UpdateCurrentSelectedSdCardInfo();
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private LsblkDisk GetCurrentSelectedDisk()
    {
        return DriveSelector != null ? DriveSelector.SelectedItem is ComboBoxItem item && item.Tag is LsblkDisk disk ? disk : null : null;
    }

    private void UpdateCurrentSelectedSdCardInfo()
    {
        UpdateSdCardInfo(GetCurrentSelectedDisk());
    }

    private void UpdateSdCardInfo(LsblkDisk disk)
    {
        if (StatusIndicator != null)
            StatusIndicator.Fill = (availableDevices?.Count ?? 0) == 0 ? Brushes.Red : Brushes.Green;

        if (CardStatusText != null)
        {
            var deviceInfo = disk != null ? $"Избрана е: {disk.Name} {FormatSize(disk.Size)}" : "Не е избрана карта";
            CardStatusText.Text = (availableDevices?.Count ?? 0) == 0 ? "Няма налични карти" : deviceInfo;
        }

        if (CardTypeTxt == null) return;

        if (disk == null)
        {
            CardTypeTxt.Text = "Тип: --";
            CapacityTxt.Text = "Капацитет: --";
            UpdateCardLogo(null);
            return;
        }

        var type = LsblkDisk.GetSdCardType(disk);
        var capacity = FormatSize(disk.Size);

        CardTypeTxt.Text = $"Тип: {type}";
        CapacityTxt.Text = $"Капацитет: {capacity}";

        UpdateCardLogo(type);
    }

    private void UpdateCardLogo(string type)
    {
        var assetPath = type switch
        {
            "SDHC" => "avares://SDCardFormatterApp/Assets/sdhc_logo.png",
            "SDXC" => "avares://SDCardFormatterApp/Assets/sdxc_logo.png",
            _ => "avares://SDCardFormatterApp/Assets/sd_logo.png"
        };

        try
        {
            var uri = new Uri(assetPath);
            CardTypeLogo.Source = new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
        }
    }

    public static bool IsValidVolumeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var regex = new Regex(@"^[A-Z0-9_]{1,11}$");
        return regex.IsMatch(label.ToUpper());
    }

    private void OnDeviceInserted(object sender, DeviceEventArgs e)
    {
        Dispatcher.UIThread.Post(async () => await LoadDrivesAsync());
    }

    private void OnDeviceRemoved(object sender, DeviceEventArgs e)
    {
        Dispatcher.UIThread.Post(async () => await LoadDrivesAsync());
    }

    private void OnDeviceListChanged(object sender, DevicesListChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            availableDevices = e.Devices.Where(d => d.Type == "disk" && d.Transport == "usb" && d.Removable).ToList();
            UpdateDriveSelectorAsync(availableDevices);
        });
    }

    private async void OnFormatClicked(object sender, RoutedEventArgs e)
    {
        var disk = GetCurrentSelectedDisk();

        if (disk == null)
        {
            UpdateStatus("Няма избрано устройство");
            return;
        }

        var label = string.IsNullOrWhiteSpace(VolumeLabelTxt.Text) ? "SDCARD" : VolumeLabelTxt.Text;
        if (!IsValidVolumeLabel(label))
        {
            await ShowError("Невалидно име на дял");
            return;
        }

        ;

        if (!await PasswordDialog.IsSudoCachedAsync())
        {
            var authenticated = await PasswordDialog.AuthenticateWithSudoAsync(this);

            if (!authenticated.Result)
            {
                UpdateStatus("Удостоверяването е отказано");
                return;
            }

            password = authenticated.Password;
        }

        var result = await ShowConfirmation(
            "Потвърждение",
            $"Сигурни ли сте, че искате да форматирате:\n/dev/{disk.Name}?\n\nВсички данни ще бъдат изтрити!");

        if (result != MessageBoxResult.Yes)
            return;

        FormatBtn.IsEnabled = false;
        ExitBtn.IsEnabled = false;
        FormatProgress.Value = 0;

        UpdateStatus("Разкачване на устройството...");

        try
        {
            await disk.UnmountAllAsync();

            UpdateStatus("Форматиране...");

            var formatType = OverwriteFormatRb.IsChecked == true ? FormatType.Overwrite : FormatType.Quick;
            var formatSuccess = false;

            if (await IsToolAvailableAsync("format_sd"))
                formatSuccess = await FormatDeviceAsync(disk, label, formatType);
            else
                formatSuccess = await FormatSDCardDeviceAsync(disk, label, formatType);

            if (formatSuccess)
            {
                if (FormatProgress.Value < 100)
                    FormatProgress.Value = 100;

                UpdateStatus("Форматирането завърши успешно!");
            }
            else
            {
                await ShowError("Грешка при форматиране");
            }
        }
        catch (Exception ex)
        {
            await ShowError($"Грешка: {ex.Message}");
        }
        finally
        {
            FormatBtn.IsEnabled = true;
            FormatBtn.Focus();
            ExitBtn.IsEnabled = true;
            await LoadDrivesAsync();
        }
    }

    private async Task<bool> FormatDeviceAsync(LsblkDisk disk, string label, FormatType formatType)
    {
        if (!await IsToolAvailableAsync("format_sd"))
        {
            await ShowError("Грешка: 'format_sd' не е намерен. Моля, инсталирайте го.");
            return false;
        }

        var initialWritten = GetTotalBytesWritten(disk.Name);
        if (initialWritten < 0) initialWritten = 0;

        var deviceSize = disk.Size;
        var device = $"/dev/{disk.Name}";

        try
        {
            var arguments = $"-l \"{label}\"";
            if (formatType == FormatType.Overwrite)
                arguments += " --overwrite";
            arguments += $" {device}";

            var psi = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"format_sd {arguments}",
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                await ShowError("Грешка: Не може да стартира format_sd.");
                return false;
            }

            await process.StandardInput.WriteLineAsync(password);
            await process.StandardInput.FlushAsync();

            var progressTask = Task.Run(async () =>
            {
                while (!process.HasExited)
                {
                    var currentWritten = GetTotalBytesWritten(disk.Name);

                    if (currentWritten >= 0)
                    {
                        var delta = currentWritten - initialWritten;
                        if (delta < 0) delta = 0;

                        var percent = deviceSize > 0 ? delta * 100.0 / deviceSize : 0;
                        percent = Math.Min(100, Math.Max(0, percent));

                        await Dispatcher.UIThread.InvokeAsync(() => FormatProgress.Value = percent);
                    }

                    await Task.Delay(1000);
                }

                await Dispatcher.UIThread.InvokeAsync(() => FormatProgress.Value = 100);
            });

            var processWaitForExitTask = process.WaitForExitAsync();
            await Task.WhenAll(processWaitForExitTask, progressTask);

            if (process.ExitCode == 0)
                await Dispatcher.UIThread.InvokeAsync(() => FormatProgress.Value = 100);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            await ShowError($"Грешка при форматиране: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> IsToolAvailableAsync(string toolName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = toolName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> OverwriteDeviceAsync(string devPath, string deviceName, long totalSize)
    {
        UpdateStatus("Пълно зануляване (Overwrite)...");
        var initialWritten = GetTotalBytesWritten(deviceName);

        var ddTask = Sudo.RunWithPasswordAsync($"dd if=/dev/zero of={devPath} bs=4M status=none iflag=fullblock");
        var progressTask = Task.Run(async () =>
        {
            while (!ddTask.IsCompleted)
            {
                var current = GetTotalBytesWritten(deviceName);

                if (current >= 0)
                {
                    var percent = (double)(current - initialWritten) / totalSize * 100;
                    Dispatcher.UIThread.Post(() => FormatProgress.Value = Math.Clamp(percent, 0, 99));
                }

                await Task.Delay(1000);
            }

            Dispatcher.UIThread.Post(() => FormatProgress.Value = 100);
        });

        await Task.WhenAll(ddTask, progressTask);

        var exitCode = await ddTask;
        return exitCode == 0 || exitCode == 1;
    }

    private async Task<bool> FormatSDCardDeviceAsync(LsblkDisk disk, string label, FormatType formatType)
    {
        if (!await EnsureToolsInstalledAsync()) return false;

        var devPath = $"/dev/{disk.Name}";
        var isDirectReader = disk.Name.StartsWith("mmcblk");
        var partPath = isDirectReader ? $"{devPath}p1" : $"{devPath}1";
        var isSDXC = disk.Size > 32L * 1024 * 1024 * 1024;

        Sudo.Password = password;

        try
        {
            UpdateStatus("Разкачване и почистване...");
            if (await Sudo.RunWithPasswordAsync($"umount -l {devPath}*") != 0)
                Debug.WriteLine("Инфо: Устройството вече е разкачено.");

            Dispatcher.UIThread.Post(() => FormatProgress.Value = 100);

            var roCheck = await Exec.RunCommandWithOutputAsync("cat", $"/sys/block/{disk.Name}/ro");
            if (roCheck.Trim() == "1")
            {
                await ShowError("Картата е в режим 'Само за четене' (Read-Only). Проверете физическото ключе за заключване!");
                return false;
            }

            UpdateStatus("Изчистване на стари таблици...");
            await Sudo.RunWithPasswordAsync($"wipefs -a -f {devPath}");

            if (formatType == FormatType.Overwrite)
                if (!await OverwriteDeviceAsync(devPath, disk.Name, disk.Size))
                {
                    await ShowError("Грешка при пълното зануляване.");
                    return false;
                }

            var align = isDirectReader ? "optimal" : "4MiB";
            UpdateStatus($"Създаване на дял ({align} подравняване)...");

            if (await Sudo.RunWithPasswordAsync($"parted -s {devPath} mklabel msdos") != 0)
            {
                await ShowError("Грешка при създаване на дялова таблица (MBR). Картата може да е повредена.");
                return false;
            }

            await Sudo.RunWithPasswordAsync($"parted -s -a optimal {devPath} mkpart primary fat32 4MiB 100%");
            await Sudo.RunWithPasswordAsync($"partprobe {devPath}");

            await Task.Delay(3000);
            int formatResult;

            if (isSDXC)
            {
                UpdateStatus("Форматиране exFAT (128KB Cluster)...");
                formatResult = await Sudo.RunWithPasswordAsync($"mkfs.exfat -n \"{label}\" -c 128K {partPath}");
            }
            else
            {
                UpdateStatus("Форматиране FAT32 (32KB Cluster)...");
                formatResult = await Sudo.RunWithPasswordAsync($"mkfs.vfat -F 32 -n \"{label}\" -s 64 -f 2 {partPath}");
            }

            if (formatResult != 0)
            {
                await ShowError("Грешка при форматирането на дяла. Опитайте да изключите и включите картата отново.");
                return false;
            }

            Dispatcher.UIThread.Post(() => FormatProgress.Value = 100);
            UpdateStatus("✅ Операцията приключи успешно!");
            return true;
        }
        catch (Exception ex)
        {
            await ShowError($"Критична грешка: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> EnsureToolsInstalledAsync()
    {
        string[] tools = { "mkfs.vfat", "mkfs.exfat" };
        var needsInstall = false;

        foreach (var tool in tools)
            if (await Exec.RunCommandAsync("which", tool) != 0)
            {
                needsInstall = true;
                break;
            }

        if (!needsInstall) return true;

        UpdateStatus("Инсталиране на липсващи инструменти...");

        var osRelease = await File.ReadAllTextAsync("/etc/os-release");

        if (osRelease.Contains("arch", StringComparison.OrdinalIgnoreCase))
        {
            await Sudo.RunWithPasswordAsync("pacman -S --needed --noconfirm dosfstools exfatprogs");
        }
        else if (osRelease.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) ||
                 osRelease.Contains("debian", StringComparison.OrdinalIgnoreCase))
        {
            await Sudo.RunWithPasswordAsync("apt-get update");
            await Sudo.RunWithPasswordAsync("apt-get install -y dosfstools exfat-fuse exfatprogs");
        }
        else
        {
            await ShowError("Неподдържана дистрибуция. Моля, инсталирайте dosfstools и exfatprogs ръчно.");
            return false;
        }

        return true;
    }


    private void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusTxt.Text = message);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCurrentSelectedSdCardInfo();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitBtn.Focus();
            e.Handled = true;
        }
    }

    private async Task<MessageBoxResult> ShowConfirmationAsync(string title, string message)
    {
        return await MessageBox.Show(this, title, message, MessageBoxButton.YesNo, MessageBoxIcon.Question);
    }

    private async Task ShowMessageBoxAsync(string title, string message)
    {
        await MessageBox.Show(this, title, message, MessageBoxButton.OK, MessageBoxIcon.Information);
    }

    private async Task ShowWarningAsync(string title, string message)
    {
        await MessageBox.Show(this, title, message, MessageBoxButton.OK, MessageBoxIcon.Warning);
    }

    private async Task ShowErrorAsync(string message)
    {
        await MessageBox.Show(this, "Грешка", message, MessageBoxButton.OK, MessageBoxIcon.Error);
    }

    private async Task<bool> ShowQuestionAsync(string title, string message)
    {
        return await MessageBox.Show(this, title, message, MessageBoxButton.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes;
    }

    private async Task<MessageBoxResult> ShowConfirmation(string title, string message)
    {
        return await Dispatcher.UIThread.InvokeAsync(() => ShowConfirmationAsync(title, message));
    }

    private async Task ShowWarning(string title, string message)
    {
        await Dispatcher.UIThread.InvokeAsync(() => ShowWarningAsync(title, message));
    }

    private async Task ShowError(string message)
    {
        await Dispatcher.UIThread.InvokeAsync(() => ShowErrorAsync(message));
    }

    private static long GetTotalBytesWritten(string deviceName)
    {
        var statPath = $"/sys/block/{deviceName}/stat";
        if (!File.Exists(statPath))
            return -1;

        try
        {
            var content = File.ReadAllText(statPath);
            var parts = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 8 && long.TryParse(parts[6], out var writeSectors))
                return writeSectors * 512L;
        }
        catch
        {
        }

        return -1;
    }
}