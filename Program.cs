using Avalonia;
using System;

namespace MySqlManagementStudio;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        // Developer tools add measurable startup/UI overhead; opt in only when needed:
        //   MMS_DEV_TOOLS=1 dotnet run
#if DEBUG
        if (string.Equals(Environment.GetEnvironmentVariable("MMS_DEV_TOOLS"), "1", StringComparison.Ordinal))
            builder = builder.WithDeveloperTools();
#endif

        // Skip WithInterFont — the app uses system UI fonts (see App.axaml), so loading Inter
        // only slows cold start.
        return builder;
    }
}
