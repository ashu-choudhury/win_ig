using System;
using System.IO;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinInstagram.Services;

public class DeepLinkService
{
    private static readonly Lazy<DeepLinkService> _instance = new(() => new DeepLinkService());
    public static DeepLinkService Instance => _instance.Value;

    private const string MutexName = "WinInstagram_SingleInstance_AppMutex";
    private const string PipeName = "WinInstagram_DeepLink_Pipe";

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _pipeCts;

    public event Action<string>? LinkActivated;

    private DeepLinkService() { }

    public bool CheckSingleInstanceAndForward(string[] args)
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already running! Forward arguments to it via Named Pipe
                var linkToSend = args.Length > 0 ? string.Join(" ", args) : "ACTIVATE";
                SendToPrimaryInstance(linkToSend);
                return false; // Exit this secondary instance
            }

            // This is the primary instance - start background pipe server
            StartPipeServer();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DEEP_LINK", "Error checking single instance", ex);
            return true;
        }
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_pipeCts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_pipeCts.Token);

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        var cleanUrl = NormalizeInstagramUrl(message.Trim());
                        AppLogger.Info("DEEP_LINK", $"Received deep link via pipe: {cleanUrl}");
                        LinkActivated?.Invoke(cleanUrl);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("DEEP_LINK", $"Pipe server connection error: {ex.Message}");
                    await Task.Delay(500);
                }
            }
        });
    }

    private static void SendToPrimaryInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(message);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DEEP_LINK", $"Could not send message to primary instance: {ex.Message}");
        }
    }

    public static string NormalizeInstagramUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "ACTIVATE") return string.Empty;

        var clean = raw.Trim('"', '\'', ' ');

        // Convert wininstagram:// or instagram:// protocols
        if (clean.StartsWith("wininstagram://", StringComparison.OrdinalIgnoreCase))
        {
            clean = "https://www.instagram.com/" + clean["wininstagram://".Length..].TrimStart('/');
        }
        else if (clean.StartsWith("instagram://", StringComparison.OrdinalIgnoreCase))
        {
            clean = "https://www.instagram.com/" + clean["instagram://".Length..].TrimStart('/');
        }
        else if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                 clean.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
        {
            clean = "https://" + clean.TrimStart('/');
        }

        // Clean query parameters like ?igsh=...
        var match = Regex.Match(clean, @"https?://(www\.)?instagram\.com/(reel|reels|p|stories)/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var type = match.Groups[2].Value.ToLower();
            if (type == "reels") type = "reel";
            var id = match.Groups[3].Value;
            return $"https://www.instagram.com/{type}/{id}/";
        }

        return clean;
    }

    public void Shutdown()
    {
        try
        {
            _pipeCts?.Cancel();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
    }
}
