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

## Hit policy

A cached result is reused only when all of the following match the current file:

1. storage identity;
2. byte size;
3. last-modified time.

If size or modification time is missing, unavailable, or the metadata call fails, the cache is not trusted and the archive is opened again. Cache read/write failures are also treated as optimization failures only; scanning falls back to the normal eager path.

New or changed archives are therefore fully validated exactly as before. Automatic work attribution continues to receive the same real archive-entry names it would receive from a full eager rescan.

## Download authorization changes

The cache belongs to the currently authorized Download tree. When the persisted SAF Download bookmark changes, the archive-index cache is cleared before the new bookmark is saved. The first scan under the new authorization is therefore a cold eager scan and builds a new cache snapshot.

Re-authorizing the exact same persisted bookmark does not clear the cache.

## Persistence lifecycle

Each completed scan writes one coherent cache snapshot containing only archives that were successfully validated or reused during that scan. Archives removed from `Download` are naturally pruned. Failed validations and files that do not expose usable metadata are not retained as reusable entries, so they are retried on the next scan.

Source archives themselves are never modified or removed by this cache.

## Performance diagnostics

The scan performance text reports archive work as:

- total archive count;
- cache hits;
- validation failures;
- successful actual re-indexes.

A failed archive is now named in the same diagnostic text together with a compact exception type/message. This closes a previous accounting gap where a failed `ListSubtitleEntriesAsync()` call could be swallowed and the archive would appear in the total count but in neither the cache-hit nor re-index count.

For example, a warm scan may report:

```text
共 25 包，缓存命中 24 包，索引失败 1 包 [broken-pack.rar: InvalidOperationException: ...]，成功重索引 0 包
```

The accounting invariant for a completed scan is therefore:

```text
total = cache hits + successful re-indexes + failed validations
```

Metadata absence is not itself a validation failure. It disables cache reuse for that file and forces a normal full archive inspection; a successful inspection still counts as a re-index.

## Top-level scan scheduling

Starting in v0.1.19, the public video-target scan and subtitle-source scan each dispatch their core SAF work to a separate worker. The UI still starts both operations together and waits with `Task.WhenAll`, but synchronous provider/IPC work performed before an `await` can no longer pin both scan chains to the same worker.

This change does **not** increase `ArchiveScanConcurrency`: archive validation remains bounded at 2. The purpose is only to allow the video traversal and subtitle-source scan to overlap when the Android storage provider permits it.

Real-device timing remains the authority. Android `DocumentsProvider` implementations may still serialize requests internally, so concurrency is not assumed to guarantee a speedup.

## Real-device baseline before v0.1.19

On the measured 25-archive workload, v0.1.18 showed:

- cold scan: archive indexing about 5303 ms, total about 7762 ms;
- immediate warm scan: archive indexing about 422 ms, total about 2797 ms;
- warm-cache accounting: 24 cache hits out of 25 archives.

The reduction from about 5.3 s to about 0.42 s validates the persistent cache itself. The 25-versus-24 accounting mismatch motivated the explicit failure diagnostics added in v0.1.19.

## Version history

- **v0.1.17** introduced persistent positive/negative archive index caching while preserving eager validation.
- **v0.1.18** completed cache invalidation on Download bookmark changes, added cache-focused tests, and surfaced hit/re-index counts.
- **v0.1.19** gives video/subtitle scans independent workers and makes archive validation failures visible and fully accounted for.
