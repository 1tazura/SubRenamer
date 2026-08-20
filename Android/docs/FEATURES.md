# Feature inventory

This document is the source of truth for **feature parity claims** between the upstream desktop application and the Android port.

The Android port should not be described as a full UI port. It reuses the upstream matching Core inside a mobile-specific storage and safety workflow.

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
| Manual matching mode | ✅ | ✅ | Android exposes desktop-compatible `$$` key marker + `*` wildcard rules and converts them to Core regex options. |
| Regex matching mode | ✅ | ✅ | Android passes user video/subtitle regex directly through `MatcherOptions`; capture group 1 is the matching key. |
| Manual matching rule editor | ✅ | 🟡 | Android has a compact rule-entry UI but not the desktop sample-file tester/editor. |
| Regex editor / tester | ✅ | 🟡 | Android has direct regex entry + validation but not the full desktop test editor. |
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

The Android port now exposes all three Core episode-matching paths used by the desktop application: automatic Diff, manual-rule-derived regex, and direct Regex. The remaining major recovery gap is **per-item correction/exclusion after a match result is generated**.

The next important parity work is therefore:

1. per-item correction/exclusion;
2. language filtering and output naming settings;
3. a proper settings surface;
4. richer rule-testing UX only if the compact matching controls prove insufficient.

The goal is not literal desktop parity. Desktop-only interaction patterns should remain upstream-specific, while Android-specific safety and automation may intentionally exceed upstream behavior.
