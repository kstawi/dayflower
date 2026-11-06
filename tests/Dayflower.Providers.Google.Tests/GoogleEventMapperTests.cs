// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text.Json;
using Dayflower.Core.Model;
using Dayflower.Providers.Google.Mapping;

namespace Dayflower.Providers.Google.Tests;

public class GoogleEventMapperTests
{
    private static CalendarEvent Event(string id)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "events.json")));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);
        return GoogleEventMapper.Map(item, "google:1", "me@example.com");
    }

    [Fact]
    public void Meeting_with_guests_and_meet()
    {
        var e = Event("meeting");

        Assert.Equal(new EventKey("google:1", "me@example.com", "meeting"), e.Key);
        Assert.Equal("meeting@google.com", e.ICalUid);
        Assert.Equal("Design review", e.Summary);
        Assert.Equal("Room 4", e.Location);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T09:00:00Z"), e.Start.DateTime);
        Assert.Equal("Europe/Warsaw", e.Start.TimeZone);
        Assert.Equal(TimeSpan.FromHours(1), e.End.DateTime - e.Start.DateTime);
        Assert.Equal("5", e.ColorId);
        Assert.True(e.IsBusy);
        Assert.Equal(EventKind.Default, e.Kind);

        Assert.Equal("Boss", e.Organizer!.DisplayName);
        Assert.Equal(4, e.Attendees.Count);
        Assert.Equal(AttendeeResponse.NeedsAction, e.Self!.Response);
        Assert.True(Assert.Single(e.Attendees, a => a.IsResource).Response == AttendeeResponse.Accepted);
        var ana = Assert.Single(e.Attendees, a => a.Email == "ana@example.com");
        Assert.True(ana.IsOptional);
        Assert.Equal("Might be late", ana.Comment);

        Assert.Equal("https://meet.google.com/abc-defg-hij", e.HangoutLink);
        Assert.Equal("Google Meet", e.Conference!.SolutionName);
        Assert.Equal(["video", "phone"], e.Conference.EntryPoints.Select(p => p.Type));

        Assert.False(e.Reminders.UseDefault);
        Assert.Equal([new Reminder(ReminderMethod.Popup, 5), new Reminder(ReminderMethod.Email, 60)], e.Reminders.Overrides);
    }

    [Fact]
    public void All_day_event_shown_as_free()
    {
        var e = Event("allday");

        Assert.True(e.Start.IsAllDay);
        Assert.Equal(new DateOnly(2026, 11, 9), e.Start.Date);
        Assert.Equal(new DateOnly(2026, 11, 11), e.End.Date);
        Assert.False(e.IsBusy);
        Assert.True(e.Reminders.UseDefault);
    }

    [Fact]
    public void Series_keeps_its_recurrence_lines()
    {
        var e = Event("series");

        Assert.True(e.IsSeries);
        Assert.Equal(["EXDATE;TZID=Europe/Warsaw:20261125T090000", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE"], e.Recurrence);
    }

    [Fact]
    public void Changed_occurrence_points_to_its_series()
    {
        var e = Event("series_20261111T080000Z");

        Assert.True(e.IsOccurrenceOverride);
        Assert.Equal("series", e.RecurringEventId);
        Assert.Equal("2026-11-11T08:00:00Z", e.OriginalStart!.OccurrenceKey);
        Assert.Equal(DateTimeOffset.Parse("2026-11-11T10:00:00Z"), e.Start.DateTime);
    }

    [Fact]
    public void Cancelled_occurrence_with_only_its_original_start()
    {
        var e = Event("series_20261118T080000Z");

        Assert.Equal(EventStatus.Cancelled, e.Status);
        Assert.Equal("series", e.RecurringEventId);
        Assert.Equal(e.OriginalStart, e.Start);
        Assert.Equal(e.Start, e.End);
    }

    [Fact]
    public void Out_of_office_is_private_and_its_own_kind()
    {
        var e = Event("ooo");

        Assert.Equal(EventKind.OutOfOffice, e.Kind);
        Assert.Equal(EventVisibility.Private, e.Visibility);
    }
}
