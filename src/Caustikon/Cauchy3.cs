using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>
/// Immutable three-term Cauchy model n = A + B / wavelength^2 + C / wavelength^4.
/// Public wavelengths are nanometers; the equation uses micrometers.
/// </summary>
/// <remarks>
/// The caller supplies the coefficient source's inclusive validity interval and air or vacuum wavelength
/// convention. No conversion of the reference medium or measurement conditions is performed.
/// The default value is uninitialized and evaluations return <see cref="DispersionStatus.InvalidInput"/>.
/// </remarks>
public readonly record struct Cauchy3
{
    /// <summary>Creates a Cauchy model with an explicit inclusive wavelength interval.</summary>
    /// <param name="a">Finite, dimensionless constant coefficient.</param>
    /// <param name="bUm2">Finite quadratic coefficient in square micrometers. Negative values are permitted.</param>
    /// <param name="cUm4">Finite quartic coefficient in micrometers to the fourth power. Negative values are permitted.</param>
    /// <param name="minimumWavelengthNanometers">Finite, positive lower endpoint of the model interval, in nanometers.</param>
    /// <param name="maximumWavelengthNanometers">Finite upper endpoint in nanometers, no less than the minimum.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A coefficient is nonfinite, the minimum is nonfinite or nonpositive, or the maximum is nonfinite or below the minimum.
    /// </exception>
    /// <remarks>Construction validates the coefficients and interval, not whether the index is physical throughout that interval.</remarks>
    public Cauchy3(
        double a,
        double bUm2,
        double cUm4,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        ThrowIfNotFinite(a, nameof(a));
        ThrowIfNotFinite(bUm2, nameof(bUm2));
        ThrowIfNotFinite(cUm4, nameof(cUm4));
        ValidateRange(minimumWavelengthNanometers, maximumWavelengthNanometers);

        A = a;
        BUm2 = bUm2;
        CUm4 = cUm4;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
    }

    /// <summary>Gets the dimensionless constant coefficient.</summary>
    public double A { get; }

    /// <summary>Gets the quadratic coefficient in square micrometers.</summary>
    public double BUm2 { get; }

    /// <summary>Gets the quartic coefficient in micrometers to the fourth power.</summary>
    public double CUm4 { get; }

    /// <summary>Gets the inclusive lower wavelength endpoint in nanometers, or zero for the default model.</summary>
    public double MinimumWavelengthNanometers { get; }

    /// <summary>Gets the inclusive upper wavelength endpoint in nanometers, or zero for the default model.</summary>
    public double MaximumWavelengthNanometers { get; }

    /// <summary>Evaluates the model at a wavelength measured in nanometers.</summary>
    /// <param name="wavelengthNanometers">Wavelength in the coefficient source's air or vacuum convention, in nanometers.</param>
    /// <param name="refractiveIndex">Receives a positive finite phase index on success, or <see cref="double.NaN"/> otherwise.</param>
    /// <returns>
    /// <see cref="DispersionStatus.Success"/> for a positive finite result;
    /// <see cref="DispersionStatus.InvalidInput"/> for a nonfinite or nonpositive wavelength or an uninitialized model;
    /// <see cref="DispersionStatus.OutsideModelRange"/> outside the inclusive interval; or
    /// <see cref="DispersionStatus.NonPhysical"/> for a nonfinite or nonpositive result.
    /// </returns>
    /// <remarks>
    /// Uses double-precision arithmetic without extrapolation. Zero B and C terms are skipped.
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
        // Repeated division avoids overflowing wavelength^4 before its ratio with C is formed.
        double quadratic = BUm2 == 0d ? 0d : (BUm2 / wavelengthMicrometers) / wavelengthMicrometers;
        double quartic = CUm4 == 0d ? 0d :
            (((CUm4 / wavelengthMicrometers) / wavelengthMicrometers) / wavelengthMicrometers) / wavelengthMicrometers;
        double candidate = A + quadratic + quartic;

        if (!double.IsFinite(candidate) || candidate <= 0d)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        refractiveIndex = candidate;
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
