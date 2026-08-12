using System.Diagnostics;
using System.Net.Http;
using System.Security;
using Microsoft.Web.WebView2.Core;
using TranslationApp.Services.Logging;

namespace TranslationApp.Services.Translation;

public sealed class WebView2RuntimeInstaller : IDisposable
{
    internal static readonly Uri EvergreenBootstrapperUri =
        new("https://go.microsoft.com/fwlink/p/?LinkId=2124703");

    private readonly HttpClient _httpClient;
    private readonly AppLogger _logger;

    public WebView2RuntimeInstaller(AppLogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException) { return false; }
        catch (FileNotFoundException) { return false; }
    }

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (IsRuntimeAvailable()) return;

        _logger.Info("WebView2 Runtime not found; downloading the official Evergreen bootstrapper");
        var installerPath = Path.Combine(
            Path.GetTempPath(),
            $"TranslationApp-WebView2-{Guid.NewGuid():N}.exe");
        try
        {
            using var response = await _httpClient.GetAsync(
                EvergreenBootstrapperUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var finalAddress = response.RequestMessage?.RequestUri
                ?? throw new InvalidOperationException("WebView2 Runtime download address is missing.");
            if (!IsOfficialMicrosoftDownload(finalAddress))
                throw new SecurityException($"WebView2 Runtime download was redirected to an untrusted host: {finalAddress.Host}");

            await using (var output = new FileStream(
                installerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(output, cancellationToken);
            }

            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/silent", "/install" }
            }) ?? throw new InvalidOperationException("WebView2 Runtime installer could not be started.");
            await installer.WaitForExitAsync(cancellationToken);
            if (installer.ExitCode is not (0 or 3010))
                throw new InvalidOperationException($"WebView2 Runtime installation failed (exit code {installer.ExitCode}).");

            for (var check = 0; check < 6 && !IsRuntimeAvailable(); check++)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (!IsRuntimeAvailable())
                throw new InvalidOperationException("WebView2 Runtime installation completed, but the Runtime is still unavailable.");

            _logger.Info("WebView2 Evergreen Runtime installed successfully");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Microsoft WebView2 Runtime을 자동으로 설치하지 못했습니다. 인터넷 연결과 Windows 설치 권한을 확인하세요.",
                ex);
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); }
            catch (Exception ex) { _logger.Error("Failed to remove the temporary WebView2 installer", ex); }
        }
    }

    internal static bool IsOfficialMicrosoftDownload(Uri address)
    {
        if (!string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(address.Host, "go.microsoft.com", StringComparison.OrdinalIgnoreCase)
               || address.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _httpClient.Dispose();
}
