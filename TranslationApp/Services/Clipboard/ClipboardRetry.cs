namespace TranslationApp.Services.Clipboard;

public static class ClipboardRetry
{
    public static async Task<T> RunAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken,
        int attempts = 9,
        int initialDelayMilliseconds = 20,
        int maximumDelayMilliseconds = 500)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return action(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                last = ex;
                if (i + 1 < attempts)
                {
                    var delay = Math.Min(initialDelayMilliseconds * (1 << Math.Min(i, 20)), maximumDelayMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        throw new InvalidOperationException("클립보드에 접근할 수 없습니다.", last);
    }
}
