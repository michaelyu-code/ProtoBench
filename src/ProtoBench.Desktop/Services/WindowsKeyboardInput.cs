using System.Runtime.InteropServices;
using ProtoBench.Application.Services;

namespace ProtoBench.Desktop.Services;

public sealed class WindowsKeyboardInput : IKeyboardInput
{
    public KeyPressResult Press(KeyboardKey key)
    {
        if (!KeyboardKeys.All.Contains(key)) throw new ArgumentException("Select a supported keyboard key.", nameof(key));
        // Do not turn a single key into a shortcut or release a key the user is holding.
        foreach (var code in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, (int)key.VirtualKey })
            if ((GetAsyncKeyState(code) & 0x8000) != 0) return KeyPressResult.HeldKeySkipped;
        var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key.VirtualKey, Flags = key.Extended ? 1u : 0u } } };
        var up = down;
        up.Data.Keyboard.Flags |= 2;
        var sent = SendInput(2, [down, up], Marshal.SizeOf<Input>());
        if (sent == 2) return KeyPressResult.Sent;
        var codeError = Marshal.GetLastWin32Error();
        // If only the down event was accepted, make a best-effort release.
        if (sent == 1) SendInput(1, [up], Marshal.SizeOf<Input>());
        throw new InvalidOperationException($"Windows did not accept the complete key press (error {codeError}). The focused app may require higher permissions.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
