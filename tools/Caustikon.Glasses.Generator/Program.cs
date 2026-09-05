// Reads manufacturer glass entries from a RefractiveIndex.INFO database checkout and writes two things into the
// repository: the normalized data of record (data/glasses/*.json) and the C# catalog (src/Caustikon.Glasses/Generated).
// Every emitted number is evaluated through Caustikon itself before it is written, and the deviation from the
// manufacturer's printed nd and Vd is recorded in the manifest so the tests can pin it.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Caustikon;
using YamlDotNet.Serialization;

Dictionary<string, string> options = ParseOptions(args);
string database = Require(options, "database");
string repository = Require(options, "repository");
string commit = Require(options, "commit");
DateOnly retrieved = DateOnly.ParseExact(Require(options, "retrieved"), "yyyy-MM-dd", CultureInfo.InvariantCulture);

if (options.TryGetValue("cie", out string? cieDirectory))
{
    CieEmitter.Emit(cieDirectory, repository);
}

string specs = Path.Combine(database, "data", "specs");
if (!Directory.Exists(specs))
{
    Console.Error.WriteLine($"No data/specs under {database}.");
    return 2;
}

string source = $"RefractiveIndex.INFO database, commit {commit}, CC0 1.0";
IDeserializer yaml = new DeserializerBuilder().Build();

