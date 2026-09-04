namespace Caustikon;

/// <summary>Describes a refractive-index model evaluation.</summary>
public enum DispersionStatus
{
    Success,
    InvalidInput,
    OutsideModelRange,
    Singular,
    NonPhysical
}
