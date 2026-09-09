namespace Caustikon.Glasses;

/// <summary>
/// The imaginary part of the refractive index, <c>k(λ)</c>, tabulated against wavelength, with the absorption and
/// internal transmittance that follow from it.
/// </summary>
/// <remarks>
/// <para>Between samples, <c>k</c> is interpolated linearly in <c>log k</c> against wavelength when both neighbours are
/// positive, because catalog extinction spans several orders of magnitude across the visible; when a neighbour is zero
/// the interpolation is linear in <c>k</c>. Outside the tabulated interval no extrapolation is performed.</para>
/// <para>The absorption coefficient is <c>α = 4πk/λ</c>, and the internal transmittance of a path of length <c>d</c> is
/// <c>τᵢ = exp(−α d)</c>. This is bulk absorption only; surface reflection is <see cref="Dielectric.Fresnel(float, float, float)"/>'s job.</para>
/// </remarks>
public sealed class TabulatedExtinction
{
    private readonly double[] wavelengthsNanometers;
    private readonly double[] extinctions;

    /// <summary>Creates a table from ascending wavelengths and their extinction coefficients.</summary>
    /// <param name="wavelengthsNanometers">Strictly ascending, finite, positive wavelengths in nanometers; at least two.</param>
    /// <param name="extinctions">Finite, nonnegative <c>k</c> values, one per wavelength.</param>
    /// <exception cref="ArgumentException">Lengths differ, fewer than two samples, or wavelengths are not strictly ascending.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A wavelength is not finite and positive, or an extinction is not finite and nonnegative.</exception>
    public TabulatedExtinction(ReadOnlySpan<double> wavelengthsNanometers, ReadOnlySpan<double> extinctions)
    {
        if (wavelengthsNanometers.Length != extinctions.Length)
        {
            throw new ArgumentException("Wavelengths and extinctions must have the same length.", nameof(extinctions));
        }

        if (wavelengthsNanometers.Length < 2)
        {
            throw new ArgumentException("At least two samples are required.", nameof(wavelengthsNanometers));
        }

        for (int i = 0; i < wavelengthsNanometers.Length; i++)
        {
            double wavelength = wavelengthsNanometers[i];
            if (!double.IsFinite(wavelength) || wavelength <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(wavelengthsNanometers), wavelength, "Wavelengths must be finite and greater than zero.");
            }

            if (i > 0 && wavelength <= wavelengthsNanometers[i - 1])
            {
                throw new ArgumentException("Wavelengths must be strictly ascending.", nameof(wavelengthsNanometers));
            }

            double extinction = extinctions[i];
            if (!double.IsFinite(extinction) || extinction < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(extinctions), extinction, "Extinction coefficients must be finite and nonnegative.");
            }
        }

        this.wavelengthsNanometers = wavelengthsNanometers.ToArray();
        this.extinctions = extinctions.ToArray();
    }

    /// <summary>Number of tabulated samples.</summary>
    public int Count => wavelengthsNanometers.Length;

    /// <summary>Wavelength of the first sample, in nanometers.</summary>
    public double MinimumWavelengthNanometers => wavelengthsNanometers[0];

    /// <summary>Wavelength of the last sample, in nanometers.</summary>
    public double MaximumWavelengthNanometers => wavelengthsNanometers[^1];

    /// <summary>Wavelength of the sample at <paramref name="index"/>, in nanometers.</summary>
    public double WavelengthAt(int index) => wavelengthsNanometers[index];

    /// <summary>Extinction coefficient <c>k</c> of the sample at <paramref name="index"/>.</summary>
    public double ExtinctionAt(int index) => extinctions[index];

    /// <summary>Interpolates <c>k</c> at a wavelength.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    /// <param name="extinction">Receives <c>k</c>, or <see cref="double.NaN"/> when the status is not success.</param>
    /// <returns><see cref="DispersionStatus.Success"/>, <see cref="DispersionStatus.InvalidInput"/> for a nonfinite or nonpositive wavelength, or <see cref="DispersionStatus.OutsideModelRange"/>.</returns>
    public DispersionStatus Evaluate(double wavelengthNanometers, out double extinction)
    {
        DispersionStatus status = Dispersion.ClassifyWavelength(wavelengthNanometers, MinimumWavelengthNanometers, MaximumWavelengthNanometers);
        if (status != DispersionStatus.Success)
        {
            extinction = double.NaN;
            return status;
        }

        int upper = Array.BinarySearch(wavelengthsNanometers, wavelengthNanometers);
        if (upper >= 0)
        {
            extinction = extinctions[upper];
            return DispersionStatus.Success;
        }

        upper = ~upper;
        int lower = upper - 1;
        double x0 = wavelengthsNanometers[lower];
        double x1 = wavelengthsNanometers[upper];
        double k0 = extinctions[lower];
        double k1 = extinctions[upper];
        double t = (wavelengthNanometers - x0) / (x1 - x0);

        extinction = k0 > 0d && k1 > 0d
            ? Math.Exp(Math.Log(k0) + t * (Math.Log(k1) - Math.Log(k0)))
            : k0 + t * (k1 - k0);
        return DispersionStatus.Success;
    }

    /// <summary>Absorption coefficient <c>α = 4πk/λ</c> in inverse meters at a wavelength.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    /// <param name="absorptionPerMeter">Receives <c>α</c>, or <see cref="double.NaN"/> when the status is not success.</param>
    public DispersionStatus AbsorptionCoefficient(double wavelengthNanometers, out double absorptionPerMeter)
    {
        DispersionStatus status = Evaluate(wavelengthNanometers, out double extinction);
        absorptionPerMeter = status == DispersionStatus.Success
            ? 4d * Math.PI * extinction / (wavelengthNanometers * 1e-9d)
            : double.NaN;
        return status;
    }

    /// <summary>Internal transmittance <c>τᵢ = exp(−α d)</c> of a path through the bulk, excluding surface reflection.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    /// <param name="pathLengthMillimeters">Geometric path length inside the glass, in millimeters; must be finite and nonnegative.</param>
    /// <param name="transmittance">Receives <c>τᵢ</c> in [0, 1], or <see cref="double.NaN"/> when the status is not success.</param>
    /// <exception cref="ArgumentOutOfRangeException">The path length is not finite and nonnegative.</exception>
    public DispersionStatus InternalTransmittance(double wavelengthNanometers, double pathLengthMillimeters, out double transmittance)
    {
        if (!double.IsFinite(pathLengthMillimeters) || pathLengthMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(pathLengthMillimeters), pathLengthMillimeters, "Path length must be finite and nonnegative.");
        }

        DispersionStatus status = AbsorptionCoefficient(wavelengthNanometers, out double absorption);
        transmittance = status == DispersionStatus.Success
            ? Math.Exp(-absorption * pathLengthMillimeters * 1e-3d)
            : double.NaN;
        return status;
    }
}
