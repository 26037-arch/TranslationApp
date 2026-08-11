using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TranslationApp.Services.Clipboard;

public interface ICopyShortcutSender
{
    void SendCopy();
}

public sealed class WindowsCopyShortcutSender : ICopyShortcutSender
{
    public void SendCopy()
    {
        var inputs = new[]
        {
            Input.Keyboard(VkControl, false), Input.Keyboard(0x43, false),
            Input.Keyboard(0x43, true), Input.Keyboard(VkControl, true)
        };
        var inputSize = Marshal.SizeOf<Input>();
        var sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"Ctrl+C 입력을 보낼 수 없습니다. SendInput={sent}/{inputs.Length}, INPUT={inputSize} bytes, Win32={error}.");
        }
    }

    internal static int NativeInputSize => Marshal.SizeOf<Input>();
    internal static int NativeKeyboardInputSize => Marshal.SizeOf<KeyboardInput>();
    internal static int NativeMouseInputSize => Marshal.SizeOf<MouseInput>();
    internal static int ExpectedInputSize => IntPtr.Size == 8 ? 40 : 28;

    private const ushort VkControl = 0x11;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
        public static Input Keyboard(ushort key, bool up) => new()
        {
            Type = InputKeyboard,
            Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? KeyeventfKeyup : 0 } }
        };
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        // INPUT's union size is determined by MOUSEINPUT, not KEYBDINPUT.
        // Omitting the other members makes INPUT 32 bytes instead of 40 on x64,
        // which causes SendInput to fail with ERROR_INVALID_PARAMETER (87).
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
