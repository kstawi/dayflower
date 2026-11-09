// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text;
using System.Text.Json;
using Dayflower.Core.Providers;

namespace Dayflower.Providers.Google.OAuth;

/// <summary>The result of a successful sign-in.</summary>
public sealed record GoogleSignIn(
    string Subject, string Email, string? Name, string RefreshToken, AccessToken AccessToken, IReadOnlyList<string> GrantedScopes);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresUtc);

/// <summary>
/// Google's OAuth 2.0 flow for installed apps: the system browser, a loopback redirect and PKCE.
/// See https://developers.google.com/identity/protocols/oauth2/native-app.
/// </summary>
public sealed class GoogleOAuthClient(GoogleOAuthSettings settings, HttpClient http, TimeProvider time)
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    /// <summary>Signs in in the browser and returns the account and its tokens.</summary>
    /// <exception cref="OperationCanceledException">The user cancelled, in the browser or the app.</exception>
    /// <exception cref="MissingPermissionsException">The user unticked permissions Dayflower needs.</exception>
    public async Task<GoogleSignIn> SignInAsync(Func<Uri, Task> openBrowser, string? loginHint, CancellationToken cancellationToken)
    {
        using var receiver = new LoopbackReceiver();
        string verifier = Pkce.CreateRandomString();
        string state = Pkce.CreateRandomString();

        await openBrowser(BuildAuthorizationUri(receiver.RedirectUri, Pkce.Challenge(verifier), state, loginHint));
        var callback = await receiver.WaitForCallbackAsync(cancellationToken);

        if (callback.TryGetValue("error", out string? error))
        {
            throw error == "access_denied"
                ? new OperationCanceledException("Sign-in was cancelled in the browser.")
                : new InvalidOperationException($"Google sign-in failed ({error}).");
        }

        if (!callback.TryGetValue("state", out string? returnedState) || returnedState != state)
        {
            throw new InvalidOperationException("The sign-in response didn't match the request.");
        }

        using var json = await PostAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["code"] = callback["code"],
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["redirect_uri"] = receiver.RedirectUri.ToString(),
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        }, cancellationToken);

        var root = json.RootElement;
        var granted = (root.TryGetProperty("scope", out var scope) ? scope.GetString() ?? string.Empty : string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var missing = MissingScopes(granted);
        if (missing.Count > 0)
        {
            throw new MissingPermissionsException(missing);
        }

        string refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
        if (refreshToken.Length == 0)
        {
            throw new InvalidOperationException("Google didn't return a refresh token.");
        }

        var claims = ParseIdToken(root.GetProperty("id_token").GetString()!);
        return new GoogleSignIn(claims.Subject, claims.Email, claims.Name, refreshToken, ReadAccessToken(root), granted);
    }

    /// <summary>A new access token for a saved refresh token.</summary>
    /// <exception cref="AccountSignInRequiredException">Google no longer accepts the refresh token.</exception>
    public async Task<AccessToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await PostAsync(TokenEndpoint, new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["grant_type"] = "refresh_token",
            }, cancellationToken);
            return ReadAccessToken(json.RootElement);
        }
        catch (OAuthErrorException ex) when (ex.Error is "invalid_grant" or "unauthorized_client" or "invalid_client")
        {
            throw new AccountSignInRequiredException($"Google rejected the saved sign-in ({ex.Error}).", ex);
        }
    }

    /// <summary>Withdraws Dayflower's access; a token Google already dropped counts as done.</summary>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        using var response = await http.PostAsync(RevokeEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    internal Uri BuildAuthorizationUri(Uri redirectUri, string codeChallenge, string state, string? loginHint)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', GoogleOAuthSettings.Scopes),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",

            // select_account: always let the user pick (or add) an account. consent: always get a refresh token.
            ["prompt"] = "select_account consent",
        };
        if (!string.IsNullOrEmpty(loginHint))
        {
            query["login_hint"] = loginHint;
        }

        var builder = new StringBuilder(AuthorizationEndpoint).Append('?');
        builder.AppendJoin('&', query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        return new Uri(builder.ToString());
    }

    internal static IReadOnlyList<string> MissingScopes(IEnumerable<string> granted)
    {
        var set = granted.ToHashSet(StringComparer.Ordinal);
        return GoogleOAuthSettings.RequiredScopes.Where(s => !set.Contains(s)).ToList();
    }

    /// <summary>
    /// Reads the claims of an ID token. Its signature isn't checked: the token came straight from Google's token
    /// endpoint over TLS, which OpenID Connect accepts in place of signature validation (Core 1.0, 3.1.3.7).
    /// </summary>
    internal static IdTokenClaims ParseIdToken(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            throw new FormatException("Malformed ID token.");
        }

        using var payload = JsonDocument.Parse(Pkce.FromBase64Url(parts[1]));
        var root = payload.RootElement;
        return new IdTokenClaims(
            root.GetProperty("sub").GetString()!,
            root.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() : null);
    }

    private AccessToken ReadAccessToken(JsonElement root)
    {
        int expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        return new AccessToken(root.GetProperty("access_token").GetString()!, time.GetUtcNow().AddSeconds(expiresIn));
    }

    private async Task<JsonDocument> PostAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(url, content, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = "unknown_error";
            try
            {
                using var json = JsonDocument.Parse(body);
                error = json.RootElement.TryGetProperty("error", out var err) ? err.GetString() ?? error : error;
            }
            catch (JsonException)
            {
            }

            throw new OAuthErrorException(error, response.StatusCode);
        }

        return JsonDocument.Parse(body);
    }

    internal sealed record IdTokenClaims(string Subject, string Email, string? Name);
}

/// <summary>An error from Google's OAuth endpoints, such as invalid_grant.</summary>
public sealed class OAuthErrorException(string error, HttpStatusCode status)
    : Exception($"Google OAuth error {error} (HTTP {(int)status}).")
{
    public string Error { get; } = error;
}
