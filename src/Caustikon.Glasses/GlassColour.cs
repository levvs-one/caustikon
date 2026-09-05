namespace Caustikon.Glasses;

/// <summary>Spectral colour computation under CIE standard illuminant D65 with the CIE 1931 2° observer.</summary>
/// <remarks>
/// Integration runs over the observer's 360–830 nm grid at 1 nm. Illuminant D65 is tabulated at 5 nm and interpolated
/// linearly onto that grid. Normalization divides by <c>Σ S(λ) ȳ(λ)</c>, so a spectrum of ones yields the illuminant's
/// white with <c>Y = 1</c>. The sRGB matrix is the IEC 61966-2-1 one for the D65 white point.
/// </remarks>
public static class GlassColour
{
    /// <summary>Colour of D65 light after one pass through a slab of the glass.</summary>
    /// <param name="glass">A glass with <see cref="Glass.Extinction"/> data; without it there is nothing to compute.</param>
    /// <param name="thicknessMillimeters">Path length inside the glass, in millimeters; finite and nonnegative.</param>
    /// <param name="includeSurfaces">
    /// When true, also applies the two air–glass surface losses at normal incidence, <c>(1 − R(λ))²</c>, with <c>R</c> from
    /// the glass's own index at each wavelength; when false, bulk absorption only.
    /// </param>
    /// <returns>The colour, or <see langword="null"/> when the glass has no extinction table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glass"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The thickness is not finite and nonnegative.</exception>
    public static TransmittedColour? Transmitted(Glass glass, double thicknessMillimeters, bool includeSurfaces = false)
    {
        ArgumentNullException.ThrowIfNull(glass);
        if (!double.IsFinite(thicknessMillimeters) || thicknessMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(thicknessMillimeters), thicknessMillimeters, "Thickness must be finite and nonnegative.");
        }

        if (glass.Extinction is not { } extinction)
        {
            return null;
        }

        Span<double> transmittance = stackalloc double[CieTables.ObserverCount];
        double coverageMinimum = Math.Max(extinction.MinimumWavelengthNanometers, CieTables.ObserverFirstNanometers);
        double coverageMaximum = Math.Min(extinction.MaximumWavelengthNanometers, CieTables.ObserverFirstNanometers + (CieTables.ObserverCount - 1) * CieTables.ObserverStepNanometers);
        for (int i = 0; i < transmittance.Length; i++)
        {
            double wavelength = CieTables.ObserverFirstNanometers + i * CieTables.ObserverStepNanometers;
            double clamped = Math.Clamp(wavelength, extinction.MinimumWavelengthNanometers, extinction.MaximumWavelengthNanometers);
            extinction.InternalTransmittance(clamped, thicknessMillimeters, out double bulk);
            double value = bulk;
            if (includeSurfaces)
            {
                value *= SurfaceFactor(glass, wavelength);
            }

            transmittance[i] = value;
        }

