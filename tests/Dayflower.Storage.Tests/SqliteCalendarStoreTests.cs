// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Microsoft.Data.Sqlite;

namespace Dayflower.Storage.Tests;

public sealed class SqliteCalendarStoreTests : IDisposable
{
    private const string Me = "google:1";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dayflower-test-{Guid.NewGuid():N}.db");
    private readonly SqliteCalendarStore _store;

    public SqliteCalendarStoreTests()
    {
        _store = SqliteCalendarStore.Open(_path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static readonly DateTimeOffset Nov6 = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    private static AccountRecord Account(string id, string email) => new(id, "google", email, null, 0, AccountState.Ok, null);

    private static CalendarRecord Calendar(string id, bool visible = true, string name = "Work") =>
        new(Me, id, name, "Europe/Warsaw", "#039be5", "#ffffff", CalendarAccess.Owner, id == "primary", visible,
            [new Reminder(ReminderMethod.Popup, 10)]);

    private static EventChange Timed(string id, DateTimeOffset start, TimeSpan length, string? seriesId = null,
                                     DateTimeOffset? originalStart = null, params string[] recurrence) =>
        new(id, new CalendarEvent
        {
            Key = new EventKey(Me, "primary", id),
            ICalUid = id + "@google.com",
            Start = EventTime.At(start, "Europe/Warsaw"),
            End = EventTime.At(start + length, "Europe/Warsaw"),
            Recurrence = recurrence,
            RecurringEventId = seriesId,
            OriginalStart = originalStart is { } o ? EventTime.At(o) : null,
        }, $$"""{"id":"{{id}}"}""");

    private async Task WithCalendarsAsync(params CalendarRecord[] calendars)
    {
        await _store.SaveAccountAsync(Account(Me, "me@example.com"), [1, 2, 3]);
        await _store.ApplyCalendarChangesAsync(Me, new CalendarListChanges(true, calendars, [], "list-1"));
    }

    [Fact]
    public async Task Accounts_keep_the_order_they_were_added_in()
    {
        await _store.SaveAccountAsync(Account("google:b", "b@example.com"), [1]);
        await _store.SaveAccountAsync(Account("google:a", "a@example.com"), [2]);
        await _store.SaveAccountAsync(Account("google:b", "b-renamed@example.com") with { State = AccountState.NeedsSignIn }, [3]);

        var accounts = await _store.GetAccountsAsync();

        Assert.Equal(["google:b", "google:a"], accounts.Select(a => a.Id));
        Assert.Equal("b-renamed@example.com", accounts[0].Email);
        Assert.Equal(AccountState.NeedsSignIn, accounts[0].State);
        Assert.Equal([3], await _store.GetAccountSecretAsync("google:b"));
    }

    [Fact]
    public async Task Account_state_and_last_sync_are_saved()
    {
        await _store.SaveAccountAsync(Account(Me, "me@example.com"), [1]);

        await _store.SetAccountStateAsync(Me, AccountState.NeedsSignIn);
        await _store.SetAccountSyncedAsync(Me, Nov6);

        var account = Assert.Single(await _store.GetAccountsAsync());
        Assert.Equal(AccountState.NeedsSignIn, account.State);
        Assert.Equal(Nov6, account.LastSyncedUtc);
    }

    [Fact]
    public async Task Calendars_are_saved_with_their_details()
    {
        await WithCalendarsAsync(Calendar("primary"), Calendar("holidays", visible: false, name: "Holidays"));

        var calendars = await _store.GetCalendarsAsync(Me);

        Assert.Equal(["primary", "holidays"], calendars.Select(c => c.Id));
        Assert.True(calendars[0].IsPrimary);
        Assert.False(calendars[1].IsVisible);
        Assert.Equal([new Reminder(ReminderMethod.Popup, 10)], calendars[0].DefaultReminders);
        Assert.Equal("list-1", await _store.GetCalendarListSyncTokenAsync(Me));
    }

    [Fact]
    public async Task Hiding_a_calendar_survives_later_syncs()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.SetCalendarVisibleAsync(Me, "primary", false);

        await _store.ApplyCalendarChangesAsync(Me, new CalendarListChanges(false, [Calendar("primary", name: "Renamed")], [], "list-2"));

        var calendar = Assert.Single(await _store.GetCalendarsAsync(Me));
        Assert.False(calendar.IsVisible);
        Assert.Equal("Renamed", calendar.Name);
    }

    [Fact]
    public async Task Full_calendar_sync_drops_calendars_no_longer_listed_with_their_events()
    {
        await WithCalendarsAsync(Calendar("primary"), Calendar("old"));
        await _store.ApplyEventChangesAsync(Me, "old", new EventChangeSet(true, [Timed("e1", Nov6, TimeSpan.FromHours(1))], "t"));

        await _store.ApplyCalendarChangesAsync(Me, new CalendarListChanges(true, [Calendar("primary")], [], "list-2"));

        Assert.Equal(["primary"], (await _store.GetCalendarsAsync(Me)).Select(c => c.Id));
        Assert.Equal(0, (await _store.GetStatsAsync()).Events);
    }

    [Fact]
    public async Task Event_sync_saves_events_and_the_token()
    {
        await WithCalendarsAsync(Calendar("primary"));

        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true,
            [Timed("a", Nov6.AddHours(9), TimeSpan.FromHours(1)), Timed("b", Nov6.AddHours(13), TimeSpan.FromHours(1))], "sync-1"));

