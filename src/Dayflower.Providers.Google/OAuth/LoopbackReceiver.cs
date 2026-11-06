// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dayflower.Providers.Google.OAuth;

/// <summary>
/// Receives Google's sign-in redirect on http://127.0.0.1:{random port}/, the loopback flow Google recommends
/// for desktop apps. A plain socket rather than HttpListener, so it needs no URL reservation or admin rights.
/// </summary>
internal sealed class LoopbackReceiver : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackReceiver()
    {
        _listener.Start();
    }

    public Uri RedirectUri => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    /// <summary>Waits for the browser to come back and returns the redirect's query parameters.</summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            string? requestLine = await ReadRequestLineAsync(stream, cancellationToken);
            var query = requestLine is null ? null : ParseCallback(requestLine);
            if (query is null || !(query.ContainsKey("code") || query.ContainsKey("error")))
            {
                // Something else, like the browser asking for /favicon.ico.
                await RespondAsync(stream, "404 Not Found", "Not found", cancellationToken);
                continue;
            }

            bool signedIn = query.ContainsKey("code");
            await RespondAsync(stream, "200 OK", Page(signedIn), cancellationToken);
            return query;
        }
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>The query parameters of a request line such as "GET /?code=abc&amp;state=xyz HTTP/1.1".</summary>
    internal static IReadOnlyDictionary<string, string>? ParseCallback(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET")
        {
            return null;
        }

        string target = parts[1];
        int q = target.IndexOf('?', StringComparison.Ordinal);
        string path = q < 0 ? target : target[..q];
        if (path != "/")
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q >= 0)
        {
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                string key = Decode(eq < 0 ? pair : pair[..eq]);
                string value = eq < 0 ? string.Empty : Decode(pair[(eq + 1)..]);
                result[key] = value;
            }
        }

        return result;
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            string soFar = Encoding.ASCII.GetString(buffer, 0, total);
            int end = soFar.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return soFar[..end];
            }
        }

        return null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        string head = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
                      "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static string Page(bool signedIn)
    {
        string title = signedIn ? "You're signed in" : "Sign-in didn't finish";
        string text = signedIn
            ? "Dayflower is adding your calendars. You can close this tab."
            : "Nothing was changed. You can close this tab and try again in Dayflower.";
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Dayflower</title>" +
               "<meta name=\"viewport\" content=\"width=device-width\"><style>" +
               "body{font-family:'Segoe UI Variable','Segoe UI',sans-serif;display:grid;place-items:center;" +
               "min-height:90vh;margin:0;background:#f3f3f3;color:#1a1a1a}" +
               "@media(prefers-color-scheme:dark){body{background:#202020;color:#fff}}" +
               "main{text-align:center;padding:24px}h1{font-weight:600;font-size:28px;margin:0 0 8px}" +
               "</style></head><body><main><h1>" + title + "</h1><p>" + text + "</p></main></body></html>";
    }
}
