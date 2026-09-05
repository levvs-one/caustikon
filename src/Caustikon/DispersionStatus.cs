namespace Caustikon;

/// <summary>Describes a refractive-index model evaluation.</summary>
public enum DispersionStatus
{
    /// <summary>The wavelength is within the model interval and the output is a positive finite refractive index.</summary>
    Success,

    /// <summary>
    /// The wavelength is nonfinite or nonpositive, or the model is an uninitialized default value.
    /// The output is <see cref="double.NaN"/>.
    /// </summary>
    InvalidInput,

    /// <summary>
    /// The wavelength is outside the caller-supplied inclusive model interval. No extrapolation is performed;
    /// the output is <see cref="double.NaN"/>.
    /// </summary>
    OutsideModelRange,

    /// <summary>
    /// An active, positive-resonance Sellmeier denominator rounds to zero.
    /// The output is <see cref="double.NaN"/>. Cauchy models do not return this status.
    /// </summary>
    Singular,

    /// <summary>
    /// The equation produces a nonfinite or nonpositive index or squared index, or an intermediate exceeds
    /// the model's supported numerical range. The output is <see cref="double.NaN"/>.
    /// </summary>
    NonPhysical
}
