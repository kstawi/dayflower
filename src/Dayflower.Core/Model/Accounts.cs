// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Model;

public enum AccountState
{
    Ok,

    /// <summary>The provider no longer accepts the saved sign-in; the user has to sign in again.</summary>
    NeedsSignIn,
}

/// <summary>A signed-in account, e.g. one Google account.</summary>
/// <param name="Id">Stable ID: provider plus the provider's user ID, e.g. "google:1234567890".</param>
public sealed record AccountRecord(
    string Id,
    string ProviderId,
    string Email,
    string? DisplayName,
    int SortOrder,
    AccountState State,
    DateTimeOffset? LastSyncedUtc);

public enum CalendarAccess
{
    FreeBusyReader,
    Reader,
    Writer,
    Owner,
}

/// <summary>One calendar in an account's calendar list.</summary>
public sealed record CalendarRecord(
    string AccountId,
    string Id,
    string Name,
    string? TimeZone,
    string? BackgroundColor,
    string? ForegroundColor,
    CalendarAccess Access,
    bool IsPrimary,
    bool IsVisible,
    IReadOnlyList<Reminder> DefaultReminders);
