using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace SDCardFormatterApp;

public class App : Application
{
    private const string MutexName = "SDCardFormatterApp_{079D6FFC-E104-4C1B-80BD-A61110B611E5}";
    private static Mutex mutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (!Debugger.IsAttached)
        {
            mutex = new Mutex(true, MutexName, out var createdNew);

            if (!createdNew)
            {
                ShowAlreadyRunningWarning();
                Environment.Exit(0);
                return;
            }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            desktop.Exit += (s, e) =>
            {
                if (mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }

                    mutex.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowAlreadyRunningWarning()
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        var isKDE = desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);

        var command = isKDE
            ? "kdialog --error 'Приложението вече е стартирано.' --title 'SD Card Formatter'"
            : "zenity --error --text='Приложението вече е стартирано.' --title='SD Card Formatter'";

        try
        {
            Process.Start("sh", $"-c \"{command}\"");
        }
        catch
        {
            ShowFallbackWindow();
        }
    }

    private static void ShowFallbackWindow()
    {
        var window = new Window
        {
            Title = "SD Card Formatter",
            Width = 320,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Приложението вече е стартирано.",
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = 80
                    }
                }
            }
        };

        var btn = (Button)((StackPanel)window.Content).Children[1];
        btn.Click += (s, e) => window.Close();

        window.Show();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}