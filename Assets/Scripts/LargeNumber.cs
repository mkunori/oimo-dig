using System;
using System.Globalization;

// Nonnegative values with double precision and a base-10 exponent independent of double's range.
public readonly struct LargeNumber : IComparable<LargeNumber>
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly double significand;
    public long Exponent { get; }
    public bool IsZero => significand == 0;
    public static LargeNumber One => new(1, 0);
    public double Log10 => IsZero ? double.NegativeInfinity : Exponent + Math.Log10(significand);

    public static LargeNumber PowerOfTen(long exponent) => new(1, exponent);

    private LargeNumber(double significand, long exponent)
    {
        this.significand = significand;
        Exponent = exponent;
    }

    private static LargeNumber Normalize(double value, long exponent)
    {
        if (value <= 0) return default;
        while (value >= 10) { value /= 10; exponent++; }
        while (value < 1) { value *= 10; exponent--; }
        return new LargeNumber(value, exponent);
    }

    public static LargeNumber From(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) return default;
        int exponent = Math.Max(-308, (int)Math.Floor(Math.Log10(value)));
        return Normalize(value / Math.Pow(10, exponent), exponent);
    }

    internal static LargeNumber FromLog10(double logarithm)
    {
        long exponent = (long)Math.Floor(logarithm);
        return Normalize(Math.Pow(10, logarithm - exponent), exponent);
    }

    public static LargeNumber Power(double growth, long level)
    {
        // Split powers before multiplication so an ordinary double never stores the full result.
        LargeNumber result = One;
        LargeNumber factor = From(growth);
        while (level > 0)
        {
            if ((level & 1) != 0) result *= factor;
            level >>= 1;
            if (level > 0) factor *= factor;
        }
        return result;
    }

    public int CompareTo(LargeNumber other)
    {
        if (IsZero || other.IsZero) return significand.CompareTo(other.significand);
        int order = Exponent.CompareTo(other.Exponent);
        return order == 0 ? significand.CompareTo(other.significand) : order;
    }

    public static bool operator <(LargeNumber a, LargeNumber b) => a.CompareTo(b) < 0;
    public static bool operator >(LargeNumber a, LargeNumber b) => a.CompareTo(b) > 0;
    public static bool operator <=(LargeNumber a, LargeNumber b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LargeNumber a, LargeNumber b) => a.CompareTo(b) >= 0;

    public static LargeNumber operator +(LargeNumber a, LargeNumber b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a < b) (a, b) = (b, a);
        // At a gap of 17 decimal places the smaller term is beyond stored precision.
        if (CanUseDecimal(a, b)) return From((double)(ToDecimal(a) + ToDecimal(b)));
        long gap = b.Exponent - a.Exponent;
        return gap < -17 ? a : Normalize(a.significand + b.significand * Math.Pow(10, gap), a.Exponent);
    }

    public static LargeNumber operator -(LargeNumber a, LargeNumber b)
    {
        if (b.IsZero) return a;
        if (a <= b) return default;
        if (CanUseDecimal(a, b)) return From((double)(ToDecimal(a) - ToDecimal(b)));
        long gap = b.Exponent - a.Exponent;
        return gap < -17 ? a : Normalize(a.significand - b.significand * Math.Pow(10, gap), a.Exponent);
    }

    public static LargeNumber operator *(LargeNumber a, LargeNumber b)
        => a.IsZero || b.IsZero ? default : Normalize(a.significand * b.significand, a.Exponent + b.Exponent);

    public LargeNumber Multiply(double scale) => this * From(scale);

    private static bool CanUseDecimal(LargeNumber a, LargeNumber b)
        => a.Exponent >= -12 && a.Exponent <= 12 && b.Exponent >= -12 && b.Exponent <= 12;

    private static decimal ToDecimal(LargeNumber value)
        => (decimal)value.significand * (decimal)Math.Pow(10, value.Exponent);

    public LargeNumber Ceiling()
    {
        if (IsZero || Exponent >= 15) return this;
        if (Exponent < 0) return One;
        double value = significand * Math.Pow(10, Exponent);
        double nearest = Math.Round(value);
        return From(Math.Abs(value - nearest) <= Math.Max(1, value) * 1e-15 ? nearest : Math.Ceiling(value));
    }

    public float FractionOf(LargeNumber total)
    {
        if (IsZero || total.IsZero) return 0;
        if (this >= total) return 1;
        double gap = (double)Exponent - total.Exponent;
        return gap < -45 ? 0 : (float)(significand / total.significand * Math.Pow(10, gap));
    }

    public string Encode() => significand.ToString("R", Invariant) + "|" + Exponent.ToString(Invariant);

    public static bool TryDecode(string text, out LargeNumber value)
    {
        value = default;
        string[] parts = text?.Split('|');
        if (parts == null || parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, Invariant, out double mantissa) ||
            !long.TryParse(parts[1], NumberStyles.Integer, Invariant, out long exponent))
            return false;
        // Leave room for arithmetic on externally edited saves.
        if (mantissa == 0) return exponent == 0;
        if (!(mantissa >= 1 && mantissa < 10) || exponent < long.MinValue / 4 || exponent > long.MaxValue / 4)
            return false;
        value = new LargeNumber(mantissa, exponent);
        return true;
    }

    public string Format()
    {
        if (IsZero) return "0";
        if (Exponent >= -3 && Exponent < 6)
            return (significand * Math.Pow(10, Exponent)).ToString("0.###", Invariant);
        return significand.ToString("0.##", Invariant) + "e" + Exponent.ToString(Invariant);
    }

    // Harvest rewards are whole potatoes. Keep all stored significant digits
    // in the floating total so grouping rewards does not introduce UI rounding.
    public string FormatWholeReward()
        => Exponent < 15
            ? Math.Round(significand * Math.Pow(10, Exponent)).ToString("0", Invariant)
            : significand.ToString("R", Invariant) + "e" + Exponent.ToString(Invariant);
}
