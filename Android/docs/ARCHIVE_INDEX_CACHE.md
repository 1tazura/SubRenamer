# Archive index cache

The Android port keeps **eager archive validation** as the source-discovery contract. A `.zip`, `.7z` or `.rar` file is not considered a subtitle source merely because of its filename: on a cold scan it is opened and its real entries are enumerated. Only archives that actually contain supported subtitle entries are surfaced to attribution and matching.

The persistent archive-index cache reduces repeated work without weakening that rule.

## Cached data

After a successful archive inspection the app stores:

- the storage identity (`IStorageFile.Path`, normally the SAF document URI);
- byte size;
- last-modified time, normalized to UTC ticks;
- the complete validated `SubtitleEntryRef` list (`Key`, display name, extension and size when available).

An empty entry list is a **negative cache entry**: the archive was fully opened and was confirmed to contain no supported subtitles.

Starting in v0.1.20, the same signature can also store one deliberately narrow **stable rejection**: SharpCompress `ArchiveOperationException` whose message starts with `Cannot determine compressed stream type`. This means the unchanged bytes were actually tried once and cannot be parsed as a supported archive stream, even though the filename ends in `.zip`, `.7z` or `.rar`.

Stable rejection is intentionally not a general exception cache. Permission errors, provider failures, I/O failures, cancellation, truncated reads and other potentially transient errors are retried on the next scan.

## Hit policy

A cached result is reused only when all of the following match the current file:

1. storage identity;
2. byte size;
3. last-modified time.

If size or modification time is missing, unavailable, or the metadata call fails, the cache is not trusted and the archive is opened again. Cache read/write failures are also treated as optimization failures only; scanning falls back to the normal eager path.

New or changed archives are therefore fully validated exactly as before. Automatic work attribution continues to receive the same real archive-entry names it would receive from a full eager rescan.

For a stable rejection, changing either size or modification time invalidates the rejection and forces a real parse attempt again. If usable metadata is unavailable, the rejection is not cached at all.

## Download authorization changes

The cache belongs to the currently authorized Download tree. When the persisted SAF Download bookmark changes, the archive-index cache is cleared before the new bookmark is saved. The first scan under the new authorization is therefore a cold eager scan and builds a new cache snapshot.

Re-authorizing the exact same persisted bookmark does not clear the cache.

## Persistence lifecycle

Each completed scan writes one coherent cache snapshot containing only archives that were successfully validated/reused or deliberately stable-rejected under a complete signature. Archives removed from `Download` are naturally pruned. Transient failures and files that do not expose usable metadata are not retained as reusable entries, so they are retried on the next scan.

Source archives themselves are never modified or removed by this cache.

## Performance diagnostics

The scan performance text reports archive work as:

- total archive count;
- normal validated cache hits;
- stable exclusions and how many were reused from cache;
- transient validation failures;
- successful actual re-indexes.

The completed-scan accounting invariant is:

```text
total = validated cache hits + successful re-indexes + stable exclusions + transient failures
```

For the real-device case that motivated v0.1.20, the 25th `.zip` was:

```text
支付宝交易明细(20251213-20260313).zip
ArchiveOperationException: Cannot determine compressed stream type.
```

v0.1.19 correctly surfaced this instead of silently losing it from the counts. v0.1.20 attempts it once under the current signature, remembers the deterministic unsupported-stream result, and later reports it as a cached stable exclusion rather than opening it again every warm scan.

Metadata absence is not itself a validation failure. It disables cache reuse for that file and forces a normal full archive inspection; a successful inspection still counts as a re-index.

## Download-root scheduling

v0.1.19 proved on the measured device that putting Torrent traversal and subtitle scanning on independent workers produced real overlap: with a warm archive cache the scan fell from about 2797 ms to about 2134 ms.

That measurement also showed both branches independently enumerating the same `Download` root at roughly the same time. v0.1.20 removes that duplicate provider work. The main UI scan now:

1. enumerates direct children of `Download` once;
2. captures the `Torrent` folder plus archive/loose-subtitle candidates from that one cursor;
3. runs Torrent **subtree** traversal and archive indexing on independent workers;
4. joins them before work attribution.

`ArchiveScanConcurrency` remains 2. The shared-root change reduces duplicated SAF/provider work; it does not weaken eager validation or increase archive fan-out.

Real-device timing remains the authority because Android `DocumentsProvider` implementations can serialize or contend internally.

## Version history

- **v0.1.17** introduced persistent positive/negative archive index caching while preserving eager validation.
- **v0.1.18** completed cache invalidation on Download bookmark changes, added cache-focused tests, and surfaced hit/re-index counts.
- **v0.1.19** gave video/subtitle scans independent workers and made archive validation failures visible and fully accounted for.
- **v0.1.20** adds conservative stable-rejection caching for the observed unsupported-stream case and replaces the two competing `Download` root enumerations with one shared snapshot before parallel downstream work.