List<VendorOutput> vendors = [];
List<SkippedEntry> skipped = [];
foreach (string vendorDirectory in Directory.GetDirectories(specs).OrderBy(static d => d, StringComparer.Ordinal))
{
    string vendorKey = Path.GetFileName(vendorDirectory).ToLowerInvariant();
    List<GlassEntry> entries = [];
    foreach (string file in Directory.GetFiles(vendorDirectory, "*.yml", SearchOption.AllDirectories).OrderBy(static f => f, StringComparer.Ordinal))
    {
        if (Path.GetFileName(file).Equals("about.yml", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        string relative = Path.GetRelativePath(database, file).Replace('\\', '/');
        try
        {
            GlassEntry? entry = ReadEntry(file, relative, vendorKey, yaml, source, retrieved, out string? skipReason);
            if (entry is null)
            {
                skipped.Add(new SkippedEntry(relative, skipReason ?? "unspecified"));
                continue;
            }

            entries.Add(entry);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException or YamlDotNet.Core.YamlException)
        {
            skipped.Add(new SkippedEntry(relative, "unreadable: " + exception.Message));
        }
    }

    if (entries.Count > 0)
    {
        vendors.Add(new VendorOutput(vendorKey, VendorDisplayName(vendorKey), VendorClassName(vendorKey), entries));
    }
}

AssignIdentifiers(vendors);

string dataDirectory = Path.Combine(repository, "data", "glasses");
string generatedDirectory = Path.Combine(repository, "src", "Caustikon.Glasses", "Generated");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(generatedDirectory);
foreach (string stale in Directory.GetFiles(generatedDirectory, "*.g.cs").Concat(Directory.GetFiles(dataDirectory, "*.json")))
{
    if (Path.GetFileName(stale) != "CieTables.g.cs")
    {
        File.Delete(stale);
    }
}

JsonSerializerOptions json = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
};

foreach (VendorOutput vendor in vendors)
{
    File.WriteAllText(Path.Combine(dataDirectory, vendor.Key + ".json"), JsonSerializer.Serialize(vendor.Entries, json) + "\n", new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(generatedDirectory, vendor.ClassName + ".g.cs"), EmitVendor(vendor), new UTF8Encoding(false));
}

File.WriteAllText(Path.Combine(generatedDirectory, "GlassCatalog.Sources.g.cs"), EmitSources(vendors), new UTF8Encoding(false));

Manifest manifest = BuildManifest(vendors, skipped, commit, retrieved);
File.WriteAllText(Path.Combine(dataDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, json) + "\n", new UTF8Encoding(false));

Console.WriteLine($"{manifest.GlassCount} glasses from {vendors.Count} manufacturers; {skipped.Count} entries skipped.");
Console.WriteLine($"max |Δnd| = {manifest.MaxAbsoluteIndexDDeviation:E2} ({manifest.MaxAbsoluteIndexDDeviationGlass}); max |ΔVd| = {manifest.MaxAbsoluteAbbeDDeviation:F4} ({manifest.MaxAbsoluteAbbeDDeviationGlass})");
foreach (VendorSummary summary in manifest.Vendors)
{
    Console.WriteLine($"  {summary.Vendor,-10} {summary.GlassCount,5}  sellmeier {summary.Sellmeier,4}  polynomial {summary.Polynomial,4}  cauchy {summary.Cauchy,3}  with-k {summary.WithExtinction,4}  with-dn/dT {summary.WithThermal,4}");
}

return 0;

static GlassEntry? ReadEntry(string file, string relativePath, string vendorKey, IDeserializer yaml, string source, DateOnly retrieved, out string? skipReason)
{
    skipReason = null;
    // Source files use CRLF and sometimes leave an empty literal block ("COMMENTS: |") holding only spaces, which the
    // YAML scanner rejects as "extra spaces in first line". Trailing whitespace carries no data, so it is stripped first.
    string text = Regex.Replace(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), "[ \t]+$", "", RegexOptions.Multiline);
    Dictionary<object, object> document = yaml.Deserialize<Dictionary<object, object>>(text);

    if (document.GetValueOrDefault("DATA") is not List<object> data)
    {
        skipReason = "no DATA section";
        return null;
    }

    Dictionary<object, object>? formulaBlock = null;
    Dictionary<object, object>? extinctionBlock = null;
    List<string> dataTypes = [];
    foreach (object item in data)
    {
        if (item is not Dictionary<object, object> block || block.GetValueOrDefault("type") is not string type)
        {
            continue;
        }

        dataTypes.Add(type);
        if (type.StartsWith("formula ", StringComparison.Ordinal))
        {
            formulaBlock ??= block;
        }
        else if (type == "tabulated k")
        {
            extinctionBlock ??= block;
        }
    }

    if (formulaBlock is null)
    {
        skipReason = "no closed-form dispersion (" + string.Join(", ", dataTypes) + ")";
        return null;
    }

    string formulaType = (string)formulaBlock["type"];
    int formulaNumber = int.Parse(formulaType["formula ".Length..], CultureInfo.InvariantCulture);
    if (formulaNumber is not (1 or 2 or 3 or 5))
    {
        skipReason = "unsupported " + formulaType;
        return null;
    }

    double[] range = ParseNumbers((string)formulaBlock["wavelength_range"]);
    if (range.Length != 2)
    {
        skipReason = "wavelength_range is not a pair";
        return null;
    }

    double minimumNm = range[0] * 1000d;
    double maximumNm = range[1] * 1000d;
    double[] coefficients = ParseNumbers((string)formulaBlock["coefficients"]);
    if (coefficients.Length == 0 || coefficients.Length % 2 == 0)
    {
        skipReason = "coefficient count is not 1 + 2n";
        return null;
    }

    double offset = coefficients[0];
    int termCount = (coefficients.Length - 1) / 2;
    double[] first = new double[termCount];
    double[] second = new double[termCount];
    for (int i = 0; i < termCount; i++)
    {
        first[i] = coefficients[1 + 2 * i];
        second[i] = coefficients[2 + 2 * i];
    }

    string notes = "";
    DispersionForm form;
    switch (formulaNumber)
    {
        case 1:
            for (int i = 0; i < termCount; i++)
            {
                second[i] *= second[i];
            }

            notes = "Sellmeier-1 source: each resonance wavelength was squared to the micrometer-squared form.";
            form = DispersionForm.Sellmeier;
            break;
        case 2:
            form = DispersionForm.Sellmeier;
            break;
        case 3:
            form = DispersionForm.Polynomial;
            break;
        default:
            form = DispersionForm.Cauchy;
            break;
    }

    // Build the model through Caustikon so construction rules and numerics are exactly what the package ships.
    IDispersionModel model;
    try
    {
        model = form switch
        {
            DispersionForm.Sellmeier => new Sellmeier(offset, first, second, minimumNm, maximumNm),
            DispersionForm.Polynomial => new Polynomial(offset, first, second, minimumNm, maximumNm),
            _ => new Cauchy(offset, first, second, minimumNm, maximumNm),
        };
    }
    catch (ArgumentException exception)
    {
        skipReason = "model rejected: " + exception.Message;
        return null;
    }

    string name = Path.GetFileNameWithoutExtension(file);
    string category = CategoryOf(relativePath, vendorKey);

    (string citation, Uri? url) = ParseReferences(document.GetValueOrDefault("REFERENCES") as string);
    double? temperature = null;
    if (document.GetValueOrDefault("CONDITIONS") is Dictionary<object, object> conditions &&
        conditions.GetValueOrDefault("temperature") is string temperatureText)
    {
        temperature = ParseDouble(temperatureText);
    }

    double? nd = null, vd = null, density = null, dPgF = null;
    string? glassCode = null;
    string status = "";
    ThermalCoefficients? thermal = null;
    if (document.GetValueOrDefault("PROPERTIES") is Dictionary<object, object> properties)
    {
        nd = OptionalDouble(properties, "nd");
        vd = OptionalDouble(properties, "Vd");
        dPgF = OptionalDouble(properties, "dPgF");
        if (properties.GetValueOrDefault("glass_code") is string code)
        {
            glassCode = code;
        }

        if (properties.GetValueOrDefault("glass_status") is string statusText)
        {
            status = statusText.Trim().ToLowerInvariant();
        }

        if (properties.GetValueOrDefault("density") is List<object> densities &&
            densities.FirstOrDefault() is Dictionary<object, object> densityBlock &&
            densityBlock.GetValueOrDefault("value") is string densityText)
        {
            double value = ParseDouble(densityText);
            if (value < 100d)
            {
                value *= 1000d;
                notes = Append(notes, "Density was published in g/cm³ and converted to kg/m³.");
            }

            density = value;
        }

        if (properties.GetValueOrDefault("thermal_dispersion") is List<object> thermals &&
            thermals.FirstOrDefault() is Dictionary<object, object> thermalBlock &&
            thermalBlock.GetValueOrDefault("type") is string thermalType &&
            thermalType == "formula A" &&
            thermalBlock.GetValueOrDefault("coefficients") is string thermalText)
        {
            double[] t = ParseNumbers(thermalText);
            if (t.Length == 6 && t.All(double.IsFinite))
            {
                thermal = new ThermalCoefficients(t[0], t[1], t[2], t[3], t[4], t[5]);
            }
        }
    }

    ExtinctionTable? extinction = null;
    if (extinctionBlock?.GetValueOrDefault("data") is string extinctionText)
    {
        extinction = ParseExtinction(extinctionText);
    }

    if (status.Length == 0 && category == "obsolete")
    {
        status = "obsolete";
    }

    FitCheck check = CheckFit(model, nd, vd);

    return new GlassEntry(
        vendorKey,
        name,
        category,
        form,
        formulaType,
        minimumNm,
        maximumNm,
        offset,
        first,
        second,
        status,
        nd,
        vd,
        glassCode,
        density,
        temperature,
        dPgF,
        thermal,
        extinction,
        new ProvenanceRecord(source, citation, url?.ToString(), relativePath, retrieved.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), notes),
        check);
}

