using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>Batch evaluation and shared argument checks for every <see cref="IDispersionModel"/>.</summary>
public static class Dispersion
{
    /// <summary>Evaluates a model at each wavelength, writing one index and one status per lane.</summary>
    /// <typeparam name="TModel">A value-type dispersion model; the call is specialized per type and does not box.</typeparam>
    /// <param name="model">The model to evaluate.</param>
    /// <param name="wavelengthsNanometers">Wavelengths in nanometers.</param>
    /// <param name="refractiveIndices">Receives one index per wavelength; may be the same span as the wavelengths.</param>
    /// <param name="statuses">Receives one status per wavelength; must not overlap the other spans.</param>
    /// <exception cref="ArgumentException">Span lengths differ, inputs and results partially overlap, or statuses overlap another span.</exception>
    /// <remarks>Follows the batch buffer rules in docs/conventions.md: no output is written before validation completes, and nothing allocates.</remarks>
    public static void EvaluateNanometers<TModel>(
        in TModel model,
        ReadOnlySpan<double> wavelengthsNanometers,
        Span<double> refractiveIndices,
        Span<DispersionStatus> statuses)
        where TModel : struct, IDispersionModel
    {
        ValidateBatch(wavelengthsNanometers, refractiveIndices, statuses);

        for (int i = 0; i < wavelengthsNanometers.Length; i++)
        {
            statuses[i] = model.EvaluateNanometers(wavelengthsNanometers[i], out refractiveIndices[i]);
        }
    }

    internal static void ValidateBatch(
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

    internal static void ValidateRange(double minimumWavelengthNanometers, double maximumWavelengthNanometers)
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

    internal static void ThrowIfNotFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Coefficient must be finite.");
        }
    }

    /// <summary>Applies the shared input rules of every model to a wavelength and an inclusive interval.</summary>
    /// <param name="wavelengthNanometers">The wavelength to classify, in nanometers.</param>
    /// <param name="minimumWavelengthNanometers">Inclusive lower bound of the interval, in nanometers.</param>
    /// <param name="maximumWavelengthNanometers">Inclusive upper bound of the interval, in nanometers.</param>
    /// <returns>
    /// <see cref="DispersionStatus.InvalidInput"/> when the interval is not positive and ordered (an uninitialized model) or the
    /// wavelength is nonfinite or nonpositive; <see cref="DispersionStatus.OutsideModelRange"/> when the wavelength lies outside
    /// the interval; otherwise <see cref="DispersionStatus.Success"/>, meaning evaluation may proceed.
    /// </returns>
    public static DispersionStatus ClassifyWavelength(
        double wavelengthNanometers,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        if (minimumWavelengthNanometers <= 0d || maximumWavelengthNanometers < minimumWavelengthNanometers)
        {
            return DispersionStatus.InvalidInput;
        }

        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0d)
        {
            return DispersionStatus.InvalidInput;
        }

        if (wavelengthNanometers < minimumWavelengthNanometers || wavelengthNanometers > maximumWavelengthNanometers)
        {
            return DispersionStatus.OutsideModelRange;
        }

        return DispersionStatus.Success;
    }
}
