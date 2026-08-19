# Architecture and safety boundary

## 1. Discovery

`ScanService` recursively discovers *directories that directly contain videos* below `Download/Torrent`.
Every target retains its own `IStorageFolder` and direct video list.

The root is never flattened into one global video collection.

Subtitle sources are scanned only from direct children of `Download` in v1:
- each zip/7z/rar is one source;
- loose subtitle files are grouped conservatively by a normalized title signature.

## 2. Work-level attribution

`AttributionService` ranks:

`subtitle source -> one VideoTarget`

Evidence:
- source/archive filename vs torrent-directory title tokens;
- common archive-entry tokens vs common video-filename tokens;
- episode-set overlap at low weight only.

A low score or a small winner margin does **not** trigger automatic placement. The UI asks for one target selection.

## 3. Episode-level mapping

After work-level attribution, only these two local filename sets are sent to original SubRenamer.Core:

- direct video filenames from the selected target directory;
- subtitle filenames from the selected source.

The Android shell does not replace the upstream `diff → extract → mapping` algorithm.

## 4. Plan

`PlanBuilder` converts matched rows to destination filenames.

- normal case: `video basename + subtitle extension`;
- one video + several different subtitle extensions: each exact basename is safe;
- several subtitles with the same extension: a recognized language suffix (`.chs`, `.cht`, `.en`, `.ja`) is preserved;
- if a unique suffix cannot be determined, the item is a conflict and is not written.

Existing target files are conflicts and are not overwritten.

## 5. Apply

`ApplyService` creates only subtitle files in the already-existing target folder.

It never calls `MoveAsync`, `DeleteAsync`, or rename-like operations on a video.
Archive sources are spooled to app-private temporary storage only to give SharpCompress a seekable stream; no temporary directory is created under the torrent.

Source archives and source loose subtitles are retained.