static FitCheck CheckFit(IDispersionModel model, double? nd, double? vd)
{
    double? fittedNd = Evaluate(model, 587.5618d);
    double? fittedNf = Evaluate(model, 486.1327d);
    double? fittedNc = Evaluate(model, 656.2725d);
    double? fittedVd = fittedNd is { } d && fittedNf is { } f && fittedNc is { } c ? (d - 1d) / (f - c) : null;
    double? deltaNd = nd is { } catalogNd && fittedNd is { } fittedD ? fittedD - catalogNd : null;
    double? deltaVd = vd is { } catalogVd && fittedVd is { } fittedV ? fittedV - catalogVd : null;
    return new FitCheck(fittedNd, fittedVd, deltaNd, deltaVd);
}

static double? Evaluate(IDispersionModel model, double wavelength) =>
    model.EvaluateNanometers(wavelength, out double index) == DispersionStatus.Success ? index : null;

static string CategoryOf(string relativePath, string vendorKey)
{
    string[] parts = relativePath.Split('/');
    // data/specs/<vendor>/<category>/.../<name>.yml
    return parts.Length > 4 ? parts[3] : "";
}

static (string Citation, Uri? Url) ParseReferences(string? references)
{
    if (string.IsNullOrWhiteSpace(references))
    {
        return ("Not cited by the source entry", null);
    }

    Match anchor = Regex.Match(references, "<a\\s+href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
    Uri? url = anchor.Success && Uri.TryCreate(anchor.Groups[1].Value.Trim(), UriKind.Absolute, out Uri? parsed) ? parsed : null;
    string text = Regex.Replace(references, "<br\\s*/?>", "; ", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, "<[^>]+>", "");
    text = System.Net.WebUtility.HtmlDecode(text);
    text = Regex.Replace(text, "\\s+", " ").Trim();
    text = Regex.Replace(text, "\\s*;\\s*$", "");
    return (text.Length == 0 ? "Not cited by the source entry" : text, url);
}

static ExtinctionTable? ParseExtinction(string text)
{
    SortedDictionary<double, double> samples = [];
    foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        double[] values = ParseNumbers(line);
        if (values.Length < 2 || !double.IsFinite(values[0]) || values[0] <= 0d || !double.IsFinite(values[1]) || values[1] < 0d)
        {
            continue;
        }

        samples[values[0] * 1000d] = values[1];
    }

    return samples.Count >= 2 ? new ExtinctionTable([.. samples.Keys], [.. samples.Values]) : null;
}

