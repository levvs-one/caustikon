namespace Caustikon;

/// <summary>Describes the physical or validation outcome of a refraction calculation.</summary>
public enum RefractionKind
{
    /// <summary>
    /// A transmitted direction was produced. Equal indices preserve the incident vector exactly,
    /// including at grazing incidence; otherwise the result is a unit direction into the transmitted medium.
    /// </summary>
    Refracted,

    /// <summary>
    /// The higher-to-lower-index critical boundary was reached within the floating-point tolerance.
    /// The output is a unit direction tangent to the interface, with its normal component snapped to zero.
    /// </summary>
    CriticalAngle,

    /// <summary>
    /// No propagating transmitted direction exists beyond the critical boundary.
    /// The output direction is <see cref="System.Numerics.Vector3.Zero"/>.
    /// </summary>
    TotalInternalReflection,

    /// <summary>
    /// An index is nonfinite or nonpositive, a vector is nonfinite or outside
    /// <see cref="Dielectric.UnitLengthSquaredTolerance"/>, or the incident-normal dot product is positive.
    /// The output direction is <see cref="System.Numerics.Vector3.Zero"/>.
    /// </summary>
    InvalidInput
}
