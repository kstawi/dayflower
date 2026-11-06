// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.OAuth;
using Microsoft.Extensions.Logging;

namespace Dayflower.Providers.Google.Api;

/// <summary>
/// Authorised GET requests to the Google Calendar API v3 for one account, with token refresh, retries for rate
/// limits and server errors, and Google's 410 "sync token expired" mapped to <see cref="SyncTokenExpiredException"/>.
/// </summary>
internal sealed class GoogleCalendarApi(
    HttpClient http,
    GoogleTokenSource tokens,
    ILogger logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const string BaseUrl = "https://www.googleapis.com/calendar/v3/";

    private const int MaxAttempts = 5;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        bool refreshed = false;
        for (int attempt = 1; ; attempt++)
        {
            string token = await tokens.GetAsync(forceRefresh: false, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(body);
            }

            var error = GoogleApiError.Parse(response.StatusCode, body);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Gone:
                    throw new SyncTokenExpiredException(error.Message);

                case HttpStatusCode.Unauthorized when !refreshed:
                    // The access token was revoked or expired early; get a new one and try once more.
                    refreshed = true;
                    await tokens.GetAsync(forceRefresh: true, cancellationToken);
                    continue;

                case HttpStatusCode.Unauthorized:
                    throw new AccountSignInRequiredException($"Google rejected the account's access: {error.Message}");
            }

            if (error.IsRetryable && attempt < MaxAttempts)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(250));
                logger.LogInformation("Google API {Status} ({Reason}); retrying in {Wait}", (int)response.StatusCode, error.Reason, wait);
                await _delay(wait, cancellationToken);
                continue;
            }

            throw new GoogleApiException(response.StatusCode, error.Reason, error.Message);
        }
    }

    /// <summary>The "error" object of a Google API error response.</summary>
    internal sealed record GoogleApiError(HttpStatusCode Status, string? Reason, string Message)
    {
        public bool IsRetryable =>
            Status == HttpStatusCode.TooManyRequests
            || (int)Status >= 500
            || (Status == HttpStatusCode.Forbidden && Reason is "rateLimitExceeded" or "userRateLimitExceeded");

        public static GoogleApiError Parse(HttpStatusCode status, string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                var error = json.RootElement.GetProperty("error");
                string message = error.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                string? reason = error.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
                                 && errors[0].TryGetProperty("reason", out var r)
                    ? r.GetString()
                    : null;
                return new GoogleApiError(status, reason, message);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return new GoogleApiError(status, null, $"HTTP {(int)status}");
            }
        }
    }
}

/// <summary>A Google Calendar API error that retrying won't fix, such as 404 for a calendar that's gone.</summary>
public sealed class GoogleApiException(HttpStatusCode status, string? reason, string message)
    : Exception($"Google Calendar API error {(int)status}{(reason is null ? string.Empty : $" ({reason})")}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public string? Reason { get; } = reason;
}
