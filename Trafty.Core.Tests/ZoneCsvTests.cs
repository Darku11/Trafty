using Trafty.Core.Zones;
using Xunit;

namespace Trafty.Core.Tests;

public class ZoneCsvTests
{
    [Fact]
    public void FixtureCsvFile_ParsesRealZoneData()
    {
        FixtureCsvFile file = FixtureCsvFile.Load("fixtures.csv");

        Assert.Equal(726, file.Entries.Count);

        FixtureCsvEntry first = file.Entries[0];
        Assert.Equal(1, first.Id);
        Assert.Equal(409, first.NifId);
        Assert.Equal("Elm", first.TextualName);
        Assert.Equal(27008.00, first.X, 2);
        Assert.Equal(58752.00, first.Y, 2);
        Assert.Equal(4052.00, first.Z, 2);
        Assert.Equal(57, first.Heading, 2);
        Assert.Equal(129, first.Scale);

        FixtureCsvEntry last = file.Entries[^1];
        Assert.Equal(726, last.Id);
        Assert.Equal(418, last.NifId);
        Assert.Equal("Fire (Instance 4)", last.TextualName);
    }

    [Fact]
    public void NifCsvFile_ParsesRealZoneData()
    {
        NifCsvFile file = NifCsvFile.Load("nifs.csv");

        Assert.Equal(42, file.Entries.Count);

        NifCsvEntry hovel = file.Entries[0];
        Assert.Equal(401, hovel.NifId);
        Assert.Equal("Hovel", hovel.TextualName);
        Assert.Equal("hovel.nif", hovel.FileName);

        NifCsvEntry lastEntry = file.Entries[^1];
        Assert.Equal(442, lastEntry.NifId);
        Assert.Equal("Elm1CL5.nif", lastEntry.FileName);
    }

    [Fact]
    public void FixtureCsvFile_EveryNifIdResolvesViaNifCsv()
    {
        FixtureCsvFile fixtures = FixtureCsvFile.Load("fixtures.csv");
        NifCsvFile nifs = NifCsvFile.Load("nifs.csv");

        HashSet<int> knownNifIds = nifs.Entries.Select(e => e.NifId).ToHashSet();

        foreach (FixtureCsvEntry fixture in fixtures.Entries)
        {
            Assert.Contains(fixture.NifId, knownNifIds);
        }
    }

    [Fact]
    public void ZoneBoundaryFile_ParsesRealZoneData()
    {
        ZoneBoundaryFile boundary = ZoneBoundaryFile.Load("bound.csv");

        Assert.Equal(232, boundary.Points.Count);
        Assert.Equal(new ZoneBoundaryPoint(0, 77), boundary.Points[0]);
        Assert.Equal(new ZoneBoundaryPoint(2414, 65535), boundary.Points[1]);
    }

    [Fact]
    public void ZoneBoundaryFile_RejectsOddTokenCount()
    {
        var ex = Assert.Throws<ZoneCsvFormatException>(() => ZoneBoundaryFile.Parse("1,2,3"));
        Assert.Contains("odd", ex.Message);
    }

    [Fact]
    public void FixtureCsvFile_RejectsTooFewFields()
    {
        string text = "header1\nheader2\n1,2,3\n";
        var ex = Assert.Throws<ZoneCsvFormatException>(() => FixtureCsvFile.Parse(text));
        Assert.Contains("line 3", ex.Message);
    }

    [Fact]
    public void ZoneMap_LoadsFromRealArchive()
    {
        ZoneMap map = ZoneMap.Load("csv003.mpk");

        Assert.Equal(726, map.Fixtures.Entries.Count);
        Assert.Equal(42, map.Nifs.Entries.Count);
        Assert.Equal(232, map.Boundary.Points.Count);
        Assert.Equal("elm1.nif", map.ResolveNifFileName(409));
        Assert.Null(map.ResolveNifFileName(999999));
    }
}
