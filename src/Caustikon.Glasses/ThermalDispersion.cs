namespace Caustikon.Glasses;

/// <summary>
/// Temperature dependence of the absolute refractive index in the form SCHOTT publishes (TIE-19) and other manufacturers
/// have adopted:
/// <c>Δn_abs(λ, T) = (n² − 1) / (2n) · [D₀ ΔT + D₁ ΔT² + D₂ ΔT³ + (E₀ ΔT + E₁ ΔT²) / (λ² − λ_TK²)]</c>,
/// with λ in micrometers, <c>ΔT = T − T₀</c>, and <c>n</c> the index at the reference temperature.
/// </summary>
/// <remarks>
/// The result is the change of the <em>absolute</em> index (relative to vacuum). The catalog index is relative to air, and
/// the index of air also changes with temperature; converting the shift to the relative index needs an air model, which
/// this type does not include. Catalog coefficients are valid over roughly −40 °C to +80 °C and the fitted wavelength
/// interval of the glass; the type does not enforce either limit.
/// </remarks>
/// <param name="D0">First-order temperature coefficient, per kelvin.</param>
/// <param name="D1">Second-order temperature coefficient, per kelvin squared.</param>
/// <param name="D2">Third-order temperature coefficient, per kelvin cubed.</param>
/// <param name="E0">First-order dispersion coefficient, micrometers squared per kelvin.</param>
/// <param name="E1">Second-order dispersion coefficient, micrometers squared per kelvin squared.</param>
/// <param name="LambdaTkUm">Effective resonance wavelength <c>λ_TK</c> in micrometers.</param>
/// <param name="ReferenceTemperatureCelsius">The temperature the catalog index refers to; 20 °C in every manufacturer catalog imported.</param>
public readonly record struct ThermalDispersion(
    double D0,
    double D1,
    double D2,
    double E0,
    double E1,
    double LambdaTkUm,
    double ReferenceTemperatureCelsius = 20d)
{
    /// <summary>Change of the absolute refractive index between the reference temperature and <paramref name="temperatureCelsius"/>.</summary>
    /// <param name="indexAtReference">The refractive index at the wavelength and the reference temperature, from the glass's dispersion model.</param>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    /// <param name="temperatureCelsius">The temperature to evaluate at.</param>
    /// <returns><c>Δn_abs</c>; add it to the absolute index at the reference temperature.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is not finite, the index is not positive, or the wavelength is not positive.</exception>
    public double AbsoluteIndexShift(double indexAtReference, double wavelengthNanometers, double temperatureCelsius)
    {
        if (!double.IsFinite(indexAtReference) || indexAtReference <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(indexAtReference), indexAtReference, "The reference index must be finite and greater than zero.");
        }

        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers), wavelengthNanometers, "Wavelength must be finite and greater than zero.");
        }

        if (!double.IsFinite(temperatureCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureCelsius), temperatureCelsius, "Temperature must be finite.");
        }

        double deltaT = temperatureCelsius - ReferenceTemperatureCelsius;
        double lambda = wavelengthNanometers * 0.001d;
        double lambda2 = lambda * lambda;
        double bracket = D0 * deltaT + D1 * deltaT * deltaT + D2 * deltaT * deltaT * deltaT
            + (E0 * deltaT + E1 * deltaT * deltaT) / (lambda2 - LambdaTkUm * LambdaTkUm);
        return (indexAtReference * indexAtReference - 1d) / (2d * indexAtReference) * bracket;
    }

    /// <summary>The slope <c>dn_abs/dT</c> at the reference temperature, per kelvin, from the first-order terms.</summary>
    /// <param name="indexAtReference">The refractive index at the wavelength and the reference temperature.</param>
    /// <param name="wavelengthNanometers">Wavelength in nanometers.</param>
    public double AbsoluteSlopeAtReference(double indexAtReference, double wavelengthNanometers)
    {
        double lambda = wavelengthNanometers * 0.001d;
        double lambda2 = lambda * lambda;
        return (indexAtReference * indexAtReference - 1d) / (2d * indexAtReference)
            * (D0 + E0 / (lambda2 - LambdaTkUm * LambdaTkUm));
    }
}
