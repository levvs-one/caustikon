using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>
/// Immutable three-term Cauchy model n(λ) = A + B/λ² + C/λ⁴. Public wavelengths are
/// nanometers; B is expressed in µm² and C in µm⁴.
/// </summary>
public readonly record struct Cauchy3
{
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

    public double A { get; }
    public double BUm2 { get; }
    public double CUm4 { get; }
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
