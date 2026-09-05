namespace Caustikon;

/// <summary>Power reflectance of a lossless dielectric interface.</summary>
/// <param name="S">Fraction of incident power reflected for polarization perpendicular to the plane of incidence.</param>
/// <param name="P">Fraction of incident power reflected for polarization parallel to the plane of incidence.</param>
/// <remarks>
/// Values returned by <see cref="Dielectric.Fresnel(float, float, float)"/> are in [0, 1].
/// Direct construction and property initialization store the supplied values without validation or clamping.
/// These are power fractions, not electric-field amplitudes.
/// </remarks>
public readonly record struct FresnelPower(float S, float P)
{
    /// <summary>Gets the arithmetic mean, (S + P) / 2, of the two stored polarization reflectances.</summary>
    public float Unpolarized => (S + P) * 0.5f;
}
