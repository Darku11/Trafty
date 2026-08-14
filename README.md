# Trafty — Modul A, Schritt 1: MPK/EPK Unpacker & Header-Parser

Dieser Schritt liefert eine schreibfähige `Trafty.Core`-Bibliothek, die das
MPAK-Containerformat verlustfrei lesen kann, plus eine kleine CLI zum
Durchtesten und ein Testprojekt, das gegen eine echte Client-Datei
(`ter002.mpk`) prüft.

## Woher das Format stammt

Reverse-engineered direkt aus deiner hochgeladenen `ter002.mpk` (541.382
Bytes, 143 Einträge). Alle Angaben unten sind gegen diese Datei verifiziert:
Directory-CRC stimmt, alle 143 Payload-CRCs stimmen, alle Größen stimmen,
das letzte Datenende trifft exakt das Dateiende.

### Header (21 Byte, versetzt)

```
0x00  "MPAK"
0x04  Version (byte, unverschleiert)          -> 2 in dieser Datei
0x05  uint32 CRC32 des komprimierten Directory
0x09  uint32 Directory-Größe (komprimiert)
0x0D  uint32 Namensblock-Größe (komprimiert)
0x11  uint32 Anzahl Dateien
```

Ab Offset `0x05` ist jedes Byte mit seinem eigenen Index (0, 1, 2, …)
XOR-verschleiert. Die Maske ist symmetrisch — dieselbe Routine kodiert und
dekodiert.

### Namensblock & Directory

Direkt nach dem Header folgt ein zlib-Stream mit dem Archivnamen
(`"ter002.mpk"`), danach ein zlib-Stream mit dem Directory: `Anzahl Dateien
× 284 Byte`.

### Directory-Eintrag (284 Byte)

```
0x000  char[13]   Dateiname, NUL-terminiert
0x00D  char[243]  ursprünglicher Quellpfad, NUL-terminiert
0x100  uint32     Zeitstempel (Unix, UTC)
0x104  uint32     Flags (konstant 4 in allen bisher gesehenen Archiven)
0x108  uint32     Offset im unkomprimierten Datenstrom
0x10C  uint32     unkomprimierte Größe
0x110  uint32     Offset im komprimierten Datenbereich
0x114  uint32     komprimierte Größe
0x118  uint32     CRC32 — deckt die KOMPRIMIERTEN Bytes ab, nicht die
                   entpackten. Das haben wir an allen 143 Einträgen
                   verifiziert.
```

**Namensfeld-Falle:** Das Namensfeld ist nur 13 Byte groß, der Pfad startet
aber fest bei Offset `0x0D`. Der Packer schreibt zuerst den Pfad, dann den
Namen — Namen über 12 Zeichen überschreiben also den Anfang des Pfads. In
`ter002.mpk` steht deshalb `lot\labyrinth\...` statt `camelot\labyrinth\...`.
`MpkEntry` bildet das exakt nach, inklusive der uninitialisierten
Padding-Bytes hinter dem Pfad-Terminator (116 von 143 Einträgen in der
Testdatei haben solche Reste) — ein unverändert wieder geschriebener Eintrag
ist dadurch byte-identisch zum Original.

Der Datenbereich beginnt direkt nach dem Directory-Block und reiht die
komprimierten Payloads lückenlos aneinander.

## Projektstruktur

```
src/
  Trafty.sln
  Trafty.Core/            Parser, keine externen Abhängigkeiten
    Archives/
      MpkHeader.cs
      MpkEntry.cs
      MpkArchive.cs
      MpkFormatException.cs
    Compression/ZlibCodec.cs
    Hashing/Crc32.cs
  Trafty.Core.Tests/       xUnit-Tests gegen ter002.mpk (liegt als Fixture bei)
  Trafty.Cli/               Kommandozeilen-Werkzeug zum Durchprobieren
```

## Bauen & Ausführen

```bash
cd src
dotnet test                                  # prüft gegen ter002.mpk
dotnet run --project Trafty.Cli -- info    ../ter002.mpk
dotnet run --project Trafty.Cli -- list    ../ter002.mpk
dotnet run --project Trafty.Cli -- verify  ../ter002.mpk
dotnet run --project Trafty.Cli -- extract ../ter002.mpk ./out
```

## API-Kurzüberblick

```csharp
using Trafty.Core.Archives;

using MpkArchive archive = MpkArchive.Open("ter002.mpk");

Console.WriteLine(archive.ArchiveName);       // "ter002.mpk"
Console.WriteLine(archive.Entries.Count);     // 143

MpkEntry entry = archive["patch0000-00.dds"]!;
byte[] dds = archive.Extract(entry);          // CRC-geprüft, entpackt

IReadOnlyList<string> problems = archive.Verify(); // leer = Archiv intakt
```

