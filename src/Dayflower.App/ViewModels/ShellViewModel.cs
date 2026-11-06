// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dayflower.Core.Calendar;

namespace Dayflower.ViewModels;

/// <summary>State of the main window: which view is shown, for which dates, and the heading above it.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PeriodTitleFormatter _titles;
    private readonly Func<DateOnly> _today;

    public ShellViewModel(PeriodTitleFormatter titles, Func<DateOnly> today, DayOfWeek firstDayOfWeek)
    {
        _titles = titles;
        _today = today;
        Period = new CalendarPeriod(CalendarViewKind.Week, today(), firstDayOfWeek);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial CalendarPeriod Period { get; set; }

    public string Title => _titles.Format(Period);

    public void ShowDate(DateOnly date) => Period = Period.WithAnchor(date);

    public void ShowView(CalendarViewKind kind) => Period = Period.WithKind(kind);

    /// <summary>Opens the day view on <paramref name="date"/>, e.g. from a day heading or "+3 more".</summary>
    public void ShowDay(DateOnly date) => Period = new CalendarPeriod(CalendarViewKind.Day, date, Period.FirstDayOfWeek);

    [RelayCommand]
    private void GoToToday() => ShowDate(_today());

    [RelayCommand]
    private void GoToPrevious() => Period = Period.Previous();

    [RelayCommand]
    private void GoToNext() => Period = Period.Next();
}
