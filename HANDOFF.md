# Trafty Work Handoff

## Purpose

This file captures the current work-in-progress state for the next development session.

Trafty is intended to become a modern, high-end Dark Age of Camelot client asset editor and world-building workstation. The long-term goal is not to recreate isolated legacy utilities, but to make client modding understandable and visual enough that users can discover, inspect, edit, and eventually create complete DAoC client content such as custom zones.

A useful product test is: a developer who understands server code and databases but has little client-modding experience should be able to learn the client structure by using Trafty rather than by manually reverse-engineering every file relationship first.

## Working rules

- All user-facing UI text must be English.
- Do not commit or push until explicit approval is given.
- Review the current work before extending it.
- Do not add tool/assistant attribution, co-author lines, authorship signatures, or similar metadata to source files, documentation, README files, or commits.
- Prefer large, coherent product solutions over tiny one-off utilities, while keeping implementation incremental and testable.
- Treat apparent DAoC client limitations as hypotheses to investigate, not automatic product boundaries.

## Repository state

Repository: `Darku11/Trafty`

`main` is intentionally unchanged by the current refactor. The latest work exists only as an unreferenced Git tree object and has not been committed.

Main commit used as the original baseline:

`4efdbdfe4051f8cd110f33dd24780e015889cddc`

The uncommitted tree immediately before adding this handoff file was:

`33778d33dd45a61d6345048482b24de8966c8022`

The final tree SHA containing this handoff file is supplied separately with the handoff. Do not assume that checking out `main` contains the work below.

## Work currently implemented

### 1. New MPK archive creation

Trafty now has a `New Archive…` workflow in the main UI.

Behavior:

- Creates a new empty `.mpk` through the existing `MpkArchiveWriter.Write(...)` implementation.
- Writes through a temporary file before replacing the target.
- Backs up an existing non-empty target before replacement.
- Immediately loads the newly created archive into the normal archive workspace.
- Uses existing archive-writing infrastructure rather than a parallel implementation.

Relevant files:

- `Trafty.App/Views/MainWindow.ArchiveCreation.cs`
- `Trafty.App/Views/MainWindow.axaml`
- `Trafty.Core.Tests/MpkArchiveCreationTests.cs`

The creation tests cover opening/verifying a newly created empty MPK and subsequently extending it with `WriteReplacing(...)`.

### 2. Client Asset Scanner / Asset Database foundation

A new Core-level client scanner recursively indexes an entire DAoC client directory.

It indexes both:

- loose files on disk;
- entries contained inside `.mpk`, `.epk`, and `.npk` archives.

Archives themselves are retained as indexed assets. Unknown file formats are retained rather than discarded so Trafty remains useful for reverse engineering.

Current coarse asset categories include:

- Archives
- 3D Models
- World Props
- Textures / Images
- Audio
- UI
- Zone / Data
- Color Tables
- Text / Config
- Unknown

A malformed or unreadable archive is recorded as a scan failure instead of aborting the full client scan.

Relevant files:

- `Trafty.Core/Client/ClientAssetIndex.cs`
- `Trafty.Core/Client/ClientAssetScanner.cs`
- `Trafty.Core.Tests/ClientAssetScannerTests.cs`

### 3. Client Explorer

The application now has a `Scan Client…` entry point that opens a dedicated Client Explorer.

The explorer currently supports:

- recursive client-wide asset browsing;
- search by asset name and location/archive path;
- filtering by asset category;
- showing physical source and archive-entry location;
- size information;
- direct preview of DDS textures;
- direct extraction and inspection of NIF entries from inside archives;
- NIF header/version/block information;
- vertex and triangle counts where the existing full NIF parser supports the model;
- direct 3D preview using the existing NIF rendering pipeline;
- graceful fallback to header information for NIF variants not yet fully supported.

Relevant files:

- `Trafty.App/ViewModels/ClientAssetRow.cs`
- `Trafty.App/ViewModels/ClientExplorerViewModel.cs`
- `Trafty.App/Views/ClientExplorerWindow.axaml`
- `Trafty.App/Views/ClientExplorerWindow.axaml.cs`
- `Trafty.App/Views/MainWindow.ClientExplorer.cs`