        Assert.Equal("sync-1", await _store.GetEventSyncTokenAsync(Me, "primary"));
        Assert.Equal(2, (await _store.GetStatsAsync()).Events);
    }

    [Fact]
    public async Task Full_event_sync_replaces_everything_and_incremental_sync_patches()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true,
            [Timed("a", Nov6, TimeSpan.FromHours(1)), Timed("b", Nov6, TimeSpan.FromHours(1))], "t1"));

        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(false,
            [new EventChange("a", null, null), Timed("c", Nov6, TimeSpan.FromHours(1))], "t2"));
        var afterIncremental = (await _store.GetEventsAsync(Nov6.AddDays(-1), Nov6.AddDays(1))).Select(e => e.Key.EventId).Order();

        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true, [Timed("d", Nov6, TimeSpan.FromHours(1))], "t3"));
        var afterFull = (await _store.GetEventsAsync(Nov6.AddDays(-1), Nov6.AddDays(1))).Select(e => e.Key.EventId);

        Assert.Equal(["b", "c"], afterIncremental);
        Assert.Equal(["d"], afterFull);
    }

    [Fact]
    public async Task Removing_a_series_removes_its_changed_occurrences()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true,
        [
            Timed("series", Nov6.AddDays(-30), TimeSpan.FromMinutes(15), recurrence: "RRULE:FREQ=DAILY"),
            Timed("series_1", Nov6.AddHours(11), TimeSpan.FromMinutes(15), "series", Nov6.AddHours(8)),
        ], "t1"));

        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(false, [new EventChange("series", null, null)], "t2"));

        Assert.Equal(0, (await _store.GetStatsAsync()).Events);
    }

    [Fact]
    public async Task Range_query_finds_overlapping_events_series_and_moved_occurrences()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true,
        [
            Timed("inside", Nov6.AddHours(9), TimeSpan.FromHours(1)),
            Timed("overnight", Nov6.AddHours(-1), TimeSpan.FromHours(2)),
            Timed("later", Nov6.AddDays(10), TimeSpan.FromHours(1)),
            Timed("old-series", Nov6.AddYears(-1), TimeSpan.FromMinutes(15), recurrence: "RRULE:FREQ=WEEKLY"),
            Timed("future-series", Nov6.AddDays(30), TimeSpan.FromMinutes(15), recurrence: "RRULE:FREQ=WEEKLY"),
            Timed("moved-away", Nov6.AddDays(20), TimeSpan.FromMinutes(15), "old-series", Nov6.AddHours(9)),
            new EventChange("all-day", new CalendarEvent
            {
                Key = new EventKey(Me, "primary", "all-day"),
                Start = EventTime.AllDay(new DateOnly(2026, 11, 6)),
                End = EventTime.AllDay(new DateOnly(2026, 11, 7)),
            }, "{}"),
        ], "t1"));

        var found = (await _store.GetEventsAsync(Nov6, Nov6.AddDays(1))).Select(e => e.Key.EventId).Order();

        Assert.Equal(["all-day", "inside", "moved-away", "old-series", "overnight"], found);
    }

    [Fact]
    public async Task Changed_occurrences_of_a_series_can_be_listed()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true,
        [
            Timed("series", Nov6, TimeSpan.FromMinutes(15), recurrence: "RRULE:FREQ=DAILY"),
            Timed("series_1", Nov6.AddDays(1), TimeSpan.FromMinutes(15), "series", Nov6.AddDays(1)),
            Timed("series_2", Nov6.AddDays(2), TimeSpan.FromMinutes(15), "series", Nov6.AddDays(2)),
            Timed("other", Nov6, TimeSpan.FromMinutes(15)),
        ], "t1"));

        var overrides = await _store.GetOccurrenceOverridesAsync(new EventKey(Me, "primary", "series"));

        Assert.Equal(["series_1", "series_2"], overrides.Select(o => o.Key.EventId).Order());
        Assert.Equal("""{"id":"series_1"}""", overrides.Single(o => o.Key.EventId == "series_1").Payload);
    }

    [Fact]
    public async Task Removing_an_account_removes_its_calendars_and_events()
    {
        await WithCalendarsAsync(Calendar("primary"));
        await _store.ApplyEventChangesAsync(Me, "primary", new EventChangeSet(true, [Timed("a", Nov6, TimeSpan.FromHours(1))], "t"));

        await _store.RemoveAccountAsync(Me);

        Assert.Equal(new Dayflower.Core.Storage.CacheStats(0, 0, 0, 0, 0), await _store.GetStatsAsync());
    }

    [Fact]
    public async Task Reopening_keeps_the_data()
    {
        await WithCalendarsAsync(Calendar("primary"));

        var reopened = SqliteCalendarStore.Open(_path);

        Assert.Single(await reopened.GetCalendarsAsync());
    }
}
