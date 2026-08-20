# Processing / apply performance

The scan path and the actual subtitle-placement path are intentionally measured separately. A fast scan does not imply that creating many SAF files, extracting archive entries and writing them beside torrent videos will also be fast.

## v0.1.20 instrumentation

After **确认处理**, the preview now reports cumulative timings for:

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

## Low-risk optimizations included with the instrumentation

v0.1.20 also applies two changes that do not alter output semantics:

1. subtitle copy/hash and non-seekable archive staging use a 256 KiB buffer instead of the smaller/default buffers, reducing provider/native read/write call count;
2. the explicit `FlushAsync` immediately before `DisposeAsync` on every destination subtitle was removed. Closing the output stream remains mandatory and is timed directly; the close/dispose operation is the commit boundary.

SHA-256 remains one-pass during the exact bytes written to the destination. Undo still records that fingerprint and will only delete an unchanged file created by the app.

## What is deliberately not optimized yet

No blind concurrency was added to subtitle writes. In particular:

- multiple `CreateFileAsync` calls are not fanned out yet;
- entries from one SharpCompress archive are not extracted concurrently;
- separate archive sessions are not opened merely to parallelize extraction;
- solid 7z handling is not changed.

Those changes can improve some workloads but can also increase DocumentsProvider contention, multiply archive reads, or interact badly with solid compression. The v0.1.20 phase timings are intended to identify whether the measured device is dominated by destination creation/open/close, archive preparation, or transfer/decompression before choosing the next optimization.

## Safety invariants

Processing performance work must continue to preserve:

- video paths, names and bytes are never changed;
- source subtitles/archives are retained;
- existing destination subtitles are not overwritten;
- partial outputs from failed items are deleted best-effort;
- undo only targets files created by the app and verifies their SHA-256 before deletion.
