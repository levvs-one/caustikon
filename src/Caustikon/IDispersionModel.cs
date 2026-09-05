namespace Caustikon;

/// <summary>A refractive-index model over an inclusive wavelength interval.</summary>
/// <remarks>
/// Implementations are value types. Generic consumers constrain a type parameter as
/// <c>where T : struct, IDispersionModel</c> so the runtime specializes per model and no boxing occurs.
/// Holding a model behind this interface as a reference boxes it once; the evaluation itself does not allocate.
/// Every implementation follows the status rules in <see cref="DispersionStatus"/>: the result is assigned on every
/// call, and any status other than <see cref="DispersionStatus.Success"/> assigns <see cref="double.NaN"/>.
/// </remarks>
public interface IDispersionModel
{
    /// <summary>Inclusive lower bound of the fitted wavelength interval, in nanometers.</summary>
    double MinimumWavelengthNanometers { get; }

    /// <summary>Inclusive upper bound of the fitted wavelength interval, in nanometers.</summary>
    double MaximumWavelengthNanometers { get; }

    /// <summary>Evaluates the phase refractive index at one wavelength.</summary>
    /// <param name="wavelengthNanometers">Wavelength in nanometers, in the convention of the coefficient source.</param>
    /// <param name="refractiveIndex">Receives the index for <see cref="DispersionStatus.Success"/>, otherwise <see cref="double.NaN"/>.</param>
    /// <returns>The evaluation status.</returns>
    DispersionStatus EvaluateNanometers(double wavelengthNanometers, out double refractiveIndex);
}
