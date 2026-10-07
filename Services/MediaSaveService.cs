using System.IO;
using System.Net.Http;
using System.Text;

namespace WinInstagram.Services;

/// <summary>
/// Saves the media the user is looking at to disk, together with a small text file describing it.
/// The description matters more than the image for a screen reader user: it carries the caption,
/// the alt text and the permalink, so the saved file stays meaningful on its own.
///
/// Every call reports what actually happened, including Instagram's own HTTP status, so a failed
/// save is never announced as a success.
/// </summary>
public class MediaSaveService
{
    private static MediaSaveService? _instance;
    public static MediaSaveService Instance => _instance ??= new MediaSaveService();

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // The CDN rejects requests without a browser-like agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0 Safari/537.36");
        return client;
    }

    /// <summary>Where saved media goes: the chosen folder, or Pictures\WinInstagram by default.</summary>
    public string SaveFolder
    {
        get
        {
            var configured = AppSettingsService.Instance.Settings.MediaSaveFolder;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "WinInstagram");
        }
    }

    /// <summary>What a save attempt produced, so the caller can speak the truth about it.</summary>
    public record SaveResult(bool Ok, string Message, string FilePath, string TextPath);

    /// <summary>
    /// Downloads one media URL and writes the accompanying text file. Returns a result describing
    /// the outcome rather than throwing, because a failed save must not break the session.
    /// </summary>
    public async Task<SaveResult> SaveAsync(string? mediaUrl, string caption, string altText, string username, string pageUrl)
    {
        var kind = DescribeKind(mediaUrl, out var extension);
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            // Nothing at all to keep: reporting success here would leave an empty file behind.
            if (string.IsNullOrWhiteSpace(caption) && string.IsNullOrWhiteSpace(altText))
            {
                return new SaveResult(false, "Nothing to save: no media, caption or description was found on this page.", string.Empty, string.Empty);
            }

            // Without a downloadable URL the readable text is still worth keeping.
            var textOnly = await WriteTextFileAsync(caption, altText, username, pageUrl, "text");
            if (textOnly.Ok)
            {
                return textOnly with
                {
                    Message = $"No media file was found on this page, so only the description was saved, in {SaveFolder}."
                };
            }

            return textOnly with { Message = "Could not save the description of this media." };
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var baseName = BuildBaseName(username, stamp);
        var folder = SaveFolder;

        try
        {
            Directory.CreateDirectory(folder);

            var bytes = await Http.GetByteArrayAsync(mediaUrl);
            if (bytes.Length == 0)
            {
                return new SaveResult(false, "Instagram returned an empty file, so nothing was saved.", string.Empty, string.Empty);
            }

            var mediaPath = Path.Combine(folder, baseName + extension);
            await File.WriteAllBytesAsync(mediaPath, bytes);

            var text = await WriteTextFileAsync(caption, altText, username, pageUrl, baseName);

            var sizeText = bytes.Length >= 1024 * 1024
                ? $"{bytes.Length / (1024.0 * 1024.0):0.#} megabytes"
                : $"{Math.Max(1, bytes.Length / 1024)} kilobytes";

            var message = text.Ok
                ? $"Saved {kind} ({sizeText}) and its description to {folder}."
                : $"Saved {kind} ({sizeText}) to {folder}, but the description file could not be written.";

            AppLogger.Success("SAVE", $"Saved media to {mediaPath}");
            return new SaveResult(true, message, mediaPath, text.TextPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error("SAVE", "Failed to save media", ex);
            return new SaveResult(false, $"Could not save the media: {ex.Message}", string.Empty, string.Empty);
        }
    }

    private async Task<SaveResult> WriteTextFileAsync(string caption, string altText, string username, string pageUrl, string baseName)
    {
        try
        {
            var folder = SaveFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{baseName}.txt");

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(username)) builder.AppendLine($"Creator: @{username}");
            if (!string.IsNullOrWhiteSpace(pageUrl)) builder.AppendLine($"Link: {pageUrl}");
            builder.AppendLine($"Saved: {DateTime.Now:g}");
            builder.AppendLine();
            builder.AppendLine("Caption:");
            builder.AppendLine(string.IsNullOrWhiteSpace(caption) ? "(no caption)" : caption);
            builder.AppendLine();
            builder.AppendLine("Image description:");
            builder.AppendLine(string.IsNullOrWhiteSpace(altText) ? "(Instagram provided no alt text for this media)" : altText);

            await File.WriteAllTextAsync(path, builder.ToString());
            return new SaveResult(true, "Description saved.", string.Empty, path);
        }
        catch (Exception ex)
        {
            AppLogger.Error("SAVE", "Failed to write media description", ex);
            return new SaveResult(false, $"Could not write the description: {ex.Message}", string.Empty, string.Empty);
        }
    }

    /// <summary>Reads the media type and file extension out of the URL, falling back to a generic file.</summary>
    private static string DescribeKind(string? mediaUrl, out string extension)
    {
        extension = ".jpg";
        if (string.IsNullOrWhiteSpace(mediaUrl)) return "media";

        var lowered = mediaUrl.ToLowerInvariant();
        if (lowered.Contains(".mp4") || lowered.Contains("/v/") || lowered.Contains("video"))
        {
            extension = ".mp4";
            return "video";
        }
        if (lowered.Contains(".webp")) { extension = ".webp"; return "image"; }
        return "image";
    }

    private static string BuildBaseName(string username, string stamp)
    {
        var handle = string.IsNullOrWhiteSpace(username) ? "instagram" : SafeFileName(username);
        return $"WinInstagram_{handle}_{stamp}";
    }

    /// <summary>Strips characters Windows will not accept in a file name.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "instagram" : cleaned;
    }
}
