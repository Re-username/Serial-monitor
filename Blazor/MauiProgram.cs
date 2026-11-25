using Microsoft.Extensions.Logging;
using SerialPlotter.Blazor.Services;

namespace SerialPlotter.Blazor;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler", e.Exception);
            e.SetObserved();
        };

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<AppState>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = FileSystem.AppDataDirectory;
            File.AppendAllText(Path.Combine(dir, "crashes.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
