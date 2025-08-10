using MDTadusMod.Data;
using MDTadusMod.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebView.Maui;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace MDTadusMod
{
    public interface IAppPaths
    {
        string DataDir { get; }
        string Combine(params string[] parts);
    }

    public sealed class AppPaths : IAppPaths
    {
        public string DataDir { get; }
        public AppPaths(string dataDir)
        {
            DataDir = dataDir;
            Directory.CreateDirectory(DataDir);
        }
        public string Combine(params string[] parts) =>
            Path.Combine(new[] { DataDir }.Concat(parts).ToArray());

        public static string Resolve()
        {
#if WINDOWS
            var reg = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Muledump.NET", "DataDir", null) as string;
            var dir = string.IsNullOrWhiteSpace(reg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Muledump.NET")
                : reg;
            return dir;
#else
            return Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
#endif
        }
    }

    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // --- NEW: resolve data directory first and set WebView2 env var ---
            var dataDir = AppPaths.Resolve();
#if WINDOWS
            var wv2 = Path.Combine(dataDir, "WebView2");
            Directory.CreateDirectory(wv2);
            // Tell WebView2 to use a writable location
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", wv2, EnvironmentVariableTarget.Process);
#endif
            var appPaths = new AppPaths(dataDir);

            var builder = MauiApp.CreateBuilder();
            builder.Services.AddSingleton<IAppPaths>(appPaths);

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();

            // Avoid crashing on background task exceptions
            TaskScheduler.UnobservedTaskException += (s, e) => { e.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { /* optionally log */ };
#endif

            builder.Services.AddSingleton<AccountService>();
            builder.Services.AddSingleton<RotmgApiService>();
            builder.Services.AddSingleton<AssetService>();
            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<UpdaterService>();
            builder.Services.AddSingleton<ReloadQueueService>();
            builder.Services.AddHttpClient();

            // Add logging configuration
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

            return builder.Build();
        }
    }
}