static void AssignIdentifiers(List<VendorOutput> vendors)
{
    foreach (VendorOutput vendor in vendors)
    {
        Dictionary<string, int> used = new(StringComparer.Ordinal);
        foreach (GlassEntry entry in vendor.Entries)
        {
            string identifier = Identifier(entry.Name);
            if (used.TryGetValue(identifier, out int count))
            {
                used[identifier] = count + 1;
                identifier += "_" + Identifier(entry.Category);
            }
            else
            {
                used[identifier] = 1;
            }

            entry.Identifier = identifier;
        }
    }
}

static string Identifier(string name)
{
    StringBuilder builder = new(name.Length);
    foreach (char c in name)
    {
        if (char.IsAsciiLetterOrDigit(c))
        {
            builder.Append(c);
        }
    }

    if (builder.Length == 0)
    {
        builder.Append("Glass");
    }

    if (char.IsAsciiDigit(builder[0]))
    {
        builder.Insert(0, 'G');
    }

    return builder.ToString();
}

static string VendorDisplayName(string key) => key switch
{
    "schott" => "SCHOTT",
    "ohara" => "OHARA",
    "hoya" => "HOYA",
    "cdgm" => "CDGM",
    "hikari" => "HIKARI",
    "sumita" => "SUMITA",
    "lzos" => "LZOS",
    "vitron" => "VITRON",
    "ami" => "Amorphous Materials",
    "cargille" => "Cargille",
    "corning" => "Corning",
    "crystran" => "Crystran",
    "isuzu" => "Isuzu Glass",
    "lightpath" => "LightPath",
    "nsg" => "NSG",
    "barberini" => "Barberini",
    _ => key.ToUpperInvariant(),
};

static string VendorClassName(string key) => char.ToUpperInvariant(key[0]) + key[1..];