`MpkArchive.Open` liest Header, Namensblock und Directory sofort, prüft die
Directory-CRC und dass alle deklarierten Offsets in die Datei passen — ein
manipuliertes oder abgeschnittenes Archiv fliegt dir schon beim Öffnen um
die Ohren (`MpkFormatException`), nicht erst beim Extrahieren einer
einzelnen Datei. Die eigentlichen Payloads werden erst bei `Extract`
gelesen, das Archiv puffert sie nicht im Speicher.

## Bewusste Entscheidungen

- **CRC auf komprimierten statt entpackten Bytes** — exakt nachgebildet,
  weil sonst jedes gültige Archiv als beschädigt gemeldet würde.
- **`ExtractAll` ignoriert den gespeicherten Quellpfad** und schreibt nur
  unter dem Dateinamen in das Zielverzeichnis. Der Pfad ist unzuverlässig
  (siehe Namensfeld-Falle oben) und aus einer präparierten Datei ließe sich
  sonst potenziell außerhalb des Zielordners schreiben.
- **`RawStringBlock`** wird beim Parsen mitgeführt, damit ein unverändert
  zurückgeschriebener Eintrag bitgenau dem Original entspricht — wichtig für
  Non-Destructive Editing, wenn später nur einzelne Assets ersetzt werden.

## Modul A, Schritt 2: Smart Replace Engine (DDS-Encoder)

Aus einer echten Terrain-Textur (`patch0001-00.dds`) verifiziert: Format ist
**DXT3 (BC2)**, 128×128, Standard-124-Byte-DDS-Header. Jedes Mip-Level liegt
als eigenständige `.dds`-Datei im Archiv (`patchNNNN-00.dds`, `-01.dds`, …),
**nicht** als eine Datei mit eingebettetem Mip-Chain — deshalb erzeugt der
Encoder pro Stufe eine komplette DDS-Datei.

```
Trafty.Core/Textures/
  DxtFormat.cs          Bc1 (DXT1, kein Alpha) / Bc2 (DXT3, scharfes 4-Bit-Alpha)
  BlockCompressor.cs     Reiner C#-Blockkompressor (Range-Fit, keine native Lib)
  DdsWriter.cs            Baut den Header exakt so, wie ihn der Original-Packer schreibt
  MipChainBuilder.cs      Lädt via ImageSharp, generiert Mip-Kette bis 4×4
  DdsEncoder.cs            Orchestriert: Bild -> Mip-Kette -> DXT -> fertige .dds-Bytes
Trafty.Core/Archives/
  BackupVault.cs           Non-Destructive Editing: Kopie vor jedem Schreibzugriff
  MpkArchiveWriter.cs        Schreibt Archive neu, ersetzt/ergänzt einzelne Einträge
```

**Bewusste Grenze:** Die Mip-Kette stoppt bei 4×4, weil Blockkompression
mindestens 4×4-Blöcke braucht. Ob der Client 2×2/1×1-Level erwartet, ließ
sich aus `ter002.mpk` nicht zweifelsfrei klären — lieber diese Stufen
auslassen als raten.

**Alpha-Erkennung:** Ohne explizite Formatangabe wählt `DdsEncoder`
automatisch BC2 (DXT3), sobald das Quellbild einen nicht-opaken Pixel hat,
sonst BC1 (DXT1) für kleinere Dateien.

### CLI: Textur ersetzen

```bash
dotnet run --project Trafty.Cli -- replace ter002.mpk mein_bild.png patch0001
```

Das legt automatisch ein Backup unter `.trafty-backups/` neben dem Archiv an
(Zeitstempel im Dateinamen), kodiert `mein_bild.png` in eine volle Mip-Kette
und schreibt `patch0001-00.dds` … `patch0001-0N.dds` ins Archiv — vorhandene
Einträge mit gleichem Namen werden ersetzt, alles andere bleibt unverändert.

**Hinweis zum Repack:** Beim Neuschreiben unveränderter Einträge wird ein
frischer, nullgefüllter Name/Pfad-Block erzeugt statt des Original-Byte-Mülls
(siehe Namensfeld-Falle oben). Der Client interessiert sich nicht für dieses
Padding, daher funktional identisch — nur ein reiner Byte-Vergleich mit dem
Original-Archiv würde nach einem Repack nicht mehr 1:1 übereinstimmen.

## Modul C: `.nhd`-Grid-Semantik (5 Samples ausgewertet)

Über fünf Dateien unterschiedlicher Objektgröße zeigt sich ein klares
Muster: der Sentinel-Wert `-2500` ("keine Geometrie hier") dominiert
konstant (42–64 % der Zellen), aber der **größte Nicht-Sentinel-Wert
skaliert mit der physischen Objektgröße**:

| Datei | Grid | Max-Wert |
|---|---|---|
| `3rdruinedpiece.nhd` (klein) | 20×28 | 85 |
| `aecentbarricade.nhd` (mittel) | 64×63 | 388 |
| `aegcliffpiece2.nhd` (groß) | 137×126 | 6862 |

