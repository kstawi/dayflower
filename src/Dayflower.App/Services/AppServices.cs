// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Providers;
using Dayflower.Core.Security;
using Dayflower.Core.Storage;
using Dayflower.Core.Sync;
using Dayflower.Providers.Google;
using Dayflower.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Serilog;
using Serilog.Extensions.Logging;

namespace Dayflower.Services;

/// <summary>Everything the app is made of, created once at start-up.</summary>
public sealed class AppServices : IDisposable
{
    private AppServices(
        ILoggerFactory loggers, HttpClient http, ICalendarStore store, ISecretProtector protector,
        GoogleCalendarProvider google, SyncCoordinator sync)
    {
        Loggers = loggers;
        Http = http;
        Store = store;
        Protector = protector;
        Google = google;
        Sync = sync;
    }

    public ILoggerFactory Loggers { get; }

    public HttpClient Http { get; }

    public ICalendarStore Store { get; }

    public ISecretProtector Protector { get; }

    public GoogleCalendarProvider Google { get; }

    public SyncCoordinator Sync { get; }

    public static AppServices Create(DispatcherQueue ui)
    {
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsFolder, "dayflower-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        var loggers = new SerilogLoggerFactory(serilog, dispose: true);
        loggers.CreateLogger<AppServices>().LogInformation(
            "Dayflower {Version} starting; data in {Folder}", typeof(AppServices).Assembly.GetName().Version, AppPaths.DataFolder);

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Dayflower/{typeof(AppServices).Assembly.GetName().Version}");

        var store = SqliteCalendarStore.Open(AppPaths.Database);
        var protector = new DpapiSecretProtector();
        var google = new GoogleCalendarProvider(
            GoogleOAuthSettings.FromBuild(), http, OpenInBrowserAsync, TimeProvider.System, loggers);
        var engine = new SyncEngine(store, [google], protector, TimeProvider.System, loggers.CreateLogger<SyncEngine>());
        var sync = new SyncCoordinator(engine, ui, loggers.CreateLogger<SyncCoordinator>());

        return new AppServices(loggers, http, store, protector, google, sync);
    }

    public IEnumerable<ICalendarProvider> Providers => [Google];

    public void Dispose()
    {
        Sync.Dispose();
        Http.Dispose();
        Loggers.Dispose();
    }

    /// <summary>Opens a web address in the user's default browser.</summary>
    public static async Task OpenInBrowserAsync(Uri uri)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
        {
            throw new InvalidOperationException("Windows couldn't open the browser.");
        }
    }
}
