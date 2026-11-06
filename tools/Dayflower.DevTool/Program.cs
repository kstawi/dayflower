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
using Dayflower.Core.Editing;
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
    case "edit-test":
        return await EditTestAsync();
    default:
        Console.Error.WriteLine($"Unknown command {command}. Use stats, sync, audit, recent, compare, meetings or edit-test.");
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

// Every kind of change Dayflower makes, on throwaway events of its own: created a month ahead at 6:00, without guests
// or notifications, checked against Google's own view after each step, and all deleted at the end, even on failure.
async Task<int> EditTestAsync()
{
    const string Prefix = "Dayflower test (safe to delete)";
    var account = (await store.GetAccountsAsync()).First(a => Option("--account") is not { } email || a.Email == email);
    var calendar = (await store.GetCalendarsAsync(account.Id)).First(c => c.IsPrimary);
    var connection = google.Connect(account.Id, protector.Unprotect((await store.GetAccountSecretAsync(account.Id))!));
    string calendarId = calendar.Id;
    string zone = TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";
    var day = DateOnly.FromDateTime(DateTime.Now.AddDays(30));
    var rangeFrom = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue)).AddDays(-1);
    var rangeTo = rangeFrom.AddDays(9);
    var quiet = new ReminderSettings(false, []);
    var created = new List<string>();
    int failures = 0;
    Console.WriteLine($"Account {account.Email}, calendar {calendar.Name}, test day {day:yyyy-MM-dd}, zone {zone}");

    EventTime At(DateOnly d, int hour, int minute = 0)
    {
        var local = d.ToDateTime(new TimeOnly(hour, minute));
        return EventTime.At(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)), zone);
    }

    void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok)
        {
            failures++;
        }
    }

    async Task<List<CalendarEvent>> Mine() => (await connection.GetExpandedEventsAsync(calendarId, rangeFrom, rangeTo, CancellationToken.None))
        .Where(e => e.Summary?.StartsWith(Prefix, StringComparison.Ordinal) == true)
        .OrderBy(e => e.Start.EarliestUtc).ToList();

    string Show(IEnumerable<CalendarEvent> events) =>
        string.Join("; ", events.Select(e => $"{e.Start.DateTime?.LocalDateTime:MM-dd HH:mm}{e.Start.Date:MM-dd} {e.Summary![Prefix.Length..].Trim()}"));

    CalendarEvent Added(IReadOnlyList<Dayflower.Core.Providers.EventChange> changes)
    {
        foreach (var c in changes.Where(c => c.Event is not null))
        {
            if (!created.Contains(c.EventId) && !c.Event!.IsOccurrenceOverride)
            {
                created.Add(c.EventId);
            }
        }

        return changes.Last().Event!;
    }

    EventTarget OccurrenceOf(CalendarEvent series, DateOnly d, int hour, int minute = 0)
    {
        var start = At(d, hour, minute);
        var end = EventTime.At(start.DateTime!.Value + (series.End.EarliestUtc - series.Start.EarliestUtc), zone);
        return new EventTarget(series, start, start, end);
    }

    try
    {
        Console.WriteLine("One-off event");
        var oneOff = Added(await connection.CreateEventAsync(calendarId, new EventEdit
        {
            Summary = $"{Prefix} one-off",
            Start = At(day, 6),
            End = At(day, 6, 30),
            Meet = true,
            Reminders = quiet,
            ColorId = "5",
            Description = "Created by dayflower-dev edit-test.",
        }, notifyGuests: false, CancellationToken.None));
        Check(oneOff.HangoutLink is not null, "created with a Google Meet link");
        Check(oneOff.ColorId == "5", "colour set");

        var target = new EventTarget(oneOff, null, oneOff.Start, oneOff.End);
        var renamed = (await connection.UpdateEventAsync(target, new EventEdit { Summary = $"{Prefix} one-off renamed", Location = "Nowhere" },
                                                         EditScope.ThisEvent, false, CancellationToken.None))[0].Event!;
        Check(renamed.Summary!.EndsWith("renamed", StringComparison.Ordinal) && renamed.Location == "Nowhere", "renamed and located");
        Check(renamed.Description == "Created by dayflower-dev edit-test." && renamed.HangoutLink is not null, "untouched fields kept");

        var moved = (await connection.UpdateEventAsync(target, new EventEdit { Start = At(day, 7), End = At(day, 7, 45), Meet = false, ColorId = string.Empty },
                                                       EditScope.ThisEvent, false, CancellationToken.None))[0].Event!;
        Check(moved.Start.EarliestUtc == At(day, 7).EarliestUtc && moved.End.EarliestUtc == At(day, 7, 45).EarliestUtc, "moved to 7:00-7:45");
        Check(moved.HangoutLink is null && moved.ColorId is null, "Meet and colour removed");

        var gone = await connection.DeleteEventAsync(target, EditScope.ThisEvent, false, CancellationToken.None);
        Check(gone.Single().IsRemoved, "deleted");
        created.Remove(oneOff.Key.EventId);

        var allDay = Added(await connection.CreateEventAsync(calendarId, new EventEdit
        {
            Summary = $"{Prefix} all-day",
            Start = EventTime.AllDay(day),
            End = EventTime.AllDay(day.AddDays(2)),
            IsBusy = false,
            Reminders = quiet,
        }, false, CancellationToken.None));
        Check(allDay.Start.Date == day && allDay.End.Date == day.AddDays(2) && !allDay.IsBusy, "two-day all-day event, free");

        Console.WriteLine("Repeating event: daily at 6:00, five times");
        var series = Added(await connection.CreateEventAsync(calendarId, new EventEdit
        {
            Summary = $"{Prefix} daily",
            Start = At(day, 6),
            End = At(day, 6, 30),
            Recurrence = ["RRULE:FREQ=DAILY;COUNT=5"],
            Reminders = quiet,
        }, false, CancellationToken.None));
        var list = await Mine();
        Check(list.Count(e => e.Summary!.EndsWith("daily", StringComparison.Ordinal)) == 5, $"five occurrences: {Show(list)}");

        await connection.UpdateEventAsync(OccurrenceOf(series, day.AddDays(1), 6), new EventEdit { Summary = $"{Prefix} daily #2 only" },
                                          EditScope.ThisEvent, false, CancellationToken.None);
        await connection.UpdateEventAsync(OccurrenceOf(series, day.AddDays(2), 6), new EventEdit { Start = At(day.AddDays(2), 8), End = At(day.AddDays(2), 8, 30) },
                                          EditScope.ThisEvent, false, CancellationToken.None);
        list = await Mine();
        Check(list.Count(e => e.Summary!.EndsWith("#2 only", StringComparison.Ordinal)) == 1, "this event: second renamed alone");
        Check(list.Any(e => e.Start.EarliestUtc == At(day.AddDays(2), 8).EarliestUtc), $"this event: third moved to 8:00: {Show(list)}");

        // Moving the fourth from 6:00 to 6:30 with "all events" moves the series by half an hour.
        await connection.UpdateEventAsync(OccurrenceOf(series, day.AddDays(3), 6), new EventEdit { Start = At(day.AddDays(3), 6, 30), End = At(day.AddDays(3), 7) },
                                          EditScope.AllEvents, false, CancellationToken.None);
        list = await Mine();
        Check(list.Any(e => e.Start.EarliestUtc == At(day, 6, 30).EarliestUtc) && list.Any(e => e.Start.EarliestUtc == At(day.AddDays(4), 6, 30).EarliestUtc),
              $"all events: series now at 6:30: {Show(list)}");

        var split = await connection.UpdateEventAsync(OccurrenceOf(series with { Start = At(day, 6, 30), End = At(day, 7) }, day.AddDays(3), 6, 30),
                                                      new EventEdit { Summary = $"{Prefix} daily following" }, EditScope.ThisAndFollowing, false, CancellationToken.None);
        var continuation = Added(split);
        list = await Mine();
        Check(list.Count(e => e.Summary!.EndsWith("following", StringComparison.Ordinal)) == 2, $"this and following: last two renamed: {Show(list)}");
        Check(list.Count(e => e.Summary!.Contains("daily", StringComparison.Ordinal)) == 5, "this and following: still five in all");
        Check(continuation.Recurrence.Single().Contains("COUNT=2", StringComparison.Ordinal), $"continuation repeats twice: {continuation.Recurrence.Single()}");

        var seriesNow = (await connection.GetOccurrencesAsync(calendarId, series.Key.EventId, rangeFrom, rangeTo, CancellationToken.None)).ToList();
        var first = seriesNow.First();
        await connection.DeleteEventAsync(new EventTarget(series with { Start = At(day, 6, 30), End = At(day, 7) }, first.OriginalStart, first.Start, first.End),
                                          EditScope.ThisEvent, false, CancellationToken.None);
        list = await Mine();
        Check(!list.Any(e => e.Start.EarliestUtc == At(day, 6, 30).EarliestUtc && e.Summary!.EndsWith("daily", StringComparison.Ordinal)),
              $"delete this event: first gone: {Show(list)}");

        await connection.DeleteEventAsync(OccurrenceOf(continuation, day.AddDays(4), 6, 30), EditScope.ThisAndFollowing, false, CancellationToken.None);
        list = await Mine();
        Check(list.Count(e => e.Summary!.EndsWith("following", StringComparison.Ordinal)) == 1, $"delete this and following: one left: {Show(list)}");

        await connection.DeleteEventAsync(OccurrenceOf(continuation, day.AddDays(3), 6, 30), EditScope.AllEvents, false, CancellationToken.None);
        await connection.DeleteEventAsync(OccurrenceOf(series with { Start = At(day, 6, 30), End = At(day, 7) }, day.AddDays(1), 6, 30), EditScope.AllEvents, false, CancellationToken.None);
        created.Remove(continuation.Key.EventId);
        created.Remove(series.Key.EventId);
        list = await Mine();
        Check(list.All(e => !e.Summary!.Contains("daily", StringComparison.Ordinal)), $"delete all events: both series gone: {Show(list)}");

        Console.WriteLine("Free/busy");
        var busy = await connection.GetFreeBusyAsync([account.Email], rangeFrom, rangeTo, CancellationToken.None);
        Check(busy.TryGetValue(account.Email, out var mine) && !mine.IsUnknown, $"own availability readable ({mine?.Busy.Count} busy periods)");

        if (account.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Workspace event types: skipped (Gmail account)");
        }
        else
        {
            Console.WriteLine("Workspace event types (declining nothing)");
            var focus = Added(await connection.CreateEventAsync(calendarId, new EventEdit
            {
                Summary = $"{Prefix} focus",
                Start = At(day.AddDays(5), 6),
                End = At(day.AddDays(5), 7),
                Kind = EventKind.FocusTime,
                Workspace = new WorkspaceDetails(AutoDeclineMode.None),
            }, false, CancellationToken.None));
            Check(focus.Kind == EventKind.FocusTime, "focus time created");
            var place = Added(await connection.CreateEventAsync(calendarId, new EventEdit
            {
                Summary = $"{Prefix} working location",
                Start = EventTime.AllDay(day.AddDays(5)),
                End = EventTime.AllDay(day.AddDays(6)),
                Kind = EventKind.WorkingLocation,
                Workspace = new WorkspaceDetails(Location: WorkingLocationType.Custom, LocationLabel: "Test place"),
            }, false, CancellationToken.None));
            Check(place.Kind == EventKind.WorkingLocation && place.Workspace?.LocationLabel == "Test place", "working location created");
            var away = Added(await connection.CreateEventAsync(calendarId, new EventEdit
            {
                Summary = $"{Prefix} out of office",
                Start = At(day.AddDays(6), 6),
                End = At(day.AddDays(6), 7),
                Kind = EventKind.OutOfOffice,
                Workspace = new WorkspaceDetails(AutoDeclineMode.None, "Test"),
            }, false, CancellationToken.None));
            Check(away.Kind == EventKind.OutOfOffice && away.Workspace?.DeclineMessage == "Test", "out of office created");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {ex.GetType().Name}: {ex.Message}");
        failures++;
    }
    finally
    {
        // Whatever is left of the test events goes.
        foreach (var e in await Mine())
        {
            string id = e.RecurringEventId ?? e.Key.EventId;
            if (!created.Contains(id))
            {
                created.Add(id);
            }
        }

        foreach (var id in created)
        {
            try
            {
                var leftover = new CalendarEvent { Key = new EventKey(account.Id, calendarId, id), Start = At(day, 6), End = At(day, 7) };
                await connection.DeleteEventAsync(new EventTarget(leftover, null, leftover.Start, leftover.End), EditScope.ThisEvent, false, CancellationToken.None);
                Console.WriteLine($"  cleaned up {id}");
            }
            catch (Exception ex)
            {
                // 404 and 410 mean it was already gone.
                if (!ex.Message.Contains("410", StringComparison.Ordinal) && !ex.Message.Contains("404", StringComparison.Ordinal))
                {
                    Console.WriteLine($"  couldn't clean up {id}: {ex.Message}");
                }
            }
        }

        var left = await Mine();
        Console.WriteLine(left.Count == 0 ? "No test events left." : $"Still there: {Show(left)}");
    }

    Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} checks failed.");
    return failures == 0 ? 0 : 4;
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

// The plain program's data folder (%AppData%\Dayflower), else the MSIX package's.
static string? FindAppDataFolder()
{
    string plain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dayflower");
    if (File.Exists(Path.Combine(plain, "dayflower.db")))
    {
        return plain;
    }

    string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
    var packaged = Directory.Exists(packages)
        ? Directory.GetDirectories(packages, "kstawi.Dayflower_*").Select(d => Path.Combine(d, "LocalState")).FirstOrDefault(Directory.Exists)
        : null;
    return packaged ?? plain;
}
