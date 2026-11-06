// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dayflower.Calendar;
using Dayflower.Core.Reminders;
using Dayflower.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Dayflower.Services;

/// <summary>
/// Shows reminder notifications. Plans the next couple of days from the cache after every sync, then checks every
/// ten seconds what's due, which also catches up after sleep: a reminder missed while the PC was off or asleep is
/// shown if its event hasn't ended. What has been shown, and snoozes, are remembered across restarts.
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    private readonly CalendarDataService _data;
    private readonly AppServices _services;
    private readonly AppSettings _settings;
    private readonly NotificationService _notifications;
    private readonly JoinService _join;
    private readonly ILogger<ReminderScheduler> _logger;
    private readonly DispatcherQueueTimer _timer;
    private readonly ReminderState _state = ReminderState.Load();
    private IReadOnlyList<PlannedReminder> _plan = [];
    private Dictionary<string, CalendarItem> _items = [];
    private int _replanning;

    public ReminderScheduler(AppServices services, AppSettings settings, NotificationService notifications, JoinService join, DispatcherQueue ui)
    {
        _services = services;
        _settings = settings;
        _notifications = notifications;
        _join = join;
        _data = new CalendarDataService(services);
        _logger = services.Loggers.CreateLogger<ReminderScheduler>();
        _timer = ui.CreateTimer();
        _timer.Interval = CheckInterval;
        _timer.Tick += (_, _) => CheckDue();
    }

    public async Task StartAsync()
    {
        await ReplanAsync();
        _timer.Start();
        CheckDue();
    }

    /// <summary>Recomputes the reminders for yesterday to three days ahead from the cache.</summary>
    public async Task ReplanAsync()
    {
        if (Interlocked.Exchange(ref _replanning, 1) == 1)
        {
            return;
        }

        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var items = await Task.Run(() => _data.LoadAsync(today.AddDays(-1), today.AddDays(3)));
            var defaults = (await _services.Store.GetCalendarsAsync()).ToDictionary(c => (c.AccountId, c.Id), c => c.DefaultReminders);
            var byOccurrence = items.GroupBy(i => ReminderPlanner.OccurrenceKey(i.Merged)).ToDictionary(g => g.Key, g => g.First());

            _plan = ReminderPlanner.Plan(
                items.Select(i => i.Merged),
                (account, calendar) => defaults.GetValueOrDefault((account, calendar)) ?? [],
                m => byOccurrence.TryGetValue(ReminderPlanner.OccurrenceKey(m), out var item) && item.Meeting is not null,
                _settings.StartingNowAlert,
                TimeZoneInfo.Local);
            _items = byOccurrence.ToDictionary(kv => IdOf(kv.Key), kv => kv.Value);

            var next = _plan.FirstOrDefault(r => r.FireAt > DateTimeOffset.Now);
            _logger.LogInformation("Planned {Count} reminders; next at {Next}", _plan.Count, next?.FireAt.ToLocalTime().ToString("g") ?? "none");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Planning reminders failed");
        }
        finally
        {
            Interlocked.Exchange(ref _replanning, 0);
        }
    }

    public async Task HandleAsync(NotificationAction action)
    {
        if (action.Id is null || !_items.TryGetValue(action.Id, out var item))
        {
            return;
        }

        switch (action.Action)
        {
            case "join":
                await _join.JoinAsync(item);
                break;
            case "snooze":
                var until = action.SnoozeChoice == "start" && item.Span.Start > DateTime.Now.AddMinutes(1)
                    ? new DateTimeOffset(item.Span.Start)
                    : DateTimeOffset.Now.AddMinutes(int.TryParse(action.SnoozeChoice, out int minutes) ? minutes : 5);
                _state.Snoozed[action.Id] = until;
                _state.Save();
                _logger.LogInformation("Snoozed {Id} until {Until}", action.Id, until.ToString("t"));
                break;
        }
    }

    /// <summary>The event behind a notification, e.g. to show it when the notification itself is clicked.</summary>
    public CalendarItem? Find(string? id) => id is not null && _items.TryGetValue(id, out var item) ? item : null;

    public void Dispose() => _timer.Stop();

    private void CheckDue()
    {
        var now = DateTimeOffset.Now;
        bool changed = false;
        var shownNow = new HashSet<string>();

        foreach (var reminder in ReminderPlanner.Due(_plan, _state.ShownKeys, now))
        {
            // Everything due for this occurrence counts as shown; only the latest becomes a notification.
            foreach (var r in _plan.Where(r => r.OccurrenceKey == reminder.OccurrenceKey && r.FireAt <= now))
            {
                _state.Shown[r.Key] = r.FireAt;
            }

            // A new reminder replaces a snoozed one for the same event.
            string id = IdOf(reminder.OccurrenceKey);
            _state.Snoozed.Remove(id);
            if (_items.TryGetValue(id, out var item))
            {
                Show(id, item, reminder.Kind == ReminderKind.StartingNow, now);
                shownNow.Add(id);
            }

            changed = true;
        }

        foreach (var (id, until) in _state.Snoozed.Where(s => s.Value <= now).ToList())
        {
            _state.Snoozed.Remove(id);
            if (!shownNow.Contains(id) && _items.TryGetValue(id, out var item) && item.Span.End > DateTime.Now)
            {
                Show(id, item, startingNow: false, now);
            }

            changed = true;
        }

        if (changed)
        {
            _state.Save();
        }
    }

    private void Show(string id, CalendarItem item, bool startingNow, DateTimeOffset now)
    {
        try
        {
            _notifications.ShowReminder(new ReminderToast(
                id,
                item.Title,
                When(item, startingNow, now.LocalDateTime),
                !string.IsNullOrWhiteSpace(item.Event.Location) && !item.Event.Location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? item.Event.Location.Trim()
                    : item.Meeting?.ProviderName,
                item.Meeting?.JoinLabel,
                _settings.KeepRemindersOnScreen,
                OfferSnooze: item.Span.End > now.LocalDateTime.AddMinutes(1)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Showing a reminder failed");
        }
    }

    private static string When(CalendarItem item, bool startingNow, DateTime now)
    {
        var span = item.Span;
        if (span.IsAllDayLike)
        {
            var day = span.FirstDay;
            var today = DateOnly.FromDateTime(now);
            string which = day == today ? "Today" : day == today.AddDays(1) ? "Tomorrow" : TimeText.LongDate(day);
            return $"{which} · All day";
        }

        string range = TimeText.TimeRange(span.Start, span.End);
        double minutes = (span.Start - now).TotalMinutes;
        if (startingNow || Math.Abs(minutes) < 1)
        {
            return $"Starting now · {range}";
        }

        if (minutes > 0)
        {
            int m = (int)Math.Round(minutes);
            string inText = m < 60 ? $"In {m} {(m == 1 ? "minute" : "minutes")}"
                : DateOnly.FromDateTime(span.Start) == DateOnly.FromDateTime(now) ? "Later today"
                : DateOnly.FromDateTime(span.Start) == DateOnly.FromDateTime(now).AddDays(1) ? "Tomorrow"
                : TimeText.LongDate(DateOnly.FromDateTime(span.Start));
            return $"{inText} · {range}";
        }

        int ago = (int)Math.Round(-minutes);
        return $"Started {ago} {(ago == 1 ? "minute" : "minutes")} ago · {range}";
    }

    /// <summary>A short, stable ID for an occurrence, for notification tags and arguments.</summary>
    private static string IdOf(string occurrenceKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(occurrenceKey)))[..16];

    /// <summary>Which reminders were shown, and snoozes; kept in reminders.json so restarts don't repeat them.</summary>
    private sealed class ReminderState
    {
        private static readonly TimeSpan KeepFor = TimeSpan.FromDays(3);

        public Dictionary<string, DateTimeOffset> Shown { get; set; } = [];

        public Dictionary<string, DateTimeOffset> Snoozed { get; set; } = [];

        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlySet<string> ShownKeys => Shown.Keys.ToHashSet();

        private static string FilePath => Path.Combine(AppPaths.DataFolder, "reminders.json");

        public static ReminderState Load()
        {
            try
            {
                var state = File.Exists(FilePath) ? JsonSerializer.Deserialize<ReminderState>(File.ReadAllText(FilePath)) : null;
                return state ?? new ReminderState();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new ReminderState();
            }
        }

        public void Save()
        {
            var cutoff = DateTimeOffset.Now - KeepFor;
            foreach (var old in Shown.Where(s => s.Value < cutoff).Select(s => s.Key).ToList())
            {
                Shown.Remove(old);
            }

            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch (IOException)
            {
                // Not fatal: at worst a reminder is repeated after a restart.
            }
        }
    }
}
