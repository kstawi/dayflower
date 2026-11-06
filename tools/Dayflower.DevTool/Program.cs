// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.
//
// Development aid for the local cache. It never prints event titles, descriptions or guests.
//
//   dayflower-dev stats  [--data <folder>]               what's in the cache
//   dayflower-dev sync   [--data <folder>]               run one sync, as the app does
//   dayflower-dev audit  [--data <folder>] [--from yyyy-MM-dd] [--days 120]
//                                                        compare repeating events with Google's own expansion
//   dayflower-dev recent [--data <folder>] [--count 5]   the most recently changed events (times and IDs only)
//   dayflower-dev compare [--data <folder>] [--from yyyy-MM-dd] [--days 42]
//                                                        what the views show vs Google's expanded list, per calendar
//
// --data defaults to the installed app's LocalState folder.

using System.Globalization;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using Dayflower.Core.Sync;
using Dayflower.Providers.Google;
using Dayflower.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

string command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "stats";
string? dataFolder = Option("--data") ?? FindAppDataFolder();
if (dataFolder is null || !File.Exists(Path.Combine(dataFolder, "dayflower.db")))
{
    Console.Error.WriteLine("No Dayflower cache found. Run the app first, or pass --data <folder>.");
    return 1;
}

string dbPath = Path.Combine(dataFolder, "dayflower.db");
var store = SqliteCalendarStore.Open(dbPath);
var protector = new DpapiSecretProtector();
using var http = new HttpClient();
var google = new GoogleCalendarProvider(
    GoogleOAuthSettings.FromBuild(), http, _ => throw new NotSupportedException("Sign in from the app."),
    TimeProvider.System, NullLoggerFactory.Instance);

Console.WriteLine($"Cache: {dbPath}");
switch (command)
{
    case "stats":
        return await StatsAsync();
    case "sync":
        return await SyncAsync();
    case "audit":
        return await AuditAsync(int.Parse(Option("--days") ?? "120", CultureInfo.InvariantCulture));
    case "recent":
        return Recent(int.Parse(Option("--count") ?? "5", CultureInfo.InvariantCulture));
    case "compare":
        return await CompareAsync(int.Parse(Option("--days") ?? "42", CultureInfo.InvariantCulture));
    case "meetings":
        return await MeetingsAsync(int.Parse(Option("--days") ?? "60", CultureInfo.InvariantCulture));
    default:
        Console.Error.WriteLine($"Unknown command {command}. Use stats, sync or audit.");
        return 1;
}

