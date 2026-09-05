using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>
/// Immutable three-resonance Sellmeier model n^2 = 1 + sum(Bi * wavelength^2 / (wavelength^2 - Ci)).
/// Public wavelengths are nanometers; the equation uses micrometers.
/// </summary>
/// <remarks>
/// The caller supplies the coefficient source's inclusive validity interval and air or vacuum wavelength
/// convention. No conversion of the reference medium or measurement conditions is performed.
/// The default value is uninitialized and evaluations return <see cref="DispersionStatus.InvalidInput"/>.
/// </remarks>
public readonly record struct Sellmeier3
{
    /// <summary>Creates a Sellmeier model with three coefficient pairs and a pole-free inclusive wavelength interval.</summary>
    /// <param name="b1">Finite, dimensionless strength of the first resonance. Zero disables the term.</param>
    /// <param name="c1Um2">Finite, nonnegative squared wavelength of the first resonance, in square micrometers.</param>
    /// <param name="b2">Finite, dimensionless strength of the second resonance. Zero disables the term.</param>
    /// <param name="c2Um2">Finite, nonnegative squared wavelength of the second resonance, in square micrometers.</param>
    /// <param name="b3">Finite, dimensionless strength of the third resonance. Zero disables the term.</param>
    /// <param name="c3Um2">Finite, nonnegative squared wavelength of the third resonance, in square micrometers.</param>
    /// <param name="minimumWavelengthNanometers">Finite, positive lower endpoint of the model interval, in nanometers.</param>
    /// <param name="maximumWavelengthNanometers">Finite upper endpoint in nanometers, no less than the minimum.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A coefficient is nonfinite, a C coefficient is negative, the wavelength interval is invalid, or
    /// an active positive resonance pole at 1000 * sqrt(Ci) nanometers lies within the inclusive interval.
    /// </exception>
    /// <remarks>
    /// Negative B coefficients are permitted. A zero C coefficient makes its term a constant B.
    /// Inactive terms do not restrict the interval. Construction does not establish physical validity throughout the interval.
    /// </remarks>
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

    /// <summary>Gets the dimensionless strength of the first resonance.</summary>
    public double B1 { get; }

    /// <summary>Gets the first squared resonance wavelength in square micrometers.</summary>
    public double C1Um2 { get; }

    /// <summary>Gets the dimensionless strength of the second resonance.</summary>
    public double B2 { get; }

    /// <summary>Gets the second squared resonance wavelength in square micrometers.</summary>
    public double C2Um2 { get; }

    /// <summary>Gets the dimensionless strength of the third resonance.</summary>
    public double B3 { get; }

    /// <summary>Gets the third squared resonance wavelength in square micrometers.</summary>
    public double C3Um2 { get; }

    /// <summary>Gets the inclusive lower wavelength endpoint in nanometers, or zero for the default model.</summary>
    public double MinimumWavelengthNanometers { get; }

    /// <summary>Gets the inclusive upper wavelength endpoint in nanometers, or zero for the default model.</summary>
    public double MaximumWavelengthNanometers { get; }

    /// <summary>Evaluates the model at a wavelength measured in nanometers.</summary>
    /// <param name="wavelengthNanometers">Wavelength in the coefficient source's air or vacuum convention, in nanometers.</param>
    /// <param name="refractiveIndex">Receives a positive finite phase index on success, or <see cref="double.NaN"/> otherwise.</param>
    /// <returns>
    /// <see cref="DispersionStatus.Success"/> for a positive finite index;
    /// <see cref="DispersionStatus.InvalidInput"/> for a nonfinite or nonpositive wavelength or an uninitialized model;
    /// <see cref="DispersionStatus.OutsideModelRange"/> outside the inclusive interval;
    /// <see cref="DispersionStatus.Singular"/> for an active positive-resonance denominator that rounds to zero; or
    /// <see cref="DispersionStatus.NonPhysical"/> for an unsupported intermediate or a nonfinite or nonpositive squared index.
    /// </returns>
    /// <remarks>
    /// Uses double-precision arithmetic without extrapolation. For an active term with positive C,
    /// a zero or subnormal squared wavelength in micrometers is unsupported and returns NonPhysical.
    /// Inactive terms are skipped and zero-C terms are constant; neither imposes this lower numerical limit.
    /// Very large wavelengths use a scaled expression when squaring would overflow.
    /// Invalid wavelengths and numerical failures are reported by status, not by exceptions.
    /// </remarks>
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
    /// <param name="wavelengthsNanometers">Input wavelengths, using the same convention as the coefficient source.</param>
    /// <param name="refractiveIndices">Caller-owned results; each unsuccessful lane receives <see cref="double.NaN"/>.</param>
    /// <param name="statuses">Caller-owned evaluation status for every input wavelength.</param>
    /// <exception cref="ArgumentException">
    /// Span lengths differ, input and result spans partially overlap, or status storage overlaps either other span.
    /// Validation occurs before any output write, including for cross-type overlap.
    /// </exception>
    /// <remarks>
    /// All spans must have the same length. Exact wavelength-to-result in-place operation is allowed.
    /// Each lane follows <see cref="EvaluateNanometers(double, out double)"/> independently;
    /// no output arrays are allocated and no work is scheduled on other threads.
    /// </remarks>
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
