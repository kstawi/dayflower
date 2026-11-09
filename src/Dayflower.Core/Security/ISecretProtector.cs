// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Security;

/// <summary>Encrypts secrets such as refresh tokens before they're saved.</summary>
public interface ISecretProtector
{
    byte[] Protect(string secret);

    string Unprotect(byte[] protectedSecret);
}
