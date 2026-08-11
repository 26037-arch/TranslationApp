using System.Runtime.InteropServices;
using System.Windows.Threading;
using TranslationApp.Services.Clipboard;

namespace TranslationApp.Tests;

public sealed class WindowsCopyShortcutSenderTests
{
    [Fact]
    public void NativeInputLayoutMatchesWindowsAbi()
    {
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, WindowsCopyShortcutSender.NativeKeyboardInputSize);
        Assert.Equal(IntPtr.Size == 8 ? 32 : 24, WindowsCopyShortcutSender.NativeMouseInputSize);
        Assert.Equal(WindowsCopyShortcutSender.ExpectedInputSize, WindowsCopyShortcutSender.NativeInputSize);
    }

    [Fact]
    public async Task SendCopyInjectsControlAndCKeyDownUpSequence()
    {
        if (!Environment.UserInteractive) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunHookScenario(completion)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(12));
    }

    private static void RunHookScenario(TaskCompletionSource completion)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var injectedEvents = new List<(uint VirtualKey, bool IsKeyUp)>();
        var eventGate = new object();
        LowLevelKeyboardProc? callback = null;
        IntPtr hook = IntPtr.Zero;

        callback = (code, message, data) =>
        {
            if (code >= 0)
            {
                var keyboard = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
                const uint injected = 0x10;
                if ((keyboard.Flags & injected) != 0 && keyboard.VirtualKey is 0x11 or 0xA2 or 0xA3 or 0x43)
                {
                    var keyUp = message.ToInt64() is 0x0101 or 0x0105;
                    var normalizedKey = keyboard.VirtualKey is 0xA2 or 0xA3 ? 0x11u : keyboard.VirtualKey;
                    lock (eventGate) injectedEvents.Add((normalizedKey, keyUp));
                }
            }
            return CallNextHookEx(hook, code, message, data);
        };

        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            completion.TrySetException(new InvalidOperationException($"Keyboard hook failed: {Marshal.GetLastWin32Error()}"));
            return;
        }

        dispatcher.BeginInvoke(async () =>
        {
            ClipboardSnapshot? snapshot = null;
            try
            {
                snapshot = await ClipboardRetry.RunAsync(
                    () => ClipboardSnapshotFactory.Clone(System.Windows.Clipboard.GetDataObject()), CancellationToken.None);
                new WindowsCopyShortcutSender().SendCopy();
                await Task.Delay(250);

                List<(uint VirtualKey, bool IsKeyUp)> actual;
                lock (eventGate) actual = injectedEvents.ToList();
                Assert.Equal(new[]
                {
                    (VirtualKey: 0x11u, IsKeyUp: false),
                    (VirtualKey: 0x43u, IsKeyUp: false),
                    (VirtualKey: 0x43u, IsKeyUp: true),
                    (VirtualKey: 0x11u, IsKeyUp: true)
                }, actual);
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                if (snapshot?.Data is not null)
                {
                    try
                    {
                        await ClipboardRetry.RunAsync(() =>
                        {
                            System.Windows.Clipboard.SetDataObject(snapshot.Data, true);
                            return true;
                        }, CancellationToken.None);
                    }
                    catch { }
                }
                UnhookWindowsHookEx(hook);
                GC.KeepAlive(callback);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        }, DispatcherPriority.ApplicationIdle);

        Dispatcher.Run();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
