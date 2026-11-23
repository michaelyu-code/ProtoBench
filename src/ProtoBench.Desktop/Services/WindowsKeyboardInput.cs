using System.Runtime.InteropServices;
using ProtoBench.Application.Services;

namespace ProtoBench.Desktop.Services;

public sealed class WindowsKeyboardInput : IKeyboardInput
{
    public void Press(KeyboardKey key)
    {
        if (!KeyboardKeys.All.Contains(key)) throw new ArgumentException("Select a supported keyboard key.", nameof(key));
        // Send the selected key on every interval without changing modifier state.
        var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key.VirtualKey, Flags = key.Extended ? 1u : 0u } } };
        var up = down;
        up.Data.Keyboard.Flags |= 2;
        var sent = SendInput(2, [down, up], Marshal.SizeOf<Input>());
        if (sent == 2) return;
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
}
