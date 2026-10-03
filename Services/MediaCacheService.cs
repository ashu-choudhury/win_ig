using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace WinInstagram.Services;

public static class MediaCacheService
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "WinInstagram", "VideoCache");
    private static readonly HttpClient _httpClient;

    static MediaCacheService()
    {
        Directory.CreateDirectory(CacheDir);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"
        );
    }

    public static async Task<string> GetOrDownloadVideoAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        // If it's already a local file
        if (File.Exists(url))
            return url;

        try
        {
            var hash = ComputeHash(url);
            var targetPath = Path.Combine(CacheDir, $"{hash}.mp4");

            // Return cached file if it exists and is complete
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 10_000)
            {
                AppLogger.Info("CACHE", $"Reel video found in local cache: {Path.GetFileName(targetPath)}");
                return targetPath;
            }

            AppLogger.Info("CACHE", $"Downloading reel video to local cache from CDN...");
            var tempPath = targetPath + ".tmp";

            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using (var sourceStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(fileStream, ct);
                }
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);

            var sizeKb = new FileInfo(targetPath).Length / 1024;
            AppLogger.Success("CACHE", $"Reel downloaded ({sizeKb} KB) to local path: {Path.GetFileName(targetPath)}");
            return targetPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("CACHE", $"Failed to download video from {url}", ex);
            return string.Empty;
        }
    }

    private static string ComputeHash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
