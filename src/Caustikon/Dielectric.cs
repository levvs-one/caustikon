using System.Numerics;
using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>Allocation-free geometric-optics operations for lossless dielectric interfaces.</summary>
/// <remarks>
/// Media are homogeneous, isotropic and nonabsorbing. Refractive indices are dimensionless phase indices
/// measured at comparable conditions and with the same reference medium. The incident medium is the one
/// the ray leaves; the transmitted medium is the one it enters. Indices and normals are never swapped automatically.
/// Batch methods execute synchronously in caller-owned storage, without allocating output arrays or scheduling parallel work.
/// </remarks>
public static class Dielectric
{
    private const float FloatMachineEpsilon = 1.1920929e-7f;

    /// <summary>
    /// Maximum accepted absolute error in a vector's squared length: eight times 2^-23, approximately 9.54e-7.
    /// </summary>
    /// <remarks>
    /// The test is abs(lengthSquared - 1) &lt;= this value, with squared length calculated in single precision.
    /// Here 2^-23 is the spacing immediately above 1f, not <see cref="float.Epsilon"/>.
    /// </remarks>
    public const float UnitLengthSquaredTolerance = 8f * FloatMachineEpsilon;

    /// <summary>Refracts a unit direction at a dielectric interface.</summary>
    /// <param name="incidentUnit">Finite ray-travel direction toward the interface, within <see cref="UnitLengthSquaredTolerance"/> of unit squared length.</param>
    /// <param name="normalUnit">Finite normal pointing into the incident medium, within the same unit squared-length tolerance.</param>
    /// <param name="nIncident">Finite, positive phase refractive index of the medium the ray leaves.</param>
    /// <param name="nTransmitted">Finite, positive phase refractive index of the medium the ray enters.</param>
    /// <param name="refractedUnit">
    /// Receives the transmitted direction for Refracted, a unit tangent for CriticalAngle, or
    /// <see cref="Vector3.Zero"/> for InvalidInput and TotalInternalReflection.
    /// </param>
    /// <returns>The physical or validation outcome; invalid arguments return <see cref="RefractionKind.InvalidInput"/> rather than throwing.</returns>
    /// <remarks>
    /// The incident-normal dot product must be nonpositive. Equal indices return the incident vector exactly,
    /// including at grazing incidence. Otherwise the calculation compensates for accepted input length error
    /// with double-precision intermediates; it does not normalize the final refracted vector.
    /// For a higher-to-lower-index transition, the critical boundary is abs(1 - sin2T) &lt;= 8 * 2^-23 * max(1, sin2T),
    /// where sin2T is the squared transmitted tangential length. Within that boundary the output is a unit tangent.
    /// Entering a higher-index medium has no critical-angle snapping.
    /// </remarks>
    public static RefractionKind RefractUnit(
        Vector3 incidentUnit,
        Vector3 normalUnit,
        float nIncident,
        float nTransmitted,
        out Vector3 refractedUnit)
    {
        if (!IsPositiveFinite(nIncident) || !IsPositiveFinite(nTransmitted))
        {
            refractedUnit = Vector3.Zero;
            return RefractionKind.InvalidInput;
        }

        return RefractCore(incidentUnit, normalUnit, (double)nIncident / nTransmitted, out refractedUnit);
    }

