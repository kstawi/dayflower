// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text.RegularExpressions;

namespace Dayflower.Core.Display;

/// <summary>Turns the limited HTML of event descriptions (Google uses b, i, u, a, br, p, ul, li) into plain text.</summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string text = html.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = LineBreak().Replace(text, "\n");
        text = CellEnd().Replace(text, " · ");
        text = BlockEnd().Replace(text, "\n");
        text = ListItem().Replace(text, "\n• ");
        text = Link().Replace(text, m =>
        {
            string href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            string label = Tag().Replace(m.Groups["label"].Value, string.Empty).Trim();
            return label.Length == 0 || WebUtility.HtmlDecode(label) == href ? href : $"{label} ({href})";
        });
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = TrailingCellSeparator().Replace(text, "\n");
        text = TrailingSpaces().Replace(text, "\n");
        text = ManyBlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|h[1-6]|ul|ol|tr|table)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"</t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex CellEnd();

    [GeneratedRegex(@"( · )+\s*(\n|$)")]
    private static partial Regex TrailingCellSeparator();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<a\s[^>]*href=""(?<href>[^""]*)""[^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Link();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();
}