Das spricht für eine **Höhenkarte** (Geometriehöhe pro Zelle über einer
Basislinie) statt einer reinen Kollisions-Bitmaske — bleibt aber
Hypothese, keine bestätigte Tatsache, und wird im Code auch so
gekennzeichnet.

```bash
dotnet run --project Trafty.Cli -- nhd 1struinedtemple.nhd
```

## Modul C, zweiter Schritt: NIF-Modell-Header

`.nif` ist — anders als MPAK/NHD — ein **offen dokumentiertes Format**
(NetImmerse/Gamebryo, NifTools-Projekt), kein Reverse-Engineering-Ziel. Der
Header wurde trotzdem an deiner echten, aus `1struinedtemple.npk`
entpackten `.NIF`-Datei gegengeprüft:

```
"NetImmerse File Format, Version 4.2.2.0\n"
uint32  Version, gepackt als Bytes [Build, Patch, Minor, Major]
uint32  BlockCount (in deiner Datei: 232)
```

**Bewusst nicht implementiert:** Das Parsen der 232 einzelnen Blöcke
(Meshes, Texturkoordinaten, Skinning-Daten, …) braucht pro Blocktyp ein
eigenes Layout — Dutzende verschiedene Typen. Das ist ein eigenes, großes
Stück Arbeit und wird hier nicht überstürzt angegangen. Aktuell liefert
`NifHeader` nur Version und Blockanzahl — genug, um Format und grobe
Komplexität einer Datei zu erkennen, ohne bei der eigentlichen
Geometrie zu raten.

```bash
dotnet run --project Trafty.Cli -- nif 1struinedtemple.NIF
```

## App-Integration: NIF-Inspektor

Die Avalonia-App öffnet jetzt auch `.npk`-Dateien direkt (Datei-Öffnen-Filter
erweitert). Wählst du darin einen `.NIF`-Eintrag aus, zeigt das Detail-Panel
automatisch Version und Blockanzahl an — die Extraktion + `NifHeader.Parse`
läuft bei jeder Auswahl neu, ohne das Archiv dauerhaft offen zu halten.

## Modul D, erster Schritt: WAV-Audio-Header

`.wav` ist wie `.nif` ein offen dokumentiertes Format (RIFF/WAVE,
Microsoft/IBM-Spezifikation) — kein Reverse-Engineering nötig.

```
"RIFF" + uint32 Größe + "WAVE"
dann Chunks: char[4] ID + uint32 Größe + Payload (auf gerade Länge gepolstert)
  "fmt "  → Format (PCM/Float/...), Kanäle, Samplerate, Bitrate, Bits/Sample
  "data"  → die eigentlichen Audiodaten
```

**Wichtiger Unterschied zu NIF/NHD:** Für WAV liegt mir noch **keine echte
Client-Datei** vor — die Test-Fixtures sind synthetisch erzeugte, aber
gültige RIFF/WAVE-Dateien (Stille, unterschiedliche Formate). Das Format
selbst ist öffentlich stabil spezifiziert, also geringeres Risiko als bei
den proprietären Formaten, aber ein echter Abgleich mit einer Datei aus
`sounds/` steht noch aus.

```bash
dotnet run --project Trafty.Cli -- wav some_sound.wav
```

## Modul B gefunden: `.col`-Wetter-/Farbtabellen

Über `color.dat` (Klartext-Config im Client-Root) aufgespürt:

```ini
[color_tables]
00=system.col
```

`SYSTEM.COL` selbst hat **keinen Header** — reine Rohdaten. Größe exakt
`128 × 66 × 2 = 16.896 Byte`, kein Rest — die Maße wurden nicht geraten,
sondern über exakten Byte-Abgleich bestimmt. Gerendert als RGB565-Raster
(gegen deine echte Datei geprüft) ergibt sich ein sauberes Bild mit vier
vertikalen Farbbändern (Grau/Orange/Blau/Grün) und weichem
Dithering-Verlauf pro Band — klar kein Zufallsrauschen, sondern echte
Bilddaten.

**Hypothese (visuell gut gestützt, nicht bestätigt):** Die vier Bänder
könnten unterschiedliche Wetter-/Tageszeit-Zustände darstellen, der
vertikale Verlauf eine Intensitäts- oder Höhenstufe. Was genau jede Spalte
bedeutet, ist offen — Zonen-/Wetter-Konfigurationsdateien, die auf
bestimmte Spaltenindizes verweisen, würden das klären.

```bash
dotnet run --project Trafty.Cli -- col system.col preview.png
```

`darkness.dat` (`drcontent=1`) und `paths.dat` (`settings=Atlas1`) sind
reine Config-Flags, keine eigenen Binärformate — vermutlich schalten sie
zwischen mehreren `.col`-Tabellen oder Kartensets um.

