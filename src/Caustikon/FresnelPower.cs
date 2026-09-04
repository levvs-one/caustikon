namespace Caustikon;

/// <summary>Power reflectance of a lossless dielectric interface.</summary>
/// <param name="S">S-polarized power reflectance.</param>
/// <param name="P">P-polarized power reflectance.</param>
public readonly record struct FresnelPower(float S, float P)
{
    /// <summary>The arithmetic mean of the two polarization reflectances.</summary>
    public float Unpolarized => (S + P) * 0.5f;
}
