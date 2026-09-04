using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>
/// Immutable three-resonance Sellmeier model n²(λ) = 1 + Σ Bᵢλ²/(λ²-Cᵢ). Public wavelengths
/// are nanometers; C coefficients are expressed in µm².
/// </summary>
public readonly record struct Sellmeier3
{
    public Sellmeier3(
        double b1,
        double c1Um2,
        double b2,
        double c2Um2,
        double b3,
        double c3Um2,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        ThrowIfNotFinite(b1, nameof(b1));
        ThrowIfInvalidResonance(c1Um2, nameof(c1Um2));
        ThrowIfNotFinite(b2, nameof(b2));
        ThrowIfInvalidResonance(c2Um2, nameof(c2Um2));
        ThrowIfNotFinite(b3, nameof(b3));
        ThrowIfInvalidResonance(c3Um2, nameof(c3Um2));
        ValidateRange(minimumWavelengthNanometers, maximumWavelengthNanometers);
        RejectPoleInRange(b1, c1Um2, minimumWavelengthNanometers, maximumWavelengthNanometers, nameof(c1Um2));
        RejectPoleInRange(b2, c2Um2, minimumWavelengthNanometers, maximumWavelengthNanometers, nameof(c2Um2));
        RejectPoleInRange(b3, c3Um2, minimumWavelengthNanometers, maximumWavelengthNanometers, nameof(c3Um2));

        B1 = b1;
        C1Um2 = c1Um2;
        B2 = b2;
        C2Um2 = c2Um2;
        B3 = b3;
        C3Um2 = c3Um2;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
    }

    public double B1 { get; }
    public double C1Um2 { get; }
    public double B2 { get; }
    public double C2Um2 { get; }
    public double B3 { get; }
    public double C3Um2 { get; }
    public double MinimumWavelengthNanometers { get; }
    public double MaximumWavelengthNanometers { get; }

    /// <summary>Evaluates the model at a wavelength measured in nanometers.</summary>
    /// <remarks>On every non-success status, <paramref name="refractiveIndex"/> is <see cref="double.NaN"/>.</remarks>
    public DispersionStatus EvaluateNanometers(double wavelengthNanometers, out double refractiveIndex)
    {
        if (MinimumWavelengthNanometers <= 0d ||
            MaximumWavelengthNanometers < MinimumWavelengthNanometers)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.InvalidInput;
        }

        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0d)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.InvalidInput;
        }

        if (wavelengthNanometers < MinimumWavelengthNanometers ||
            wavelengthNanometers > MaximumWavelengthNanometers)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.OutsideModelRange;
        }

        double wavelengthMicrometers = wavelengthNanometers * 0.001d;
        double wavelengthSquared = wavelengthMicrometers * wavelengthMicrometers;
        if ((wavelengthSquared == 0d || double.IsSubnormal(wavelengthSquared)) &&
            ((B1 != 0d && C1Um2 > 0d) || (B2 != 0d && C2Um2 > 0d) || (B3 != 0d && C3Um2 > 0d)))
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        double denominator1 = wavelengthSquared - C1Um2;
        double denominator2 = wavelengthSquared - C2Um2;
        double denominator3 = wavelengthSquared - C3Um2;

        if ((B1 != 0d && C1Um2 > 0d && denominator1 == 0d) ||
            (B2 != 0d && C2Um2 > 0d && denominator2 == 0d) ||
            (B3 != 0d && C3Um2 > 0d && denominator3 == 0d))
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.Singular;
        }

        double refractiveIndexSquared = 1d +
            Term(B1, C1Um2, wavelengthMicrometers, wavelengthSquared) +
            Term(B2, C2Um2, wavelengthMicrometers, wavelengthSquared) +
            Term(B3, C3Um2, wavelengthMicrometers, wavelengthSquared);

        if (!double.IsFinite(refractiveIndexSquared) || refractiveIndexSquared <= 0d)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        refractiveIndex = Math.Sqrt(refractiveIndexSquared);
        return DispersionStatus.Success;
    }

    /// <summary>Evaluates a batch of wavelengths measured in nanometers.</summary>
    public void EvaluateNanometers(
        ReadOnlySpan<double> wavelengthsNanometers,
        Span<double> refractiveIndices,
        Span<DispersionStatus> statuses)
    {
        ValidateBatch(wavelengthsNanometers, refractiveIndices, statuses);

        for (int i = 0; i < wavelengthsNanometers.Length; i++)
        {
            statuses[i] = EvaluateNanometers(wavelengthsNanometers[i], out refractiveIndices[i]);
        }
    }

    private static double Term(double strength, double resonanceSquared, double wavelength, double wavelengthSquared)
    {
        if (strength == 0d)
        {
            return 0d;
        }

        if (resonanceSquared == 0d)
        {
            return strength;
        }

        // A large wavelength may overflow on squaring while C / wavelength^2 remains significant.
        return double.IsPositiveInfinity(wavelengthSquared)
            ? strength / (1d - (resonanceSquared / wavelength) / wavelength)
            : strength * (wavelengthSquared / (wavelengthSquared - resonanceSquared));
    }

    private static void RejectPoleInRange(double strength, double resonanceSquared, double minimum, double maximum, string parameterName)
    {
        if (strength == 0d || resonanceSquared == 0d)
        {
            return;
        }

        double poleNanometers = Math.Sqrt(resonanceSquared) * 1_000d;
        if (poleNanometers >= minimum && poleNanometers <= maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, resonanceSquared, "The model interval must not contain an active resonance pole.");
        }
    }

    private static void ValidateRange(double minimumWavelengthNanometers, double maximumWavelengthNanometers)
    {
        if (!double.IsFinite(minimumWavelengthNanometers) || minimumWavelengthNanometers <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWavelengthNanometers), minimumWavelengthNanometers, "Minimum wavelength must be finite and greater than zero.");
        }

        if (!double.IsFinite(maximumWavelengthNanometers) || maximumWavelengthNanometers < minimumWavelengthNanometers)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWavelengthNanometers), maximumWavelengthNanometers, "Maximum wavelength must be finite and no less than the minimum.");
        }
    }

    private static void ThrowIfNotFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Coefficient must be finite.");
        }
    }

    private static void ThrowIfInvalidResonance(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Squared resonance wavelength must be finite and non-negative.");
        }
    }

    private static void ValidateBatch(
        ReadOnlySpan<double> wavelengthsNanometers,
        Span<double> refractiveIndices,
        Span<DispersionStatus> statuses)
    {
        int length = wavelengthsNanometers.Length;
        if (refractiveIndices.Length != length)
        {
            throw new ArgumentException("All spans must have the same length.", nameof(refractiveIndices));
        }

        if (statuses.Length != length)
        {
            throw new ArgumentException("All spans must have the same length.", nameof(statuses));
        }

        if (wavelengthsNanometers.Overlaps(refractiveIndices, out int elementOffset) && elementOffset != 0)
        {
            throw new ArgumentException("Input and output may be identical, but must not partially overlap.", nameof(refractiveIndices));
        }

        ReadOnlySpan<byte> statusBytes = MemoryMarshal.AsBytes(statuses);
        if (MemoryMarshal.AsBytes(wavelengthsNanometers).Overlaps(statusBytes) ||
            MemoryMarshal.AsBytes(refractiveIndices).Overlaps(statusBytes))
        {
            throw new ArgumentException("Status storage must not overlap input or result storage.", nameof(statuses));
        }
    }
}
