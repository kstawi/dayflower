// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Security;
using Dayflower.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Dayflower.Core.Sync;

public enum SyncOutcome
{
    Synced,

    /// <summary>Some calendars failed; the others are up to date.</summary>
    PartlySynced,

    NeedsSignIn,

    /// <summary>No connection to the provider; the cache is unchanged.</summary>
    Offline,

    Failed,
}

public sealed record AccountSyncResult(
    string AccountId,
    SyncOutcome Outcome,
    int CalendarsSynced,
    int EventsChanged,
    IReadOnlyList<string> FailedCalendars,
    string? Error);

public sealed record SyncReport(IReadOnlyList<AccountSyncResult> Accounts)
{
    public int EventsChanged => Accounts.Sum(a => a.EventsChanged);

    public bool IsOffline => Accounts.Count > 0 && Accounts.All(a => a.Outcome == SyncOutcome.Offline);
}

/// <summary>
/// Brings the local cache up to date with every signed-in account: calendar lists first, then each calendar's
/// events, using the providers' incremental sync tokens so each run only downloads what changed.
/// </summary>
public sealed class SyncEngine(
    ICalendarStore store,
    IEnumerable<ICalendarProvider> providers,
    ISecretProtector protector,
    TimeProvider time,
    ILogger<SyncEngine> logger)
{
    /// <summary>
    /// Some calendars (certain Google holiday calendars) expire their sync token on every request. Those are fully
    /// re-downloaded at most this often; in between, their cached events stay as they are.
    /// </summary>
    public static readonly TimeSpan ExpiredTokenRefreshInterval = TimeSpan.FromHours(6);

    private readonly Dictionary<string, ICalendarProvider> _providers = providers.ToDictionary(p => p.Id);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Account, string Calendar), DateTimeOffset> _forcedFullSyncs = new();

    public async Task<SyncReport> SyncAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<AccountSyncResult>();
        foreach (var account in await store.GetAccountsAsync(cancellationToken))
        {
            if (account.State == AccountState.Ok)
            {
                results.Add(await SyncAccountAsync(account, cancellationToken));
            }
        }

        return new SyncReport(results);
    }

    public async Task<AccountSyncResult> SyncAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = (await store.GetAccountsAsync(cancellationToken)).FirstOrDefault(a => a.Id == accountId)
            ?? throw new InvalidOperationException($"No account {accountId}.");
        return await SyncAccountAsync(account, cancellationToken);
    }

    private async Task<AccountSyncResult> SyncAccountAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(account.ProviderId, out var provider))
        {
            return new(account.Id, SyncOutcome.Failed, 0, 0, [], $"Unknown provider {account.ProviderId}.");
        }

        var secret = await store.GetAccountSecretAsync(account.Id, cancellationToken);
        if (secret is null)
        {
            await store.SetAccountStateAsync(account.Id, AccountState.NeedsSignIn, cancellationToken);
            return new(account.Id, SyncOutcome.NeedsSignIn, 0, 0, [], "No saved sign-in.");
        }

        var connection = provider.Connect(account.Id, protector.Unprotect(secret));
        try
        {
            await SyncCalendarListAsync(connection, account.Id, cancellationToken);

            int synced = 0, changed = 0;
            var failed = new List<string>();
            foreach (var calendar in await store.GetCalendarsAsync(account.Id, cancellationToken))
            {
                try
                {
                    changed += await SyncCalendarAsync(connection, account.Id, calendar.Id, cancellationToken);
                    synced++;
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or AccountSignInRequiredException
                                                      or HttpRequestException))
                {
                    logger.LogWarning(ex, "Syncing calendar {Calendar} of {Account} failed", calendar.Id, account.Id);
                    failed.Add(calendar.Id);
                }
            }

            await store.SetAccountSyncedAsync(account.Id, time.GetUtcNow(), cancellationToken);
            logger.LogInformation(
                "Synced {Account}: {Calendars} calendars, {Changes} event changes, {Failed} failed",
                account.Id, synced, changed, failed.Count);
            return new(account.Id, failed.Count == 0 ? SyncOutcome.Synced : SyncOutcome.PartlySynced,
                       synced, changed, failed, null);
        }
        catch (AccountSignInRequiredException ex)
        {
            logger.LogWarning(ex, "{Account} needs to sign in again", account.Id);
            await store.SetAccountStateAsync(account.Id, AccountState.NeedsSignIn, cancellationToken);
            return new(account.Id, SyncOutcome.NeedsSignIn, 0, 0, [], ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation("Can't reach {Provider} for {Account}: {Message}", account.ProviderId, account.Id, ex.Message);
            return new(account.Id, SyncOutcome.Offline, 0, 0, [], ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Request to {Provider} timed out for {Account}", account.ProviderId, account.Id);
            return new(account.Id, SyncOutcome.Offline, 0, 0, [], ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Syncing {Account} failed", account.Id);
            return new(account.Id, SyncOutcome.Failed, 0, 0, [], ex.Message);
        }
    }

    private async Task SyncCalendarListAsync(ICalendarConnection connection, string accountId, CancellationToken cancellationToken)
    {
        var token = await store.GetCalendarListSyncTokenAsync(accountId, cancellationToken);
        CalendarListChanges changes;
        try
        {
            changes = await connection.GetCalendarChangesAsync(token, cancellationToken);
        }
        catch (SyncTokenExpiredException)
        {
            logger.LogInformation("Calendar-list sync token of {Account} expired; syncing the whole list", accountId);
            changes = await connection.GetCalendarChangesAsync(null, cancellationToken);
        }

        await store.ApplyCalendarChangesAsync(accountId, changes, cancellationToken);
    }

    private async Task<int> SyncCalendarAsync(
        ICalendarConnection connection, string accountId, string calendarId, CancellationToken cancellationToken)
    {
        var token = await store.GetEventSyncTokenAsync(accountId, calendarId, cancellationToken);
        EventChangeSet changes;
        try
        {
            changes = await connection.GetEventChangesAsync(calendarId, token, cancellationToken);
        }
        catch (SyncTokenExpiredException)
        {
            var key = (accountId, calendarId);
            var now = time.GetUtcNow();
            if (_forcedFullSyncs.TryGetValue(key, out var last) && now - last < ExpiredTokenRefreshInterval)
            {
                logger.LogDebug("Sync token of {Calendar} expired again; keeping its cached events for now", calendarId);
                return 0;
            }

            logger.LogInformation("Event sync token of {Calendar} expired; syncing every event again", calendarId);
            changes = await connection.GetEventChangesAsync(calendarId, null, cancellationToken);
            _forcedFullSyncs[key] = now;
        }

        await store.ApplyEventChangesAsync(accountId, calendarId, changes, cancellationToken);
        return changes.Changes.Count;
    }
}
