namespace Schnapp.Client.Linux;

using Avalonia;

using Schnapp.Client.Linux.Services;

using System;

/// <summary>
/// Linux tray client entry point (Phase 9).
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.InitFromArgs(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