static string EmitVendor(VendorOutput vendor)
{
    StringBuilder b = new();
    b.Append("// <auto-generated>\n");
    b.Append("// Generated by tools/Caustikon.Glasses.Generator from the RefractiveIndex.INFO database (CC0 1.0).\n");
    b.Append("// Do not edit; rerun the generator. The data of record is data/glasses/").Append(vendor.Key).Append(".json.\n");
    b.Append("// </auto-generated>\n\n");
    b.Append("namespace Caustikon.Glasses;\n\n");
    b.Append("/// <summary>").Append(Xml(vendor.DisplayName)).Append(" glasses: ").Append(vendor.Entries.Count.ToString(CultureInfo.InvariantCulture))
        .Append(" entries with closed-form dispersion. Each field is the concrete model; <see cref=\"GlassCatalog\"/> carries the same glasses with their provenance and catalog data.</summary>\n");
    b.Append("[System.CodeDom.Compiler.GeneratedCode(\"Caustikon.Glasses.Generator\", \"1\")]\n");
    b.Append("public static partial class ").Append(vendor.ClassName).Append('\n');
    b.Append("{\n");

    foreach (GlassEntry entry in vendor.Entries)
    {
        b.Append("    /// <summary>").Append(Xml(FieldSummary(vendor, entry))).Append("</summary>\n");
        b.Append("    public static readonly ").Append(ModelType(entry.Form)).Append(' ').Append(entry.Identifier).Append(" = new(")
            .Append(Num(entry.Offset)).Append(", ")
            .Append(Array(entry.FirstCoefficients)).Append(", ")
            .Append(Array(entry.SecondCoefficients)).Append(", ")
            .Append(Num(entry.MinimumWavelengthNanometers)).Append(", ")
            .Append(Num(entry.MaximumWavelengthNanometers)).Append(");\n\n");
    }

    b.Append("    internal static IEnumerable<Glass> Entries()\n    {\n");
    foreach (GlassEntry entry in vendor.Entries)
    {
        b.Append("        yield return Create").Append(entry.Identifier).Append("();\n");
    }

    b.Append("    }\n");

    foreach (GlassEntry entry in vendor.Entries)
    {
        b.Append('\n');
        b.Append("    private static Glass Create").Append(entry.Identifier).Append("() => new()\n    {\n");
        b.Append("        Vendor = ").Append(Str(vendor.Key)).Append(",\n");
        b.Append("        VendorDisplayName = ").Append(Str(vendor.DisplayName)).Append(",\n");
        b.Append("        Name = ").Append(Str(entry.Name)).Append(",\n");
        b.Append("        Category = ").Append(Str(entry.Category)).Append(",\n");
        b.Append("        Model = ").Append(entry.Identifier).Append(",\n");
        b.Append("        Formula = DispersionFormula.").Append(entry.Form.ToString()).Append(",\n");
        b.Append("        Provenance = new GlassProvenance(")
            .Append(Str(entry.Provenance.Source)).Append(", ")
            .Append(Str(entry.Provenance.Citation)).Append(", ")
            .Append(entry.Provenance.Url is null ? "null" : "new Uri(" + Str(entry.Provenance.Url) + ")").Append(", ")
            .Append(Str(entry.Provenance.Path)).Append(", ")
            .Append(DateLiteral(entry.Provenance.RetrievedOn)).Append(", ")
            .Append(Str(entry.Provenance.Notes)).Append("),\n");
        b.Append("        Status = GlassStatus.").Append(StatusName(entry.Status)).Append(",\n");
        AppendOptional(b, "CatalogIndexD", entry.CatalogIndexD);
        AppendOptional(b, "CatalogAbbeD", entry.CatalogAbbeD);
        if (entry.GlassCode is not null)
        {
            b.Append("        GlassCode = ").Append(Str(entry.GlassCode)).Append(",\n");
        }

        AppendOptional(b, "DensityKgPerM3", entry.DensityKgPerM3);
        AppendOptional(b, "ReferenceTemperatureKelvin", entry.ReferenceTemperatureKelvin);
        AppendOptional(b, "PartialDispersionDeviationGF", entry.PartialDispersionDeviationGF);
        if (entry.Thermal is { } t)
        {
            b.Append("        Thermal = new ThermalDispersion(").Append(Num(t.D0)).Append(", ").Append(Num(t.D1)).Append(", ").Append(Num(t.D2)).Append(", ")
                .Append(Num(t.E0)).Append(", ").Append(Num(t.E1)).Append(", ").Append(Num(t.LambdaTkUm)).Append("),\n");
        }

        if (entry.Extinction is { } k)
        {
            b.Append("        Extinction = new TabulatedExtinction(").Append(Array(k.WavelengthsNanometers)).Append(", ").Append(Array(k.Extinctions)).Append("),\n");
        }

        b.Append("    };\n");
    }

    b.Append("}\n");
    return b.ToString();
}

