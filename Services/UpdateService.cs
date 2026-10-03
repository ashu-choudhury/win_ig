using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WinInstagram.Services;

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public Version LatestVersion { get; set; } = new(1, 0, 0);
    public string TagName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public long AssetSize { get; set; }
}

public class UpdateService
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();

    private static readonly HttpClient _httpClient = new();
    private const string GitHubApiUrl = "https://api.github.com/repos/ashu-choudhury/win_ig/releases/latest";

    public event Action<UpdateInfo>? UpdateAvailable;
    public event Action<string>? UpdateCheckStatusUpdated;

    public static Version CurrentVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

            // 1. Check AssemblyInformationalVersion (e.g. 1.0.8 or 1.0.8+hash)
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var clean = infoVer.Split('+')[0].Trim().TrimStart('v', 'V');
                if (Version.TryParse(clean, out var parsedInfo))
                {
                    return new Version(parsedInfo.Major, parsedInfo.Minor, Math.Max(0, parsedInfo.Build));
                }
            }

            // 2. Check FileVersion of executing binary
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(processPath);
                    if (!string.IsNullOrWhiteSpace(fvi.FileVersion) && Version.TryParse(fvi.FileVersion, out var parsedFile))
                    {
                        return new Version(parsedFile.Major, parsedFile.Minor, Math.Max(0, parsedFile.Build));
                    }
                }
            }
            catch { }

            // 3. Check Assembly Name Version
            var ver = asm.GetName().Version;
            if (ver != null && ver != new Version(0, 0, 0, 0))
            {
                return new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build));
            }

            return new Version(1, 0, 0);
        }
    }

    private UpdateService()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinInstagram-App", "1.0"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool isManual = false, CancellationToken ct = default)
    {
        try
        {
            AppLogger.Info("UPDATER", $"Checking GitHub for latest release... (Current version: {CurrentVersion})");
            UpdateCheckStatusUpdated?.Invoke("Checking for updates...");

            using var response = await _httpClient.GetAsync(GitHubApiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = $"Update check returned status code: {response.StatusCode}";
                AppLogger.Warn("UPDATER", err);
                UpdateCheckStatusUpdated?.Invoke(isManual ? "Failed to check for updates. GitHub API unavailable." : string.Empty);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            var cleanTag = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(cleanTag, out var remoteVersion))
            {
                var parts = cleanTag.Split('.');
                if (parts.Length == 2 && int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min))
                {
                    remoteVersion = new Version(maj, min, 0);
                }
                else
                {
                    remoteVersion = new Version(1, 0, 0);
                }
            }

            var updateInfo = new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = remoteVersion,
                TagName = tagName,
                Title = title,
                ReleaseNotes = body
            };

            // Inspect assets
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                JsonElement? chosenAsset = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var aName = asset.TryGetProperty("name", out var anProp) ? anProp.GetString() ?? "" : "";
                    if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        chosenAsset = asset;
                        break;
                    }
                    if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        chosenAsset ??= asset;
                    }
                }

                if (chosenAsset.HasValue)
                {
                    updateInfo.AssetName = chosenAsset.Value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    updateInfo.DownloadUrl = chosenAsset.Value.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    updateInfo.AssetSize = chosenAsset.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                }
            }

            if (remoteVersion > CurrentVersion && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                updateInfo.IsUpdateAvailable = true;
                AppLogger.Success("UPDATER", $"New version found: {remoteVersion} (current: {CurrentVersion})");
                UpdateAvailable?.Invoke(updateInfo);
            }
            else
            {
                updateInfo.IsUpdateAvailable = false;
                AppLogger.Info("UPDATER", $"Application is up to date: {CurrentVersion}");
                if (isManual)
                {
                    UpdateCheckStatusUpdated?.Invoke($"You are running the latest version of WinInstagram ({CurrentVersion}).");
                }
            }

            return updateInfo;
        }
        catch (Exception ex)
        {
            AppLogger.Error("UPDATER", "Failed to check for updates", ex);
            if (isManual)
            {
                UpdateCheckStatusUpdated?.Invoke("Error checking for updates. Please try again later.");
            }
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WinInstagram_Update");
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
        Directory.CreateDirectory(tempDir);

        var downloadedFile = Path.Combine(tempDir, updateInfo.AssetName);
        AppLogger.Info("UPDATER", $"Downloading update asset from {updateInfo.DownloadUrl} to {downloadedFile}...");

        using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.AssetSize;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(downloadedFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            int lastReportedPercent = -1;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct);
                totalRead += read;

                if (totalBytes > 0)
                {
                    int pct = (int)((double)totalRead / totalBytes * 100.0);
                    if (pct != lastReportedPercent)
                    {
                        lastReportedPercent = pct;
                        progress?.Report(pct);
                    }
                }
            }
        }

        AppLogger.Success("UPDATER", "Download completed successfully.");

        // If the downloaded asset is a ZIP, extract WinInstagram.exe from it
        if (downloadedFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractDir = Path.Combine(tempDir, "extracted");
            AppLogger.Info("UPDATER", $"Extracting ZIP package to {extractDir}...");
            ZipFile.ExtractToDirectory(downloadedFile, extractDir, true);

            var exeFiles = Directory.GetFiles(extractDir, "WinInstagram.exe", SearchOption.AllDirectories);
            if (exeFiles.Length > 0)
            {
                return exeFiles[0];
            }

            var anyExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
            if (anyExe.Length > 0)
            {
                return anyExe[0];
            }

            throw new FileNotFoundException("Could not find WinInstagram.exe inside the extracted ZIP release archive.");
        }

        return downloadedFile;
    }

    public void ApplyUpdateAndRelaunch(string newExePath)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        }
        if (string.IsNullOrEmpty(currentExe))
        {
            currentExe = Path.Combine(AppContext.BaseDirectory, "WinInstagram.exe");
        }

        var tempDir = Path.GetDirectoryName(newExePath) ?? Path.GetTempPath();
        var batchPath = Path.Combine(tempDir, "win_ig_updater.cmd");

        // Generate robust batch file that replaces current binary and relaunches
        var batchContent = $@"@echo off
chcp 65001 > nul
timeout /t 1 /nobreak > nul
:retry
del ""{currentExe}"" > nul 2>&1
if exist ""{currentExe}"" (
    timeout /t 1 /nobreak > nul
    goto retry
)
copy /y ""{newExePath}"" ""{currentExe}"" > nul
start """" ""{currentExe}""
";

        File.WriteAllText(batchPath, batchContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{batchPath}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        AppLogger.Info("UPDATER", $"Launching update replacer script: {batchPath}");
        Process.Start(psi);

        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }
}
