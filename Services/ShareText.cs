namespace WinInstagram.Services;

/// <summary>
/// Builds the text that lands on the clipboard when the user shares something. A bare URL is
/// useless to a reader who has no visual context, so the copy always leads with the caption and
/// the creator, in the same shape as <c>caption — @creator — url</c>.
/// </summary>
public static class ShareText
{
    private const string Separator = " — ";

    public static string Build(string? caption, string? username, string? url)
    {
        var parts = new List<string>();

        var cleanCaption = CleanCaption(caption);
        if (!string.IsNullOrWhiteSpace(cleanCaption)) parts.Add(cleanCaption);

        if (!string.IsNullOrWhiteSpace(username)) parts.Add($"@{username.Trim().TrimStart('@')}");

        if (!string.IsNullOrWhiteSpace(url)) parts.Add(url.Trim());

        return parts.Count == 0 ? string.Empty : string.Join(Separator, parts);
    }

    /// <summary>Collapses whitespace and trims a caption to something that survives a chat message.</summary>
    private static string CleanCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return string.Empty;

        var collapsed = string.Join(" ", caption.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        const int maxLength = 200;
        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength].TrimEnd() + "…";
        }
        return collapsed;
    }
}
