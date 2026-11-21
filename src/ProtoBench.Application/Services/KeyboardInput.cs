namespace ProtoBench.Application.Services;

public sealed record KeyboardKey(string Name, ushort VirtualKey, bool Extended = false)
{
    public override string ToString() => Name;
}

public static class KeyboardKeys
{
    public static IReadOnlyList<KeyboardKey> All { get; } = Array.AsReadOnly(
        Enumerable.Range(1, 24).Select(n => new KeyboardKey($"F{n}", (ushort)(0x6F + n)))
        .Concat(Enumerable.Range('A', 26).Select(n => new KeyboardKey(((char)n).ToString(), (ushort)n)))
        .Concat(Enumerable.Range('0', 10).Select(n => new KeyboardKey(((char)n).ToString(), (ushort)n)))
        .Concat(new KeyboardKey[]
        {
            new("Space", 0x20), new("Enter", 0x0D), new("Tab", 0x09), new("Escape", 0x1B), new("Backspace", 0x08),
            new("Left arrow", 0x25, true), new("Up arrow", 0x26, true), new("Right arrow", 0x27, true), new("Down arrow", 0x28, true),
            new("Home", 0x24, true), new("End", 0x23, true), new("Page up", 0x21, true), new("Page down", 0x22, true),
            new("Insert", 0x2D, true), new("Delete", 0x2E, true)
        }).ToArray());
}

public enum KeyPressResult { Sent, HeldKeySkipped }

public interface IKeyboardInput
{
    KeyPressResult Press(KeyboardKey key);
}
