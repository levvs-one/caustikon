using System.Globalization;
using System.Numerics;
using Caustikon;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

const string Usage = "Usage: CriticalBoundary [--help]\nSix N-BK7 wavelengths at a fixed 41 degree glass-to-air incidence.";
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine(Usage);
    return 0;
}

if (args.Length != 0)
{
    Console.Error.WriteLine(Usage);
    return 1;
}

try
{
    const double incidenceDegrees = 41;
    const double radiansToDegrees = 180 / Math.PI;
    double[] wavelengthsNanometers = [404.7, 435.8, 486.1, 546.1, 587.6, 656.3];
    int count = wavelengthsNanometers.Length;

    // The catalogue index is relative to air, matching the surrounding index of 1.
    Sellmeier3 glass = new(
        b1: 1.039612120, c1Um2: 0.006000699,
        b2: 0.231792344, c2Um2: 0.0200179144,
        b3: 1.010469450, c3Um2: 103.56065300,
        minimumWavelengthNanometers: 365, maximumWavelengthNanometers: 2325.4);
    double[] indices = new double[count];
    DispersionStatus[] dispersionStatuses = new DispersionStatus[count];
    glass.EvaluateNanometers(wavelengthsNanometers, indices, dispersionStatuses);

    Vector3[] incidents = new Vector3[count];
    Vector3[] normals = new Vector3[count];
    float[] nIncidents = new float[count];
    float[] nTransmitteds = new float[count];
    float[] cosIncidents = new float[count];
    Vector3[] transmitted = new Vector3[count];
    RefractionKind[] kinds = new RefractionKind[count];
    FresnelPower[] powers = new FresnelPower[count];

    double angle = incidenceDegrees / radiansToDegrees;
    Vector3 incident = Vector3.Normalize(new Vector3((float)Math.Sin(angle), -(float)Math.Cos(angle), 0));
    double incidentLength = Math.Sqrt((double)incident.X * incident.X + (double)incident.Y * incident.Y);
    double sinIncident = incident.X / incidentLength;
    float cosIncident = (float)(-incident.Y / incidentLength);
    for (int i = 0; i < count; i++)
    {
        if (dispersionStatuses[i] != DispersionStatus.Success)
        {
            throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: dispersion returned {dispersionStatuses[i]}; no interface calculation was attempted.");
        }

        // Check the status before converting a dispersion result to the interface API's float index.
        nIncidents[i] = (float)indices[i];
        if (!float.IsFinite(nIncidents[i]) || nIncidents[i] <= 0)
        {
            throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: the index cannot be represented as a positive finite float.");
        }

        incidents[i] = incident;
        normals[i] = Vector3.UnitY;
        nTransmitteds[i] = 1;
        cosIncidents[i] = cosIncident;
    }

    Dielectric.RefractUnit(incidents, normals, nIncidents, nTransmitteds, transmitted, kinds);
    Dielectric.Fresnel(cosIncidents, nIncidents, nTransmitteds, powers);

    Console.WriteLine($"N-BK7 -> air | incidence {incidenceDegrees:F1} deg from the normal | initially unpolarized light");
    Console.WriteLine("lambda_nm   n_used       critical_deg   kind                      transmitted_deg   R_unpolarized");
    int refractedCount = 0;
    int reflectedCount = 0;
    for (int i = 0; i < count; i++)
    {
        DispersionStatus scalarStatus = glass.EvaluateNanometers(wavelengthsNanometers[i], out double scalarIndex);
        RefractionKind scalarKind = Dielectric.RefractUnit(incidents[i], normals[i], nIncidents[i],
            nTransmitteds[i], out Vector3 scalarDirection);
        FresnelPower scalarPower = Dielectric.Fresnel(cosIncidents[i], nIncidents[i], nTransmitteds[i]);
        if (scalarStatus != dispersionStatuses[i] || scalarIndex != indices[i] ||
            scalarKind != kinds[i] || scalarDirection != transmitted[i] || scalarPower != powers[i])
        {
            throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: scalar and batch results disagree.");
        }

        double sinTransmitted = nIncidents[i] * sinIncident;
        double criticalDegrees = Math.Asin(1d / nIncidents[i]) * radiansToDegrees;
        string transmittedDegrees = "-";
        switch (kinds[i])
        {
            case RefractionKind.TotalInternalReflection:
                if (sinTransmitted <= 1 || transmitted[i] != Vector3.Zero || powers[i] != new FresnelPower(1, 1))
                {
                    throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: total internal reflection disagrees with Snell's law or unit reflectance.");
                }

                reflectedCount++;
                break;

            case RefractionKind.Refracted:
                double cosTransmitted = Math.Sqrt(1 - sinTransmitted * sinTransmitted);
                // Each expected component is rounded once to float; two spacings at 1 leave a small arithmetic margin.
                double componentTolerance = 2d * (MathF.BitIncrement(1f) - 1f);
                if (!double.IsFinite(cosTransmitted) ||
                    Math.Abs(transmitted[i].X - sinTransmitted) > componentTolerance ||
                    Math.Abs(transmitted[i].Y + cosTransmitted) > componentTolerance || transmitted[i].Z != 0 ||
                    !float.IsFinite(powers[i].S) || !float.IsFinite(powers[i].P) ||
                    powers[i].S is < 0 or > 1 || powers[i].P is < 0 or > 1)
                {
                    throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: the transmitted direction or power fails the interface check.");
                }

                transmittedDegrees = (Math.Atan2(transmitted[i].X, -transmitted[i].Y) * radiansToDegrees)
                    .ToString("F5", CultureInfo.InvariantCulture);
                refractedCount++;
                break;

            default:
                throw new InvalidOperationException($"{wavelengthsNanometers[i]:F1} nm: unexpected {kinds[i]}; these samples must lie away from the snapped critical boundary.");
        }

        Console.WriteLine($"{wavelengthsNanometers[i],9:F1}   {nIncidents[i],10:F8}   {criticalDegrees,12:F5}   {kinds[i],-25} {transmittedDegrees,15}   {powers[i].Unpolarized,13:F6}");
    }

    if (refractedCount != 4 || reflectedCount != 2)
    {
        throw new InvalidOperationException("The fixed N-BK7 spectrum must produce four refracted and two totally internally reflected rays.");
    }

    Console.WriteLine($"Checked {count} wavelengths: {refractedCount} refracted, {reflectedCount} totally internally reflected; scalar and batch results agree.");
    return 0;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
