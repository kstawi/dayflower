// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Providers;

namespace Dayflower.Core.Storage;

/// <summary>The local cache of accounts, calendars and events.</summary>
public interface ICalendarStore
{
    Task<IReadOnlyList<AccountRecord>> GetAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an account, or updates it (and its saved secret) if it's already there.</summary>
    Task SaveAccountAsync(AccountRecord account, byte[] protectedSecret, CancellationToken cancellationToken = default);

    Task<byte[]?> GetAccountSecretAsync(string accountId, CancellationToken cancellationToken = default);

    Task SetAccountStateAsync(string accountId, AccountState state, CancellationToken cancellationToken = default);

    Task SetAccountSyncedAsync(string accountId, DateTimeOffset syncedUtc, CancellationToken cancellationToken = default);

    /// <summary>Removes an account with all its calendars and events.</summary>
    Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarRecord>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default);

    Task<string?> GetCalendarListSyncTokenAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies calendar-list changes and saves the next sync token. A full sync removes calendars that are no
    /// longer listed. The user's visibility choice survives updates.
    /// </summary>
    Task ApplyCalendarChangesAsync(string accountId, CalendarListChanges changes, CancellationToken cancellationToken = default);

    Task SetCalendarVisibleAsync(string accountId, string calendarId, bool visible, CancellationToken cancellationToken = default);

    Task<string?> GetEventSyncTokenAsync(string accountId, string calendarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies event changes and saves the next sync token in one transaction. A full sync replaces every
    /// event of the calendar.
    /// </summary>
    Task ApplyEventChangesAsync(string accountId, string calendarId, EventChangeSet changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the changes an edit made, so they show before the next sync. The sync token stays as it is, so the
    /// next sync still brings everything that changed since, including these edits.
    /// </summary>
    Task ApplyLocalChangesAsync(string accountId, string calendarId, IReadOnlyList<EventChange> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Events that may appear between <paramref name="from"/> and <paramref name="to"/>: events overlapping the
    /// range, plus every repeating series that may have occurrences in it. Callers filter precisely.
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>One cached event, or null if it isn't there.</summary>
    Task<StoredEvent?> GetEventAsync(EventKey key, CancellationToken cancellationToken = default);

    /// <summary>The changed and cancelled occurrences of one series.</summary>
    Task<IReadOnlyList<StoredEvent>> GetOccurrenceOverridesAsync(EventKey series, CancellationToken cancellationToken = default);

    Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>An event as cached: its key, provider and the provider's payload.</summary>
public sealed record StoredEvent(EventKey Key, string ProviderId, string Payload);

public sealed record CacheStats(int Accounts, int Calendars, int Events, int Series, int OccurrenceOverrides);
