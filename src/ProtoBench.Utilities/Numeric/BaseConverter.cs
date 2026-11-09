using System.Numerics;
using System.Text;
namespace ProtoBench.Utilities.Numeric;

public interface IBaseConverter
{
    string Convert(string input, int fromBase, int toBase);
}
public sealed class BaseConverter : IBaseConverter
{
    public string Convert(string input, int fromBase, int toBase)
    {
        if (fromBase is not (2 or 10 or 16) || toBase is not (2 or 10 or 16))
            throw new ArgumentException("Supported bases are 2, 10 and 16.");
        var text = input.Trim();
        if (text.Length == 0)
            throw new FormatException("Enter a value to convert.");
        if (text.Length > 16384)
            throw new FormatException("Input is limited to 16,384 characters.");
        var negative = text[0] == '-';
        if (text[0] is '-' or '+')
            text = text[1..];
        if (fromBase == 16 && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        if (fromBase == 2 && text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        if (text.Length == 0)
            throw new FormatException("At least one digit is required.");
        BigInteger value = 0;
        foreach (var c in text)
        {
            int digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
            if (digit < 0 || digit >= fromBase)
                throw new FormatException($"'{c}' is not a valid base-{fromBase} digit.");
            value = value * fromBase + digit;
        }
        if (value.IsZero)
            return "0";
        var result = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, toBase, out var digit);
            result.Append("0123456789ABCDEF"[(int)digit]);
        }
        if (negative)
            result.Append('-');
        return new string(result.ToString().Reverse().ToArray());
    }
}