static string EmitSources(List<VendorOutput> vendors)
{
    StringBuilder b = new();
    b.Append("// <auto-generated>\n// Generated by tools/Caustikon.Glasses.Generator. Do not edit; rerun the generator.\n// </auto-generated>\n\n");
    b.Append("namespace Caustikon.Glasses;\n\n");
    b.Append("public static partial class GlassCatalog\n{\n");
    b.Append("    private static readonly Func<IEnumerable<Glass>>[] Sources =\n    [\n");
    foreach (VendorOutput vendor in vendors)
    {
        b.Append("        ").Append(vendor.ClassName).Append(".Entries,\n");
    }

    b.Append("    ];\n\n");
    b.Append("    private static readonly string[] VendorKeys =\n    [\n");
    foreach (VendorOutput vendor in vendors)
    {
        b.Append("        ").Append(Str(vendor.Key)).Append(",\n");
    }

    b.Append("    ];\n}\n");
    return b.ToString();
}

static string FieldSummary(VendorOutput vendor, GlassEntry entry)
{
    StringBuilder s = new();
    s.Append(vendor.DisplayName).Append(' ').Append(entry.Name);
    if (entry.Category.Length > 0 && entry.Category != "optical")
    {
        s.Append(" (").Append(entry.Category).Append(')');
    }

    s.Append(": ").Append(entry.Form).Append(", ")
        .Append(entry.MinimumWavelengthNanometers.ToString("0.###", CultureInfo.InvariantCulture)).Append('–')
        .Append(entry.MaximumWavelengthNanometers.ToString("0.###", CultureInfo.InvariantCulture)).Append(" nm.");
    if (entry.CatalogIndexD is { } nd)
    {
        s.Append(" Catalog n_d ").Append(nd.ToString("0.#####", CultureInfo.InvariantCulture));
        if (entry.CatalogAbbeD is { } vd)
        {
            s.Append(", ν_d ").Append(vd.ToString("0.##", CultureInfo.InvariantCulture));
        }

        s.Append('.');
    }

    s.Append(' ').Append(entry.Provenance.Citation).Append('.');
    return s.ToString();
}

static Manifest BuildManifest(List<VendorOutput> vendors, List<SkippedEntry> skipped, string commit, DateOnly retrieved)
{
    List<VendorSummary> summaries = [];
    double maxNd = 0d, maxVd = 0d;
    string maxNdGlass = "", maxVdGlass = "";
    int total = 0;
    foreach (VendorOutput vendor in vendors)
    {
        summaries.Add(new VendorSummary(
            vendor.Key,
            vendor.DisplayName,
            vendor.Entries.Count,
            vendor.Entries.Count(static e => e.Form == DispersionForm.Sellmeier),
            vendor.Entries.Count(static e => e.Form == DispersionForm.Polynomial),
            vendor.Entries.Count(static e => e.Form == DispersionForm.Cauchy),
            vendor.Entries.Count(static e => e.Extinction is not null),
            vendor.Entries.Count(static e => e.Thermal is not null),
            vendor.Entries.Count(static e => e.CatalogIndexD is not null && e.CatalogAbbeD is not null)));
        total += vendor.Entries.Count;
        foreach (GlassEntry entry in vendor.Entries)
        {
            if (entry.Check.DeltaIndexD is { } dn && Math.Abs(dn) > maxNd)
            {
                maxNd = Math.Abs(dn);
                maxNdGlass = vendor.Key + "/" + entry.Name;
            }

            if (entry.Check.DeltaAbbeD is { } dv && Math.Abs(dv) > maxVd)
            {
                maxVd = Math.Abs(dv);
                maxVdGlass = vendor.Key + "/" + entry.Name;
            }
        }
    }

    return new Manifest(
        "RefractiveIndex.INFO database (https://github.com/polyanskiy/refractiveindex.info-database), CC0 1.0 Universal",
        commit,
        retrieved.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        total,
        summaries,
        maxNd,
        maxNdGlass,
        maxVd,
        maxVdGlass,
        skipped);
}

static void AppendOptional(StringBuilder b, string property, double? value)
{
    if (value is { } v)
    {
        b.Append("        ").Append(property).Append(" = ").Append(Num(v)).Append(",\n");
    }
}

static string ModelType(DispersionForm form) => form switch
{
    DispersionForm.Sellmeier => "Sellmeier",
    DispersionForm.Polynomial => "Polynomial",
    _ => "Cauchy",
};