    /// <summary>Refracts a batch with one pair of refractive indices shared by every lane.</summary>
    /// <param name="incidentUnits">Ray-travel directions toward the interface; each must satisfy the scalar unit-vector contract.</param>
    /// <param name="normalUnits">Normals pointing into the incident medium; each must satisfy the scalar unit-vector and orientation contract.</param>
    /// <param name="nIncident">Shared finite, positive phase index of the incident medium.</param>
    /// <param name="nTransmitted">Shared finite, positive phase index of the transmitted medium.</param>
    /// <param name="refractedUnits">Caller-owned transmitted directions; invalid and totally internally reflected lanes receive <see cref="Vector3.Zero"/>.</param>
    /// <param name="kinds">Caller-owned physical or validation outcome for every lane.</param>
    /// <exception cref="ArgumentException">Span lengths differ or buffers have forbidden overlap. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A shared refractive index is nonfinite or nonpositive. No output is written.</exception>
    /// <remarks>
    /// All spans must have the same length. Exact incident-to-result in-place operation is allowed, but partial
    /// overlap is not. Results must not overlap normals, even exactly; status storage must not overlap any other span,
    /// including across element types. Invalid vectors are reported per lane, not as exceptions.
    /// Direction and boundary rules are those of <see cref="RefractUnit(Vector3, Vector3, float, float, out Vector3)"/>.
    /// </remarks>
    public static void RefractUnit(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        float nIncident,
        float nTransmitted,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        ValidateRefractionShape(incidentUnits, normalUnits, refractedUnits, kinds);
        ValidateRefractionOverlap(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        double eta = (double)nIncident / nTransmitted;
        for (int i = 0; i < incidentUnits.Length; i++)
        {
            kinds[i] = RefractCore(incidentUnits[i], normalUnits[i], eta, out refractedUnits[i]);
        }
    }

    /// <summary>Refracts a batch whose refractive indices vary per lane.</summary>
    /// <param name="incidentUnits">Ray-travel directions toward the interface; each must satisfy the scalar unit-vector contract.</param>
    /// <param name="normalUnits">Normals pointing into the incident medium; each must satisfy the scalar unit-vector and orientation contract.</param>
    /// <param name="nIncidents">Finite, positive phase index of the incident medium for each lane.</param>
    /// <param name="nTransmitteds">Finite, positive phase index of the transmitted medium for each lane.</param>
    /// <param name="refractedUnits">Caller-owned transmitted directions; invalid and totally internally reflected lanes receive <see cref="Vector3.Zero"/>.</param>
    /// <param name="kinds">Caller-owned physical or validation outcome for every lane.</param>
    /// <exception cref="ArgumentException">Span lengths differ or buffers have forbidden overlap. No output is written.</exception>
    /// <remarks>
    /// All spans must have the same length. Exact incident-to-result in-place operation is allowed, but partial
    /// overlap is not. Results must not overlap normals or index spans, even exactly or across element types.
    /// Status storage must not overlap any input or other output. Invalid vectors and indices produce
    /// <see cref="RefractionKind.InvalidInput"/> per lane, allowing other lanes to succeed.
    /// Direction and boundary rules are those of <see cref="RefractUnit(Vector3, Vector3, float, float, out Vector3)"/>.
    /// </remarks>
    public static void RefractUnit(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        ValidateRefractionShape(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfLengthDiffers(nIncidents.Length, incidentUnits.Length, nameof(nIncidents));
        ThrowIfLengthDiffers(nTransmitteds.Length, incidentUnits.Length, nameof(nTransmitteds));
        ValidateRefractionOverlap(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfOverlap(nIncidents, refractedUnits, nameof(refractedUnits));
        ThrowIfOverlap(nTransmitteds, refractedUnits, nameof(refractedUnits));
        ThrowIfOverlap(nIncidents, kinds, nameof(kinds));
        ThrowIfOverlap(nTransmitteds, kinds, nameof(kinds));

        for (int i = 0; i < incidentUnits.Length; i++)
        {
            kinds[i] = RefractUnit(
                incidentUnits[i],
                normalUnits[i],
                nIncidents[i],
                nTransmitteds[i],
                out refractedUnits[i]);
        }
    }

    /// <summary>Computes exact S, P, and unpolarized power reflectance.</summary>
    /// <param name="cosIncident">Finite, nonnegative cosine of the incidence angle, in [0, 1]; 0 is grazing and 1 is normal incidence.</param>
    /// <param name="nIncident">Finite, positive phase refractive index of the medium the ray leaves.</param>
    /// <param name="nTransmitted">Finite, positive phase refractive index of the medium the ray enters.</param>
    /// <returns>Reflected power fractions in [0, 1] for both polarizations and their unpolarized mean.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cosine is nonfinite or outside [0, 1], or an index is nonfinite or nonpositive.</exception>
    /// <remarks>
    /// Equal indices give zero reflectance, including at grazing incidence. For a higher-to-lower-index transition,
    /// reflectance is one when 1 - sin2T &lt;= 8 * 2^-23 * max(1, sin2T), including total internal reflection;
    /// sin2T = (nIncident / nTransmitted)^2 * (1 - cosIncident^2). No critical snapping applies in the other direction.
    /// Ratios and amplitudes use double-precision intermediates. Derive the cosine from normalized directions,
    /// not from vectors with residual unit-length error.
    /// </remarks>
    public static FresnelPower Fresnel(float cosIncident, float nIncident, float nTransmitted)
    {
        ThrowIfInvalidCosine(cosIncident, nameof(cosIncident));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        if (nIncident == nTransmitted)
        {
            return default;
        }

        double eta = (double)nIncident / nTransmitted;
        double sinIncidentSquared = (1d - cosIncident) * (1d + cosIncident);
        double sinTransmittedSquared = eta * eta * sinIncidentSquared;
        double transmittedCosineSquared = 1d - sinTransmittedSquared;
        double criticalTolerance = CriticalTolerance(sinTransmittedSquared);
        if (eta > 1d && transmittedCosineSquared <= criticalTolerance)
        {
            return new FresnelPower(1f, 1f);
        }

        double cosTransmitted = Math.Sqrt(transmittedCosineSquared);
        double sIncident = (double)nIncident * cosIncident;
        double sTransmitted = nTransmitted * cosTransmitted;
        double pIncident = (double)nTransmitted * cosIncident;
        double pTransmitted = nIncident * cosTransmitted;
        double s = (sIncident - sTransmitted) / (sIncident + sTransmitted);
        double p = (pIncident - pTransmitted) / (pIncident + pTransmitted);
        return new FresnelPower((float)(s * s), (float)(p * p));
    }

    /// <summary>Computes exact Fresnel power for a batch with shared refractive indices.</summary>
    /// <param name="cosIncidents">Finite incidence cosines in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="nIncident">Shared finite, positive phase index of the incident medium.</param>
    /// <param name="nTransmitted">Shared finite, positive phase index of the transmitted medium.</param>
    /// <param name="powers">Caller-owned power-reflectance results, with the same length as the input.</param>
    /// <exception cref="ArgumentException">Span lengths differ or output storage overlaps the input, including across element types. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any cosine is nonfinite or outside [0, 1], or a shared index is nonfinite or nonpositive. No output is written.</exception>
    /// <remarks>Every lane follows <see cref="Fresnel(float, float, float)"/>. All inputs are validated before writing any result.</remarks>
    public static void Fresnel(
        ReadOnlySpan<float> cosIncidents,
        float nIncident,
        float nTransmitted,
        Span<FresnelPower> powers)
    {
        ThrowIfLengthDiffers(powers.Length, cosIncidents.Length, nameof(powers));
        ThrowIfOverlap(cosIncidents, powers, nameof(powers));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            powers[i] = Fresnel(cosIncidents[i], nIncident, nTransmitted);
        }
    }

    /// <summary>Computes exact Fresnel power for a batch whose refractive indices vary per lane.</summary>
    /// <param name="cosIncidents">Finite incidence cosines in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="nIncidents">Finite, positive phase index of the incident medium for each lane.</param>
    /// <param name="nTransmitteds">Finite, positive phase index of the transmitted medium for each lane.</param>
    /// <param name="powers">Caller-owned power-reflectance results.</param>
    /// <exception cref="ArgumentException">Span lengths differ or output storage overlaps any input, including across element types. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any cosine is nonfinite or outside [0, 1], or any index is nonfinite or nonpositive. No output is written.</exception>
    /// <remarks>
    /// All spans must have the same length. Every lane follows <see cref="Fresnel(float, float, float)"/>.
    /// All inputs are validated before writing any result; invalid lanes throw rather than returning a status.
    /// </remarks>
    public static void Fresnel(
        ReadOnlySpan<float> cosIncidents,
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<FresnelPower> powers)
    {
        int length = cosIncidents.Length;
        ThrowIfLengthDiffers(nIncidents.Length, length, nameof(nIncidents));
        ThrowIfLengthDiffers(nTransmitteds.Length, length, nameof(nTransmitteds));
        ThrowIfLengthDiffers(powers.Length, length, nameof(powers));
        ThrowIfOverlap(cosIncidents, powers, nameof(powers));
        ThrowIfOverlap(nIncidents, powers, nameof(powers));
        ThrowIfOverlap(nTransmitteds, powers, nameof(powers));

        for (int i = 0; i < length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
            ThrowIfNotPositiveFinite(nIncidents[i], nameof(nIncidents));
            ThrowIfNotPositiveFinite(nTransmitteds[i], nameof(nTransmitteds));
        }

        for (int i = 0; i < length; i++)
        {
            powers[i] = Fresnel(cosIncidents[i], nIncidents[i], nTransmitteds[i]);
        }
    }

    /// <summary>Computes power reflectance at normal incidence.</summary>
    /// <param name="nIncident">Finite, positive phase refractive index of the medium the ray leaves.</param>
    /// <param name="nTransmitted">Finite, positive phase refractive index of the medium the ray enters.</param>
    /// <returns>The power fraction ((nIncident - nTransmitted) / (nIncident + nTransmitted))^2, in [0, 1].</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either index is nonfinite or nonpositive.</exception>
    /// <remarks>Uses double-precision intermediates so sums and differences of finite float indices do not overflow.</remarks>
    public static float NormalReflectance(float nIncident, float nTransmitted)
    {
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));
        double amplitude = ((double)nIncident - nTransmitted) / ((double)nIncident + nTransmitted);
        return (float)(amplitude * amplitude);
    }

