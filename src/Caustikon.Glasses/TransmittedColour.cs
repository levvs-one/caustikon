namespace Caustikon.Glasses;

/// <summary>The colour of light after passing through a glass, computed spectrally and expressed in CIE XYZ and sRGB.</summary>
/// <remarks>
/// <para><see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> are normalized so that a perfect transmitter under the same
/// illuminant has <c>Y = 1</c>; <see cref="Y"/> is therefore the luminous transmittance. The sRGB values use the IEC 61966-2-1
/// D65 white point, the standard linear matrix and the piecewise companding curve; <see cref="ClippedToGamut"/> reports
/// whether any linear channel had to be clamped to [0, 1], which a strongly coloured filter can require.</para>
/// <para><see cref="CoverageMinimumNanometers"/> and <see cref="CoverageMaximumNanometers"/> bound the wavelengths the
/// glass's own data covered. Outside them, within the observer's 360–830 nm, the nearest tabulated value was held
/// constant. When the covered interval does not reach 380–780 nm the colour rests partly on that assumption; the
/// values are still returned, and the bounds say so.</para>
/// </remarks>
/// <param name="X">CIE 1931 X, illuminant-normalized.</param>
/// <param name="Y">CIE 1931 Y, illuminant-normalized: the luminous transmittance in [0, 1].</param>
/// <param name="Z">CIE 1931 Z, illuminant-normalized.</param>
/// <param name="LinearRed">Linear sRGB red before companding, clamped to [0, 1].</param>
/// <param name="LinearGreen">Linear sRGB green before companding, clamped to [0, 1].</param>
/// <param name="LinearBlue">Linear sRGB blue before companding, clamped to [0, 1].</param>
/// <param name="ClippedToGamut">Whether any linear channel fell outside [0, 1] before clamping.</param>
/// <param name="CoverageMinimumNanometers">Shortest wavelength the glass data itself covered.</param>
/// <param name="CoverageMaximumNanometers">Longest wavelength the glass data itself covered.</param>
public readonly record struct TransmittedColour(
    double X,
    double Y,
    double Z,
    double LinearRed,
    double LinearGreen,
    double LinearBlue,
    bool ClippedToGamut,
    double CoverageMinimumNanometers,
    double CoverageMaximumNanometers)
{
    /// <summary>Companded sRGB red in [0, 1].</summary>
    public double Red => Compand(LinearRed);

    /// <summary>Companded sRGB green in [0, 1].</summary>
    public double Green => Compand(LinearGreen);

    /// <summary>Companded sRGB blue in [0, 1].</summary>
    public double Blue => Compand(LinearBlue);

    /// <summary>Companded sRGB as an 8-bit triplet.</summary>
    public (byte Red, byte Green, byte Blue) Rgb8 => (ToByte(Red), ToByte(Green), ToByte(Blue));

    /// <summary>Companded sRGB as a lower-case hexadecimal string, for example <c>#fdfcf7</c>.</summary>
    public string Hex
    {
        get
        {
            (byte r, byte g, byte b) = Rgb8;
            return $"#{r:x2}{g:x2}{b:x2}";
        }
    }

    /// <summary>Applies the sRGB transfer function to a linear channel value in [0, 1].</summary>
    public static double Compand(double linear) =>
        linear <= 0.0031308d ? 12.92d * linear : 1.055d * Math.Pow(linear, 1d / 2.4d) - 0.055d;

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255d), 0d, 255d);
}
