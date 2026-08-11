using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Translation;
using TranslationApp.Settings;

namespace TranslationApp.Tests;

public sealed class NllbIpcTests
{
    private static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "Runtime", "nllb_worker.py");

    [Fact]
    public void ClientIpcEncodingHasNoBom() => Assert.Empty(NllbTranslator.IpcEncoding.GetPreamble());

    [Fact]
    public async Task FirstRequestAndRequestAfterWorkerRestartRoundTripUnicodeJson()
    {
        var settings = new AppSettings { PythonExecutable = "python", ModelPath = "unused-for-self-test" };
        using var translator = new NllbTranslator(settings, new AppLogger(), WorkerPath, testEchoMode: true);

        var first = "한글 English \"quotes\" \\ slash <tag> & symbols\n새 줄 😀";
        var firstResult = await translator.TranslateAsync(first, TargetLanguage.English, TranslationOptions.Primary, CancellationToken.None);
        Assert.Equal(first, firstResult.Primary.Text);

        translator.TerminateWorkerForTest();
        var afterRestart = "재시작 후: 한국어 + English + 日本語 + © € {json}";
        var restartedResult = await translator.TranslateAsync(afterRestart, TargetLanguage.Korean, TranslationOptions.Primary, CancellationToken.None);
        Assert.Equal(afterRestart, restartedResult.Primary.Text);
    }

    [Fact]
    public async Task WorkerStdoutStartsWithJsonAndNeverBom()
    {
        using var process = StartEchoWorker();
        var prefix = new byte[3];
        var read = 0;
        while (read < prefix.Length)
        {
            var count = await process.StandardOutput.BaseStream.ReadAsync(prefix.AsMemory(read, prefix.Length - read));
            if (count == 0) break;
            read += count;
        }
        Assert.Equal(3, read);
        Assert.Equal((byte)'{', prefix[0]);
        Assert.False(prefix.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Stop(process);
    }

    [Fact]
    public async Task WorkerDefensivelyAcceptsBomOnFirstInputLine()
    {
        using var process = StartEchoWorker();
        var ready = await process.StandardOutput.ReadLineAsync();
        Assert.NotNull(ready);

        var id = Guid.NewGuid().ToString("N");
        var text = "BOM 방어: English/한글/!@#$%^&*()";
        var json = JsonSerializer.Serialize(new
        {
            type = "translate", id, text,
            sourceLanguage = "eng_Latn", targetLanguage = "kor_Hang",
            candidateCount = 1, beamWidth = 1, maxNewTokens = 32
        }) + "\n";
        await process.StandardInput.BaseStream.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF });
        await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(json));
        await process.StandardInput.BaseStream.FlushAsync();

        var responseLine = await process.StandardOutput.ReadLineAsync();
        Assert.NotNull(responseLine);
        using var response = JsonDocument.Parse(responseLine);
        Assert.Equal("result", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(text, response.RootElement.GetProperty("candidates")[0].GetProperty("text").GetString());
        Stop(process);
    }

    [Fact]
    [Trait("Category", "ModelIntegration")]
    public async Task ActualLocalModelAcceptsBomlessFirstRequest()
    {
        if (Environment.GetEnvironmentVariable("TRANSLATIONAPP_RUN_MODEL_TESTS") != "1") return;
        var settings = new AppSettings { PythonExecutable = "python" };
        Assert.True(Directory.Exists(settings.ModelPath), $"Local model not found: {settings.ModelPath}");
        using var translator = new NllbTranslator(settings, new AppLogger(), WorkerPath, testEchoMode: false);

        var result = await translator.TranslateAsync(
            "Hello, world! JSON symbols: <>&\"",
            TargetLanguage.Korean,
            TranslationOptions.Primary,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Primary.Text));
    }

    private static Process StartEchoWorker()
    {
        Assert.True(File.Exists(WorkerPath), $"Worker not found: {WorkerPath}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(WorkerPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add("unused");
        startInfo.ArgumentList.Add("--threads");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--self-test-echo");
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Python test worker did not start.");
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
        process.WaitForExit(5000);
    }
}
