namespace Caustikon;

/// <summary>
/// Power-series dispersion in the index itself: <c>n(λ) = a₀ + Σ aᵢ λ^pᵢ</c>, with λ in micrometers and up to
/// <see cref="MaximumTermCount"/> terms.
/// </summary>
/// <remarks>
/// <para>The classic three-term form <c>A + B/λ² + C/λ⁴</c> is <see cref="Cauchy3"/>; this type accepts any number of
/// terms up to the capacity and any finite exponents, which is how index-matching liquids and some polymers are catalogued.
/// Even integer exponents are evaluated without <see cref="Math.Pow"/>.</para>
/// <para>Coefficient units follow each exponent: <c>aᵢ</c> carries micrometers to the power <c>−pᵢ</c>.
/// The struct stores its coefficients inline and does not allocate.</para>
/// </remarks>
public readonly struct Cauchy : IDispersionModel, IEquatable<Cauchy>
{
    /// <summary>The largest number of power terms one model can hold.</summary>
    public const int MaximumTermCount = PowerSeries.MaximumTermCount;

    private readonly PowerSeries series;

    /// <summary>Creates a model from a constant and interleaved coefficient/exponent pairs.</summary>
    /// <param name="offset">The constant term <c>a₀</c> of <c>n</c>.</param>
    /// <param name="coefficients"><c>aᵢ</c>, one per term, at most <see cref="MaximumTermCount"/>.</param>
    /// <param name="exponents"><c>pᵢ</c>, the power of the wavelength in micrometers for each term.</param>
    /// <param name="minimumWavelengthNanometers">Inclusive lower bound of the fitted interval, in nanometers.</param>
    /// <param name="maximumWavelengthNanometers">Inclusive upper bound of the fitted interval, in nanometers.</param>
    /// <exception cref="ArgumentException">The two coefficient spans differ in length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="MaximumTermCount"/> terms, a nonfinite value, or an invalid interval.</exception>
    public Cauchy(
        double offset,
        ReadOnlySpan<double> coefficients,
        ReadOnlySpan<double> exponents,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        series = new PowerSeries(offset, coefficients, exponents, minimumWavelengthNanometers, maximumWavelengthNanometers);
    }

    /// <summary>The constant term <c>a₀</c> of <c>n</c>.</summary>
    public double Offset => series.Offset;

    /// <summary>Number of power terms in use.</summary>
    public int TermCount => series.TermCount;

    /// <inheritdoc />
    public double MinimumWavelengthNanometers => series.MinimumWavelengthNanometers;

    /// <inheritdoc />
    public double MaximumWavelengthNanometers => series.MaximumWavelengthNanometers;

    /// <summary>Returns <c>aᵢ</c> for the zero-based term <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="TermCount"/>.</exception>
    public double CoefficientAt(int index) => series.CoefficientAt(index);

    /// <summary>Returns <c>pᵢ</c> for the zero-based term <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="TermCount"/>.</exception>
    public double ExponentAt(int index) => series.ExponentAt(index);

    /// <inheritdoc />
    public DispersionStatus EvaluateNanometers(double wavelengthNanometers, out double refractiveIndex)
    {
        DispersionStatus status = series.Evaluate(wavelengthNanometers, out double index);
        if (status != DispersionStatus.Success)
        {
            refractiveIndex = double.NaN;
            return status;
        }

        if (index <= 0d)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        refractiveIndex = index;
        return DispersionStatus.Success;
    }

    /// <summary>Evaluates each wavelength into caller-owned spans; see <see cref="Dispersion.EvaluateNanometers{TModel}"/> for the buffer rules.</summary>
    public void EvaluateNanometers(
        ReadOnlySpan<double> wavelengthsNanometers,
        Span<double> refractiveIndices,
        Span<DispersionStatus> statuses) =>
        Dispersion.EvaluateNanometers(in this, wavelengthsNanometers, refractiveIndices, statuses);

    /// <inheritdoc />
    public bool Equals(Cauchy other) => series.Equals(other.series);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Cauchy other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => series.GetHashCode();

    /// <summary>Value equality over offset, terms and interval.</summary>
    public static bool operator ==(Cauchy left, Cauchy right) => left.Equals(right);

    /// <summary>Value inequality over offset, terms and interval.</summary>
    public static bool operator !=(Cauchy left, Cauchy right) => !left.Equals(right);
}
