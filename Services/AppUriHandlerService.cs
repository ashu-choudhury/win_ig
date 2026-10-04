using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinInstagram.Services;

public class AppUriHandlerService
{
    private static readonly Lazy<AppUriHandlerService> _instance = new(() => new AppUriHandlerService());
    public static AppUriHandlerService Instance => _instance.Value;

    private const string PackageName = "WinInstagram.App";
    private const string PackageFamilyName = "WinInstagram.App_hmkf4qeh88sdj";

    private AppUriHandlerService() { }

    public bool IsRegistered()
    {
        try
        {
            using var keyWin = Registry.CurrentUser.OpenSubKey(@"Software\Classes\wininstagram\shell\open\command");
            using var keyIg = Registry.CurrentUser.OpenSubKey(@"Software\Classes\instagram\shell\open\command");
            if (keyWin != null && keyIg != null) return true;

            using var appUriKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\LocalSettings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{PackageFamilyName}\AppUriHandlers");
            if (appUriKey != null && (int)(appUriKey.GetValue("ForceValidation", 0) ?? 0) == 1) return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("APP_URI", "Failed checking registration status", ex);
        }
        return false;
    }

    public async Task<(bool success, string message)> RegisterAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinInstagram.exe");
                var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

                // 1. Register User-Level Protocol Handlers (wininstagram:// and instagram://)
                RegisterProtocol("wininstagram", "URL:WinInstagram Protocol", exePath);
                RegisterProtocol("instagram", "URL:Instagram Protocol", exePath);
                AppLogger.Info("APP_URI", "Registered wininstagram:// and instagram:// protocols in HKCU");

                // 2. Ensure Assets and AppxManifest.xml exist
                EnsureManifestAndAssets(appDir, exePath);

                // 3. Attempt Sparse Package Registration & ForceValidation for https://instagram.com
                bool sparseSuccess = false;
                string sparseError = "";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Register '{Path.Combine(appDir, "AppxManifest.xml")}' -ExternalLocation '{appDir}'\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var err = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        if (proc.ExitCode == 0)
                        {
                            sparseSuccess = true;
                            AppLogger.Info("APP_URI", "Sparse package registered successfully via Add-AppxPackage");
                        }
                        else
                        {
                            sparseError = err;
                            AppLogger.Warn("APP_URI", $"Add-AppxPackage exit code {proc.ExitCode}: {err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sparseError = ex.Message;
                    AppLogger.Warn("APP_URI", $"Sparse package registration skipped: {ex.Message}");
                }

                // 4. Set ForceValidation in HKCU if package exists or for testing
                try
                {
                    using var appUriKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\LocalSettings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{PackageFamilyName}\AppUriHandlers");
                    if (appUriKey != null)
                    {
                        appUriKey.SetValue("ForceValidation", 1, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("APP_URI", $"Could not set ForceValidation key: {ex.Message}");
                }

                if (sparseSuccess)
                {
                    return (true, "Registered successfully! WinInstagram is now configured to handle instagram.com web links and protocols.");
                }
                else
                {
                    return (true, "Protocols registered! (wininstagram:// & instagram://). Note: For direct https:// URLs without browser prompt, Windows requires Developer Mode (Settings > For developers).");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("APP_URI", "Registration failed", ex);
                return (false, $"Failed to register: {ex.Message}");
            }
        });
    }

    public async Task<(bool success, string message)> UnregisterAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Unregister protocols
                UnregisterProtocol("wininstagram");
                UnregisterProtocol("instagram");

                // 2. Clear ForceValidation
                try
                {
                    using var appUriKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\LocalSettings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{PackageFamilyName}\AppUriHandlers", true);
                    if (appUriKey != null)
                    {
                        appUriKey.DeleteValue("ForceValidation", false);
                    }
                }
                catch { }

                // 3. Remove AppX package
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-AppxPackage -Name '{PackageName}' | Remove-AppxPackage\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                }
                catch { }

                AppLogger.Info("APP_URI", "Unregistered protocols and AppUriHandler");
                return (true, "Unregistered successfully. WinInstagram will no longer intercept Instagram web links.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("APP_URI", "Unregister failed", ex);
                return (false, $"Failed to unregister: {ex.Message}");
            }
        });
    }

    private static void RegisterProtocol(string protocol, string description, string exePath)
    {
        using var protoKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}");
        if (protoKey == null) return;

        protoKey.SetValue("", description);
        protoKey.SetValue("URL Protocol", "");

        using var cmdKey = protoKey.CreateSubKey(@"shell\open\command");
        cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    private static void UnregisterProtocol(string protocol)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocol}", false);
        }
        catch { }
    }

    private static void EnsureManifestAndAssets(string appDir, string exePath)
    {
        try
        {
            var assetsDir = Path.Combine(appDir, "Assets");
            if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);

            var manifestPath = Path.Combine(appDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                var exeName = Path.GetFileName(exePath);
                var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Package
  xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""
  xmlns:uap=""http://schemas.microsoft.com/appx/manifest/uap/windows10""
  xmlns:uap3=""http://schemas.microsoft.com/appx/manifest/uap/windows10/3""
  xmlns:uap10=""http://schemas.microsoft.com/appx/manifest/uap/windows10/10""
  xmlns:rescap=""http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities""
  IgnorableNamespaces=""uap uap3 uap10 rescap"">

  <Identity
    Name=""{PackageName}""
    Publisher=""CN=WinInstagram""
    Version=""1.0.0.0""
    ProcessorArchitecture=""x64"" />

  <Properties>
    <DisplayName>WinInstagram</DisplayName>
    <PublisherDisplayName>WinInstagram</PublisherDisplayName>
    <Logo>Assets\Square150x150Logo.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name=""Windows.Desktop"" MinVersion=""10.0.19041.0"" MaxVersionTested=""10.0.22621.0"" />
  </Dependencies>

  <Capabilities>
    <rescap:Capability Name=""runFullTrust"" />
    <rescap:Capability Name=""unvirtualizedResources"" />
  </Capabilities>

  <Applications>
    <Application Id=""App""
      Executable=""{exeName}""
      EntryPoint=""Windows.FullTrustApplication"">
      <uap:VisualElements
        DisplayName=""WinInstagram""
        Description=""WinInstagram Desktop""
        BackgroundColor=""transparent""
        Square150x150Logo=""Assets\Square150x150Logo.png""
        Square44x44Logo=""Assets\Square44x44Logo.png"" />
      <Extensions>
        <uap3:Extension Category=""windows.appUriHandler"">
          <uap3:AppUriHandler>
            <uap3:Host Name=""instagram.com"" />
            <uap3:Host Name=""www.instagram.com"" />
          </uap3:AppUriHandler>
        </uap3:Extension>
        <uap:Extension Category=""windows.protocol"">
          <uap:Protocol Name=""instagram"">
            <uap:DisplayName>Instagram Link</uap:DisplayName>
          </uap:Protocol>
        </uap:Extension>
        <uap:Extension Category=""windows.protocol"">
          <uap:Protocol Name=""wininstagram"">
            <uap:DisplayName>WinInstagram Link</uap:DisplayName>
          </uap:Protocol>
        </uap:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>";
                File.WriteAllText(manifestPath, xml, System.Text.Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("APP_URI", $"EnsureManifestAndAssets warning: {ex.Message}");
        }
    }
}
