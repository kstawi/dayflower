// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Security;
using Dayflower.Core.Storage;
using Dayflower.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dayflower.Core.Tests;

public class SyncEngineTests
{
    private const string AccountId = "google:1";

    private readonly FakeStore _store = new();
    private readonly FakeConnection _connection = new();

    public SyncEngineTests()
    {
        _store.Accounts.Add(new AccountRecord(AccountId, "fake", "me@example.com", null, 0, AccountState.Ok, null));
        _store.Secrets[AccountId] = Encoding.UTF8.GetBytes("refresh-token");
        _connection.Calendars = [Calendar("primary"), Calendar("team")];
    }

    private readonly ManualTime _time = new();
    private SyncEngine? _engine;

    private SyncEngine Engine => _engine ??= new(_store, [new FakeProvider(_connection)], new PlainProtector(), _time,
                                                 NullLogger<SyncEngine>.Instance);

    [Fact]
    public async Task Calendar_whose_token_keeps_expiring_is_refreshed_at_most_every_six_hours()
    {
        await Engine.SyncAllAsync(CancellationToken.None);
        _connection.ExpiredTokens.Add("team-1"); // Google hands out the same useless token every time

        await Engine.SyncAllAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));
        _connection.EventTokensSeen.Clear();
        var meanwhile = await Engine.SyncAllAsync(CancellationToken.None);
        var skipped = _connection.EventTokensSeen.ToList();

        _time.Advance(SyncEngine.ExpiredTokenRefreshInterval);
        _connection.EventTokensSeen.Clear();
        await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Synced, meanwhile.Accounts[0].Outcome);
        Assert.Equal(["primary:primary-1", "team:team-1"], skipped);
        Assert.Equal(["primary:primary-1", "team:team-1", "team:"], _connection.EventTokensSeen);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public async Task First_sync_downloads_everything_and_saves_the_tokens()
    {
        var report = await Engine.SyncAllAsync(CancellationToken.None);

        var account = Assert.Single(report.Accounts);
        Assert.Equal(SyncOutcome.Synced, account.Outcome);
        Assert.Equal(2, account.CalendarsSynced);
        Assert.Equal([null], _connection.CalendarListTokensSeen);
        Assert.Equal(["primary:", "team:"], _connection.EventTokensSeen);
        Assert.Equal("list-1", _store.CalendarListToken);
        Assert.Equal("primary-1", _store.EventTokens["primary"]);
        Assert.NotNull(_store.Accounts[0].LastSyncedUtc);
    }

    [Fact]
    public async Task Next_sync_sends_the_saved_tokens()
    {
        await Engine.SyncAllAsync(CancellationToken.None);
        _connection.CalendarListTokensSeen.Clear();
        _connection.EventTokensSeen.Clear();

        await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Equal(["list-1"], _connection.CalendarListTokensSeen);
        Assert.Equal(["primary:primary-1", "team:team-1"], _connection.EventTokensSeen);
    }

    [Fact]
    public async Task Expired_token_triggers_a_full_resync_of_that_calendar()
    {
        await Engine.SyncAllAsync(CancellationToken.None);
        _connection.EventTokensSeen.Clear();
        _connection.ExpiredTokens.Add("team-1");

        var report = await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Synced, report.Accounts[0].Outcome);
        Assert.Equal(["primary:primary-1", "team:team-1", "team:"], _connection.EventTokensSeen);
        Assert.Contains("team", _store.FullSyncs);
    }

    [Fact]
    public async Task Rejected_sign_in_marks_the_account()
    {
        _connection.SignInRejected = true;

        var report = await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.NeedsSignIn, report.Accounts[0].Outcome);
        Assert.Equal(AccountState.NeedsSignIn, _store.Accounts[0].State);
    }

    [Fact]
    public async Task Accounts_that_need_sign_in_are_skipped()
    {
        _store.Accounts[0] = _store.Accounts[0] with { State = AccountState.NeedsSignIn };

        var report = await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Empty(report.Accounts);
        Assert.Empty(_connection.CalendarListTokensSeen);
    }

    [Fact]
    public async Task No_connection_reports_offline_and_keeps_the_cache()
    {
        _connection.Offline = true;

        var report = await Engine.SyncAllAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Offline, report.Accounts[0].Outcome);
        Assert.True(report.IsOffline);
        Assert.Null(_store.Accounts[0].LastSyncedUtc);
    }

    [Fact]
    public async Task One_failing_calendar_doesnt_stop_the_others()
    {
        _connection.FailingCalendars.Add("team");

        var report = await Engine.SyncAllAsync(CancellationToken.None);

        var account = report.Accounts[0];
        Assert.Equal(SyncOutcome.PartlySynced, account.Outcome);
        Assert.Equal(1, account.CalendarsSynced);
        Assert.Equal(["team"], account.FailedCalendars);
    }

    private static CalendarRecord Calendar(string id) =>
        new(AccountId, id, id, "Europe/Warsaw", "#4285f4", "#ffffff", CalendarAccess.Owner, id == "primary", true, []);

    private sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(string secret) => Encoding.UTF8.GetBytes(secret);

        public string Unprotect(byte[] protectedSecret) => Encoding.UTF8.GetString(protectedSecret);
    }

    private sealed class FakeProvider(FakeConnection connection) : ICalendarProvider
    {
        public string Id => "fake";

        public bool IsConfigured => true;

        public Task<NewAccount> SignInAsync(string? loginHint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ICalendarConnection Connect(string accountId, string secret) => connection;

        public CalendarEvent ParseEvent(string accountId, string calendarId, string payload) =>
            throw new NotSupportedException();

        public string? EventColor(string colorId) => null;
    }

    private sealed class FakeConnection : ICalendarConnection
    {
        public List<CalendarRecord> Calendars { get; set; } = [];

        public List<string?> CalendarListTokensSeen { get; } = [];

        public List<string> EventTokensSeen { get; } = [];

        public HashSet<string> ExpiredTokens { get; } = [];

        public HashSet<string> FailingCalendars { get; } = [];

        public bool SignInRejected { get; set; }

        public bool Offline { get; set; }

        public Task<CalendarListChanges> GetCalendarChangesAsync(string? syncToken, CancellationToken cancellationToken)
        {
            if (SignInRejected)
            {
                throw new AccountSignInRequiredException("invalid_grant");
            }

            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            CalendarListTokensSeen.Add(syncToken);
            return Task.FromResult(new CalendarListChanges(syncToken is null, Calendars, [], "list-1"));
        }

        public Task<EventChangeSet> GetEventChangesAsync(string calendarId, string? syncToken, CancellationToken cancellationToken)
        {
            EventTokensSeen.Add($"{calendarId}:{syncToken}");
            if (FailingCalendars.Contains(calendarId))
            {
                throw new InvalidOperationException("Not Found");
            }

            if (syncToken is not null && ExpiredTokens.Contains(syncToken))
            {
                throw new SyncTokenExpiredException("410 Gone");
            }

            var evt = new CalendarEvent
            {
                Key = new EventKey(AccountId, calendarId, $"{calendarId}-event"),
                Start = EventTime.At(DateTimeOffset.UnixEpoch),
                End = EventTime.At(DateTimeOffset.UnixEpoch.AddHours(1)),
            };
            return Task.FromResult(new EventChangeSet(
                syncToken is null, [new EventChange(evt.Key.EventId, evt, "{}")], $"{calendarId}-1"));
        }

        public Task<IReadOnlyList<CalendarEvent>> GetOccurrencesAsync(
            string calendarId, string seriesId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> GetExpandedEventsAsync(
            string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Just enough of a store for the sync engine.</summary>
    private sealed class FakeStore : ICalendarStore
    {
        public List<AccountRecord> Accounts { get; } = [];

        public Dictionary<string, byte[]> Secrets { get; } = [];

        public List<CalendarRecord> Calendars { get; } = [];

        public string? CalendarListToken { get; private set; }

        public Dictionary<string, string?> EventTokens { get; } = [];

        public List<string> FullSyncs { get; } = [];

        public Task<IReadOnlyList<AccountRecord>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountRecord>>(Accounts.ToList());

        public Task SaveAccountAsync(AccountRecord account, byte[] protectedSecret, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetAccountSecretAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Secrets.GetValueOrDefault(accountId));

        public Task SetAccountStateAsync(string accountId, AccountState state, CancellationToken cancellationToken = default)
        {
            Update(accountId, a => a with { State = state });
            return Task.CompletedTask;
        }

        public Task SetAccountSyncedAsync(string accountId, DateTimeOffset syncedUtc, CancellationToken cancellationToken = default)
        {
            Update(accountId, a => a with { LastSyncedUtc = syncedUtc });
            return Task.CompletedTask;
        }

        public Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarRecord>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CalendarRecord>>(Calendars.ToList());

        public Task<string?> GetCalendarListSyncTokenAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CalendarListToken);

        public Task ApplyCalendarChangesAsync(string accountId, CalendarListChanges changes, CancellationToken cancellationToken = default)
        {
            if (changes.IsFullSync)
            {
                Calendars.Clear();
            }

            Calendars.RemoveAll(c => changes.Changed.Any(n => n.Id == c.Id) || changes.RemovedIds.Contains(c.Id));
            Calendars.AddRange(changes.Changed);
            CalendarListToken = changes.NextSyncToken;
            return Task.CompletedTask;
        }

        public Task SetCalendarVisibleAsync(string accountId, string calendarId, bool visible, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetEventSyncTokenAsync(string accountId, string calendarId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventTokens.GetValueOrDefault(calendarId));

        public Task ApplyEventChangesAsync(string accountId, string calendarId, EventChangeSet changes, CancellationToken cancellationToken = default)
        {
            if (changes.IsFullSync)
            {
                FullSyncs.Add(calendarId);
            }

            EventTokens[calendarId] = changes.NextSyncToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredEvent>> GetOccurrenceOverridesAsync(EventKey series, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private void Update(string accountId, Func<AccountRecord, AccountRecord> change)
        {
            int i = Accounts.FindIndex(a => a.Id == accountId);
            Accounts[i] = change(Accounts[i]);
        }
    }
}
