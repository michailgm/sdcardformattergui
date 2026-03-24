using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SDCardFormatterApp;

public partial class PasswordDialog : Window
{
    private readonly TextBox passwordField;

    public PasswordDialog()
    {
        InitializeComponent();

        passwordField = this.FindControl<TextBox>("PasswordField");

        if (passwordField == null)
            throw new InvalidOperationException("PasswordField not found");

        StackPanel buttonPanel = this.FindControl<StackPanel>("ButtonPanel");
        Button defaultButton = buttonPanel.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault);
        if (defaultButton == null && buttonPanel.Children.Count > 0)
            defaultButton = buttonPanel.Children[0] as Button;

        Opened += (sender, e) => { passwordField.Focus(); };
    }

    public string Password { get; private set; } = string.Empty;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Password = passwordField.Text ?? string.Empty;
        Close(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    public static async Task<string> ShowPasswordDialogAsync(Window parent)
    {
        if (parent == null) return null;

        PasswordDialog dialog = new PasswordDialog();
        bool result = await dialog.ShowDialog<bool>(parent);

        return result ? dialog.Password : null;
    }

    public static async Task<bool> VerifySudoPasswordAsync(string password)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = "-S -v",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(psi);
            if (process == null) return false;

            await process.StandardInput.WriteLineAsync(password);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            Task exitTask = process.WaitForExitAsync();

            if (await Task.WhenAny(exitTask, Task.Delay(5000)) != exitTask)
            {
                process.Kill();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ShowWarning(Window parent, string title, string message) =>
        await MessageBox.Show(parent, title, message, MessageBoxButton.OK, MessageBoxIcon.Warning);

    private static async Task ShowError(Window parent, string title, string message) =>
        await MessageBox.Show(parent, title, message, MessageBoxButton.OK, MessageBoxIcon.Error);

    public static async Task<(bool Result, string Password)> AuthenticateWithSudoAsync(Window parent)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string password = await ShowPasswordDialogAsync(parent);

            if (string.IsNullOrEmpty(password))
                return (false, string.Empty);

            if (await VerifySudoPasswordAsync(password))
                return (true, password);

            if (attempt < maxAttempts)
                await ShowWarning(parent, "Грешна парола",
                    $"Невалидна sudo парола. Опит {attempt} от {maxAttempts}. Опитайте отново.");
            else
                await ShowError(parent, "Грешна парола",
                    $"Невалидна sudo парола след {maxAttempts} опита. Операцията е отказана.");
        }

        return (false, string.Empty);
    }

    public static async Task<bool> IsSudoCachedAsync()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = "-n true",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}