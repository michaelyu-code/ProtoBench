using System.Globalization;

namespace ProtoBench.Utilities.Numeric;

public enum BitwiseOperation { And, Or, Xor, Not, ShiftLeft, ShiftRight, RotateLeft, RotateRight }
public sealed record BitwiseResult(ulong Value, string Hex, string Binary, string Decimal);

public static class BitwiseCalculator
{
    public static BitwiseResult Calculate(string a, string b, int radix, int width, BitwiseOperation operation)
    {
        if (width is not (8 or 16 or 32 or 64)) throw new ArgumentException("Choose a width of 8, 16, 32 or 64 bits.");
        if (radix is not (2 or 10 or 16)) throw new ArgumentException("Choose base 2, 10 or 16.");
        if (!Enum.IsDefined(operation)) throw new ArgumentException("Choose a supported bitwise operation.");
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        var left = Parse(a, radix, mask);
        ulong result;
        if (operation == BitwiseOperation.Not) result = ~left & mask;
        else if (operation is BitwiseOperation.ShiftLeft or BitwiseOperation.ShiftRight or BitwiseOperation.RotateLeft or BitwiseOperation.RotateRight)
        {
            if (b.Length > 128 || !int.TryParse(b.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var shift) || shift < 0 || shift > width)
                throw new FormatException($"The shift/rotation count must be a decimal number from 0 to {width}.");
            result = operation switch
            {
                BitwiseOperation.ShiftLeft => shift == width ? 0 : (left << shift) & mask,
                BitwiseOperation.ShiftRight => shift == width ? 0 : left >> shift,
                BitwiseOperation.RotateLeft => shift == 0 || shift == width ? left : ((left << shift) | (left >> (width - shift))) & mask,
                _ => shift == 0 || shift == width ? left : ((left >> shift) | (left << (width - shift))) & mask
            };
        }
        else
        {
            var right = Parse(b, radix, mask);
            result = operation switch { BitwiseOperation.And => left & right, BitwiseOperation.Or => left | right, _ => left ^ right };
        }
        return new(result, result.ToString($"X{width / 4}", CultureInfo.InvariantCulture),
            string.Concat(Enumerable.Range(0, width).Reverse().Select(i => ((result >> i) & 1) == 0 ? '0' : '1')),
            result.ToString(CultureInfo.InvariantCulture));
    }

    private static ulong Parse(string input, int radix, ulong mask)
    {
        if (input.Length > 128) throw new FormatException("Each operand is limited to 128 characters.");
        var text = input.Trim();
        if (radix == 16 && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || radix == 2 && text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length == 0) throw new FormatException("Enter an unsigned operand in the selected base.");
        ulong value = 0;
        foreach (var c in text)
        {
            var digit = c switch { >= '0' and <= '9' => c - '0', >= 'A' and <= 'F' => c - 'A' + 10, >= 'a' and <= 'f' => c - 'a' + 10, _ => -1 };
            if (digit < 0 || digit >= radix) throw new FormatException("Operands must contain unsigned digits in the selected base; internal spaces are not accepted.");
            if ((ulong)digit > mask || value > (mask - (ulong)digit) / (ulong)radix) throw new FormatException("Operand exceeds the selected bit width.");
            value = value * (ulong)radix + (ulong)digit;
        }
        return value;
    }
}
