// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Meetings;
using Dayflower.Core.Model;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class MeetingLinkDetectorTests
{
    private static CalendarEvent Event(string? location = null, string? description = null, string? hangout = null, ConferenceInfo? conference = null) =>
        Timed("e", "2026-11-06T10:00:00+01:00", "2026-11-06T11:00:00+01:00") with
        {
            Location = location,
            Description = description,
            HangoutLink = hangout,
            Conference = conference,
        };

    private static ConferenceInfo Conference(string? solution, params (string Type, string Uri)[] entryPoints) =>
        new(solution, entryPoints.Select(p => new ConferenceEntryPoint(p.Type, p.Uri, null)).ToList());

    [Fact]
    public void Google_meet_from_conference_data()
    {
        var link = MeetingLinkDetector.Find(Event(
            hangout: "https://meet.google.com/abc-defg-hij",
            conference: Conference("Google Meet", ("video", "https://meet.google.com/abc-defg-hij"), ("phone", "tel:+48-22-000-0000"))));

        Assert.Equal(MeetingProvider.GoogleMeet, link!.Provider);
        Assert.Equal("https://meet.google.com/abc-defg-hij", link.Uri.ToString());
        Assert.Equal("Join Google Meet", link.JoinLabel);
        Assert.Equal("meet.google.com/abc-defg-hij", link.DisplayText);
    }

    [Fact]
    public void Zoom_added_through_its_google_calendar_add_on()
    {
        var link = MeetingLinkDetector.Find(Event(conference: Conference(
            "Zoom Meeting", ("video", "https://us02web.zoom.us/j/81234567890?pwd=aBcDeF123"), ("phone", "tel:+12063379723,,81234567890#"))));

        Assert.Equal(MeetingProvider.Zoom, link!.Provider);
        Assert.Equal("https://us02web.zoom.us/j/81234567890?pwd=aBcDeF123", link.Uri.ToString());
        Assert.Equal("Join Zoom", link.JoinLabel);
    }

    [Fact]
    public void Unknown_service_in_conference_data_still_counts()
    {
        var link = MeetingLinkDetector.Find(Event(conference: Conference("Acme Video", ("video", "https://video.acme.example/r/123"))));

        Assert.Equal(MeetingProvider.Other, link!.Provider);
        Assert.Equal("Join video call", link.JoinLabel);
    }

    [Fact]
    public void Conference_data_with_only_a_phone_number_falls_back_to_the_text()
    {
        var link = MeetingLinkDetector.Find(Event(
            location: "https://acme.zoom.us/j/99887766554",
            conference: Conference("Phone", ("phone", "tel:+1-555-0100"))));

        Assert.Equal(MeetingProvider.Zoom, link!.Provider);
    }

    [Fact]
    public void Zoom_link_in_the_location()
    {
        var link = MeetingLinkDetector.Find(Event(location: "https://us06web.zoom.us/j/84567890123?pwd=XyZ.1"));

        Assert.Equal("https://us06web.zoom.us/j/84567890123?pwd=XyZ.1", link!.Uri.ToString());
    }

    [Fact]
    public void Teams_invite_written_by_outlook()
    {
        const string description =
            "________________________________________________________________________________\n" +
            "Microsoft Teams meeting\nJoin on your computer, mobile app or room device\n" +
            "Click here to join the meeting<https://teams.microsoft.com/l/meetup-join/19%3ameeting_ZjQ1NmE4%40thread.v2/0?context=%7b%22Tid%22%3a%22a1b2%22%2c%22Oid%22%3a%22c3d4%22%7d>\n" +
            "Meeting ID: 312 456 789 012\nPasscode: Ab3cDe\n" +
            "Download Teams<https://www.microsoft.com/en-us/microsoft-teams/download-app> | Join on the web<https://www.microsoft.com/microsoft-teams/join-a-meeting>\n" +
            "Learn More<https://aka.ms/JoinTeamsMeeting> | Meeting options<https://teams.microsoft.com/meetingOptions/?organizerId=c3d4>";

        var link = MeetingLinkDetector.Find(Event(description: description));

        Assert.Equal(MeetingProvider.MicrosoftTeams, link!.Provider);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/19%3ameeting_ZjQ1NmE4%40thread.v2/0?context=", link.Uri.OriginalString);
        Assert.Equal("Join Teams", link.JoinLabel);
    }

    [Theory]
    [InlineData("https://teams.microsoft.com/meet/2345678901234?p=AbCdEfGh")]
    [InlineData("https://teams.live.com/meet/9876543210987?p=xYz123")]
    public void Newer_teams_links(string url)
    {
        Assert.Equal(MeetingProvider.MicrosoftTeams, MeetingLinkDetector.Find(Event(description: "Join: " + url))!.Provider);
    }

    [Fact]
    public void Link_wrapped_by_outlook_safe_links_is_unwrapped()
    {
        const string wrapped = "https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fzoom.us%2Fj%2F96123456789%3Fpwd%3Dabc&data=05%7C01%7C&reserved=0";

        var link = MeetingLinkDetector.Find(Event(description: wrapped));

        Assert.Equal("https://zoom.us/j/96123456789?pwd=abc", link!.Uri.ToString());
    }

    [Fact]
    public void Link_wrapped_by_google_redirect_is_unwrapped()
    {
        const string html = "<a href=\"https://www.google.com/url?q=https%3A%2F%2Fmeet.google.com%2Fxyz-abcd-efg&amp;sa=D&amp;source=calendar\">Join</a>";

        var link = MeetingLinkDetector.Find(Event(description: html));

        Assert.Equal("https://meet.google.com/xyz-abcd-efg", link!.Uri.ToString());
    }

    [Theory]
    [InlineData("https://acme.webex.com/acme/j.php?MTID=m1234abcd5678", MeetingProvider.Webex)]
    [InlineData("https://acme.webex.com/meet/jdoe", MeetingProvider.Webex)]
    [InlineData("https://app.slack.com/huddle/T012AB3C4/C012AB3C4", MeetingProvider.SlackHuddle)]
    [InlineData("https://whereby.com/team-standup", MeetingProvider.Whereby)]
    [InlineData("https://meet.goto.com/123456789", MeetingProvider.GoToMeeting)]
    [InlineData("https://global.gotomeeting.com/join/123456789", MeetingProvider.GoToMeeting)]
    [InlineData("https://meet.jit.si/DayflowerStandup", MeetingProvider.Jitsi)]
    [InlineData("https://chime.aws/1234567890", MeetingProvider.AmazonChime)]
    [InlineData("https://zoom.us/my/jdoe", MeetingProvider.Zoom)]
    [InlineData("https://acme.zoomgov.com/j/1612345678", MeetingProvider.Zoom)]
    public void Other_meeting_services(string url, MeetingProvider provider)
    {
        Assert.Equal(provider, MeetingLinkDetector.Find(Event(location: url))!.Provider);
    }

    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/1tG99QD/edit?usp=sharing")]
    [InlineData("https://zoom.us/")]
    [InlineData("https://zoom.us/download")]
    [InlineData("https://teams.microsoft.com/")]
    [InlineData("https://www.microsoft.com/microsoft-teams/join-a-meeting")]
    [InlineData("https://meet.google.com/")]
    [InlineData("https://meet.google.com/new")]
    [InlineData("https://maps.google.com/?q=Warsaw")]
    [InlineData("Room 4, 2nd floor")]
    [InlineData("tel:+48-22-000-0000")]
    public void Things_that_arent_meetings(string text)
    {
        Assert.Null(MeetingLinkDetector.Find(Event(location: text, description: text)));
    }

    [Fact]
    public void Conference_data_beats_links_in_the_text()
    {
        var link = MeetingLinkDetector.Find(Event(
            description: "Backup: https://zoom.us/j/111",
            conference: Conference("Google Meet", ("video", "https://meet.google.com/abc-defg-hij"))));

        Assert.Equal(MeetingProvider.GoogleMeet, link!.Provider);
    }

    [Fact]
    public void Location_beats_description()
    {
        var link = MeetingLinkDetector.Find(Event(location: "https://zoom.us/j/222", description: "https://meet.jit.si/Other"));

        Assert.Equal(MeetingProvider.Zoom, link!.Provider);
    }

    [Fact]
    public void First_meeting_link_in_the_description_wins_and_trailing_punctuation_is_dropped()
    {
        var link = MeetingLinkDetector.Find(Event(description:
            "Agenda: https://docs.google.com/document/d/abc. Join here (https://zoom.us/j/333444555). Fallback https://meet.jit.si/X"));

        Assert.Equal("https://zoom.us/j/333444555", link!.Uri.ToString());
    }

    [Fact]
    public void Meet_opens_as_the_given_account()
    {
        var link = MeetingLinkDetector.Recognize("https://meet.google.com/abc-defg-hij?hs=122&authuser=0")!;

        Assert.Equal("https://meet.google.com/abc-defg-hij?hs=122&authuser=kacper%40example.com", link.ForAccount("kacper@example.com").ToString());
        Assert.Equal(link.Uri, link.ForAccount(null));
    }

    [Fact]
    public void Other_services_ignore_the_account()
    {
        var link = MeetingLinkDetector.Recognize("https://zoom.us/j/123?pwd=x")!;

        Assert.Equal(link.Uri, link.ForAccount("kacper@example.com"));
    }

    [Fact]
    public void Non_web_links_are_never_opened()
    {
        Assert.Null(MeetingLinkDetector.Recognize("javascript:alert(1)"));
        Assert.Null(MeetingLinkDetector.Recognize("file:///C:/Windows/notepad.exe"));
        Assert.Null(MeetingLinkDetector.Find(Event(conference: Conference("Evil", ("video", "file:///C:/x.exe")))));
    }
}
