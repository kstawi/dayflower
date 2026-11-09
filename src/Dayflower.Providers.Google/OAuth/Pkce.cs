// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Security.Cryptography;
using System.Text;

namespace Dayflower.Providers.Google.OAuth;

/// <summary>Proof Key for Code Exchange (RFC 7636), which protects the sign-in code on its way back to the app.</summary>
internal static class Pkce
{
    /// <summary>A random, URL-safe string, used for the code verifier and the state parameter.</summary>
    public static string CreateRandomString() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 code challenge for a verifier.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string value)
    {
        string s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
