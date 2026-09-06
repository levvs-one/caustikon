// Renders the pictures in docs/gallery with the same spectral tracer the site uses, on the CPU, so the README shows
// what the packages compute rather than a screenshot. Run: dotnet run --project tools/Caustikon.Gallery -c Release
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Caustikon.Glasses;
using Caustikon.Site.Services;

string output = args.Length > 0 ? args[0] : Path.Combine(FindRepository(), "docs", "gallery");
Directory.CreateDirectory(output);

(string File, string Vendor, string Name, RenderShape Shape, double SizeMm, Backdrop Backdrop)[] shots =
[
    ("sphere-n-sf11.png", "schott", "N-SF11", RenderShape.Sphere(), 60, Backdrop.Checker),
    ("octahedron-n-sf66.png", "schott", "N-SF66", RenderShape.Octahedron(), 60, Backdrop.Checker),
    ("prism-n-bk7.png", "schott", "N-BK7", RenderShape.Prism(6), 60, Backdrop.Grid),
    ("cube-sf57.png", "schott", "SF57", RenderShape.Cube(), 80, Backdrop.Stripes),
];

const int width = 960, height = 600;
foreach ((string file, string vendor, string name, RenderShape shape, double sizeMm, Backdrop backdrop) in shots)
{
    Glass glass = GlassCatalog.Find(vendor, name)!;
    GlassRenderer renderer = new(glass, shape, sizeMm / 2d, backdrop);
    Vector3[] linear = new Vector3[width * height];
    byte[] rgba = new byte[width * height * 4];
    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
    renderer.RenderRows(linear, rgba, width, height, 0, height);
    int refined = renderer.RefineEdges(linear, rgba, width, height, 0, height);
    watch.Stop();
    string path = Path.Combine(output, file);
    File.WriteAllBytes(path, Png.Encode(rgba, width, height));
    Console.WriteLine($"{file}: {glass.VendorDisplayName} {glass.Name}, {width}x{height}, {refined} edge pixels refined, {watch.Elapsed.TotalSeconds:F1} s");
}

static string FindRepository()
{
    string? directory = AppContext.BaseDirectory;
    while (directory is not null && !File.Exists(Path.Combine(directory, "Caustikon.sln")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? Directory.GetCurrentDirectory();
}

/// <summary>A minimal PNG encoder: one IHDR, one zlib-compressed IDAT of filter-0 rows, IEND.</summary>
static class Png
{
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        byte[] raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        stream.Write(crcBytes);
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }
}
