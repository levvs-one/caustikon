namespace Caustikon.Glasses;

/// <summary>The algebraic form a glass's refractive-index model takes.</summary>
public enum DispersionFormula
{
    /// <summary>The form was not recorded; <see cref="Glass.Model"/> still evaluates.</summary>
    Unspecified,

    /// <summary><see cref="Caustikon.Sellmeier"/>: <c>n² − 1 = offset + Σ Bᵢ λ² / (λ² − Cᵢ)</c>.</summary>
    Sellmeier,

    /// <summary><see cref="Caustikon.Polynomial"/>: <c>n² = a₀ + Σ aᵢ λ^pᵢ</c>.</summary>
    Polynomial,

    /// <summary><see cref="Caustikon.Cauchy"/>: <c>n = a₀ + Σ aᵢ λ^pᵢ</c>.</summary>
    Cauchy,
}
