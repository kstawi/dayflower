// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.Api;
using Microsoft.Extensions.Logging;
using Windows.Networking.Connectivity;

namespace Dayflower.Services;

/// <summary>Why a change didn't go through, worded for the user.</summary>
public sealed class EditFailedException(string title, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Title { get; } = title;
}

/// <summary>Someone who has been on the user's events, for guest suggestions.</summary>
public sealed record KnownPerson(string Email, string? Name);

/// <summary>
/// Makes changes to events at the provider and shows them at once: the events the provider sends back go
/// straight into the cache, and a sync follows to pick up anything else the change caused.
/// </summary>
public sealed class EventEditService(AppServices services)
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<EventEditService>();
    private readonly Dictionary<string, ICalendarProvider> _providers = services.Providers.ToDictionary(p => p.Id);
    private readonly Dictionary<string, (byte[] Secret, ICalendarConnection Connection)> _connections = [];
    private readonly SemaphoreSlim _connectionsLock = new(1, 1);
    private IReadOnlyList<KnownPerson>? _people;

    /// <summary>Raised on the UI thread once a change is in the cache.</summary>
    public event EventHandler? Changed;

    public static bool IsOnline =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

    public Task CreateAsync(string accountId, string calendarId, EventEdit edit, bool notifyGuests) =>
        RunAsync(accountId, calendarId, "create", (c, ct) => c.CreateEventAsync(calendarId, edit, notifyGuests, ct));

    public Task UpdateAsync(EventTarget target, EventEdit edit, EditScope scope, bool notifyGuests) =>
        RunAsync(target.Event.Key.AccountId, target.CalendarId, "update", (c, ct) => c.UpdateEventAsync(target, edit, scope, notifyGuests, ct));

    public Task DeleteAsync(EventTarget target, EditScope scope, bool notifyGuests) =>
        RunAsync(target.Event.Key.AccountId, target.CalendarId, "delete", (c, ct) => c.DeleteEventAsync(target, scope, notifyGuests, ct));

    public Task RespondAsync(EventTarget target, Rsvp rsvp, EditScope scope) =>
        RunAsync(target.Event.Key.AccountId, target.CalendarId, "respond", (c, ct) => c.RespondAsync(target, rsvp, scope, ct));

    /// <summary>When the given people are busy, seen from <paramref name="accountId"/>; null when it can't be found out.</summary>
    public async Task<IReadOnlyDictionary<string, FreeBusy>?> GetFreeBusyAsync(
        string accountId, IReadOnlyList<string> emails, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (emails.Count == 0 || !IsOnline)
        {
            return null;
        }

        try
        {
            return await Task.Run(async () =>
            {
                var connection = await ConnectAsync(accountId);
                return await connection.GetFreeBusyAsync(emails, from, to, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Free/busy lookup failed");
            return null;
        }
    }

    /// <summary>People from the user's events over the past and coming months, most frequent first.</summary>
    public async Task<IReadOnlyList<KnownPerson>> GetKnownPeopleAsync()
    {
        if (_people is { } cached)
        {
            return cached;
        }

        var people = await Task.Run(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var seen = new Dictionary<string, (string? Name, int Count)>(StringComparer.OrdinalIgnoreCase);
            var own = (await services.Store.GetAccountsAsync()).Select(a => a.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stored in await services.Store.GetEventsAsync(now.AddDays(-180), now.AddDays(180)))
            {
                if (!_providers.TryGetValue(stored.ProviderId, out var provider))
                {
                    continue;
                }

                CalendarEvent e;
                try
                {
                    e = provider.ParseEvent(stored.Key.AccountId, stored.Key.CalendarId, stored.Payload);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var a in e.Attendees)
                {
                    if (a.Email is not { Length: > 0 } email || a.IsResource || own.Contains(email))
                    {
                        continue;
                    }

                    var known = seen.GetValueOrDefault(email);
                    seen[email] = (a.DisplayName ?? known.Name, known.Count + 1);
                }
            }

            return seen.OrderByDescending(p => p.Value.Count).Select(p => new KnownPerson(p.Key, p.Value.Name)).ToList();
        });
        _people = people;
        return people;
    }

    private async Task RunAsync(
        string accountId, string calendarId, string what,
        Func<ICalendarConnection, CancellationToken, Task<IReadOnlyList<EventChange>>> action)
    {
        if (!IsOnline)
        {
            throw new EditFailedException("You're offline", "Changes need an internet connection. Your calendar is still here to look at.");
        }

        try
        {
            await Task.Run(async () =>
            {
                var connection = await ConnectAsync(accountId);
                var changes = await action(connection, CancellationToken.None);
                await services.Store.ApplyLocalChangesAsync(accountId, calendarId, changes);
            });
        }
        catch (AccountSignInRequiredException ex)
        {
            _logger.LogWarning(ex, "Can't {What}: the account needs signing in again", what);
            await services.Store.SetAccountStateAsync(accountId, AccountState.NeedsSignIn);
            throw new EditFailedException("Sign in again", "Google stopped accepting this account's saved sign-in. Sign in again from the side pane, then try once more.", ex);
        }
        catch (GoogleApiException ex)
        {
            _logger.LogWarning(ex, "Google refused to {What}", what);
            throw ex.Status switch
            {
                HttpStatusCode.Forbidden => new EditFailedException("Google didn't allow that", Explain(ex, "You may not have permission to change this event."), ex),
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new EditFailedException("The event is gone", "It was deleted or moved elsewhere. Dayflower is syncing to catch up.", ex),
                _ => new EditFailedException("Google couldn't save that", Explain(ex, "Try again in a moment."), ex),
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Couldn't reach Google to {What}", what);
            throw new EditFailedException("Couldn't reach Google", "Check your internet connection and try again.", ex);
        }
        catch (Exception ex) when (ex is not EditFailedException)
        {
            _logger.LogError(ex, "Couldn't {What}", what);
            throw new EditFailedException("Something went wrong", ex.Message, ex);
        }
        finally
        {
            // Whatever happened, the next sync brings the cache in line with Google.
            _ = services.Sync.SyncNowAsync();
        }

        _people = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Explain(GoogleApiException ex, string fallback) =>
        ex.Message.Split(": ", 2) is [_, var detail] && detail.Length > 0 ? $"Google says: {detail}" : fallback;

    /// <summary>A connection per account, made again when its saved sign-in changes.</summary>
    private async Task<ICalendarConnection> ConnectAsync(string accountId)
    {
        var secret = await services.Store.GetAccountSecretAsync(accountId)
            ?? throw new AccountSignInRequiredException("The account has no saved sign-in.");
        await _connectionsLock.WaitAsync();
        try
        {
            if (_connections.TryGetValue(accountId, out var known) && known.Secret.AsSpan().SequenceEqual(secret))
            {
                return known.Connection;
            }

            var provider = _providers[accountId.Split(':')[0]];
            var connection = provider.Connect(accountId, services.Protector.Unprotect(secret));
            _connections[accountId] = (secret, connection);
            return connection;
        }
        finally
        {
            _connectionsLock.Release();
        }
    }
}