## App-Integration: Wetter-Tabellen-Vorschau

Neuer Button **"Open Color Table…"** in der Toolbar: öffnet eine `.col`-Datei
direkt (ohne Umweg über ein MPK-Archiv), rendert sie in Echtzeit über
`SystemColExporter` und zeigt sie pixelgenau vergrößert (kein Weichzeichnen,
`BitmapInterpolationMode="None"`) im Detail-Panel an — die 128×66-Textur
bleibt scharf statt zu verschwimmen.

## App-Integration: Backup-Vault-UI (Grundprinzip #2 vervollständigt)

Bisher existierte `BackupVault` nur im Core-Code — es gab keine
Möglichkeit, ein Backup über die Oberfläche wiederherzustellen. Jetzt:

- Beim Öffnen eines Archivs (und nach jedem Textur-Ersetzen) lädt die App
  automatisch die Liste vorhandener Backups für diese Datei.
- Ein neuer **"Backup Vault"**-Bereich im Detail-Panel zeigt alle
  Zeitstempel, auswählbar per Klick.
- **"Restore Selected Backup"** fragt erst per Dialog nach Bestätigung
  (destruktive Aktion — Nutzer muss aktiv zustimmen), überschreibt dann
  das aktuelle Archiv mit dem gewählten Backup und lädt die Ansicht neu.

Damit ist das in der Spezifikation zugesagte "Restore"-Prinzip jetzt
tatsächlich benutzbar, nicht nur im Code vorhanden.

## CLI-Ergänzung: Backup-Vault skriptbar

Passend zur App-UI jetzt auch über die Kommandozeile nutzbar:

```bash
dotnet run --project Trafty.Cli -- backups ter002.mpk
dotnet run --project Trafty.Cli -- restore ter002.mpk        # neuestes Backup
dotnet run --project Trafty.Cli -- restore ter002.mpk 2      # Index aus "backups"-Liste
```

## App-Integration: World-Props-Ordner-Browser (Modul C)

Neuer Button **"Open World Props Folder…"** — öffnet einen kompletten
Ordner (z. B. `zones\Nifs`) und listet alle `.nhd`-Dateien darin mit
Modellname und Grid-Größe auf, in einem eigenen Tab neben der
Archiv-Asset-Liste. Nicht parsbare Dateien werden übersprungen statt den
ganzen Scan abzubrechen — ein Zonen-Ordner enthält erfahrungsgemäß auch
andere Dateitypen.

Das ist der erste Baustein Richtung "Zone anklicken → zugehörige Assets
sehen" aus der ursprünglichen Modul-C-Spezifikation — noch ohne Kartenview,
aber die Datengrundlage (viele `.nhd`-Dateien auf einen Blick) steht.

## Modul C: Zonen-Kartenansicht (`fixtures.csv`/`nifs.csv`/`bound.csv`)

Über eine echte `csv003.mpk`/`dat003.mpk` (Zone "Black Mountains North")
gefunden: Beide Archive enthalten byte-identische `fixtures.csv`, `nifs.csv`
und `bound.csv` — reiner Klartext, kein Reverse-Engineering nötig. `dat003.mpk`
trägt zusätzlich die Terrain-PCX-Bilder (`terrain.pcx` u.a.), ist aber sonst
identisch.

```
fixtures.csv   ID,NIF #,Textual Name,X,Y,Z,A,Scale,... — ein Datensatz pro
               platziertem Objekt in der Zone (Weltposition + Blickrichtung)
nifs.csv       NIF #,Textual Name,Filename,... — löst die NIF-ID aus
               fixtures.csv zum tatsächlichen Modell-Dateinamen auf
bound.csv      flache Komma-Liste von (x, y)-Paaren — Umriss-Polygon der Zone
```

Verifiziert an `zone003` (Black Mountains North): 726 Fixture-Einträge, 42
NIF-Referenzen, 232 Umriss-Punkte — jede NIF-ID aus `fixtures.csv` löst sich
in `nifs.csv` auf.

```
Trafty.Core/Zones/
  FixtureCsvFile.cs      Objekt-Platzierungen
  NifCsvFile.cs           NIF-ID -> Dateiname
  ZoneBoundaryFile.cs      Umriss-Polygon
  ZoneMap.cs               Fasst alle drei zusammen, lädt direkt aus MpkArchive
  ZoneCsvFormatException.cs
```

**App-Integration**: Neuer Button **"Open Zone Map…"** öffnet ein
`.mpk`-Archiv (z. B. `csv003.mpk`), projiziert Umriss + Fixtures in ein
700x700-Canvas (Seitenverhältnis erhalten) und zeigt sie in einem eigenen
Tab. Klick auf einen Fixture-Punkt zeigt Name + aufgelösten Modell-Dateinamen
im Detail-Panel.

### PCX-Terrain-Hintergrund

