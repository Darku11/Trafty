using Trafty.Core.Weather;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class SystemColFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [Fact]
    public void Parse_RealFile_MatchesExactSize()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("SYSTEM.COL"));

        Assert.Equal(128 * 66 * 2, bytes.Length);

        SystemColFile col = SystemColFile.Parse(bytes);

        Assert.Equal(128 * 66, col.Pixels.Length);
    }

    [Fact]
    public void Parse_KnownPixelValues_MatchRealFile()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        Assert.Equal((byte)0, col.GetPixel(0, 0).R);
        Assert.Equal((byte)0, col.GetPixel(0, 0).G);
        Assert.Equal((byte)0, col.GetPixel(0, 0).B);

        // pixel(127,65) is the raw value 0xFFFF -> full white in RGB565
        var corner = col.GetPixel(127, 65);
        Assert.Equal((byte)255, corner.R);
        Assert.Equal((byte)255, corner.G);
        Assert.Equal((byte)255, corner.B);
    }

    [Fact]
    public void Parse_OutOfRangeCoordinate_Throws()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        Assert.Throws<ArgumentOutOfRangeException>(() => col.GetPixel(128, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => col.GetPixel(0, 66));
    }

    [Fact]
    public void Parse_WrongByteCount_Throws()
    {
        byte[] tooShort = new byte[100];

        Assert.Throws<WeatherFormatException>(() => SystemColFile.Parse(tooShort));
    }

    [Fact]
    public void ToRgb24_ProducesCorrectSizedBuffer()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        byte[] rgb = col.ToRgb24();

        Assert.Equal(128 * 66 * 3, rgb.Length);
    }

    [Fact]
    public void SaveAsPng_ToStream_ProducesReadablePngBytes()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        using var stream = new MemoryStream();
        SystemColExporter.SaveAsPng(col, stream);

        Assert.True(stream.Length > 0);

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        byte[] signature = stream.ToArray()[..8];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, signature);
    }

    [Fact]
    public void SetPixel_ThenGetPixel_RoundTripsWithin565Precision()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        col.SetPixel(10, 20, 200, 100, 50);
        var (r, g, b) = col.GetPixel(10, 20);

        // RGB565 has 5/6/5 bits per channel, so round-tripping loses precision — within
        // one quantization step is the correct expectation, not an exact match.
        Assert.InRange(r, 192, 208);
        Assert.InRange(g, 96, 104);
        Assert.InRange(b, 40, 56);
    }

    [Fact]
    public void SetPixel_OutOfRangeCoordinate_Throws()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));

        Assert.Throws<ArgumentOutOfRangeException>(() => col.SetPixel(128, 0, 0, 0, 0));
    }

    [Fact]
    public void Save_ThenReload_PreservesEdit()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));
        col.SetPixel(5, 5, 255, 0, 0);

        string outputPath = Path.Combine(Path.GetTempPath(), $"system-col-edit-{Guid.NewGuid()}.col");

        try
        {
            col.Save(outputPath);

            Assert.Equal(128 * 66 * 2, new FileInfo(outputPath).Length);

            SystemColFile reloaded = SystemColFile.Load(outputPath);
            var (r, g, b) = reloaded.GetPixel(5, 5);

            Assert.True(r > 240);
            Assert.Equal(0, g);
            Assert.Equal(0, b);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void SaveAsPng_ProducesAReadablePngFile()
    {
        SystemColFile col = SystemColFile.Load(Fixture("SYSTEM.COL"));
        string outputPath = Path.Combine(Path.GetTempPath(), $"system-col-{Guid.NewGuid()}.png");

        try
        {
            SystemColExporter.SaveAsPng(col, outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
