// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text;

namespace Dayflower.Providers.Google.Tests;

/// <summary>
/// An HTTP handler that answers from queued responses, matched by the first registered URL fragment the request
/// contains, and records every request.
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly List<(string Fragment, Queue<(HttpStatusCode Status, string Body)> Responses)> _routes = [];

    public List<(string Url, string? Authorization, string? Body, string Method)> Requests { get; } = [];

    public FakeHttp On(string urlFragment, HttpStatusCode status, string body)
    {
        var route = _routes.FirstOrDefault(r => r.Fragment == urlFragment);
        if (route.Responses is null)
        {
            route = (urlFragment, new Queue<(HttpStatusCode, string)>());
            _routes.Add(route);
        }

        route.Responses.Enqueue((status, body));
        return this;
    }

    public FakeHttp OnToken(string accessToken = "access-1") =>
        On("oauth2.googleapis.com/token", HttpStatusCode.OK, $$"""{"access_token":"{{accessToken}}","expires_in":3599,"token_type":"Bearer"}""");

    public IEnumerable<string> UrlsContaining(string fragment) => Requests.Select(r => r.Url).Where(u => u.Contains(fragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((url, request.Headers.Authorization?.ToString(), body, request.Method.Method));

        foreach (var (fragment, responses) in _routes)
        {
            if (url.Contains(fragment, StringComparison.Ordinal) && responses.Count > 0)
            {
                var (status, text) = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
                return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($$$"""{"error":{"code":404,"message":"No fake for {{{url}}}"}}"""),
        };
    }
}