`Trafty.Core.Images.PcxFile` liest `.pcx` (ZSoft Paintbrush) — öffentlich
dokumentiertes Format, byte-genau gegen die echte `terrain.pcx` aus
`dat003.mpk` verifiziert (256×256, 8bpp, RLE-komprimiert, 256-Farben-Palette
am Dateiende). Nur diese eine gefundene Variante ist implementiert; andere
Bittiefen/Multi-Plane-PCX werden bewusst mit Fehler abgelehnt statt geraten.
Die App lädt `terrain.pcx` automatisch mit, wenn das geöffnete Zonen-Archiv
sie enthält (bei `dat003.mpk`, nicht bei `csv003.mpk`), und zeigt sie als
Hintergrund hinter Umriss und Fixture-Punkten.

### Y-Achsen-Ausrichtung (teilweise verifiziert)

Alle 726 Fixture-Positionen liegen exakt innerhalb der Bounding-Box von
`bound.csv` (0 Ausreißer) — bestätigt, dass `fixtures.csv`, `bound.csv` und
`terrain.pcx` dasselbe Koordinatensystem und denselben Ursprung teilen.
Ein visueller Overlay-Test (Fixture-Punkte über `terrain.pcx` gelegt, mit
Y-Flip für "Norden oben") zeigt, dass sich Fixture-Cluster exakt mit
sichtbaren Geländemerkmalen (helle/dunkle Flecken, vermutlich Siedlungen)
decken — die Projektion ist also intern konsistent.

**Weiterhin offen:** Ob "oben im Bild" tatsächlich geografisch Norden ist
(und nicht Süden gespiegelt), lässt sich aus den Zonendateien allein nicht
zweifelsfrei beweisen — dafür fehlt eine externe Referenz (z. B. eine
offizielle Weltkarte mit Kompassrichtung). Aktuell in `MainWindowViewModel`
klar als Hypothese kommentiert, nicht als bestätigte Tatsache behandelt.

## Modul C: NIF-Geometrie-Parser (voller Block-Parser, nicht nur Header)

`NifHeader` (siehe oben) liest nur Version + Blockanzahl. `Trafty.Core.Models.Nif.NifDocument`
geht jetzt weiter und parst die komplette Blockliste — bei diesem alten NIF-Format (4.2.2.0)
gibt es **keine Sprungtabelle und keine Block-Größenangabe**: jeder Block ist nur Typname +
Felder, direkt gefolgt vom nächsten Block. Ein nicht implementierter Blocktyp lässt sich
also nicht überspringen, nur exakt parsen — deshalb wirft der Parser bei unbekannten
Blocktypen bewusst einen Fehler, statt zu raten.

Layouts stammen aus der öffentlichen NifTools-`nif.xml`-Spezifikation (kein
Reverse-Engineering, wie bei NIF/WAV üblich), gefiltert auf das, was bei Version 4.2.2.0
tatsächlich zutrifft (viele Felder in der Spec sind an spätere Versionen gebunden).
**Verifiziert an der echten `1struinedtemple.NIF`:** Ein Scan der Rohdatei ergab genau 10
vorkommende Blocktypen (nicht "Dutzende", wie ursprünglich befürchtet) — NiNode,
NiLODNode, NiTriShape, NiTriShapeData, NiMaterialProperty, NiTexturingProperty,
NiSourceTexture, NiVertexColorProperty, NiZBufferProperty, NiDitherProperty. Alle zehn sind
implementiert. Die stärkste Verifikation: nach allen 232 Blöcken + Root-Liste bleiben
**exakt 0 Byte** übrig — jede falsche Feldlänge in irgendeinem der 232 Blöcke hätte die
Leseposition verschoben und entweder einen Absturz oder Restbytes verursacht.

```
Trafty.Core/Models/Nif/
  NifByteReader.cs     Low-Level-Cursor (alle Primitivtypen: Vector3, Matrix33, Color4, ...)
  NifBlocks.cs           Typisierte Block-Klassen (NiNodeBlock, NiTriShapeDataBlock, ...)
  NifDocument.cs          Orchestriert das Parsen der ganzen Blockliste
```

**App-Integration:** Der NIF-Inspektor zeigt jetzt zusätzlich Vertex-/Dreieck-Anzahl an, wenn
der volle Parser die Datei versteht (fällt bei unbekannten Blocktypen sauber auf die
Header-Only-Anzeige zurück, statt abzustürzen).

**Bewusst nicht implementiert:** Andere `BoundingVolume`-Typen als Sphere (Typ 0), sowie
jeder Blocktyp, der nicht in der echten Testdatei vorkam — es gibt schlicht keine zweite
echte Datei, gegen die man das verifizieren könnte.

## Modul C: 3D-Vorschau-Renderer (statisches Bild, kein 3D-Engine-Dependency)

