// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Core.Providers;

/// <summary>An online calendar service, such as Google Calendar.</summary>
public interface ICalendarProvider
{
    /// <summary>Short stable ID used in account IDs and the cache, e.g. "google".</summary>
    string Id { get; }

    /// <summary>Whether this build can sign in (for Google: whether it was built with an OAuth client).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Signs in a new account, or an existing one again, in the user's browser.
    /// </summary>
    /// <param name="loginHint">Email of the account to suggest, for "sign in again".</param>
    Task<NewAccount> SignInAsync(string? loginHint, CancellationToken cancellationToken);

    /// <summary>Opens an API connection for an account using the secret <see cref="SignInAsync"/> returned.</summary>
    ICalendarConnection Connect(string accountId, string secret);

    /// <summary>Turns an event payload stored in the cache back into an event.</summary>
    CalendarEvent ParseEvent(string accountId, string calendarId, string payload);
}

/// <summary>An account that has just signed in.</summary>
/// <param name="Secret">What the provider needs to reconnect later (for Google, the refresh token).</param>
public sealed record NewAccount(string AccountId, string Email, string? DisplayName, string Secret);

/// <summary>API access for one signed-in account.</summary>
public interface ICalendarConnection
{
    /// <summary>Calendar-list changes since <paramref name="syncToken"/>, or the whole list when it's null.</summary>
    Task<CalendarListChanges> GetCalendarChangesAsync(string? syncToken, CancellationToken cancellationToken);

    /// <summary>
    /// Event changes in one calendar since <paramref name="syncToken"/>, or every event when it's null.
    /// Repeating events come back as series plus changed or cancelled occurrences, not expanded.
    /// </summary>
    /// <exception cref="SyncTokenExpiredException">The token is too old; sync again without one.</exception>
    Task<EventChangeSet> GetEventChangesAsync(string calendarId, string? syncToken, CancellationToken cancellationToken);

    /// <summary>The provider's own expansion of a repeating event within a time range.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetOccurrencesAsync(
        string calendarId, string seriesId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Withdraws Dayflower's access to the account at the provider.</summary>
    Task RevokeAsync(CancellationToken cancellationToken);
}

public sealed record CalendarListChanges(
    bool IsFullSync,
    IReadOnlyList<CalendarRecord> Changed,
    IReadOnlyList<string> RemovedIds,
    string? NextSyncToken);

/// <summary>One changed event: either the new version with its raw payload, or a removal.</summary>
public sealed record EventChange(string EventId, CalendarEvent? Event, string? Payload)
{
    public bool IsRemoved => Event is null;
}

public sealed record EventChangeSet(bool IsFullSync, IReadOnlyList<EventChange> Changes, string? NextSyncToken);

/// <summary>The provider no longer accepts the account's saved sign-in.</summary>
public sealed class AccountSignInRequiredException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The sync token is too old; the client has to drop its copy and sync everything again.</summary>
public sealed class SyncTokenExpiredException(string message) : Exception(message);

/// <summary>The user didn't grant every permission Dayflower needs.</summary>
public sealed class MissingPermissionsException(IReadOnlyList<string> missing)
    : Exception("Missing permissions: " + string.Join(", ", missing))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}
