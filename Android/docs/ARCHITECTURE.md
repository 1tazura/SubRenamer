# Android architecture and safety boundary

The Android port is a mobile shell around the original `SubRenamer.Core` matcher plus Android-specific storage discovery, work attribution, planning, placement and undo.

The design intentionally separates **which work/source belongs together** from **which episode maps to which subtitle**.

## 1. Storage authorization

The app requests `/storage/emulated/0/Download` through Android SAF / Avalonia storage APIs.

The selected folder is bookmarked and restored on later launches. The implementation does not depend on converting SAF content URIs into real filesystem paths.

On Android ExternalStorageProvider, Avalonia's item enumeration already returns document ids but reading `IStorageItem.Name` may issue a separate metadata query for each item. The mobile shell therefore derives display names from the `com.android.externalstorage.documents` document URI when possible and falls back to `IStorageItem.Name` on other providers/platforms. This is only a metadata/scan optimization; files are still opened, created and deleted through the authorized SAF objects.

## 2. Video-target discovery

`ScanService` recursively discovers directories below `Download/Torrent`.

A `VideoTarget` is exactly a physical folder that directly contains one or more supported video files. The hierarchy is not flattened into one global video list.

This matters because torrent directory boundaries are semantically meaningful and because the app must not reorganize a seeding torrent.

Folder enumeration uses **small bounded concurrency** to hide provider/storage latency without flooding Android `DocumentsProvider` with unbounded cursors/queries. Names discovered during enumeration are reused rather than repeatedly querying Android metadata.

## 3. Subtitle-source discovery

Only direct children of `Download` are treated as subtitle sources in the current workflow:

- each `zip`, `7z`, or `rar` containing subtitle entries is one archive source;
- loose subtitle files are grouped conservatively by normalized filename signature.

Archive indexing also uses bounded concurrency. SharpCompress handles archive formats. The current eager archive indexing remains measurable separately so it can be replaced by lazy indexing if it remains a significant cost after SAF name-query removal.

If a SAF source stream is not seekable, the archive may be spooled to **app-private temporary storage** solely to provide a seekable stream. Nothing temporary is created inside the torrent directory.

## 4. Work-level attribution

`AttributionService` ranks:

```text
subtitle source -> one VideoTarget
```

Evidence includes:

- source/archive filename tokens vs torrent-directory title tokens;
- common archive-entry tokens vs video filename tokens;
- episode-set overlap at low weight only.

Episode overlap alone is never enough to establish a work relationship.

When confidence or winner margin is too low, Android requires one manual torrent-target selection rather than silently placing subtitles into a weak guess.

This selection is **not** the upstream desktop "manual matching mode". It only chooses the work/torrent directory. Episode mapping remains a separate layer.

## 5. Episode-level mapping

After work attribution, the Android shell passes only:

- direct video filenames from the selected target directory;
- subtitle display filenames from the selected source.

`SubRenamerCoreBridge` is a typed adapter around the original `SubRenamer.Core.Matcher.Execute` call. Android exposes three episode-matching modes without changing `SubRenamer.Core`:

- **Diff**: call Core with empty `MatcherOptions`, preserving upstream automatic diff/extract behavior;
- **Manual**: translate desktop-style patterns where `$$` marks the matching key and `*` is a wildcard into regular expressions, then pass them as `MatcherOptions.VideoRegex` / `SubtitleRegex`;
- **Regex**: validate user regex and pass it directly through the same `MatcherOptions`; capture group 1 is the key because that is what the upstream Core reads.

The Android shell does not reimplement upstream episode mapping. It only prepares the options accepted by Core and forwards the result into the same planning layer.

Matching mode and rule text are persisted in app-private settings and do not alter the authorized storage tree.

## 6. Plan generation

`PlanBuilder` converts matched rows into destination subtitle filenames.

Rules currently include:

- normal case: `video basename + subtitle extension`;
- multiple different subtitle extensions may coexist on the same video basename;
- multiple subtitles with the same extension require a recognized unique language tag (for example `chs`, `cht`, `en`, `ja`), which is appended before the extension;
- ambiguous duplicate source names or duplicate destination names become conflicts rather than writes;
- existing destination files are never overwritten.

Android SAF directory enumeration is comparatively expensive, so the target directory is snapshotted once into an in-memory filename set for planning instead of re-enumerating the same folder for every destination.

## 7. Apply

`ApplyService` creates subtitle files only inside the already-existing selected `VideoTarget` folder.

Before applying, the target directory is snapshotted again to protect against files created after preview without performing one full SAF enumeration per planned subtitle.

Archive sources are opened once for the batch. Archive entries are indexed by key so each subtitle lookup does not linearly rescan the archive entry collection.

The service never performs video move/rename/delete operations.

Source archives and loose source subtitles remain intact.

## 8. Undo journal

Android adds a one-level undo operation that upstream does not expose as a one-click command.

For every subtitle successfully created by an apply batch, the app records:

- target relative path;
- destination filename;
- SHA-256 of the exact bytes written.

The record is persisted in app settings so undo survives an app restart.

`UndoService` resolves the recorded target directory, reopens each recorded destination and recomputes SHA-256 before deletion.

A file is deleted only when its current content still matches the app-created content. If the file was edited or replaced after creation, it is preserved and reported as changed.

Undo never targets:

- video files;
- source subtitle archives;
- loose source subtitle files;
- unrelated pre-existing destination files.

A later successful processing batch replaces the previous one-level undo journal.

## 9. UI / execution boundary

Long storage and archive operations run away from the Avalonia UI thread. This was required to avoid Android ANRs during SAF scanning and processing.

The main screen currently orchestrates:

```text
authorize Download
  -> scan targets/sources
  -> choose source and target attribution
  -> choose Diff / Manual / Regex episode matching
  -> build Core preview
  -> apply subtitle outputs
  -> optional undo
```

The current matching-mode UI is intentionally compact. It exposes Core functionality but does not yet reproduce the desktop sample-file rule testers or per-item editor.

## 10. Hard safety invariants

The following should be treated as architectural constraints, not casual preferences:

1. Torrent video files are read-only inputs.
2. Existing destination subtitles are not overwritten.
3. Source subtitle material is retained.
4. No extra organization folder is created inside a torrent target.
5. Weak work attribution requires user confirmation.
6. Episode matching remains delegated to upstream Core unless explicitly reviewed otherwise.
7. Undo deletes only content-verified outputs created by Android.