Die Geometrie aus `NifDocument` (s.o.) wird jetzt tatsächlich sichtbar gemacht, ohne eine
3D-Bibliothek als neue Abhängigkeit einzuführen (Projektprinzip: bewusst minimal gehalten):

```
Trafty.Core/Models/Nif/
  NifTransform.cs           Translation/Rotation/Scale + Gamebryo-Kompositionsregel
                             (Kind-Transform relativ zum Eltern-Node)
  NifSceneMesh.cs             Läuft den Szenengraph ab Root ab, akkumuliert Transforms bis zu
                               jedem NiTriShape, gibt alle Dreiecke in Weltkoordinaten zurück
  NifMeshPreviewRenderer.cs    Handgeschriebener Software-Rasterizer: feste 3/4-Rotation,
                               orthographische Projektion, Flat-Shading, Backface-Culling,
                               Painter's-Algorithm-Tiefensortierung (kein Z-Buffer)
```

An der echten `1struinedtemple.NIF` verifiziert: alle 40.554 Dreiecke aus den 68
`NiTriShapeData`-Blöcken werden über die 56 `NiNode`-Transforms korrekt im Weltkoordinatensystem
platziert (nicht am lokalen Ursprung kollabiert) und ergeben ein visuell kohärentes
Ruinen-Tempel-Modell (Mauern, Säulen, Wendeltreppen-Zylinder) — kein Rauschen, keine
zufällige Punktwolke.

**App-Integration:** Der NIF-Inspektor zeigt jetzt neben Vertex-/Dreieckszahl auch ein
gerendertes Vorschaubild, sobald der volle Block-Parser die Datei versteht.

**Update:** Interaktive Kamera (Klick+Ziehen dreht live) und echter Z-Buffer (statt
Painter's-Algorithm-Sortierung, die bei sich überschneidenden/nicht-konvexen Teilen
gelegentlich sichtbar falsch war) sind inzwischen dabei — siehe eigene Abschnitte unten.

**Weiterhin bewusst nicht umgesetzt:** Texturen werden nicht angewendet (reines
Flat-Shading nach Flächennormale).

## Modul B: Atmosphäre-Tweaker-UI (Pixel-Editor für `.col`-Tabellen)

`SystemColFile` kann jetzt auch schreiben: `SetPixel(x, y, r, g, b)` ändert einen Pixel im
Speicher (RGB565-quantisiert, wie beim Original), `Save(path)` schreibt den kompletten
128×66-Raster zurück — byte-identisches Format zum Original (kein Header, reine Rohdaten).

