// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Providers.Google.OAuth;

/// <summary>Hands out a valid access token for one account, refreshing it shortly before it expires.</summary>
internal sealed class GoogleTokenSource(GoogleOAuthClient oauth, string refreshToken, TimeProvider time)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private AccessToken? _current;

    public string RefreshToken => refreshToken;

    public async Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (forceRefresh || _current is null || _current.ExpiresUtc - RefreshMargin <= time.GetUtcNow())
            {
                _current = await oauth.RefreshAsync(refreshToken, cancellationToken);
            }

            return _current.Value;
        }
        finally
        {
            _lock.Release();
        }
    }
}
