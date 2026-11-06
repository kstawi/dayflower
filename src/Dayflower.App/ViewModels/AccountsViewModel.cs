// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Sync;
using Dayflower.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace Dayflower.ViewModels;

/// <summary>The accounts and calendars in the side pane, adding and removing accounts, and sync status.</summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly AppServices _services;
    private readonly ILogger<AccountsViewModel> _logger;
    private CancellationTokenSource? _signIn;

    public AccountsViewModel(AppServices services)
    {
        _services = services;
        _logger = services.Loggers.CreateLogger<AccountsViewModel>();
        services.Sync.Started += (_, _) => SyncStatus = "Syncing…";
        services.Sync.Completed += async (_, report) => await OnSyncCompletedAsync(report);
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAccounts))]
    public partial bool HasAccounts { get; set; }

    public bool HasNoAccounts => !HasAccounts;

    [ObservableProperty]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    public partial bool HasMessage { get; set; }

    [ObservableProperty]
    public partial string? MessageTitle { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    [ObservableProperty]
    public partial string SyncStatus { get; set; } = string.Empty;

    /// <summary>Headline for the main area until the calendar views exist.</summary>
    [ObservableProperty]
    public partial string CacheTitle { get; set; } = "Add a Google account to get started";

    [ObservableProperty]
    public partial string CacheDetail { get; set; } = "Your events will show here.";

    /// <summary>Asks the user to confirm; set by the window, which owns dialogs. Arguments: title, text, button.</summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Raised when accounts or calendars change, or a calendar is shown or hidden.</summary>
    public event EventHandler? CalendarsChanged;

    public async Task LoadAsync()
    {
        var accounts = await _services.Store.GetAccountsAsync();
        var calendars = await _services.Store.GetCalendarsAsync();

        // Rebuild the list only when something visible in it changed, so a sync every two minutes doesn't redraw
        // the pane under the user's pointer. Show/hide is left out: the checkboxes already show it.
        string signature = string.Join('|', accounts.Select(a => $"{a.Id}:{a.State}")
            .Concat(calendars.Select(c => $"{c.AccountId}/{c.Id}/{c.Name}/{c.BackgroundColor}")));
        if (signature != _signature)
        {
            _signature = signature;
            Accounts.Clear();
            foreach (var account in accounts)
            {
                var item = new AccountItemViewModel(this, account);
                foreach (var calendar in calendars.Where(c => c.AccountId == account.Id))
                {
                    item.Calendars.Add(new CalendarItemViewModel(this, calendar));
                }

                Accounts.Add(item);
            }

            HasAccounts = Accounts.Count > 0;
            CalendarsChanged?.Invoke(this, EventArgs.Empty);
        }

        await UpdateCacheSummaryAsync();
    }

    private string? _signature;

    [RelayCommand]
    private Task AddAccountAsync() => SignInAsync(null);

    [RelayCommand]
    private void CancelSignIn() => _signIn?.Cancel();

    internal async Task SignInAsync(string? loginHint)
    {
        var google = _services.Google;
        if (!google.IsConfigured)
        {
            ShowMessage(InfoBarSeverity.Error, "Can't sign in",
                        "This build of Dayflower has no Google sign-in client. See docs/google-cloud-setup.md.");
            return;
        }

        HasMessage = false;
        IsSigningIn = true;
        _signIn = new CancellationTokenSource(SignInTimeout);
        try
        {
            var account = await google.SignInAsync(loginHint, _signIn.Token);
            var existing = Accounts.FirstOrDefault(a => a.Id == account.AccountId);
            await _services.Store.SaveAccountAsync(
                new AccountRecord(account.AccountId, google.Id, account.Email, account.DisplayName, 0, AccountState.Ok, null),
                _services.Protector.Protect(account.Secret));
            _logger.LogInformation("Signed in {Account}", account.AccountId);

            await LoadAsync();
            ShowMessage(InfoBarSeverity.Success, existing is null ? "Account added" : "Signed in again",
                        $"{account.Email}: downloading calendars…");
            await _services.Sync.SyncNowAsync();
        }
        catch (OperationCanceledException)
        {
            ShowMessage(InfoBarSeverity.Informational, "Sign-in cancelled", "Nothing was changed.");
        }
        catch (MissingPermissionsException)
        {
            ShowMessage(InfoBarSeverity.Warning, "Calendar access is needed",
                        "Dayflower needs to see your calendar list and events. Sign in again and leave those boxes ticked.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign-in failed");
            ShowMessage(InfoBarSeverity.Error, "Couldn't sign in", ex.Message);
        }
        finally
        {
            IsSigningIn = false;
            _signIn.Dispose();
            _signIn = null;
        }
    }

    internal async Task RemoveAsync(AccountItemViewModel account)
    {
        if (ConfirmAsync is not null && !await ConfirmAsync(
                $"Remove {account.Email}?",
                "Dayflower forgets this account and the events it saved on this PC. Nothing changes in your Google calendar.",
                "Remove"))
        {
            return;
        }

        // Withdraw Dayflower's access at Google too, but don't let a network problem keep the account here.
        try
        {
            var secret = await _services.Store.GetAccountSecretAsync(account.Id);
            if (secret is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _services.Google.Connect(account.Id, _services.Protector.Unprotect(secret)).RevokeAsync(timeout.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't revoke access for {Account}", account.Id);
        }

        await _services.Store.RemoveAccountAsync(account.Id);
        _logger.LogInformation("Removed {Account}", account.Id);
        await LoadAsync();
    }

    internal async Task SetCalendarVisibleAsync(CalendarItemViewModel calendar, bool visible)
    {
        await _services.Store.SetCalendarVisibleAsync(calendar.AccountId, calendar.Id, visible);
        CalendarsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnSyncCompletedAsync(SyncReport report)
    {
        await LoadAsync();
        string time = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
        SyncStatus = report switch
        {
            { Accounts.Count: 0 } => string.Empty,
            { IsOffline: true } => $"Offline. Showing events saved on this PC.",
            _ when report.Accounts.Any(a => a.Outcome is SyncOutcome.Failed or SyncOutcome.PartlySynced) =>
                $"Synced at {time}, with problems. See the log.",
            _ => $"Synced at {time}",
        };

        if (IsSigningIn is false && Message?.EndsWith("downloading calendars…", StringComparison.Ordinal) == true)
        {
            HasMessage = false;
        }
    }

    private async Task UpdateCacheSummaryAsync()
    {
        if (!HasAccounts)
        {
            CacheTitle = "Add a Google account to get started";
            CacheDetail = "Your events will show here.";
            return;
        }

        var stats = await _services.Store.GetStatsAsync();
        CacheTitle = string.Create(CultureInfo.CurrentCulture,
            $"{stats.Events:N0} {(stats.Events == 1 ? "event" : "events")} from {stats.Calendars} {(stats.Calendars == 1 ? "calendar" : "calendars")} saved on this PC");
        CacheDetail = "The day, week and month views come in the next version.";
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        MessageSeverity = severity;
        MessageTitle = title;
        Message = message;
        HasMessage = true;
    }
}

/// <summary>One account in the side pane.</summary>
public sealed partial class AccountItemViewModel(AccountsViewModel owner, AccountRecord account) : ObservableObject
{
    public string Id { get; } = account.Id;

    public string Email { get; } = account.Email;

    public bool NeedsSignIn { get; } = account.State == AccountState.NeedsSignIn;

    public ObservableCollection<CalendarItemViewModel> Calendars { get; } = [];

    [RelayCommand]
    private Task SignInAgainAsync() => owner.SignInAsync(Email);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);
}

/// <summary>One calendar in the side pane, with its colour and show/hide checkbox.</summary>
public sealed partial class CalendarItemViewModel : ObservableObject
{
    private readonly AccountsViewModel _owner;

    public CalendarItemViewModel(AccountsViewModel owner, CalendarRecord calendar)
    {
        _owner = owner;
        AccountId = calendar.AccountId;
        Id = calendar.Id;
        Name = calendar.Name;
        Color = ParseColor(calendar.BackgroundColor);
        _isVisible = calendar.IsVisible;
    }

    public string AccountId { get; }

    public string Id { get; }

    public string Name { get; }

    public Windows.UI.Color Color { get; }

    private bool _isVisible;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _ = _owner.SetCalendarVisibleAsync(this, value);
            }
        }
    }

    internal static Windows.UI.Color ParseColor(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return Microsoft.UI.ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return Microsoft.UI.ColorHelper.FromArgb(255, 0x03, 0x9b, 0xe5);
    }
}
