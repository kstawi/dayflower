// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.OAuth;

namespace Dayflower.Providers.Google.Tests;

public class GoogleOAuthTests
{
    private static readonly GoogleOAuthSettings Settings = new("client-id.apps.googleusercontent.com", "client-secret");

    [Fact]
    public void Pkce_challenge_matches_the_rfc_example()
    {
        // RFC 7636, appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Random_strings_are_url_safe_and_different()
    {
        string a = Pkce.CreateRandomString(), b = Pkce.CreateRandomString();

        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);
        Assert.DoesNotContain(a, c => c is '+' or '/' or '=');
    }

    [Fact]
    public void Authorization_url_asks_for_offline_access_with_pkce()
    {
        var oauth = new GoogleOAuthClient(Settings, new HttpClient(), TimeProvider.System);

        var uri = oauth.BuildAuthorizationUri(new Uri("http://127.0.0.1:54321/"), "challenge", "state-1", "me@example.com");
        var query = LoopbackReceiver.ParseCallback("GET /" + uri.Query + " HTTP/1.1")!;

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", uri.ToString());
        Assert.Equal("client-id.apps.googleusercontent.com", query["client_id"]);
        Assert.Equal("http://127.0.0.1:54321/", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("challenge", query["code_challenge"]);
        Assert.Equal("state-1", query["state"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("select_account consent", query["prompt"]);
        Assert.Equal("me@example.com", query["login_hint"]);
        Assert.Equal(GoogleOAuthSettings.Scopes, query["scope"].Split(' '));
    }

    [Fact]
    public void Id_token_claims_are_read()
    {
        string payload = Pkce.Base64Url(Encoding.UTF8.GetBytes("""{"sub":"1234567890","email":"me@example.com","name":"Me Myself"}"""));

        var claims = GoogleOAuthClient.ParseIdToken($"header.{payload}.signature");

        Assert.Equal("1234567890", claims.Subject);
        Assert.Equal("me@example.com", claims.Email);
        Assert.Equal("Me Myself", claims.Name);
    }

    [Fact]
    public void Missing_calendar_permissions_are_reported_but_free_busy_is_optional()
    {
        Assert.Empty(GoogleOAuthClient.MissingScopes(["openid", GoogleOAuthSettings.CalendarListReadOnlyScope, GoogleOAuthSettings.EventsScope]));
        Assert.Equal([GoogleOAuthSettings.EventsScope], GoogleOAuthClient.MissingScopes(["openid", GoogleOAuthSettings.CalendarListReadOnlyScope]));
    }

    [Theory]
    [InlineData("GET /?state=s&code=4%2F0Abc HTTP/1.1", "code", "4/0Abc")]
    [InlineData("GET /?error=access_denied&state=s HTTP/1.1", "error", "access_denied")]
    public void Callback_parameters_are_decoded(string requestLine, string key, string value)
    {
        Assert.Equal(value, LoopbackReceiver.ParseCallback(requestLine)![key]);
    }

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("POST /?code=x HTTP/1.1")]
    [InlineData("garbage")]
    public void Other_requests_are_not_callbacks(string requestLine)
    {
        Assert.Null(LoopbackReceiver.ParseCallback(requestLine));
    }

    [Fact]
    public async Task Loopback_receiver_returns_the_redirect_and_shows_a_page()
    {
        using var receiver = new LoopbackReceiver();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waiting = receiver.WaitForCallbackAsync(cts.Token);

        using var browser = new HttpClient();
        var favicon = await browser.GetAsync(new Uri(receiver.RedirectUri, "favicon.ico"), cts.Token);
        var page = await browser.GetAsync(new Uri(receiver.RedirectUri, "?state=s1&code=c1&scope=openid"), cts.Token);
        var query = await waiting;

        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("signed in", await page.Content.ReadAsStringAsync(cts.Token));
        Assert.Equal("c1", query["code"]);
        Assert.Equal("s1", query["state"]);
        Assert.StartsWith("http://127.0.0.1:", receiver.RedirectUri.ToString());
    }

    [Fact]
    public async Task Sign_in_exchanges_the_code_and_checks_the_state()
    {
        string idPayload = Pkce.Base64Url(Encoding.UTF8.GetBytes("""{"sub":"42","email":"me@example.com"}"""));
        var http = new FakeHttp().On("oauth2.googleapis.com/token", HttpStatusCode.OK,
            $$"""{"access_token":"a","expires_in":3599,"refresh_token":"r","scope":"openid {{GoogleOAuthSettings.CalendarListReadOnlyScope}} {{GoogleOAuthSettings.EventsScope}}","id_token":"h.{{idPayload}}.s"}""");
        var oauth = new GoogleOAuthClient(Settings, new HttpClient(http), TimeProvider.System);

        // Play the browser: follow the authorization URL's redirect_uri back to the app with a code.
        using var browser = new HttpClient();
        var signIn = await oauth.SignInAsync(async authUri =>
        {
            var q = LoopbackReceiver.ParseCallback("GET /" + authUri.Query + " HTTP/1.1")!;
            _ = browser.GetAsync($"{q["redirect_uri"]}?state={q["state"]}&code=the-code");
            await Task.CompletedTask;
        }, null, CancellationToken.None);

        Assert.Equal("42", signIn.Subject);
        Assert.Equal("r", signIn.RefreshToken);
        var exchange = Assert.Single(http.Requests);
        Assert.Contains("code=the-code", exchange.Body);
        Assert.Contains("code_verifier=", exchange.Body);
        Assert.Contains("grant_type=authorization_code", exchange.Body);
    }

    [Fact]
    public async Task Cancel_in_the_browser_cancels_the_sign_in()
    {
        var oauth = new GoogleOAuthClient(Settings, new HttpClient(new FakeHttp()), TimeProvider.System);
        using var browser = new HttpClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oauth.SignInAsync(authUri =>
        {
            var q = LoopbackReceiver.ParseCallback("GET /" + authUri.Query + " HTTP/1.1")!;
            _ = browser.GetAsync($"{q["redirect_uri"]}?error=access_denied&state={q["state"]}");
            return Task.CompletedTask;
        }, null, CancellationToken.None));
    }

    [Fact]
    public async Task Unticked_calendar_permission_fails_the_sign_in()
    {
        string idPayload = Pkce.Base64Url(Encoding.UTF8.GetBytes("""{"sub":"42","email":"me@example.com"}"""));
        var http = new FakeHttp().On("oauth2.googleapis.com/token", HttpStatusCode.OK,
            $$"""{"access_token":"a","expires_in":3599,"refresh_token":"r","scope":"openid email","id_token":"h.{{idPayload}}.s"}""");
        var oauth = new GoogleOAuthClient(Settings, new HttpClient(http), TimeProvider.System);
        using var browser = new HttpClient();

        var ex = await Assert.ThrowsAsync<MissingPermissionsException>(() => oauth.SignInAsync(authUri =>
        {
            var q = LoopbackReceiver.ParseCallback("GET /" + authUri.Query + " HTTP/1.1")!;
            _ = browser.GetAsync($"{q["redirect_uri"]}?state={q["state"]}&code=c");
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        Assert.Equal(GoogleOAuthSettings.RequiredScopes, ex.Missing);
    }
}
