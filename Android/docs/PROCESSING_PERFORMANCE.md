# Processing / apply performance

The scan path and the actual subtitle-placement path are intentionally measured separately. A fast scan does not imply that creating many SAF files, extracting archive entries and writing them beside torrent videos will also be fast.

## v0.1.20 instrumentation

After **确认处理**, the preview reports cumulative timings for:

- target-directory recheck: one SAF snapshot used to preserve no-overwrite behavior;
- source preparation: opening the source archive and, when a SAF stream is not seekable, staging that archive into app-private temporary storage;
- destination creation: `CreateFileAsync` for every output subtitle;
- destination open: opening each created SAF file for writing;
- source open: opening loose subtitle sources (archive entry opening/decompression is included in transfer time);
- transfer / decompression / SHA-256: the one-pass read → hash → write loop;
- destination close: closing each SAF output stream, which is treated as the commit boundary;
- total bytes written;
- total `ApplyService` time;
- undo-journal persistence time;
- wall-clock time from the apply click until the result is ready.

The cumulative phase times are diagnostic counters, not a promise that every millisecond belongs exclusively to one subsystem. In particular archive decompression work may occur while the entry stream is being read, so it is intentionally counted under transfer.

## v0.1.20 real-device result

A 50-subtitle archive apply writing 22,064,348 bytes measured:

- target-directory recheck: 89 ms;
- source preparation: 125 ms;
- destination creation: 6333 ms;
- destination open: 1708 ms;
- transfer / decompression / SHA-256: 2546 ms;
- destination close: 58 ms;
- `ApplyService` total: 10,872 ms;
- undo-journal persistence: 26 ms;
- click-to-complete wall clock: 10,912 ms.

Destination creation alone was about 58% of apply time. Creation plus destination opening accumulated about 8.0 seconds, making SAF destination preparation the dominant actionable bottleneck. Transfer/decompression/SHA was secondary at about 2.5 seconds.

## Low-risk optimizations in v0.1.20

v0.1.20 applied two changes that do not alter output semantics:

1. subtitle copy/hash and non-seekable archive staging use a 256 KiB buffer instead of the smaller/default buffers, reducing provider/native read/write call count;
2. the explicit `FlushAsync` immediately before `DisposeAsync` on every destination subtitle was removed. Closing the output stream remains mandatory and is timed directly; the close/dispose operation is the commit boundary.

SHA-256 remains one-pass during the exact bytes written to the destination. Undo still records that fingerprint and will only delete an unchanged file created by the app.

## v0.1.21 bounded destination-preparation pipeline

The v0.1.20 measurement makes destination preparation sufficiently clear to optimize without guessing.

v0.1.21 keeps archive extraction and actual subtitle transfer **strictly sequential**, but prepares a small look-ahead window of destination files concurrently:

- up to 4 destination subtitles are being created/opened ahead of the current transfer;
- after one prepared destination is consumed, the next preparation starts before the current subtitle is copied;
- this lets provider latency from `CreateFileAsync` / `OpenWriteAsync` overlap with archive extraction, hashing and writing;
- at most the bounded look-ahead window can exist as created-but-not-yet-written files;
- cancellation or an unexpected outer failure drains that window, closes its streams and deletes those unwritten files best-effort;
- a provider-returned filename that differs from the exact previewed destination is rejected and deleted rather than silently accepting an auto-renamed collision.

No extraction concurrency is introduced. A single SharpCompress archive session is still consumed in sequence, avoiding unsafe shared-session access and avoiding repeated work for solid 7z archives.

Because creation/open operations now overlap, their displayed timings are explicitly labelled **cumulative**. A new `等待目标就绪` counter measures how long the sequential consumer actually stalls waiting for the look-ahead pipeline. The useful success signal is therefore a much lower Apply wall-clock time and a small destination-ready wait, even if cumulative provider call time remains numerically large.

## v0.1.21 real-device acceptance

The same 50-subtitle workload, again writing 22,064,348 bytes, measured after the bounded preparation pipeline:

- target-directory recheck: 102 ms;
- source preparation: 144 ms;
- destination creation cumulative: 3914 ms;
- destination open cumulative: 858 ms;
- destination-ready wait: 538 ms at concurrency 4;
- transfer / decompression / SHA-256: 907 ms;
- destination close: 27 ms;
- `ApplyService` total: 1728 ms;
- undo-journal persistence: 22 ms;
- click-to-complete wall clock: 1762 ms.

The wall-clock apply time therefore fell from 10,872 ms to 1,728 ms on this representative workload, while all 50 outputs still completed successfully. The large cumulative create/open counters are expected under overlap; only 538 ms of destination preparation remained visible to the sequential consumer.

This is the stop point for generic destination-concurrency tuning. Raising the look-ahead above 4 might shave part of the remaining 538 ms, but the maximum possible gain is now small relative to the added provider pressure and the larger number of empty pre-created files that would need cleanup after cancellation or process loss.

The largest remaining measured phase is real transfer/decompression/SHA work at about 0.9 s for roughly 22 MB. That path already performs one-pass SHA-256 over the exact bytes written and uses a 256 KiB buffer. Generic parallel extraction is deliberately not introduced because one SharpCompress session is shared and solid 7z workloads can regress badly when entries are treated independently.

Further performance work should therefore be workload-specific rather than unconditional:

- investigate solid-7z batch/streaming extraction only when a real solid-7z source is measurably slow;
- investigate an Android-native document-creation path only if a different provider/device again shows substantial destination-ready wait;
- otherwise treat scan/apply performance as accepted and prioritize functionality.

## Safety invariants

Processing performance work must continue to preserve:

- video paths, names and bytes are never changed;
- source subtitles/archives are retained;
- existing destination subtitles are not overwritten;
- exact previewed destination names are not silently changed by provider collision handling;
- partial or merely pre-created outputs from failed/cancelled items are deleted best-effort;
- undo only targets files created by the app and verifies their SHA-256 before deletion.
