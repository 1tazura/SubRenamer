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
- actual re-indexes.

For example, a warm scan of 25 unchanged archives may report `共 25 包，缓存命中 25，实际重索引 0`. A first scan or a scan after changing Download authorization should report zero cache hits and re-index the archives normally.

## Version history

- **v0.1.17** introduced persistent positive/negative archive index caching while preserving eager validation.
- **v0.1.18** completes the acceptance contract by clearing cache state when the Download bookmark changes, adding cache-focused tests, and surfacing hit/re-index counts in scan diagnostics.