static string StatusName(string status) => status switch
{
    "preferred" => "Preferred",
    "standard" => "Standard",
    "special" => "Special",
    "obsolete" => "Obsolete",
    _ => "Unspecified",
};

static string Num(double value)
{
    string text = value.ToString("R", CultureInfo.InvariantCulture);
    return text + "d";
}

static string Array(double[] values) => values.Length == 0 ? "[]" : "[" + string.Join(", ", values.Select(Num)) + "]";

static string Str(string value)
{
    StringBuilder b = new(value.Length + 2);
    b.Append('"');
    foreach (char c in value)
    {
        switch (c)
        {
            case '"': b.Append("\\\""); break;
            case '\\': b.Append("\\\\"); break;
            case '\n': b.Append("\\n"); break;
            case '\r': b.Append("\\r"); break;
            case '\t': b.Append("\\t"); break;
            default:
                if (c < ' ')
                {
                    b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    b.Append(c);
                }

                break;
        }
    }

    b.Append('"');
    return b.ToString();
}

static string Xml(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

static string DateLiteral(string isoDate)
{
    DateOnly date = DateOnly.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    return $"new DateOnly({date.Year}, {date.Month}, {date.Day})";
}

static double[] ParseNumbers(string text) =>
    text.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(ParseDouble).ToArray();

static double ParseDouble(string text) => double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

static double? OptionalDouble(Dictionary<object, object> map, string key) =>
    map.GetValueOrDefault(key) is string text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

static string Append(string notes, string more) => notes.Length == 0 ? more : notes + " " + more;

static Dictionary<string, string> ParseOptions(string[] args)
{
    Dictionary<string, string> options = new(StringComparer.Ordinal);
    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expected an option, got '{args[i]}'.");
        }

        options[args[i][2..]] = args[i + 1];
    }

    return options;
}

static string Require(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out string? value) ? value : throw new ArgumentException($"--{name} is required.");

enum DispersionForm
{
    Sellmeier,
    Polynomial,
    Cauchy,
}

sealed record VendorOutput(string Key, string DisplayName, string ClassName, List<GlassEntry> Entries);

sealed record ThermalCoefficients(double D0, double D1, double D2, double E0, double E1, double LambdaTkUm);

sealed record ExtinctionTable(double[] WavelengthsNanometers, double[] Extinctions);

sealed record ProvenanceRecord(string Source, string Citation, string? Url, string Path, string RetrievedOn, string Notes);

sealed record FitCheck(double? FittedIndexD, double? FittedAbbeD, double? DeltaIndexD, double? DeltaAbbeD);

sealed record GlassEntry(
    string Vendor,
    string Name,
    string Category,
    DispersionForm Form,
    string SourceFormula,
    double MinimumWavelengthNanometers,
    double MaximumWavelengthNanometers,
    double Offset,
    double[] FirstCoefficients,
    double[] SecondCoefficients,
    string Status,
    double? CatalogIndexD,
    double? CatalogAbbeD,
    string? GlassCode,
    double? DensityKgPerM3,
    double? ReferenceTemperatureKelvin,
    double? PartialDispersionDeviationGF,
    ThermalCoefficients? Thermal,
    ExtinctionTable? Extinction,
    ProvenanceRecord Provenance,
    FitCheck Check)
{
    [JsonPropertyOrder(-1)]
    public string Identifier { get; set; } = "";
}

sealed record SkippedEntry(string Path, string Reason);

sealed record VendorSummary(
    string Vendor,
    string DisplayName,
    int GlassCount,
    int Sellmeier,
    int Polynomial,
    int Cauchy,
    int WithExtinction,
    int WithThermal,
    int WithCatalogIndexAndAbbe);

sealed record Manifest(
    string Source,
    string DatabaseCommit,
    string RetrievedOn,
    int GlassCount,
    List<VendorSummary> Vendors,
    double MaxAbsoluteIndexDDeviation,
    string MaxAbsoluteIndexDDeviationGlass,
    double MaxAbsoluteAbbeDDeviation,
    string MaxAbsoluteAbbeDDeviationGlass,
    List<SkippedEntry> Skipped);
