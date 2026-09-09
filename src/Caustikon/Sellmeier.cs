using System.Runtime.CompilerServices;

namespace Caustikon;

/// <summary>
/// Sellmeier dispersion with a constant offset and up to <see cref="MaximumTermCount"/> resonance terms:
/// <c>n(λ)² = 1 + offset + Σ Bᵢ λ² / (λ² − Cᵢ)</c>, with λ in micrometers.
/// </summary>
/// <remarks>
/// <para>This is the form manufacturer catalogs and the RefractiveIndex.INFO database use for glass ("Sellmeier-2"):
/// <c>Cᵢ</c> is the squared resonance wavelength in micrometers squared and is not squared again during evaluation.
/// A source that lists resonance wavelengths themselves ("Sellmeier-1") is converted by squaring each value before construction.</para>
/// <para>Unlike <see cref="Sellmeier3"/>, negative resonances are accepted: catalog fits for some glasses carry them, and a
/// negative <c>Cᵢ</c> has no pole, so it cannot make the interval singular. A positive resonance whose pole
/// <c>1000·√Cᵢ</c> nanometers lies inside the interval is still rejected at construction.</para>
/// <para>The struct stores its coefficients inline and does not allocate. It is a value type of fixed size regardless of
/// term count; pass it by <c>in</c> reference in hot loops.</para>
/// </remarks>
public readonly struct Sellmeier : IDispersionModel, IEquatable<Sellmeier>
{
    /// <summary>The largest number of resonance terms one model can hold.</summary>
    public const int MaximumTermCount = 8;

    [InlineArray(MaximumTermCount)]
    private struct TermBuffer
    {
        private double element0;
    }

    private readonly TermBuffer strengths;
    private readonly TermBuffer resonancesUm2;

    /// <summary>Creates a model from interleaved catalog coefficients.</summary>
    /// <param name="offset">Constant added to <c>n² − 1</c>; zero for nearly all optical glass.</param>
    /// <param name="strengths">Dimensionless <c>Bᵢ</c>, one per term, at most <see cref="MaximumTermCount"/>.</param>
    /// <param name="resonancesUm2"><c>Cᵢ</c> in micrometers squared, one per term, same length as <paramref name="strengths"/>.</param>
    /// <param name="minimumWavelengthNanometers">Inclusive lower bound of the fitted interval, in nanometers.</param>
    /// <param name="maximumWavelengthNanometers">Inclusive upper bound of the fitted interval, in nanometers.</param>
    /// <exception cref="ArgumentException">The two coefficient spans differ in length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// More than <see cref="MaximumTermCount"/> terms, a nonfinite coefficient, an invalid interval, or an active positive
    /// resonance whose pole lies inside the interval.
    /// </exception>
    public Sellmeier(
        double offset,
        ReadOnlySpan<double> strengths,
        ReadOnlySpan<double> resonancesUm2,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers)
    {
        Dispersion.ThrowIfNotFinite(offset, nameof(offset));
        if (strengths.Length != resonancesUm2.Length)
        {
            throw new ArgumentException("Strengths and resonances must have the same length.", nameof(resonancesUm2));
        }

        if (strengths.Length > MaximumTermCount)
        {
            throw new ArgumentOutOfRangeException(nameof(strengths), strengths.Length, $"At most {MaximumTermCount} terms are supported.");
        }

        for (int i = 0; i < strengths.Length; i++)
        {
            Dispersion.ThrowIfNotFinite(strengths[i], nameof(strengths));
            Dispersion.ThrowIfNotFinite(resonancesUm2[i], nameof(resonancesUm2));
        }

        Dispersion.ValidateRange(minimumWavelengthNanometers, maximumWavelengthNanometers);

        for (int i = 0; i < strengths.Length; i++)
        {
            RejectPoleInRange(strengths[i], resonancesUm2[i], minimumWavelengthNanometers, maximumWavelengthNanometers);
            this.strengths[i] = strengths[i];
            this.resonancesUm2[i] = resonancesUm2[i];
        }

        Offset = offset;
        TermCount = strengths.Length;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
    }

    /// <summary>Constant added to <c>n² − 1</c>.</summary>
    public double Offset { get; }

    /// <summary>Number of resonance terms in use.</summary>
    public int TermCount { get; }

    /// <inheritdoc />
    public double MinimumWavelengthNanometers { get; }

    /// <inheritdoc />
    public double MaximumWavelengthNanometers { get; }

    /// <summary>Returns <c>Bᵢ</c> for the zero-based term <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="TermCount"/>.</exception>
    public double StrengthAt(int index)
    {
        ThrowIfTermOutOfRange(index);
        return strengths[index];
    }

    /// <summary>Returns <c>Cᵢ</c> in micrometers squared for the zero-based term <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="TermCount"/>.</exception>
    public double ResonanceUm2At(int index)
    {
        ThrowIfTermOutOfRange(index);
        return resonancesUm2[index];
    }

    /// <inheritdoc />
    public DispersionStatus EvaluateNanometers(double wavelengthNanometers, out double refractiveIndex)
    {
        DispersionStatus status = Dispersion.ClassifyWavelength(wavelengthNanometers, MinimumWavelengthNanometers, MaximumWavelengthNanometers);
        if (status != DispersionStatus.Success)
        {
            refractiveIndex = double.NaN;
            return status;
        }

        double wavelengthMicrometers = wavelengthNanometers * 0.001d;
        double wavelengthSquared = wavelengthMicrometers * wavelengthMicrometers;
        if ((wavelengthSquared == 0d || double.IsSubnormal(wavelengthSquared)) && HasActivePositiveResonance())
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        double squared = 1d + Offset;
        for (int i = 0; i < TermCount; i++)
        {
            double strength = strengths[i];
            if (strength == 0d)
            {
                continue;
            }

            double resonance = resonancesUm2[i];
            if (resonance == 0d)
            {
                squared += strength;
                continue;
            }

            if (resonance > 0d && wavelengthSquared - resonance == 0d)
            {
                refractiveIndex = double.NaN;
                return DispersionStatus.Singular;
            }

            // A large wavelength may overflow on squaring while C / wavelength^2 remains significant.
            squared += double.IsPositiveInfinity(wavelengthSquared)
                ? strength / (1d - (resonance / wavelengthMicrometers) / wavelengthMicrometers)
                : strength * (wavelengthSquared / (wavelengthSquared - resonance));
        }

        if (!double.IsFinite(squared) || squared <= 0d)
        {
            refractiveIndex = double.NaN;
            return DispersionStatus.NonPhysical;
        }

        refractiveIndex = Math.Sqrt(squared);
        return DispersionStatus.Success;
    }

    /// <summary>Evaluates each wavelength into caller-owned spans; see <see cref="Dispersion.EvaluateNanometers{TModel}"/> for the buffer rules.</summary>
    public void EvaluateNanometers(
        ReadOnlySpan<double> wavelengthsNanometers,
        Span<double> refractiveIndices,
        Span<DispersionStatus> statuses) =>
        Dispersion.EvaluateNanometers(in this, wavelengthsNanometers, refractiveIndices, statuses);

    /// <inheritdoc />
    public bool Equals(Sellmeier other)
    {
        if (Offset != other.Offset ||
            TermCount != other.TermCount ||
            MinimumWavelengthNanometers != other.MinimumWavelengthNanometers ||
            MaximumWavelengthNanometers != other.MaximumWavelengthNanometers)
        {
            return false;
        }

        for (int i = 0; i < TermCount; i++)
        {
            if (strengths[i] != other.strengths[i] || resonancesUm2[i] != other.resonancesUm2[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Sellmeier other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Offset);
        hash.Add(TermCount);
        hash.Add(MinimumWavelengthNanometers);
        hash.Add(MaximumWavelengthNanometers);
        for (int i = 0; i < TermCount; i++)
        {
            hash.Add(strengths[i]);
            hash.Add(resonancesUm2[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Value equality over offset, terms and interval.</summary>
    public static bool operator ==(Sellmeier left, Sellmeier right) => left.Equals(right);

    /// <summary>Value inequality over offset, terms and interval.</summary>
    public static bool operator !=(Sellmeier left, Sellmeier right) => !left.Equals(right);

    private bool HasActivePositiveResonance()
    {
        for (int i = 0; i < TermCount; i++)
        {
            if (strengths[i] != 0d && resonancesUm2[i] > 0d)
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfTermOutOfRange(int index)
    {
        if ((uint)index >= (uint)TermCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The term index must be below TermCount.");
        }
    }

    private static void RejectPoleInRange(double strength, double resonanceUm2, double minimum, double maximum)
    {
        if (strength == 0d || resonanceUm2 <= 0d)
        {
            return;
        }

        double poleNanometers = Math.Sqrt(resonanceUm2) * 1_000d;
        if (poleNanometers >= minimum && poleNanometers <= maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(resonanceUm2), resonanceUm2, "The model interval must not contain an active resonance pole.");
        }
    }
}
