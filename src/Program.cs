using System;
using Avalonia;

namespace SDCardFormatterApp;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new X11PlatformOptions
            {
                EnableMultiTouch = true,
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 1024 * 1024 * 1024
            })
            .LogToTrace();
    }
}