async Task<int> StatsAsync()
{
    var stats = await store.GetStatsAsync();
    Console.WriteLine($"{stats.Accounts} accounts, {stats.Calendars} calendars, {stats.Events} events " +
                      $"({stats.Series} series, {stats.OccurrenceOverrides} changed/cancelled occurrences)");

    foreach (var account in await store.GetAccountsAsync())
    {
        Console.WriteLine();
        Console.WriteLine($"{account.Email}  [{account.State}]  last synced {account.LastSyncedUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        using var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.name, c.is_visible, c.access, c.sync_token IS NOT NULL,
                   COUNT(e.id), SUM(e.is_series), SUM(e.series_id IS NOT NULL),
                   MIN(e.range_start), MAX(COALESCE(e.range_end, e.range_start))
            FROM calendars c LEFT JOIN events e ON e.account_id = c.account_id AND e.calendar_id = c.id
            WHERE c.account_id = $a
            GROUP BY c.account_id, c.id ORDER BY c.is_primary DESC, c.name
            """;
        cmd.Parameters.AddWithValue("$a", account.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string range = r.IsDBNull(7) ? "-" :
                $"{DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(7)):yyyy-MM-dd} .. {DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(8)):yyyy-MM-dd}";
            Console.WriteLine($"  {(r.GetBoolean(1) ? "[x]" : "[ ]")} {r.GetString(0),-40} {(CalendarAccess)r.GetInt32(2),-14} " +
                              $"{r.GetInt32(4),6} events {r.GetInt32(5),4} series {r.GetInt32(6),4} changed  " +
                              $"{(r.GetBoolean(3) ? "token" : "NO TOKEN")}  {range}");
        }
    }

    return 0;
}

async Task<int> SyncAsync()
{
    var engine = new SyncEngine(store, [google], protector, TimeProvider.System, NullLogger<SyncEngine>.Instance);
    var started = DateTimeOffset.UtcNow;
    var report = await engine.SyncAllAsync(CancellationToken.None);
    foreach (var a in report.Accounts)
    {
        Console.WriteLine($"{a.AccountId}: {a.Outcome}, {a.CalendarsSynced} calendars, {a.EventsChanged} changes" +
                          (a.FailedCalendars.Count > 0 ? $", failed: {string.Join(", ", a.FailedCalendars)}" : string.Empty) +
                          (a.Error is null ? string.Empty : $" ({a.Error})"));
    }

    Console.WriteLine($"Took {(DateTimeOffset.UtcNow - started).TotalSeconds:F1} s");
    return report.Accounts.All(a => a.Outcome == SyncOutcome.Synced) ? 0 : 2;
}

async Task<int> AuditAsync(int days)
{
    var from = Option("--from") is { } start
        ? new DateTimeOffset(DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero)
        : new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-7);
    var to = from.AddDays(days);

    // Only compare occurrences scheduled well inside the range, so edge effects (all-day events in other time
    // zones, events crossing the range boundary) can't produce false differences.
    var innerFrom = from.AddDays(1);
    var innerTo = to.AddDays(-1);

    int series = 0, matched = 0, problems = 0, skipped = 0;
    foreach (var account in (await store.GetAccountsAsync()).Where(a => a.State == AccountState.Ok))
    {
        var secret = await store.GetAccountSecretAsync(account.Id);
        var connection = google.Connect(account.Id, protector.Unprotect(secret!));
        var stored = await store.GetEventsAsync(from, to);

        var parsed = stored.Where(s => s.Key.AccountId == account.Id)
            .Select(s => google.ParseEvent(s.Key.AccountId, s.Key.CalendarId, s.Payload))
            .ToList();

        foreach (var s in parsed.Where(e => e.IsSeries && e.Status != EventStatus.Cancelled))
        {
            var overrides = parsed.Where(e => e.Key.CalendarId == s.Key.CalendarId && e.RecurringEventId == s.Key.EventId);
            var local = OccurrenceResolver.Resolve([s, .. overrides], from, to)
                .Where(o => o.OriginalStart is not null && Inside(o.OriginalStart))
                .ToDictionary(o => o.OriginalStart!.OccurrenceKey);

            IReadOnlyList<CalendarEvent> remote;
            try
            {
                remote = await connection.GetOccurrencesAsync(s.Key.CalendarId, s.Key.EventId, from, to, CancellationToken.None);
            }
            catch (Exception ex)
            {
                skipped++;
                Console.WriteLine($"  skip {s.Key.EventId}: {ex.Message}");
                continue;
            }

            var remoteByKey = remote.Where(r => r.OriginalStart is not null && Inside(r.OriginalStart))
                .GroupBy(r => r.OriginalStart!.OccurrenceKey).ToDictionary(g => g.Key, g => g.First());

            series++;
            var issues = new List<string>();
            foreach (var key in remoteByKey.Keys.Union(local.Keys).Order())
            {
                bool inRemote = remoteByKey.TryGetValue(key, out var r);
                bool inLocal = local.TryGetValue(key, out var l);
                if (inRemote && inLocal && Same(r!.Start, l!.Start) && Same(r.End, l.End))
                {
                    matched++;
                }
                else if (inRemote && inLocal)
                {
                    issues.Add($"    {key}: Google {r!.Start} - {r.End}, Dayflower {l!.Start} - {l.End}");
                }
                else
                {
                    issues.Add($"    {key}: only in {(inRemote ? "Google" : "Dayflower")}");
                }
            }

            if (issues.Count > 0)
            {
                problems += issues.Count;
                Console.WriteLine($"  {account.Email} / series {s.Key.EventId} starting {s.Start}");
                foreach (var line in s.Recurrence)
                {
                    Console.WriteLine($"    {line}");
                }

                issues.ForEach(Console.WriteLine);
            }
        }
    }

    Console.WriteLine($"Audited {series} repeating events from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}: " +
                      $"{matched} occurrences match, {problems} differ{(skipped > 0 ? $", {skipped} skipped" : string.Empty)}.");
    return problems == 0 ? 0 : 3;

    bool Inside(EventTime t) => t.EarliestUtc.AddHours(t.IsAllDay ? 14 : 0) >= innerFrom && t.EarliestUtc < innerTo;

    static bool Same(EventTime a, EventTime b) => a.IsAllDay == b.IsAllDay && a.OccurrenceKey == b.OccurrenceKey;
}

// Everything the calendar views would show for a range (cache, repeats expanded, overrides applied) compared with
// Google's own expanded list of the same range, event by event.
async Task<int> CompareAsync(int days)
{
    var from = Option("--from") is { } start
        ? new DateTimeOffset(DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero)
        : new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-7);
    var to = from.AddDays(days);
    var innerFrom = from.AddDays(1);
    var innerTo = to.AddDays(-1);

    int calendars = 0, matched = 0, problems = 0;
    foreach (var account in (await store.GetAccountsAsync()).Where(a => a.State == AccountState.Ok))
    {
        var connection = google.Connect(account.Id, protector.Unprotect((await store.GetAccountSecretAsync(account.Id))!));
        var cached = (await store.GetEventsAsync(from, to)).Where(s => s.Key.AccountId == account.Id)
            .Select(s => google.ParseEvent(s.Key.AccountId, s.Key.CalendarId, s.Payload)).ToList();

        foreach (var calendar in await store.GetCalendarsAsync(account.Id))
        {
            calendars++;
            var local = OccurrenceResolver.Resolve(cached.Where(e => e.Key.CalendarId == calendar.Id), from, to)
                .Where(o => Inside(o.Start))
                .GroupBy(InstanceId).ToDictionary(g => g.Key, g => g.First());
            var remote = (await connection.GetExpandedEventsAsync(calendar.Id, from, to, CancellationToken.None))
                .Where(e => Inside(e.Start))
                .GroupBy(e => e.Key.EventId).ToDictionary(g => g.Key, g => g.First());

            var issues = new List<string>();
            foreach (var id in remote.Keys.Union(local.Keys).Order())
            {
                bool inRemote = remote.TryGetValue(id, out var r), inLocal = local.TryGetValue(id, out var l);
                if (inRemote && inLocal && r!.Start.OccurrenceKey == l!.Start.OccurrenceKey && r.End.OccurrenceKey == l.End.OccurrenceKey)
                {
                    matched++;
                }
                else if (inRemote && inLocal)
                {
                    issues.Add($"    {id}: Google {r!.Start} - {r.End}, Dayflower {l!.Start} - {l.End}");
                }
                else
                {
                    issues.Add($"    {id} ({(inRemote ? r!.Start : l!.Start)}): only in {(inRemote ? "Google" : "Dayflower")}");
                }
            }

            Console.WriteLine($"  {account.Email} / {calendar.Name}: {remote.Count} at Google, {local.Count} in Dayflower");
            issues.ForEach(Console.WriteLine);
            problems += issues.Count;
        }
    }

    Console.WriteLine($"Compared {calendars} calendars from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}: {matched} events match, {problems} differ.");
    return problems == 0 ? 0 : 3;

    bool Inside(EventTime t) => t.EarliestUtc.AddHours(t.IsAllDay ? 14 : 0) >= innerFrom && t.EarliestUtc < innerTo;

    // Google names an occurrence of a series "<series>_<original start>", e.g. abc_20261106T090000Z or abc_20261106.
    // A series split off by a "this and following" change is called abc_R20261105T170000, but its occurrences
    // keep the original name: abc_20261112T170000Z.
    static string InstanceId(EventOccurrence o) => o.Event.IsSeries
        ? $"{System.Text.RegularExpressions.Regex.Replace(o.Event.Key.EventId, @"_R\d{8}(T\d{6})?$", string.Empty)}_{(o.OriginalStart!.IsAllDay
            ? o.OriginalStart.Date!.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : o.OriginalStart.DateTime!.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture))}"
        : o.Event.Key.EventId;
}

// Which events in a range get a Join button, by service, and which might be missing one: no link found, but the
// location or description mentions a meeting service. Prints event IDs and link hosts only.
async Task<int> MeetingsAsync(int days)
{
    var from = Option("--from") is { } start
        ? new DateTimeOffset(DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero)
        : new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-7);
    var to = from.AddDays(days);
    string[] hints = ["zoom", "teams.", "meet.google", "webex", "whereby", "gotomeet", "goto.com", "jit.si", "chime.aws", "huddle"];

    var events = (await store.GetEventsAsync(from, to))
        .Select(s => google.ParseEvent(s.Key.AccountId, s.Key.CalendarId, s.Payload)).ToList();
    var occurrences = OccurrenceResolver.Resolve(events, from, to);

    var byProvider = new Dictionary<string, int>();
    var misses = new Dictionary<string, string>();
    int without = 0;
    foreach (var o in occurrences)
    {
        var link = Dayflower.Core.Meetings.MeetingLinkDetector.Find(o.Event);
        if (link is not null)
        {
            byProvider[link.ProviderName] = byProvider.GetValueOrDefault(link.ProviderName) + 1;
            continue;
        }

        without++;
        string text = $"{o.Event.Location} {o.Event.Description}";
        if (hints.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase)))
        {
            var hosts = System.Text.RegularExpressions.Regex.Matches(System.Net.WebUtility.HtmlDecode(text), @"https?://[^\s<>""'/]+")
                .Select(m => m.Value).Distinct();
            misses[o.Event.RecurringEventId ?? o.Event.Key.EventId] = string.Join(", ", hosts);
        }
    }

    Console.WriteLine($"{occurrences.Count} events from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}; {occurrences.Count - without} with a Join link:");
    foreach (var (provider, count) in byProvider.OrderByDescending(p => p.Value))
    {
        Console.WriteLine($"  {provider,-16} {count}");
    }

    Console.WriteLine($"{without} without a link; {misses.Count} of those mention a meeting service:");
    foreach (var (id, hosts) in misses)
    {
        Console.WriteLine($"  {id}: {(hosts.Length == 0 ? "(no links)" : hosts)}");
    }

    return misses.Count == 0 ? 0 : 3;
}

int Recent(int count)
{
    // The most recently changed events according to Google's "updated" time, to confirm a change arrived.
    using var db = new SqliteConnection($"Data Source={dbPath}");
    db.Open();
    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        SELECT account_id, calendar_id, id, provider, payload FROM events
        ORDER BY json_extract(payload, '$.updated') DESC LIMIT $n
        """;
    cmd.Parameters.AddWithValue("$n", count);
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var e = google.ParseEvent(r.GetString(0), r.GetString(1), r.GetString(4));
        Console.WriteLine($"updated {e.Updated?.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {e.Status,-9}  starts {e.Start}  " +
                          $"{(e.IsSeries ? "series " : e.IsOccurrenceOverride ? "occurrence " : string.Empty)}{e.Key.EventId}");
    }

    return 0;
}

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string? FindAppDataFolder()
{
    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string packages = Path.Combine(local, "Packages");
    var packaged = Directory.Exists(packages)
        ? Directory.GetDirectories(packages, "kstawi.Dayflower_*").Select(d => Path.Combine(d, "LocalState")).FirstOrDefault(Directory.Exists)
        : null;
    return packaged ?? Path.Combine(local, "Dayflower");
}
