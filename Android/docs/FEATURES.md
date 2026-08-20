# Feature inventory

This document is the source of truth for **feature parity claims** between the upstream desktop application and the Android port.

The Android port should not be described as a full UI port. It currently reuses the upstream matching Core inside a mobile-specific storage and safety workflow.

Legend:

- ✅ implemented on Android
- 🟡 partially implemented / equivalent behavior with different UX
- ❌ not implemented on Android
- ➖ intentionally not planned as a direct port

## Matching and editing

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| Automatic diff matching | ✅ | ✅ | Android calls the original `SubRenamer.Core.Matcher.Execute`. |
| One-to-many / multilingual mapping | ✅ | ✅ | Android preserves recognized language tags when multiple same-extension subtitles map to one video. |
| Preview planned filenames | ✅ | ✅ | Android previews exact source → destination operations. |
| Manual matching mode | ✅ | ❌ | High-priority gap. This is episode-level matching, distinct from Android's torrent-target selection. |
| Regex matching mode | ✅ | ❌ | Core supports options; Android lacks configuration/editor UI. |
| Manual matching rule editor | ✅ | ❌ | High-priority gap. |
| Regex editor / tester | ✅ | ❌ | Medium/high priority. |
| Per-item match correction | ✅ | ❌ | High-priority mobile escape hatch for SP/OVA/NCOP and unusual packs. |
| Delete/edit imported rows | ✅ | ❌ | Should be redesigned for touch instead of copied literally. |

## Import, discovery and attribution

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| Open arbitrary files/folders | ✅ | ❌ | Android currently uses a specialized `Download` workflow. |
| Drag and drop | ✅ | ➖ | Desktop interaction; not useful as a primary Android flow. |
| Torrent-safe recursive target discovery | ❌ | ✅ | Fork-specific Android feature. |
| Subtitle source discovery from `Download` root | ❌ | ✅ | Fork-specific Android feature. |
| Source → torrent work attribution | ❌ | ✅ | Fork-specific layer above episode mapping; ambiguous results require user selection. |
| ZIP / 7z / RAR source archives | not the same workflow | ✅ | Android reads archive entries and extracts only selected subtitles. |

## Output and safety

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| Batch rename/copy | ✅ | ✅ | Android only creates subtitle outputs beside existing videos; videos are never altered. |
| Preserve source subtitles | conditional | ✅ | Android always preserves source archive/loose subtitles. |
| Subtitle backup | ✅ | ➖ | Upstream needs backup for in-place rename. Android never edits source subtitles, so direct parity is unnecessary. |
| Existing destination protection | ✅ | ✅ | Android never overwrites existing destination subtitles. |
| One-click undo | ❌ | ✅ | Fork-specific. Last Android batch is journaled and content-verified before deletion. |
| Multi-level operation history | ❌ | ❌ | Candidate future Android enhancement. |

## Language and output naming

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| Language filtering | ✅ | ❌ | High-value gap for packs containing several subtitle languages. |
| Keep language suffix | ✅ | 🟡 | Android currently detects common tags such as CHS/CHT automatically where required by one-to-many output. No setting UI yet. |
| Custom appended suffix | ✅ | ❌ | Worth porting through settings. |
| Custom video/subtitle extensions | ✅ | ❌ | Lower priority. |
| Conflict filtering UX | ✅ | 🟡 | Android has strict no-overwrite/conflict reporting but not the desktop import conflict dialog. |

## Subtitle synchronization

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| FFsubsync / FFmpeg automatic sync | ✅ | ❌ | Significant future feature; requires Android-specific binary/runtime design. |
| Sync selected item only | ✅ | ❌ | Depends on the same subsystem. |

## Application UX

| Capability | Upstream desktop | Android | Notes |
|---|---:|---:|---|
| Settings UI | ✅ | ❌ | High-priority structural gap. |
| Multiple UI languages | ✅ | ❌ | Android text is currently Chinese-first/hard-coded. |
| Light/dark theme support | ✅ | 🟡 | Avalonia follows platform basics; no Android preference UI yet. |
| Keyboard shortcuts | ✅ | ➖ | Not a mobile priority. |
| Window always-on-top | ✅ | ➖ | Desktop-only concept. |
| Reveal file in file manager | ✅ | ❌ | Possible on Android but low priority. |
| Copy Linux rename commands | ✅ | ➖ | Low-value desktop power-user action on Android. |
| Update checking | ✅ | ❌ | Defer until Android distribution/release flow stabilizes. |

## Interpretation

The Android port has completed the **automatic-match + safe-placement** path, but most of the upstream toolset for **recovering when automatic matching is insufficient** is still absent.

The most important parity work is therefore:

1. per-item correction/exclusion;
2. Manual and Regex matching modes;
3. language filtering and output naming settings;
4. a proper settings surface.

The goal is not literal desktop parity. Desktop-only interaction patterns should remain upstream-specific, while Android-specific safety and automation may intentionally exceed upstream behavior.