    /// <summary>Computes normal-incidence power reflectance for a batch.</summary>
    /// <param name="nIncidents">Finite, positive phase index of the incident medium for each lane.</param>
    /// <param name="nTransmitteds">Finite, positive phase index of the transmitted medium for each lane.</param>
    /// <param name="reflectances">Caller-owned power fractions in [0, 1].</param>
    /// <exception cref="ArgumentException">Span lengths differ or an input and output partially overlap. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any index is nonfinite or nonpositive. No output is written.</exception>
    /// <remarks>
    /// All spans must have the same length. Output may exactly replace either input in place.
    /// All inputs are validated before writing; each result follows <see cref="NormalReflectance(float, float)"/>.
    /// </remarks>
    public static void NormalReflectance(
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<float> reflectances)
    {
        int length = nIncidents.Length;
        ThrowIfLengthDiffers(nTransmitteds.Length, length, nameof(nTransmitteds));
        ThrowIfLengthDiffers(reflectances.Length, length, nameof(reflectances));
        ValidateNoPartialOverlap(nIncidents, reflectances, nameof(reflectances));
        ValidateNoPartialOverlap(nTransmitteds, reflectances, nameof(reflectances));

        for (int i = 0; i < length; i++)
        {
            ThrowIfNotPositiveFinite(nIncidents[i], nameof(nIncidents));
            ThrowIfNotPositiveFinite(nTransmitteds[i], nameof(nTransmitteds));
        }

        for (int i = 0; i < length; i++)
        {
            reflectances[i] = NormalReflectance(nIncidents[i], nTransmitteds[i]);
        }
    }