**App-Integration:** Im "Open Color Table…"-Panel ist die Vorschau jetzt klickbar — ein Klick
auf einen Pixel liest dessen aktuelle Farbe aus und zeigt sie in editierbaren R/G/B-Feldern
(0–255). "Apply" schreibt die neue Farbe ins Speicherbild und rendert die Vorschau sofort neu
(noch nicht auf Platte). "Save…" legt zuerst ein Backup an (`BackupVault`, Grundprinzip #2)
und schreibt dann die Datei. Die Klick-auf-Pixel-Zuordnung rechnet die Letterboxing-Skalierung
des `Stretch="Uniform"`-Bildes zurück auf die 128×66-Rasterkoordinaten.

**Bewusst nicht umgesetzt:** Kein Preset-Manager (Speichern/Laden benannter Farbschemata) und
keine automatische Zuordnung der vier Farbbänder zu Wetter-/Tageszeit-Zuständen — deren
Bedeutung ist weiterhin nicht bestätigt (siehe Abschnitt oben), ein Tweaker mit "Preset:
Regen" wäre also geraten statt verifiziert.

## Modul D: WAV gegen echte Client-Sounds verifiziert

Drei echte Sound-Effekte aus dem `sounds/`-Ordner (`agramon_die.wav`, `adrghit.wav`,
`adrghit3.wav`) liegen jetzt als Test-Fixtures bei. `WavHeader` parst alle drei korrekt:
16-bit PCM, Mono, 22050 Hz — plausible Werte für Sound-Effekte, byte-genaue
Dateigrößen-Kontrolle inklusive. Der bisherige offene Punkt ("noch keine echte
Client-`.wav`-Datei geprüft") ist damit erledigt.

## Modul D: UI-Fenster-Vorschau (`Trafty.Core/UI/`)

`DaocUiFile` liest die XML-Fensterdefinitionen des Clients (z. B. `chat_window.xml`,
`command_window.xml`) — reines XML, kein Reverse-Engineering nötig, aber mit einer echten
Eigenart: Element-Namen sind meist PascalCase, aber nicht durchgängig (`<width>`/`<height>`
kommt kleingeschrieben bei manchen Controls in derselben Datei vor, die woanders
`<Width>`/`<Height>` groß schreibt) — alle Lookups sind deshalb case-insensitive.

```
Trafty.Core/UI/
  DaocWindowTemplate.cs    Ein <WindowTemplate>: Name, Größe, Titelleiste, Tabs, Controls
  DaocControlDef.cs         Ein Control (ButtonDef, LabelDef, ...) — generisch: Kind +
                             Property-Bag, damit auch unbekannte Control-Typen nichts verlieren
  DaocUiFile.cs              Parst Root_Element -> Texture/ImageAreaTemplate/WindowTemplate*
  DaocWindowRenderer.cs      Schematische Vorschau: Fenster-Umriss, Titelleiste, farbige
                             Rechtecke pro Control mit bekannter Größe
```

Verifiziert an beiden echten Dateien: `chat_window.xml` (350×200, 5 Tabs, 8 Controls,
1 Textur, 2 ImageAreaTemplates) und `command_window.xml` (135×216, 23 Controls, davon
22 Buttons ohne explizite Größe — deren Größe kommt vom `TemplateName`, z. B.
"button_large", den wir nicht kennen).

**App-Integration:** Neuer Button "Open UI Window…" + Tab "UI Windows": links die
gerenderte Vorschau, rechts eine Liste aller Controls mit Label/ID/Position.

**Update:** Text-Rendering ist jetzt dabei (`SixLabors.ImageSharp.Drawing`, einzige zusätzliche
Abhängigkeit im ganzen Projekt neben ImageSharp selbst — Text ohne Font-Bibliothek ist praktisch
nicht machbar). Fenstername in der Titelleiste, Control-Label auf den Rechtecken. Sucht sich zur
Laufzeit eine verfügbare System-Schriftart (Segoe UI/Arial/DejaVu Sans/...); ist auf der
Zielmaschine keine Schrift installiert, wird Text einfach übersprungen statt abzustürzen.

**Bewusste Grenzen:** Keine echten Button-/Hintergrund-Texturen (der TGA-Decoder existiert
inzwischen — siehe unten — aber diese Vorschau komponiert die Texturen noch nicht in die
Fensterdarstellung). Controls ohne explizite Breite/Höhe (Größe kommt vom `TemplateName`)
werden nur als kleiner Positions-Marker gezeichnet, nicht als geratene Box — ehrlicher als
eine erfundene Größe darzustellen.

## Modul D: Sound-Player

`Trafty.App/Services/WavPlayer.cs` nutzt `winmm.dll` (Windows-Systembibliothek) per P/Invoke
für Async-Wiedergabe — bewusst kein neues NuGet-Paket, DAoC läuft ohnehin nur unter Windows.
"Open Sound…" lädt eine `.wav`, zeigt Header-Infos (Format/Kanäle/Samplerate/Dauer), "▶ Play"
/ "■ Stop" steuern die Wiedergabe. Auf anderen Plattformen bewusst deaktiviert
(`WavPlayer.IsSupported`) statt zu crashen.

## Modul A: DDS-Decoder + Thumbnail-Grid

`Trafty.Core.Textures.BlockDecompressor`/`DdsFile` dekodieren BC1/BC2-DDS zurück zu RGBA —
die Umkehrung des bereits vorhandenen Encoders, gegen echte Terrain-Patches aus `ter002.mpk`
verifiziert. Überraschender Fund dabei: echte Terrain-DXT3-Texturen haben **Alpha=0 im
gesamten Bild** — der Terrain-Renderer ignoriert Alpha offenbar komplett. `DdsExporter`
erzwingt deshalb standardmäßig volle Deckkraft beim PNG-Export, sonst wäre die
Thumbnail-Vorschau unsichtbar (transparent) trotz sichtbarer RGB-Daten.

**App-Integration:** Checkbox "Grid view" im "Archive Assets"-Tab schaltet zwischen
Textliste und einem Thumbnail-Raster um (nur `.dds`-Einträge bekommen ein Vorschaubild,
alles andere zeigt eine Namens-Kachel ohne Bild statt zu verschwinden).

## Extrahieren (einzeln + gesamt)

Neue Buttons: **"Extract All…"** in der Toolbar (nur aktiv bei geöffnetem Archiv) entpackt
alle Einträge in einen gewählten Ordner (`MpkArchive.ExtractAll`, bereits vorhanden in Core).
**"Extract Selected…"** im Detail-Panel entpackt nur den aktuell ausgewählten Eintrag an
einen frei wählbaren Zielpfad (Speichern-Dialog).

## Oberflächen-Redesign

Dunkles Gold/Anthrazit-Theme (angelehnt an aldhran-server.eu): zentrale Farb-Ressourcen in
`App.axaml` (`TraftyGold`, `TraftyBg*`, `TraftyBorder`, ...), globale Styles für
Button/TabItem/ListBox/CheckBox. Neuer Header-Banner mit "TRAFTY"-Wortmarke (goldene
Versalien, Letter-Spacing) + "About"-Button. `AboutWindow.axaml` zeigt Versionsinfo,
Support-Link und einen Hinweis, dass Client-Modding gegen die EA-Nutzungsbedingungen
verstoßen kann und auf eigene Gefahr erfolgt.

## Modul C: Interaktive 3D-Kamera

Die 3D-Vorschau war bisher ein statisches Bild mit fest einprogrammierter Rotation.
`NifMeshPreviewRenderer.Render`/`SaveAsPng` nehmen jetzt `rotationYDegrees`/
`rotationXDegrees` als Parameter (Default bleibt die alte feste Ansicht, keine
Verhaltensänderung für bestehende Aufrufe). App: Klick-und-Ziehen auf das Vorschaubild
dreht das Modell live — ein voller Drag über die Bildbreite/-höhe entspricht ~180°.
Render-Zeit fürs größte Testmodell (40.554 Dreiecke) liegt bei ~90ms/Frame — nicht
butterweich, aber für eine Vorschau ausreichend flüssig.

## Modul D: TGA-Decoder für echte UI-Texturen

`Trafty.Core.Images.TgaFile` liest `.tga` — öffentlich dokumentiertes Format, byte-genau
gegen die echte `emoticons.tga` (aus `chat_window.xml` referenziert) verifiziert: 256×128,
32bpp unkomprimiert BGRA, Bottom-Left-Origin (Zeilen bottom-to-top gespeichert, hier auf
Top-Down geflippt), TGA-2.0-Footer-Signatur "TRUEVISION-XFILE." am Dateiende exakt getroffen.
Nur diese eine gefundene Variante ist implementiert — RLE-Komprimierung, 24bpp,
paletted Bilder werden bewusst mit Fehler abgelehnt statt geraten.

**App-Integration:** Neuer Button "Open Texture…" öffnet `.tga`- oder `.dds`-Dateien direkt
von der Platte (nicht aus einem Archiv) und zeigt sie an — für lose UI-Texturen wie
`atlantis/emoticons.tga`, die von UI-XML-Fenstern referenziert werden. Bei `.dds` bleibt hier
(anders als beim Terrain-Thumbnail) der echte Alpha-Kanal erhalten, da UI-Sprites
Transparenz brauchen.

## Vergleich mit DAoC MPAK Package Manager (DOL-Projekt)

Quellcode + Binary des existierenden "DAoC MPAK Package Manager" (Delphi, ursprünglich vom
Dawn-of-Light-Projekt) geprüft, um sicherzustellen, dass Trafty mindestens dieselben
Kernfunktionen bietet:

| Feature | DOL-Tool | Trafty |
|---|---|---|
| Archiv öffnen | ✓ | ✓ |
| Extract Selected / Extract All | ✓ | ✓ |
| Add Files (beliebige Dateien ins Archiv) | ✓ | ✓ (neu, siehe unten) |
| Save/Save As | ✓ | ✓ (Backup Vault übernimmt "Save"-Semantik) |
| "Preview" | nur `ShellExecute` auf temp. Datei — kein eigenes Rendering | echtes eingebautes Rendering: DDS/PCX/TGA/COL/NIF-3D/Zonenkarte/UI-Fenster |

Als Nebeneffekt bestätigt der Pascal-Quellcode unabhängig unsere MPAK-Reverse-Engineering-
Ergebnisse (XOR-Maskierung, Directory-Feldlayout, CRC32 auf komprimierten Daten) — exakte
Übereinstimmung mit dem, was wir bereits an `ter002.mpk` verifiziert hatten.

**Nachgezogen:** "Add Files…"-Button (Core konnte das schon über `MpkArchiveWriter.WriteReplacing`,
nur die App-Oberfläche hatte noch keinen generischen Button dafür — bisher nur DDS-Ersetzen per
Drag&Drop). Backup Vault wird dabei automatisch genutzt.

## Backup-Sicherheit (Grundprinzip #2, expliziter gemacht)

War technisch schon vollständig (jeder Schreibvorgang sichert vorher, nichts wird je gelöscht),
aber jetzt sichtbarer für den Nutzer:
- Der **älteste** Backup-Eintrag einer Datei ist immer der unberührte Originalzustand ("Vanilla")
  — wird in der Backup-Liste jetzt explizit als "(Original)" markiert.
- Neuer **"Backup Now"**-Button: manuell sichern, auch ohne dass gerade etwas geändert wird
  (z. B. direkt nach dem Öffnen eines frischen Archivs).
- `MpkArchiveWriter.WriteReplacing` hatte bisher **keine Core-Tests** — nachgeholt
  (`MpkArchiveWriterTests.cs`: neue Datei anhängen, bestehende überschreiben, Ergebnis-Archiv
  verifizieren).

## Nächster Schritt

Größter offener Punkt: Preset-Manager für Modul B, sobald die Bedeutung der vier
Farbbänder in `.col`-Dateien geklärt ist (aktuell weiterhin nur Hypothese).