The earlier runtime button-injection approach was removed. `Scan Client…` is now part of the actual main-window XAML.

### 4. Large UI / identity refactor

The current UI work moves Trafty away from a generic dark developer utility toward a fantasy workshop / asset-forge identity inspired by the usability and atmosphere of classic DAoC community tools while remaining a modern application.

The refactor includes:

- darker leather / bronze / parchment-like palette;
- stronger panel hierarchy and framed work areas;
- grouped main tools instead of one undifferentiated toolbar;
- a `DAoC CLIENT ASSET FORGE` presentation direction;
- workshop-oriented wording;
- a reusable guide/persona system;
- context-sensitive guide content for different workspaces;
- matching Client Explorer styling.

Current guide personas:

- Aelwyn — Elven Archivist: archives and client discovery
- Brokk — Dwarven Worldwright: world props and construction
- Corvin — Breton Cartographer: zone maps and placement
- Pipwick — Lurikeen Tinkerer: UI work
- Liora — Elven Artisan: textures
- Maelis — atmosphere / color tables
- Rurik — audio

The guides are intended to teach while the user works. Their role is not purely decorative. Later artwork can replace the current visual placeholders without changing the guide architecture.

Relevant files:

- `Trafty.App/Guides/GuideProfile.cs`
- `Trafty.App/Guides/TraftyGuides.cs`
- `Trafty.App/Views/MainWindow.Guides.cs`
- `Trafty.App/Views/MainWindow.axaml`
- `Trafty.App/Views/ClientExplorerWindow.axaml`
- `Trafty.App/App.axaml`

## Product direction

The intended long-term abstraction is that the user works with concepts rather than DAoC's historical file layout.

For example, the user should eventually be able to ask Trafty for a tree, house, ruin, bridge, texture, or zone object without first knowing which MPK/NPK contains it.

A future zone project should eventually look conceptually like:

- Terrain
- Boundaries
- Water
- Fixtures
- Models
- Textures
- Lighting / Atmosphere
- Client Resources

Trafty should handle the underlying archive and file-format relationships.

The long-term north star is visual creation and editing of custom DAoC zones, including the kind of custom-world work historically done for freeshard content such as entirely new adventure regions. Zone creation is intentionally ambitious and should not be rejected merely because the original client tooling is obscure.

## Recommended next review

Before adding another large feature, perform a full review of the current uncommitted tree.

Priority checks:

1. Build the complete solution with .NET 8.
2. Run all Core tests.
3. Validate Avalonia XAML compilation for `App.axaml`, `MainWindow.axaml`, and `ClientExplorerWindow.axaml`.
4. Verify all existing main-window handlers are still reachable after the layout rewrite.
5. Verify guide switching for Archive Assets, World Props, Zone Map, UI Windows, textures, atmosphere, audio, and Client Explorer.
6. Test `New Archive…` end-to-end with an empty MPK and then `Add Files…`.
7. Scan a real DAoC client and evaluate scanner performance/memory usage with many thousands of files/archive entries.
8. Test NIF and DDS previews for both loose files and archive-contained assets.
9. Check window behavior at minimum supported sizes and common 1080p/1440p desktop resolutions.
10. Remove or simplify anything that became obsolete during the UI refactor rather than keeping compatibility scaffolding indefinitely.

No build or test execution has been possible in the current environment, so compilation and runtime validation remain mandatory before the work should be considered ready for commit.

## Likely next product steps after review

A strong next technical direction is dependency resolution and asset relationships:

- NIF -> referenced textures
- NHD -> model
- model/prop -> where used
- zone fixture -> referenced NIF/model
- asset -> zones in which it appears

This turns the Client Explorer from a search index into a navigable DAoC asset graph and lays groundwork for visual world editing.

Other useful archive completeness work still available later includes entry rename/removal and CLI archive creation, but these should not displace the larger asset/workspace direction unless they are immediately needed.
