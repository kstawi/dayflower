// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;

namespace Dayflower.Core.Display;

/// <summary>The time somewhere else: the wall-clock time there, how far ahead or behind it is, and on which day.</summary>
/// <param name="Difference">Their offset from UTC minus ours: positive when they're ahead.</param>
/// <param name="DayOffset">+1 when it's already tomorrow there, −1 when it's still yesterday.</param>
public sealed record WorldClockReading(DateTime Time, TimeSpan Difference, int DayOffset);

/// <summary>The world clock in the side pane: other time zones compared with Windows' own, daylight saving included.</summary>
public static class WorldClock
{
    public static WorldClockReading Read(DateTimeOffset now, TimeZoneInfo zone, TimeZoneInfo home)
    {
        var there = TimeZoneInfo.ConvertTime(now, zone);
        var here = TimeZoneInfo.ConvertTime(now, home);
        int days = DateOnly.FromDateTime(there.DateTime).DayNumber - DateOnly.FromDateTime(here.DateTime).DayNumber;
        return new WorldClockReading(there.DateTime, there.Offset - here.Offset, Math.Sign(days));
    }

    /// <summary>"+6h", "−6h", "+5:30", or "Same time".</summary>
    public static string FormatDifference(TimeSpan difference)
    {
        if (difference == TimeSpan.Zero)
        {
            return "Same time";
        }

        string sign = difference > TimeSpan.Zero ? "+" : "−";
        var size = difference.Duration();
        return size.Minutes == 0
            ? $"{sign}{(int)size.TotalHours}h"
            : $"{sign}{(int)size.TotalHours}:{size.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"tomorrow", "yesterday", or null on the same day.</summary>
    public static string? FormatDay(int dayOffset) => dayOffset switch
    {
        > 0 => "tomorrow",
        < 0 => "yesterday",
        _ => null,
    };
}

/// <summary>A city to pick for the world clock, with its IANA time zone.</summary>
public sealed record City(string Name, string Country, string ZoneId);

/// <summary>Cities people look up most, for the world clock's search; any time zone can be found by name too.</summary>
public static class Cities
{
    public static IReadOnlyList<City> All { get; } =
    [
        new("New York", "United States", "America/New_York"), new("Boston", "United States", "America/New_York"),
        new("Washington", "United States", "America/New_York"), new("Miami", "United States", "America/New_York"),
        new("Atlanta", "United States", "America/New_York"), new("Detroit", "United States", "America/Detroit"),
        new("Chicago", "United States", "America/Chicago"), new("Dallas", "United States", "America/Chicago"),
        new("Houston", "United States", "America/Chicago"), new("Austin", "United States", "America/Chicago"),
        new("Minneapolis", "United States", "America/Chicago"), new("Denver", "United States", "America/Denver"),
        new("Salt Lake City", "United States", "America/Denver"), new("Phoenix", "United States", "America/Phoenix"),
        new("Los Angeles", "United States", "America/Los_Angeles"), new("San Francisco", "United States", "America/Los_Angeles"),
        new("San Diego", "United States", "America/Los_Angeles"), new("Seattle", "United States", "America/Los_Angeles"),
        new("Portland", "United States", "America/Los_Angeles"), new("Las Vegas", "United States", "America/Los_Angeles"),
        new("Anchorage", "United States", "America/Anchorage"), new("Honolulu", "United States", "Pacific/Honolulu"),
        new("Toronto", "Canada", "America/Toronto"), new("Montreal", "Canada", "America/Toronto"),
        new("Ottawa", "Canada", "America/Toronto"), new("Vancouver", "Canada", "America/Vancouver"),
        new("Calgary", "Canada", "America/Edmonton"), new("Halifax", "Canada", "America/Halifax"),
        new("Mexico City", "Mexico", "America/Mexico_City"), new("Bogotá", "Colombia", "America/Bogota"),
        new("Lima", "Peru", "America/Lima"), new("Santiago", "Chile", "America/Santiago"),
        new("Buenos Aires", "Argentina", "America/Argentina/Buenos_Aires"), new("São Paulo", "Brazil", "America/Sao_Paulo"),
        new("Rio de Janeiro", "Brazil", "America/Sao_Paulo"), new("Caracas", "Venezuela", "America/Caracas"),
        new("London", "United Kingdom", "Europe/London"), new("Manchester", "United Kingdom", "Europe/London"),
        new("Edinburgh", "United Kingdom", "Europe/London"), new("Dublin", "Ireland", "Europe/Dublin"),
        new("Lisbon", "Portugal", "Europe/Lisbon"), new("Reykjavík", "Iceland", "Atlantic/Reykjavik"),
        new("Paris", "France", "Europe/Paris"), new("Berlin", "Germany", "Europe/Berlin"),
        new("Munich", "Germany", "Europe/Berlin"), new("Frankfurt", "Germany", "Europe/Berlin"),
        new("Hamburg", "Germany", "Europe/Berlin"), new("Amsterdam", "Netherlands", "Europe/Amsterdam"),
        new("Brussels", "Belgium", "Europe/Brussels"), new("Luxembourg", "Luxembourg", "Europe/Luxembourg"),
        new("Zurich", "Switzerland", "Europe/Zurich"), new("Geneva", "Switzerland", "Europe/Zurich"),
        new("Vienna", "Austria", "Europe/Vienna"), new("Madrid", "Spain", "Europe/Madrid"),
        new("Barcelona", "Spain", "Europe/Madrid"), new("Rome", "Italy", "Europe/Rome"),
        new("Milan", "Italy", "Europe/Rome"), new("Copenhagen", "Denmark", "Europe/Copenhagen"),
        new("Oslo", "Norway", "Europe/Oslo"), new("Stockholm", "Sweden", "Europe/Stockholm"),
        new("Helsinki", "Finland", "Europe/Helsinki"), new("Tallinn", "Estonia", "Europe/Tallinn"),
        new("Riga", "Latvia", "Europe/Riga"), new("Vilnius", "Lithuania", "Europe/Vilnius"),
        new("Warsaw", "Poland", "Europe/Warsaw"), new("Kraków", "Poland", "Europe/Warsaw"),
        new("Wrocław", "Poland", "Europe/Warsaw"), new("Gdańsk", "Poland", "Europe/Warsaw"),
        new("Poznań", "Poland", "Europe/Warsaw"), new("Łódź", "Poland", "Europe/Warsaw"),
        new("Prague", "Czechia", "Europe/Prague"), new("Bratislava", "Slovakia", "Europe/Bratislava"),
        new("Budapest", "Hungary", "Europe/Budapest"), new("Ljubljana", "Slovenia", "Europe/Ljubljana"),
        new("Zagreb", "Croatia", "Europe/Zagreb"), new("Belgrade", "Serbia", "Europe/Belgrade"),
        new("Bucharest", "Romania", "Europe/Bucharest"), new("Sofia", "Bulgaria", "Europe/Sofia"),
        new("Athens", "Greece", "Europe/Athens"), new("Istanbul", "Türkiye", "Europe/Istanbul"),
        new("Kyiv", "Ukraine", "Europe/Kyiv"), new("Lviv", "Ukraine", "Europe/Kyiv"),
        new("Minsk", "Belarus", "Europe/Minsk"), new("Moscow", "Russia", "Europe/Moscow"),
        new("Tel Aviv", "Israel", "Asia/Jerusalem"), new("Cairo", "Egypt", "Africa/Cairo"),
        new("Lagos", "Nigeria", "Africa/Lagos"), new("Nairobi", "Kenya", "Africa/Nairobi"),
        new("Johannesburg", "South Africa", "Africa/Johannesburg"), new("Cape Town", "South Africa", "Africa/Johannesburg"),
        new("Casablanca", "Morocco", "Africa/Casablanca"), new("Dubai", "United Arab Emirates", "Asia/Dubai"),
        new("Abu Dhabi", "United Arab Emirates", "Asia/Dubai"), new("Riyadh", "Saudi Arabia", "Asia/Riyadh"),
        new("Doha", "Qatar", "Asia/Qatar"), new("Tehran", "Iran", "Asia/Tehran"),
        new("Karachi", "Pakistan", "Asia/Karachi"), new("Mumbai", "India", "Asia/Kolkata"),
        new("Delhi", "India", "Asia/Kolkata"), new("Bengaluru", "India", "Asia/Kolkata"),
        new("Hyderabad", "India", "Asia/Kolkata"), new("Chennai", "India", "Asia/Kolkata"),
        new("Kolkata", "India", "Asia/Kolkata"), new("Colombo", "Sri Lanka", "Asia/Colombo"),
        new("Kathmandu", "Nepal", "Asia/Kathmandu"), new("Dhaka", "Bangladesh", "Asia/Dhaka"),
        new("Bangkok", "Thailand", "Asia/Bangkok"), new("Ho Chi Minh City", "Vietnam", "Asia/Ho_Chi_Minh"),
        new("Hanoi", "Vietnam", "Asia/Ho_Chi_Minh"), new("Jakarta", "Indonesia", "Asia/Jakarta"),
        new("Singapore", "Singapore", "Asia/Singapore"), new("Kuala Lumpur", "Malaysia", "Asia/Kuala_Lumpur"),
        new("Manila", "Philippines", "Asia/Manila"), new("Hong Kong", "China", "Asia/Hong_Kong"),
        new("Shanghai", "China", "Asia/Shanghai"), new("Beijing", "China", "Asia/Shanghai"),
        new("Shenzhen", "China", "Asia/Shanghai"), new("Taipei", "Taiwan", "Asia/Taipei"),
        new("Seoul", "South Korea", "Asia/Seoul"), new("Tokyo", "Japan", "Asia/Tokyo"),
        new("Osaka", "Japan", "Asia/Tokyo"), new("Perth", "Australia", "Australia/Perth"),
        new("Adelaide", "Australia", "Australia/Adelaide"), new("Darwin", "Australia", "Australia/Darwin"),
        new("Brisbane", "Australia", "Australia/Brisbane"), new("Sydney", "Australia", "Australia/Sydney"),
        new("Melbourne", "Australia", "Australia/Melbourne"), new("Hobart", "Australia", "Australia/Hobart"),
        new("Auckland", "New Zealand", "Pacific/Auckland"), new("Wellington", "New Zealand", "Pacific/Auckland"),
        new("UTC", "Coordinated Universal Time", "Etc/UTC"),
    ];

    /// <summary>Cities whose name or country starts with the query's words (accents ignored), best matches first.</summary>
    public static IReadOnlyList<City> Find(string query, int limit = 12)
    {
        var words = Search.EventSearchIndex.Words(query);
        if (words.Count == 0)
        {
            return [];
        }

        return All
            .Select(c => (City: c, Name: Search.EventSearchIndex.Fold(c.Name), Country: Search.EventSearchIndex.Fold(c.Country)))
            .Where(c => words.All(w => StartsAnyWord(c.Name, w) || StartsAnyWord(c.Country, w)))
            .OrderByDescending(c => c.Name.StartsWith(words[0], StringComparison.Ordinal))
            .ThenBy(c => c.City.Name, StringComparer.CurrentCulture)
            .Take(limit)
            .Select(c => c.City)
            .ToList();
    }

    private static bool StartsAnyWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal) || text.Contains(" " + word, StringComparison.Ordinal);
}
