namespace NonStopToolkit.Utilities.Payments;

public static class Luhn
{
    // Spaces and hyphens are presentation separators; other non-ASCII digits are rejected.
    private static string Digits(string input, int minimum, int maximum)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > 256) throw new FormatException("Input is limited to 256 characters.");
        var digits = input.Replace(" ", "").Replace("-", "");
        if (digits.Length < minimum || digits.Length > maximum || digits.Any(c => c is < '0' or > '9'))
            throw new FormatException($"Enter {minimum}–{maximum} ASCII digits; spaces and hyphens are allowed.");
        return digits;
    }

    private static int Sum(string digits, bool doubleLast)
    {
        var sum = 0;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (doubleLast) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            doubleLast = !doubleLast;
        }
        return sum;
    }

    public static bool Validate(string input) => Sum(Digits(input, 2, 19), false) % 10 == 0;
    public static int CheckDigit(string prefix) => (10 - Sum(Digits(prefix, 1, 18), true) % 10) % 10;
}
