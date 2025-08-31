using MDTadusMod.Data;
using MDTadusMod.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebView.Maui;
using System.Net.Http;
using System.Text;
using System.Diagnostics;
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
            var dataDir = AppPaths.Resolve();
#if WINDOWS
            var wv2 = Path.Combine(dataDir, "WebView2");
            Directory.CreateDirectory(wv2);
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
#endif

            // Core services
            builder.Services.AddSingleton<AccountService>();
            builder.Services.AddSingleton<RotmgApiService>();
            builder.Services.AddSingleton<AssetService>();
            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<UpdaterService>();
            builder.Services.AddSingleton<ReloadQueueService>();
            builder.Services.AddHttpClient();

            // Logging buffer first
            builder.Services.AddSingleton<ILogBuffer, LogBuffer>();
            // Register our custom provider (picked up automatically)
            builder.Services.AddSingleton<ILoggerProvider, MDTadusMod.Services.BufferLoggerProvider>();

            // Filters
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

#if DEBUG
            builder.Logging
                .AddDebug()
#if WINDOWS
                .AddConsole()
#endif
                .SetMinimumLevel(LogLevel.Debug); // allow debug lines if you want them
#else
            builder.Logging
                .SetMinimumLevel(LogLevel.Information);
#endif

            // HTTP logging handler (optional – currently not used by RotmgApiService)
            builder.Services.AddTransient<LoggingHttpMessageHandler>();
            builder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.example.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).AddHttpMessageHandler<LoggingHttpMessageHandler>();

            RegisterGlobalExceptionHooks(builder.Services);

            var app = builder.Build();

            // Add Debug -> buffer listener (after DI ready)
            var logBuffer = app.Services.GetRequiredService<ILogBuffer>();
            Trace.Listeners.Add(new MDTadusMod.Services.DebugForwardingTraceListener(logBuffer));

            return app;
        }

        private static void RegisterGlobalExceptionHooks(IServiceCollection services)
        {
#if DEBUG
            // Prevent crash on background exceptions in debug (already present)
            TaskScheduler.UnobservedTaskException += (s, e) => { e.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { };
#endif
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (services.BuildServiceProvider().GetService<ILogBuffer>() is { } lb)
                    lb.LogCritical("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                if (services.BuildServiceProvider().GetService<ILogBuffer>() is { } lb)
                {
                    lb.LogError("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved();
                }
            };
        }
    }
}
