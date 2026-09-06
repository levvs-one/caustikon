using System.Runtime.CompilerServices;

namespace Caustikon;

/// <summary>Shared storage and arithmetic for dispersion models written as <c>offset + Σ aᵢ λ^pᵢ</c>, λ in micrometers.</summary>
internal readonly struct PowerSeries : IEquatable<PowerSeries>
{
    public const int MaximumTermCount = 8;

    [InlineArray(MaximumTermCount)]
    private struct TermBuffer
    {
        private double element0;
    }

    private readonly TermBuffer coefficients;
    private readonly TermBuffer exponents;

    public PowerSeries(
        double offset,
        ReadOnlySpan<double> coefficients,
        ReadOnlySpan<double> exponents,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        Dispersion.ThrowIfNotFinite(offset, nameof(offset));
        if (coefficients.Length != exponents.Length)
        {
            throw new ArgumentException("Coefficients and exponents must have the same length.", nameof(exponents));
        }

        if (coefficients.Length > MaximumTermCount)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficients), coefficients.Length, $"At most {MaximumTermCount} terms are supported.");
        }

        for (int i = 0; i < coefficients.Length; i++)
        {
            Dispersion.ThrowIfNotFinite(coefficients[i], nameof(coefficients));
            Dispersion.ThrowIfNotFinite(exponents[i], nameof(exponents));
        }

        Dispersion.ValidateRange(minimumWavelengthNanometers, maximumWavelengthNanometers);

        for (int i = 0; i < coefficients.Length; i++)
        {
            this.coefficients[i] = coefficients[i];
            this.exponents[i] = exponents[i];
        }

        Offset = offset;
        TermCount = coefficients.Length;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
    }

    public double Offset { get; }

    public int TermCount { get; }

    public double MinimumWavelengthNanometers { get; }

    public double MaximumWavelengthNanometers { get; }

    public double CoefficientAt(int index)
    {
        ThrowIfTermOutOfRange(index);
        return coefficients[index];
    }

    public double ExponentAt(int index)
    {
        ThrowIfTermOutOfRange(index);
        return exponents[index];
    }

    /// <summary>Evaluates the series; returns <see cref="DispersionStatus.Success"/> with a finite sum, or a failure status with NaN.</summary>
    public DispersionStatus Evaluate(double wavelengthNanometers, out double sum)
    {
        DispersionStatus status = Dispersion.ClassifyWavelength(wavelengthNanometers, MinimumWavelengthNanometers, MaximumWavelengthNanometers);
        if (status != DispersionStatus.Success)
        {
            sum = double.NaN;
            return status;
        }

        double wavelengthMicrometers = wavelengthNanometers * 0.001d;
        double total = Offset;
        for (int i = 0; i < TermCount; i++)
        {
            double coefficient = coefficients[i];
            if (coefficient == 0d)
            {
                continue;
            }

            total += ScaledPower(coefficient, wavelengthMicrometers, exponents[i]);
        }

        if (!double.IsFinite(total))
        {
            sum = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        sum = total;
        return DispersionStatus.Success;
    }

    public bool Equals(PowerSeries other)
    {
        if (Offset != other.Offset ||
            TermCount != other.TermCount ||
            MinimumWavelengthNanometers != other.MinimumWavelengthNanometers ||
            MaximumWavelengthNanometers != other.MaximumWavelengthNanometers)
        {
            return false;
        }

        for (int i = 0; i < TermCount; i++)
        {
            if (coefficients[i] != other.coefficients[i] || exponents[i] != other.exponents[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is PowerSeries other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Offset);
        hash.Add(TermCount);
        hash.Add(MinimumWavelengthNanometers);
        hash.Add(MaximumWavelengthNanometers);
        for (int i = 0; i < TermCount; i++)
        {
            hash.Add(coefficients[i]);
            hash.Add(exponents[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes <c>coefficient · λ^exponent</c>. Integer exponents of magnitude up to 16 use repeated multiplication or
    /// division applied to the coefficient, so a small coefficient over a tiny wavelength does not overflow an intermediate
    /// power before the ratio is formed. Other exponents use <see cref="Math.Pow"/>.
    /// </summary>
    private static double ScaledPower(double coefficient, double wavelengthMicrometers, double exponent)
    {
        if (exponent == 0d)
        {
            return coefficient;
        }

        if (exponent == Math.Truncate(exponent) && Math.Abs(exponent) <= 16d)
        {
            int count = (int)Math.Abs(exponent);
            double value = coefficient;
            if (exponent > 0d)
            {
                for (int i = 0; i < count; i++)
                {
                    value *= wavelengthMicrometers;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    value /= wavelengthMicrometers;
                }
            }

            return value;
        }

        return coefficient * Math.Pow(wavelengthMicrometers, exponent);
    }

    private void ThrowIfTermOutOfRange(int index)
    {
        if ((uint)index >= (uint)TermCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The term index must be below TermCount.");
        }
    }
}