    /// <summary>Applies Schlick's approximation using a supplied normal-incidence reflectance.</summary>
    /// <param name="cosIncident">Finite incidence cosine in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="normalReflectance">Finite normal-incidence power fraction R0 in [0, 1].</param>
    /// <returns>R0 + (1 - R0) * (1 - cosIncident)^5, a power fraction in [0, 1].</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is nonfinite or outside [0, 1].</exception>
    /// <remarks>
    /// This approximation cannot detect total internal reflection or the critical boundary.
    /// Use <see cref="Fresnel(float, float, float)"/> for exact dielectric power reflectance and boundary handling.
    /// </remarks>
    public static float Schlick(float cosIncident, float normalReflectance)
    {
        ThrowIfInvalidCosine(cosIncident, nameof(cosIncident));
        if (!float.IsFinite(normalReflectance) || normalReflectance < 0f || normalReflectance > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalReflectance), normalReflectance, "Reflectance must be finite and in [0, 1].");
        }

        float complement = 1f - cosIncident;
        float complementSquared = complement * complement;
        float complementFifth = complementSquared * complementSquared * complement;
        return MathF.FusedMultiplyAdd(1f - normalReflectance, complementFifth, normalReflectance);
    }

    /// <summary>Applies Schlick's approximation using reflectance derived from the two indices.</summary>
    /// <param name="cosIncident">Finite incidence cosine in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="nIncident">Finite, positive phase refractive index of the medium the ray leaves.</param>
    /// <param name="nTransmitted">Finite, positive phase refractive index of the medium the ray enters.</param>
    /// <returns>The Schlick power fraction in [0, 1], using <see cref="NormalReflectance(float, float)"/> as R0.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cosine is nonfinite or outside [0, 1], or an index is nonfinite or nonpositive.</exception>
    /// <remarks>
    /// Supplying both indices only derives R0; it does not add total-internal-reflection or critical-boundary detection.
    /// Equal indices are not special-cased: the approximation still evaluates (1 - cosIncident)^5.
    /// Use <see cref="Fresnel(float, float, float)"/> for exact dielectric power reflectance.
    /// </remarks>
    public static float Schlick(float cosIncident, float nIncident, float nTransmitted) =>
        Schlick(cosIncident, NormalReflectance(nIncident, nTransmitted));

    /// <summary>Applies Schlick's approximation to a batch using one shared normal reflectance.</summary>
    /// <param name="cosIncidents">Finite incidence cosines in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="normalReflectance">Shared finite normal-incidence power fraction R0 in [0, 1].</param>
    /// <param name="reflectances">Caller-owned Schlick power fractions, with the same length as the input.</param>
    /// <exception cref="ArgumentException">Span lengths differ or input and output partially overlap. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any cosine or the shared reflectance is nonfinite or outside [0, 1]. No output is written.</exception>
    /// <remarks>
    /// Exact cosine-to-result in-place operation is allowed. All inputs are validated before writing any result.
    /// Every lane follows <see cref="Schlick(float, float)"/>; no total-internal-reflection or critical-boundary detection is performed.
    /// </remarks>
    public static void Schlick(
        ReadOnlySpan<float> cosIncidents,
        float normalReflectance,
        Span<float> reflectances)
    {
        ThrowIfLengthDiffers(reflectances.Length, cosIncidents.Length, nameof(reflectances));
        ValidateNoPartialOverlap(cosIncidents, reflectances, nameof(reflectances));
        if (!float.IsFinite(normalReflectance) || normalReflectance < 0f || normalReflectance > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalReflectance), normalReflectance, "Reflectance must be finite and in [0, 1].");
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            reflectances[i] = Schlick(cosIncidents[i], normalReflectance);
        }
    }

    /// <summary>Applies Schlick's approximation to a batch using shared refractive indices.</summary>
    /// <param name="cosIncidents">Finite incidence cosines in [0, 1], where 0 is grazing and 1 is normal incidence.</param>
    /// <param name="nIncident">Shared finite, positive phase index of the incident medium.</param>
    /// <param name="nTransmitted">Shared finite, positive phase index of the transmitted medium.</param>
    /// <param name="reflectances">Caller-owned Schlick power fractions, with the same length as the input.</param>
    /// <exception cref="ArgumentException">Span lengths differ or input and output partially overlap. No output is written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any cosine is nonfinite or outside [0, 1], or a shared index is nonfinite or nonpositive. No output is written.</exception>
    /// <remarks>
    /// Exact cosine-to-result in-place operation is allowed. All inputs are validated before writing any result.
    /// Every lane follows <see cref="Schlick(float, float, float)"/>; the indices only derive R0 and do not add
    /// total-internal-reflection or critical-boundary detection, including for equal indices.
    /// </remarks>
    public static void Schlick(
        ReadOnlySpan<float> cosIncidents,
        float nIncident,
        float nTransmitted,
        Span<float> reflectances)
    {
        ThrowIfLengthDiffers(reflectances.Length, cosIncidents.Length, nameof(reflectances));
        ValidateNoPartialOverlap(cosIncidents, reflectances, nameof(reflectances));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        float normalReflectance = NormalReflectance(nIncident, nTransmitted);
        for (int i = 0; i < cosIncidents.Length; i++)
        {
            reflectances[i] = Schlick(cosIncidents[i], normalReflectance);
        }
    }

    private static RefractionKind RefractCore(Vector3 incident, Vector3 normal, double eta, out Vector3 transmitted)
    {
        transmitted = Vector3.Zero;
        if (!IsUnitVector(incident) || !IsUnitVector(normal))
        {
            return RefractionKind.InvalidInput;
        }

        double dot = (double)incident.X * normal.X + (double)incident.Y * normal.Y + (double)incident.Z * normal.Z;
        if (dot > 0d)
        {
            return RefractionKind.InvalidInput;
        }

        if (eta == 1d)
        {
            transmitted = incident;
            return RefractionKind.Refracted;
        }

        // Cross products preserve small tangential components that 1 - cos^2 loses near normal incidence.
        double crossX = (double)incident.Y * normal.Z - (double)incident.Z * normal.Y;
        double crossY = (double)incident.Z * normal.X - (double)incident.X * normal.Z;
        double crossZ = (double)incident.X * normal.Y - (double)incident.Y * normal.X;
        double normalSquared = (double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z;
        double incidentSquared = (double)incident.X * incident.X + (double)incident.Y * incident.Y + (double)incident.Z * incident.Z;
        // Correct only the accepted unit-length rounding error, which matters at grazing incidence.
        double scale = eta / (normalSquared * Math.Sqrt(incidentSquared));
        double tangentX = (normal.Y * crossZ - normal.Z * crossY) * scale;
        double tangentY = (normal.Z * crossX - normal.X * crossZ) * scale;
        double tangentZ = (normal.X * crossY - normal.Y * crossX) * scale;
        double sinTransmittedSquared = tangentX * tangentX + tangentY * tangentY + tangentZ * tangentZ;
        double discriminant = 1d - sinTransmittedSquared;
        double tolerance = CriticalTolerance(sinTransmittedSquared);
        if (discriminant < -tolerance)
        {
            return RefractionKind.TotalInternalReflection;
        }

        bool critical = eta > 1d && Math.Abs(discriminant) <= tolerance;
        if (critical)
        {
            // Snapping the boundary also snaps the tangent to unit length.
            double tangentScale = 1d / Math.Sqrt(sinTransmittedSquared);
            transmitted = new Vector3((float)(tangentX * tangentScale), (float)(tangentY * tangentScale), (float)(tangentZ * tangentScale));
            return RefractionKind.CriticalAngle;
        }

        double normalScale = Math.Sqrt(discriminant / normalSquared);
        transmitted = new Vector3(
            (float)(tangentX - normalScale * normal.X),
            (float)(tangentY - normalScale * normal.Y),
            (float)(tangentZ - normalScale * normal.Z));
        return RefractionKind.Refracted;
    }

    private static double CriticalTolerance(double sinTransmittedSquared) =>
        8d * FloatMachineEpsilon * Math.Max(1d, sinTransmittedSquared);

    private static bool IsUnitVector(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            return false;
        }

        float lengthSquared = Vector3.Dot(value, value);
        return MathF.Abs(lengthSquared - 1f) <= UnitLengthSquaredTolerance;
    }

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;

    private static void ValidateRefractionShape(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        int length = incidentUnits.Length;
        ThrowIfLengthDiffers(normalUnits.Length, length, nameof(normalUnits));
        ThrowIfLengthDiffers(refractedUnits.Length, length, nameof(refractedUnits));
        ThrowIfLengthDiffers(kinds.Length, length, nameof(kinds));
    }

    private static void ThrowIfLengthDiffers(int actual, int expected, string parameterName)
    {
        if (actual != expected)
        {
            throw new ArgumentException("All spans must have the same length.", parameterName);
        }
    }

    private static void ThrowIfInvalidCosine(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Cosine must be finite and in [0, 1].");
        }
    }

    private static void ThrowIfNotPositiveFinite(float value, string parameterName)
    {
        if (!IsPositiveFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Refractive index must be finite and greater than zero.");
        }
    }

    private static void ValidateNoPartialOverlap<T>(ReadOnlySpan<T> source, Span<T> destination, string parameterName)
    {
        if (source.Overlaps(destination, out int elementOffset) && elementOffset != 0)
        {
            throw new ArgumentException("Source and destination may be identical, but must not partially overlap.", parameterName);
        }
    }

    private static void ValidateRefractionOverlap(
        ReadOnlySpan<Vector3> incidents, ReadOnlySpan<Vector3> normals,
        Span<Vector3> transmitted, Span<RefractionKind> kinds)
    {
        ValidateNoPartialOverlap(incidents, transmitted, "refractedUnits");
        ThrowIfOverlap(normals, transmitted, "refractedUnits");
        ThrowIfOverlap(incidents, kinds, nameof(kinds));
        ThrowIfOverlap(normals, kinds, nameof(kinds));
        ThrowIfOverlap((ReadOnlySpan<Vector3>)transmitted, kinds, nameof(kinds));
    }

    private static void ThrowIfOverlap<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, string parameterName)
        where TSource : struct
        where TDestination : struct
    {
        if (MemoryMarshal.AsBytes(source).Overlaps(MemoryMarshal.AsBytes(destination)))
        {
            throw new ArgumentException("These buffers must not overlap.", parameterName);
        }
    }
}
