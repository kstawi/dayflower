// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Dayflower.Core.Security;

namespace Dayflower.Storage;

/// <summary>
/// Encrypts secrets with Windows DPAPI for the current Windows user, so a copied cache file is useless on another
/// account or PC.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dayflower sign-in");

    public byte[] Protect(string secret) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);

    public string Unprotect(byte[] protectedSecret) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedSecret, Entropy, DataProtectionScope.CurrentUser));
}
