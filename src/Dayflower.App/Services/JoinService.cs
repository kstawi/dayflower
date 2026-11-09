// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Microsoft.Extensions.Logging;

namespace Dayflower.Services;

/// <summary>Opens an event's meeting in the default browser, as the right Google account for Google Meet.</summary>
public sealed class JoinService(ILogger<JoinService> logger)
{
    /// <param name="accountEmail">The account to join as; the event's primary copy's account when null.</param>
    public async Task JoinAsync(CalendarItem item, string? accountEmail = null)
    {
        if (item.Meeting is not { } meeting)
        {
            return;
        }

        string? account = accountEmail ?? item.Accounts.FirstOrDefault();
        logger.LogInformation("Joining {Provider} for event {Event}{As}", meeting.Provider, item.Event.Key.EventId,
                              meeting.Provider == Core.Meetings.MeetingProvider.GoogleMeet && account is not null ? " as " + account : string.Empty);
        try
        {
            await AppServices.OpenInBrowserAsync(meeting.ForAccount(account));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't open the meeting link");
        }
    }
}