        return FromTransmittance(transmittance, coverageMinimum, coverageMaximum);
    }

    /// <summary>Colour of D65 light after passing through a filter with the given spectral transmittance.</summary>
    /// <param name="transmittance360To830At1Nanometer">Transmittance in [0, 1] at 360, 361, …, 830 nm: exactly 471 values.</param>
    /// <exception cref="ArgumentException">The span does not hold 471 values.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite and in [0, 1].</exception>
    public static TransmittedColour FromTransmittance(ReadOnlySpan<double> transmittance360To830At1Nanometer)
    {
        if (transmittance360To830At1Nanometer.Length != CieTables.ObserverCount)
        {
            throw new ArgumentException($"Exactly {CieTables.ObserverCount} values are required, one per nanometer from 360 to 830.", nameof(transmittance360To830At1Nanometer));
        }

        foreach (double value in transmittance360To830At1Nanometer)
        {
            if (!double.IsFinite(value) || value < 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(transmittance360To830At1Nanometer), value, "Transmittance must be finite and in [0, 1].");
            }
        }

        return FromTransmittance(transmittance360To830At1Nanometer, CieTables.ObserverFirstNanometers, CieTables.ObserverFirstNanometers + (CieTables.ObserverCount - 1) * CieTables.ObserverStepNanometers);
    }

    private static TransmittedColour FromTransmittance(ReadOnlySpan<double> transmittance, double coverageMinimum, double coverageMaximum)
    {
        ReadOnlySpan<double> xBar = CieTables.XBar;
        ReadOnlySpan<double> yBar = CieTables.YBar;
        ReadOnlySpan<double> zBar = CieTables.ZBar;

        double x = 0d, y = 0d, z = 0d, normalization = 0d;
        for (int i = 0; i < transmittance.Length; i++)
        {
            double wavelength = CieTables.ObserverFirstNanometers + i * CieTables.ObserverStepNanometers;
            double illuminant = IlluminantAt(wavelength);
            double weight = illuminant * transmittance[i];
            x += weight * xBar[i];
            y += weight * yBar[i];
            z += weight * zBar[i];
            normalization += illuminant * yBar[i];
        }

        x /= normalization;
        y /= normalization;
        z /= normalization;

        // XYZ (D65) to linear sRGB, IEC 61966-2-1 matrix inverted at full precision (Lindbloom's tabulation).
        double red = 3.2404542d * x - 1.5371385d * y - 0.4985314d * z;
        double green = -0.9692660d * x + 1.8760108d * y + 0.0415560d * z;
        double blue = 0.0556434d * x - 0.2040259d * y + 1.0572252d * z;
        // The illuminant's own white lands within 1e-5 of (1, 1, 1) after 1 nm integration; only excursions beyond that
        // count as leaving the gamut.
        const double gamutSlack = 1e-4d;
        bool clipped = red < -gamutSlack || red > 1d + gamutSlack || green < -gamutSlack || green > 1d + gamutSlack || blue < -gamutSlack || blue > 1d + gamutSlack;

        return new TransmittedColour(
            x,
            y,
            z,
            Math.Clamp(red, 0d, 1d),
            Math.Clamp(green, 0d, 1d),
            Math.Clamp(blue, 0d, 1d),
            clipped,
            coverageMinimum,
            coverageMaximum);
    }

    /// <summary>The sRGB colour of a monochromatic line, for drawing spectra.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers; outside 360–830 nm the result is black.</param>
    /// <returns>
    /// Companded sRGB channels in [0, 1]. The chromaticity is the observer's for that wavelength; since every pure spectral
    /// colour lies outside the sRGB gamut, the linear channels are desaturated toward the D65 white just enough to bring the
    /// colour into gamut, then scaled so the brightest channel is 1. The result is a display colour, not a radiometric one.
    /// </returns>
    public static (double Red, double Green, double Blue) Monochromatic(double wavelengthNanometers)
    {
        double position = (wavelengthNanometers - CieTables.ObserverFirstNanometers) / CieTables.ObserverStepNanometers;
        if (!double.IsFinite(position) || position < 0d || position > CieTables.ObserverCount - 1)
        {
            return (0d, 0d, 0d);
        }

        int lower = (int)Math.Floor(position);
        int upper = Math.Min(lower + 1, CieTables.ObserverCount - 1);
        double t = position - lower;
        double x = CieTables.XBar[lower] + t * (CieTables.XBar[upper] - CieTables.XBar[lower]);
        double y = CieTables.YBar[lower] + t * (CieTables.YBar[upper] - CieTables.YBar[lower]);
        double z = CieTables.ZBar[lower] + t * (CieTables.ZBar[upper] - CieTables.ZBar[lower]);
        double sum = x + y + z;
        if (sum <= 0d)
        {
            return (0d, 0d, 0d);
        }

        // Chromaticity only: the observer's own magnitude near the edges of the visible range would be invisible.
        x /= sum;
        y /= sum;
        z /= sum;
        double red = 3.2404542d * x - 1.5371385d * y - 0.4985314d * z;
        double green = -0.9692660d * x + 1.8760108d * y + 0.0415560d * z;
        double blue = 0.0556434d * x - 0.2040259d * y + 1.0572252d * z;

        // Desaturate toward white until no channel is negative.
        double minimum = Math.Min(red, Math.Min(green, blue));
        if (minimum < 0d)
        {
            double white = -minimum / (1d - minimum);
            red = red + white * (1d - red);
            green = green + white * (1d - green);
            blue = blue + white * (1d - blue);
        }

        double maximum = Math.Max(red, Math.Max(green, blue));
        if (maximum > 0d)
        {
            red /= maximum;
            green /= maximum;
            blue /= maximum;
        }

        return (TransmittedColour.Compand(Math.Clamp(red, 0d, 1d)), TransmittedColour.Compand(Math.Clamp(green, 0d, 1d)), TransmittedColour.Compand(Math.Clamp(blue, 0d, 1d)));
    }

    /// <summary>Relative spectral power of CIE illuminant D65 at a wavelength (100 at 560 nm), linearly interpolated from the 5 nm table and zero outside 300–780 nm.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    public static double IlluminantAt(double wavelengthNanometers)
    {
        ReadOnlySpan<double> table = CieTables.D65;
        double position = (wavelengthNanometers - CieTables.IlluminantFirstNanometers) / CieTables.IlluminantStepNanometers;
        if (position < 0d || position > table.Length - 1)
        {
            return 0d;
        }

        int lower = (int)Math.Floor(position);
        if (lower >= table.Length - 1)
        {
            return table[^1];
        }

        double t = position - lower;
        return table[lower] + t * (table[lower + 1] - table[lower]);
    }

    private static double SurfaceFactor(Glass glass, double wavelengthNanometers)
    {
        if (glass.Model.EvaluateNanometers(wavelengthNanometers, out double index) != DispersionStatus.Success)
        {
            // Outside the fitted interval, hold the nearest fitted index rather than invent one.
            double clamped = Math.Clamp(wavelengthNanometers, glass.Model.MinimumWavelengthNanometers, glass.Model.MaximumWavelengthNanometers);
            if (glass.Model.EvaluateNanometers(clamped, out index) != DispersionStatus.Success)
            {
                return 1d;
            }
        }

        double reflectance = Dielectric.NormalReflectance(1f, (float)index);
        double pass = 1d - reflectance;
        return pass * pass;
    }
}
