using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Trafty.Core.Archives;
using Trafty.Core.Audio;
using Trafty.Core.Images;
using Trafty.Core.Models;
using Trafty.Core.Models.Nif;
using Trafty.Core.Textures;
using Trafty.Core.UI;
using Trafty.Core.Weather;
using Trafty.Core.WorldProps;
using Trafty.Core.Zones;

namespace Trafty.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _statusText = "No archive open.";
    private string? _archivePath;
    private AssetRow? _selectedEntry;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AssetRow> Entries { get; } = new();

    private bool _isGridView;

    /// <summary>Toggles the Archive Assets tab between the text list and a texture thumbnail grid (Modul A).</summary>
    public bool IsGridView
    {
        get => _isGridView;
        set => SetField(ref _isGridView, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string? ArchivePath
    {
        get => _archivePath;
        set
        {
            if (SetField(ref _archivePath, value))
            {
                OnPropertyChanged(nameof(ArchiveDisplayName));
                OnPropertyChanged(nameof(HasArchive));
            }
        }
    }

    public string ArchiveDisplayName =>
        ArchivePath is null ? "No archive open" : Path.GetFileName(ArchivePath);

    public bool HasArchive => ArchivePath is not null;

    public AssetRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(CanReplaceSelected));
            }
        }
    }

    public bool CanReplaceSelected => SelectedEntry is not null && HasArchive;

    /// <summary>Extracts every entry in the currently open archive into a target folder.</summary>
    public void ExtractAll(string targetDirectory)
    {
        if (ArchivePath is null)
        {
            return;
        }

        using MpkArchive archive = MpkArchive.Open(ArchivePath);
        int count = archive.ExtractAll(targetDirectory);
        StatusText = $"Extracted {count} file(s) to {targetDirectory}.";
    }

    private static readonly string[] ArchiveExtensions = { ".mpk", ".epk", ".npk" };

    /// <summary>
    /// Batch-extracts every .mpk/.epk/.npk archive found anywhere under
    /// <paramref name="sourceFolder"/> (recursively — a client's zones\ folder alone has
    /// hundreds of them) into <paramref name="destinationFolder"/>, mirroring the source's
    /// relative folder structure and giving each archive its own subfolder named after it,
    /// so extracting two same-named archives from different subfolders doesn't collide.
    /// A single unreadable archive is skipped (recorded, not fatal) rather than aborting the
    /// whole batch.
    /// </summary>
    public void ExtractAllArchives(string sourceFolder, string destinationFolder)
    {
        var archivePaths = Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
            .Where(p => ArchiveExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int archiveCount = 0;
        int fileCount = 0;
        var failures = new List<string>();

        foreach (string archivePath in archivePaths)
        {
            try
            {
                string relativeDir = Path.GetRelativePath(sourceFolder, Path.GetDirectoryName(archivePath) ?? sourceFolder);
                string targetDir = Path.Combine(
                    destinationFolder,
                    relativeDir == "." ? "" : relativeDir,
                    Path.GetFileNameWithoutExtension(archivePath));

                using MpkArchive archive = MpkArchive.Open(archivePath);
                fileCount += archive.ExtractAll(targetDir);
                archiveCount++;
            }
            catch (Exception ex) when (ex is MpkFormatException or IOException)
            {
                failures.Add($"{Path.GetFileName(archivePath)} ({ex.Message})");
            }
        }

        string summary = $"Extracted {archiveCount}/{archivePaths.Count} archive(s), {fileCount} file(s) total, to {destinationFolder}.";

        if (failures.Count > 0)
        {
            summary += $" {failures.Count} archive(s) failed: {string.Join(", ", failures.Take(5))}" +
                       (failures.Count > 5 ? $" (+{failures.Count - 5} more)" : "");
        }

        StatusText = summary;
    }

    /// <summary>Extracts one entry to an explicit destination file path.</summary>
    public void ExtractSelected(string destinationPath)
    {
        if (ArchivePath is null || SelectedEntry is null)
        {
            return;
        }

        using MpkArchive archive = MpkArchive.Open(ArchivePath);
        MpkEntry? entry = archive[SelectedEntry.Name];

        if (entry is null)
        {
            StatusText = "Entry not found in archive.";
            return;
        }

        byte[] bytes = archive.Extract(entry);
        File.WriteAllBytes(destinationPath, bytes);
        StatusText = $"Extracted \"{entry.Name}\" to {destinationPath}.";
    }

    /// <summary>
    /// Adds one or more arbitrary files into the current archive as new entries (or
    /// overwrites existing entries with the same name) — the general-purpose counterpart to
    /// the DDS-specific drag-and-drop replace flow, matching what tools like the DOL MPAK
    /// Package Manager call "Add Files". Backs up first (Grundprinzip #2), then reloads.
    /// </summary>
    public void AddFiles(IEnumerable<string> filePaths)
    {
        if (ArchivePath is null)
        {
            return;
        }

        var pending = new List<MpkPendingEntry>();

        foreach (string path in filePaths)
        {
            pending.Add(new MpkPendingEntry
            {
                Name = Path.GetFileName(path),
                UncompressedData = File.ReadAllBytes(path),
                Timestamp = File.GetLastWriteTimeUtc(path),
            });
        }

        if (pending.Count == 0)
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? ".";
        new BackupVault(directory).Backup(ArchivePath);

        string tempPath = ArchivePath + ".tmp";

        using (MpkArchive source = MpkArchive.Open(ArchivePath))
        {
            MpkArchiveWriter.WriteReplacing(source, pending, tempPath);
        }

        File.Move(tempPath, ArchivePath, overwrite: true);

        Reload();
        RefreshBackups();
        StatusText = $"Added {pending.Count} file(s) to {ArchiveDisplayName}. Backup created.";
    }

    public ObservableCollection<BackupRow> Backups { get; } = new();

    public bool HasBackups => Backups.Count > 0;

    private BackupRow? _selectedBackup;

    public BackupRow? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetField(ref _selectedBackup, value))
            {
                OnPropertyChanged(nameof(CanRestoreSelectedBackup));
            }
        }
    }

    public bool CanRestoreSelectedBackup => SelectedBackup is not null && HasArchive;

    public ObservableCollection<WorldPropRow> WorldProps { get; } = new();

    private WorldPropRow? _selectedWorldProp;

    public WorldPropRow? SelectedWorldProp
    {
        get => _selectedWorldProp;
        set => SetField(ref _selectedWorldProp, value);
    }

    private string? _worldPropsFolderName;

    public string? WorldPropsFolderName
    {
        get => _worldPropsFolderName;
        private set => SetField(ref _worldPropsFolderName, value);
    }

    /// <summary>
    /// Scans a folder for .nhd files and lists them with their referenced model name and
    /// footprint dimensions. Files that fail to parse are skipped rather than aborting the
    /// whole scan — a folder full of unrelated files is a normal thing to point this at.
    /// </summary>
    public void LoadWorldPropsFolder(string folderPath)
    {
        WorldProps.Clear();
        SelectedWorldProp = null;

        foreach (string nhdPath in Directory.EnumerateFiles(folderPath, "*.nhd").OrderBy(p => p))
        {
            try
            {
                NhdFile nhd = NhdFile.Load(nhdPath);
                WorldProps.Add(WorldPropRow.FromNhd(nhdPath, nhd));
            }
            catch (WorldPropFormatException)
            {
                // Skip files that don't actually parse as .nhd — a folder scan should not
                // die on one bad or unrelated file.
            }
        }

        WorldPropsFolderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
        StatusText = $"Found {WorldProps.Count} world prop(s) in {WorldPropsFolderName}.";
    }

    private const double ZoneMapCanvasSize = 700;
    private const int ModelPreviewSize = 320;

    public ObservableCollection<ZoneFixtureRow> ZoneFixtures { get; } = new();

    public Points ZoneBoundaryPoints { get; } = new();

    private ZoneFixtureRow? _selectedZoneFixture;

    public ZoneFixtureRow? SelectedZoneFixture
    {
        get => _selectedZoneFixture;
        set => SetField(ref _selectedZoneFixture, value);
    }

    private string? _zoneMapName;

    public string? ZoneMapName
    {
        get => _zoneMapName;
        private set => SetField(ref _zoneMapName, value);
    }

    private Bitmap? _zoneTerrainImage;

    /// <summary>
    /// Rendered terrain.pcx from the loaded zone archive, stretched under the boundary/fixture
    /// canvas. Null when the archive doesn't carry terrain.pcx (e.g. csv003.mpk, as opposed to
    /// dat003.mpk which does) — the map still works without it, just without a background.
    /// </summary>
    public Bitmap? ZoneTerrainImage
    {
        get => _zoneTerrainImage;
        private set => SetField(ref _zoneTerrainImage, value);
    }

    /// <summary>
    /// Loads fixtures.csv/nifs.csv/bound.csv from a zone's csv/dat archive (e.g. csv003.mpk
    /// or dat003.mpk) and projects them into a square canvas of <see cref="ZoneMapCanvasSize"/>,
    /// preserving aspect ratio. Projection uses the boundary polygon's bounding box as the
    /// world extent — fixtures outside it (there shouldn't be any) still project, just outside
    /// the canvas.
    /// </summary>
    public void LoadZoneMap(string archivePath)
    {
        using MpkArchive archive = MpkArchive.Open(archivePath);
        ZoneMap map = ZoneMap.LoadFromArchive(archive);

        ZoneTerrainImage?.Dispose();
        ZoneTerrainImage = TryLoadTerrainImage(archive);

        double minX = map.Boundary.Points.Min(p => p.X);
        double maxX = map.Boundary.Points.Max(p => p.X);
        double minY = map.Boundary.Points.Min(p => p.Y);
        double maxY = map.Boundary.Points.Max(p => p.Y);
        double worldWidth = Math.Max(maxX - minX, 1);
        double worldHeight = Math.Max(maxY - minY, 1);
        double scale = ZoneMapCanvasSize / Math.Max(worldWidth, worldHeight);

        // Canvas Y grows downward; world Y is assumed north-positive (unverified — no
        // documented DAoC coordinate spec was found), so it's flipped for a north-up map.
        Point Project(double worldX, double worldY) =>
            new((worldX - minX) * scale, ZoneMapCanvasSize - (worldY - minY) * scale);

        ZoneBoundaryPoints.Clear();

        foreach (ZoneBoundaryPoint p in map.Boundary.Points)
        {
            ZoneBoundaryPoints.Add(Project(p.X, p.Y));
        }

        ZoneFixtures.Clear();
        SelectedZoneFixture = null;

        foreach (FixtureCsvEntry fixture in map.Fixtures.Entries)
        {
            Point projected = Project(fixture.X, fixture.Y);

            ZoneFixtures.Add(new ZoneFixtureRow
            {
                Id = fixture.Id,
                TextualName = fixture.TextualName,
                NifFileName = map.ResolveNifFileName(fixture.NifId),
                WorldX = fixture.X,
                WorldY = fixture.Y,
                CanvasX = projected.X,
                CanvasY = projected.Y,
            });
        }

        ZoneMapName = Path.GetFileName(archivePath);
        StatusText = $"Loaded zone map from {ZoneMapName}: {ZoneFixtures.Count} fixture(s), {ZoneBoundaryPoints.Count} boundary point(s).";
    }

    private static Bitmap? TryLoadTerrainImage(MpkArchive archive)
    {
        MpkEntry? entry = archive["terrain.pcx"];

        if (entry is null)
        {
            return null;
        }

        try
        {
            byte[] bytes = archive.Extract(entry);
            PcxFile pcx = PcxFile.Parse(bytes);

            using var pngStream = new MemoryStream();
            PcxExporter.SaveAsPng(pcx, pngStream);
            pngStream.Position = 0;

            return new Bitmap(pngStream);
        }
        catch (PcxFormatException)
        {
            // No background is better than crashing the whole zone map load over an
            // unexpected terrain.pcx variant.
            return null;
        }
    }

    /// <summary>Refreshes <see cref="Backups"/> from disk for the currently open archive.</summary>
    public void RefreshBackups()
    {
        Backups.Clear();
        SelectedBackup = null;

        if (ArchivePath is null)
        {
            OnPropertyChanged(nameof(HasBackups));
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? ".";
        var vault = new BackupVault(directory);
        string fileName = Path.GetFileName(ArchivePath);

        // ListBackups returns most-recent-first, so the last one is the oldest — the
        // untouched state from before Trafty's very first write to this file.
        IReadOnlyList<string> backupPaths = vault.ListBackups(fileName);

        for (int i = 0; i < backupPaths.Count; i++)
        {
            BackupRow row = BackupRow.FromPath(backupPaths[i]);
            Backups.Add(i == backupPaths.Count - 1
                ? new BackupRow { FullPath = row.FullPath, Timestamp = row.Timestamp, IsOriginal = true }
                : row);
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    /// <summary>
    /// Manually snapshots the current archive into the Backup Vault without making any
    /// edit — lets the user proactively capture a known-good state (e.g. right after
    /// opening a fresh, unmodified archive) rather than relying only on the automatic
    /// backup that happens before each write.
    /// </summary>
    public void CreateBackupNow()
    {
        if (ArchivePath is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? ".";
        new BackupVault(directory).Backup(ArchivePath);
        RefreshBackups();
        StatusText = $"Backup created for {ArchiveDisplayName}.";
    }

    /// <summary>
    /// Restores <see cref="SelectedBackup"/> over the current archive file, then reloads
    /// the entry list so the UI reflects the restored state.
    /// </summary>
    public void RestoreSelectedBackup()
    {
        if (SelectedBackup is null || ArchivePath is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? ".";
        var vault = new BackupVault(directory);

        vault.Restore(SelectedBackup.FullPath, ArchivePath);

        StatusText = $"Restored {Path.GetFileName(ArchivePath)} from backup ({SelectedBackup.TimestampDisplay}).";
        Reload();
        RefreshBackups();
    }

    private string? _modelInfo;

    private Bitmap? _colorTableImage;
    private string? _colorTableName;
    private string? _colorTablePath;
    private SystemColFile? _colorTable;

    /// <summary>Rendered preview of the last color table opened via "Open Color Table…".</summary>
    public Bitmap? ColorTableImage
    {
        get => _colorTableImage;
        private set => SetField(ref _colorTableImage, value);
    }

    /// <summary>File name of the currently shown color table, e.g. "SYSTEM.COL".</summary>
    public string? ColorTableName
    {
        get => _colorTableName;
        private set => SetField(ref _colorTableName, value);
    }

    public bool HasColorTable => _colorTable is not null;

    private Bitmap? _texturePreviewImage;
    private string? _texturePreviewName;

    /// <summary>Rendered preview of the last .tga/.dds texture opened via "Open Texture…".</summary>
    public Bitmap? TexturePreviewImage
    {
        get => _texturePreviewImage;
        private set => SetField(ref _texturePreviewImage, value);
    }

    public string? TexturePreviewName
    {
        get => _texturePreviewName;
        private set => SetField(ref _texturePreviewName, value);
    }

    /// <summary>
    /// Loads a standalone .tga or .dds texture file directly from disk (as opposed to one
    /// packed inside an .mpk archive) — needed for UI textures like atlantis/emoticons.tga,
    /// which the client keeps as loose files referenced by UI XML window definitions.
    /// </summary>
    public void LoadTexturePreview(string path)
    {
        using var pngStream = new MemoryStream();
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        switch (extension)
        {
            case "tga":
                TgaExporter.SaveAsPng(TgaFile.Load(path), pngStream);
                break;
            case "dds":
                // Preserve real alpha here (forceOpaque: false) — unlike terrain patches, UI
                // textures like emoticon sprite sheets rely on alpha for transparency.
                DdsExporter.SaveAsPng(DdsFile.Load(path), pngStream, forceOpaque: false);
                break;
            default:
                throw new TextureFormatException($"Unsupported texture extension \"{extension}\" — only .tga and .dds are implemented.");
        }

        pngStream.Position = 0;
        TexturePreviewImage?.Dispose();
        TexturePreviewImage = new Bitmap(pngStream);
        TexturePreviewName = Path.GetFileName(path);
        StatusText = $"Loaded texture {TexturePreviewName}.";
    }

    private int? _selectedPixelX;
    private int? _selectedPixelY;

    public int? SelectedPixelX
    {
        get => _selectedPixelX;
        private set => SetField(ref _selectedPixelX, value);
    }

    public int? SelectedPixelY
    {
        get => _selectedPixelY;
        private set => SetField(ref _selectedPixelY, value);
    }

    public bool HasSelectedPixel => SelectedPixelX is not null;

    private byte _selectedPixelR;
    private byte _selectedPixelG;
    private byte _selectedPixelB;

    /// <summary>Editable red channel (0-255) of the pixel selected via <see cref="SelectColorTablePixel"/>.</summary>
    public byte SelectedPixelR
    {
        get => _selectedPixelR;
        set => SetField(ref _selectedPixelR, value);
    }

    public byte SelectedPixelG
    {
        get => _selectedPixelG;
        set => SetField(ref _selectedPixelG, value);
    }

    public byte SelectedPixelB
    {
        get => _selectedPixelB;
        set => SetField(ref _selectedPixelB, value);
    }

    /// <summary>Loads a .col file, renders it, and makes it available via <see cref="ColorTableImage"/>.</summary>
    public void LoadColorTable(string path)
    {
        SystemColFile col = SystemColFile.Load(path);

        RenderColorTable(col);
        _colorTable = col;
        _colorTablePath = path;
        ColorTableName = Path.GetFileName(path);
        SelectedPixelX = null;
        SelectedPixelY = null;
        OnPropertyChanged(nameof(HasColorTable));
        OnPropertyChanged(nameof(HasSelectedPixel));
        StatusText = $"Loaded color table {ColorTableName} ({SystemColFile.Width}x{SystemColFile.Height}).";
    }

    /// <summary>
    /// Selects a pixel in the loaded color table for editing (Modul B's atmosphere
    /// tweaker) and loads its current color into <see cref="SelectedPixelR"/>/G/B.
    /// </summary>
    public void SelectColorTablePixel(int x, int y)
    {
        if (_colorTable is null || (uint)x >= SystemColFile.Width || (uint)y >= SystemColFile.Height)
        {
            return;
        }

        (byte r, byte g, byte b) = _colorTable.GetPixel(x, y);
        SelectedPixelX = x;
        SelectedPixelY = y;
        SelectedPixelR = r;
        SelectedPixelG = g;
        SelectedPixelB = b;
        OnPropertyChanged(nameof(HasSelectedPixel));
    }

    /// <summary>
    /// Writes <see cref="SelectedPixelR"/>/G/B onto the selected pixel and re-renders the
    /// preview. In memory only — call <see cref="SaveColorTable"/> to persist to disk.
    /// </summary>
    public void ApplySelectedPixelColor()
    {
        if (_colorTable is null || SelectedPixelX is not { } x || SelectedPixelY is not { } y)
        {
            return;
        }

        _colorTable.SetPixel(x, y, SelectedPixelR, SelectedPixelG, SelectedPixelB);
        RenderColorTable(_colorTable);
        StatusText = $"Pixel ({x}, {y}) set to R{SelectedPixelR} G{SelectedPixelG} B{SelectedPixelB} (not saved yet).";
    }

    /// <summary>Backs up the current .col file (Grundprinzip #2), then writes all edits to disk.</summary>
    public void SaveColorTable()
    {
        if (_colorTable is null || _colorTablePath is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(_colorTablePath)) ?? ".";
        new BackupVault(directory).Backup(_colorTablePath);

        _colorTable.Save(_colorTablePath);
        StatusText = $"Saved {ColorTableName}. Backup created.";
    }

    private string? _soundPath;
    private string? _soundName;
    private string? _soundInfo;

    public string? SoundName
    {
        get => _soundName;
        private set => SetField(ref _soundName, value);
    }

    /// <summary>Header summary of the currently loaded sound, e.g. "PCM, 1ch, 22050Hz, 16bit, 00:00:01.44".</summary>
    public string? SoundInfo
    {
        get => _soundInfo;
        private set => SetField(ref _soundInfo, value);
    }

    public bool HasSound => _soundPath is not null;

    /// <summary>Loads and parses a .wav file's header for display — playback is a separate step (see Views).</summary>
    public void LoadSound(string path)
    {
        WavHeader header = WavHeader.Load(path);

        _soundPath = path;
        SoundName = Path.GetFileName(path);
        SoundInfo = $"{header.AudioFormatDisplay}, {header.ChannelCount}ch, {header.SampleRate}Hz, " +
                    $"{header.BitsPerSample}bit, {header.Duration:mm\\:ss\\.ff}";
        OnPropertyChanged(nameof(HasSound));
        StatusText = $"Loaded {SoundName} ({SoundInfo}).";
    }

    public string? SoundPath => _soundPath;

    /// <summary>Hides the sound player panel (playback itself is stopped by the caller — see Views).</summary>
    public void CloseSound()
    {
        _soundPath = null;
        SoundName = null;
        SoundInfo = null;
        OnPropertyChanged(nameof(HasSound));
    }

    private Bitmap? _uiWindowPreviewImage;
    private string? _uiWindowName;
    private DaocWindowTemplate? _uiWindowTemplate;
    private UiControlRow? _selectedUiControl;

    /// <summary>Schematic layout render of the last opened UI window XML (see DaocWindowRenderer).</summary>
    public Bitmap? UiWindowPreviewImage
    {
        get => _uiWindowPreviewImage;
        private set => SetField(ref _uiWindowPreviewImage, value);
    }

    public string? UiWindowName
    {
        get => _uiWindowName;
        private set => SetField(ref _uiWindowName, value);
    }

    public ObservableCollection<UiControlRow> UiWindowControls { get; } = new();

    /// <summary>Selecting a control in the list re-renders the preview with that control outlined in gold.</summary>
    public UiControlRow? SelectedUiControl
    {
        get => _selectedUiControl;
        set
        {
            if (SetField(ref _selectedUiControl, value))
            {
                RenderUiWindowPreview(value?.Index ?? -1);
            }
        }
    }

    /// <summary>
    /// Loads a client UI XML file (Modul D) and renders the first WindowTemplate it defines.
    /// Files can hold more than one WindowTemplate, but every real sample seen so far has
    /// exactly one — later WindowTemplates are parsed but not shown, rather than guessing
    /// which one the user wants.
    /// </summary>
    public void LoadUiWindow(string path)
    {
        DaocUiFile ui = DaocUiFile.Load(path);

        if (ui.Windows.Count == 0)
        {
            throw new DaocUiFormatException($"{Path.GetFileName(path)} has no <WindowTemplate> to preview.");
        }

        DaocWindowTemplate window = ui.Windows[0];
        _uiWindowTemplate = window;
        UiWindowName = window.Name;

        UiWindowControls.Clear();

        for (int i = 0; i < window.Controls.Count; i++)
        {
            UiWindowControls.Add(UiControlRow.FromControl(i, window.Controls[i]));
        }

        _selectedUiControl = null;
        OnPropertyChanged(nameof(SelectedUiControl));
        RenderUiWindowPreview(-1);

        string extra = ui.Windows.Count > 1 ? $" ({ui.Windows.Count - 1} more window(s) in this file not shown)" : "";
        StatusText = $"Loaded UI window \"{window.Name}\" ({window.Width}x{window.Height}, {window.Controls.Count} controls){extra}.";
    }

    private void RenderUiWindowPreview(int highlightIndex)
    {
        if (_uiWindowTemplate is null)
        {
            return;
        }

        using var pngStream = new MemoryStream();
        DaocWindowRenderer.SaveAsPng(_uiWindowTemplate, pngStream, highlightIndex);
        pngStream.Position = 0;

        UiWindowPreviewImage?.Dispose();
        UiWindowPreviewImage = new Bitmap(pngStream);
    }

    private void RenderColorTable(SystemColFile col)
    {
        using var pngStream = new MemoryStream();
        SystemColExporter.SaveAsPng(col, pngStream);
        pngStream.Position = 0;

        ColorTableImage?.Dispose();
        ColorTableImage = new Bitmap(pngStream);
    }

    /// <summary>
    /// Header summary for the selected entry when it's a .nif model, e.g.
    /// "NetImmerse 4.2.2.0 — 232 blocks". Null when nothing applicable is selected.
    /// </summary>
    public string? ModelInfo
    {
        get => _modelInfo;
        private set => SetField(ref _modelInfo, value);
    }

    private Bitmap? _modelPreviewImage;
    private IReadOnlyList<NifWorldTriangle>? _modelPreviewMesh;
    private float _modelPreviewYaw = NifMeshPreviewRenderer.DefaultRotationYDegrees;
    private float _modelPreviewPitch = NifMeshPreviewRenderer.DefaultRotationXDegrees;

    /// <summary>
    /// 3/4-view render of the selected .nif's geometry (see NifSceneMesh/
    /// NifMeshPreviewRenderer in Trafty.Core), when the full block-list parse succeeds. Null
    /// when nothing applicable is selected or the model uses an unimplemented block type.
    /// Rotatable by dragging in the view — see <see cref="RotateModelPreview"/>.
    /// </summary>
    public Bitmap? ModelPreviewImage
    {
        get => _modelPreviewImage;
        private set => SetField(ref _modelPreviewImage, value);
    }

    /// <summary>
    /// Re-renders <see cref="ModelPreviewImage"/> at a new rotation, for mouse-drag
    /// interaction. No-op if nothing is currently loaded.
    /// </summary>
    public void RotateModelPreview(float deltaYawDegrees, float deltaPitchDegrees)
    {
        if (_modelPreviewMesh is not { Count: > 0 } mesh)
        {
            return;
        }

        _modelPreviewYaw += deltaYawDegrees;
        _modelPreviewPitch = Math.Clamp(_modelPreviewPitch + deltaPitchDegrees, -89f, 89f);

        using var stream = new MemoryStream();
        NifMeshPreviewRenderer.SaveAsPng(mesh, ModelPreviewSize, ModelPreviewSize, stream, _modelPreviewYaw, _modelPreviewPitch);
        stream.Position = 0;

        ModelPreviewImage?.Dispose();
        ModelPreviewImage = new Bitmap(stream);
    }

    /// <summary>
    /// If the current selection is a .nif entry, extracts it and parses its header.
    /// Safe to call repeatedly — it's a no-op once <see cref="ModelInfo"/> is already set
    /// for the current selection, and clears itself when the selection isn't a model.
    /// </summary>
    public void InspectSelectedIfModel()
    {
        ModelPreviewImage?.Dispose();
        ModelPreviewImage = null;
        _modelPreviewMesh = null;
        _modelPreviewYaw = NifMeshPreviewRenderer.DefaultRotationYDegrees;
        _modelPreviewPitch = NifMeshPreviewRenderer.DefaultRotationXDegrees;

        if (SelectedEntry is not { IsModel: true } selected || ArchivePath is null)
        {
            ModelInfo = null;
            return;
        }

        try
        {
            using MpkArchive archive = MpkArchive.Open(ArchivePath);
            MpkEntry? entry = archive[selected.Name];

            if (entry is null)
            {
                ModelInfo = "Entry not found in archive.";
                return;
            }

            byte[] bytes = archive.Extract(entry);
            NifHeader header = NifHeader.Parse(bytes);
            string summary = $"NetImmerse {header.VersionDisplay} — {header.BlockCount} blocks";

            try
            {
                NifDocument doc = NifDocument.Parse(bytes);
                int vertexCount = doc.Blocks.OfType<NiTriShapeDataBlock>().Sum(b => b.Vertices.Count);
                int triangleCount = doc.Blocks.OfType<NiTriShapeDataBlock>().Sum(b => b.Triangles.Count);
                summary += $"\n{vertexCount} vertices, {triangleCount} triangles";

                IReadOnlyList<NifWorldTriangle> mesh = NifSceneMesh.Build(doc);

                if (mesh.Count > 0)
                {
                    _modelPreviewMesh = mesh;

                    using var previewStream = new MemoryStream();
                    NifMeshPreviewRenderer.SaveAsPng(mesh, ModelPreviewSize, ModelPreviewSize, previewStream);
                    previewStream.Position = 0;
                    ModelPreviewImage = new Bitmap(previewStream);
                }
            }
            catch (ModelFormatException)
            {
                // Full block-list parsing only covers the block types verified against this
                // project's real test file (see NifDocument's remarks) — a model using an
                // unimplemented block type still shows header info, just no 3D preview.
            }

            ModelInfo = summary;
        }
        catch (Exception ex) when (ex is ModelFormatException or MpkFormatException or IOException)
        {
            ModelInfo = $"Could not read model: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens an archive and populates <see cref="Entries"/>. Any previous selection or
    /// status message is cleared.
    /// </summary>
    public void LoadArchive(string path)
    {
        using MpkArchive archive = MpkArchive.Open(path);

        Entries.Clear();

        foreach (MpkEntry entry in archive.Entries)
        {
            AssetRow row = AssetRow.FromEntry(entry);

            if (row.Extension.Equals("dds", StringComparison.OrdinalIgnoreCase))
            {
                row.Thumbnail = TryDecodeThumbnail(archive, entry);
            }

            Entries.Add(row);
        }

        ArchivePath = path;
        SelectedEntry = null;
        StatusText = $"Loaded {archive.Entries.Count} entries from {archive.ArchiveName}.";
        RefreshBackups();
    }

    /// <summary>
    /// Decodes a .dds entry for the Modul A thumbnail grid. Returns null rather than
    /// throwing on a format this project's decoder doesn't support (e.g. an unsupported
    /// FourCC) — one bad texture shouldn't stop the rest of the archive from listing.
    /// </summary>
    private static Bitmap? TryDecodeThumbnail(MpkArchive archive, MpkEntry entry)
    {
        try
        {
            byte[] bytes = archive.Extract(entry);
            DdsFile dds = DdsFile.Parse(bytes);

            using var pngStream = new MemoryStream();
            DdsExporter.SaveAsPng(dds, pngStream);
            pngStream.Position = 0;

            return new Bitmap(pngStream);
        }
        catch (TextureFormatException)
        {
            return null;
        }
    }

    /// <summary>Refreshes the entry list from disk, keeping the archive path.</summary>
    public void Reload()
    {
        if (ArchivePath is not null)
        {
            LoadArchive(ArchivePath);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
