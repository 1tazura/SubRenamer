# Processing / apply / undo performance

The scan path, actual subtitle-placement path and safe undo path are measured separately. A fast scan or apply does not imply that SAF verification/deletion will also be fast.

## Apply instrumentation

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

## v0.1.20 real-device apply result

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

v0.1.20 also moved subtitle copy/hash and non-seekable archive staging to 256 KiB buffers and removed a redundant explicit `FlushAsync` immediately before output-stream disposal. Close/dispose remains the required commit boundary.

## v0.1.21 bounded destination-preparation pipeline

v0.1.21 keeps archive extraction and actual subtitle transfer **strictly sequential**, but prepares a small look-ahead window of destination files concurrently:

- up to 4 destination subtitles are created/opened ahead of the current transfer;
- after one prepared destination is consumed, the next preparation starts before the current subtitle is copied;
- provider latency from `CreateFileAsync` / `OpenWriteAsync` can overlap with archive extraction, hashing and writing;
- at most the bounded look-ahead window can exist as created-but-not-yet-written files;
- cancellation or an unexpected outer failure drains that window, closes its streams and deletes those unwritten files best-effort;
- a provider-returned filename that differs from the exact previewed destination is rejected and deleted rather than silently accepting an auto-renamed collision.

No extraction concurrency is introduced. A single SharpCompress archive session is still consumed in sequence, avoiding unsafe shared-session access and avoiding repeated work for solid 7z archives.

Because creation/open operations overlap, their displayed timings are labelled **cumulative**. `等待目标就绪` measures how long the sequential consumer actually stalls waiting for the look-ahead pipeline.

## v0.1.21 real-device apply acceptance

The same 50-subtitle workload, again writing 22,064,348 bytes, measured:

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

The wall-clock apply time fell from 10,872 ms to 1,728 ms while all 50 outputs completed successfully. Generic destination-concurrency tuning stops at 4 unless a different device/provider again shows substantial destination-ready wait.

## v0.1.22 undo performance pass

The original safe undo path already took one target-directory snapshot, but then processed every recorded output strictly serially:

`OpenReadAsync -> full SHA-256 -> DeleteAsync`

for each file. This preserved safety but exposed all SAF open/read/delete latency directly in wall-clock time.

v0.1.22 keeps the safety contract and changes only scheduling/I/O details:

- the target folder is still resolved once and snapshotted once;
- every existing candidate still receives a **full SHA-256 verification**; size/mtime metadata is never accepted as a substitute;
- SHA-only reads now use the same 256 KiB pooled buffer size as the apply path;
- each read stream is closed immediately after hashing and before deletion;
- independent files are processed with bounded concurrency 4;
- for each individual file the order remains `open -> full hash -> close -> compare -> delete`;
- changed files are retained exactly as before;
- the undo journal is cleared only when the batch finishes without per-file errors.

The UI now reports:

- target-folder resolution;
- target-directory snapshot;
- cumulative file-open time;
- cumulative SHA verification time;
- cumulative deletion time;
- total bytes hashed;
- journal-clear time;
- concurrency;
- `UndoService` wall-clock time;
- click-to-complete wall-clock time (including preview refresh when applicable).

Open/hash/delete counters are cumulative across concurrent workers and can therefore sum to more than the Undo wall-clock time. The wall-clock value is the main success metric.

This optimization does **not** remove SHA verification. The intentional cost of rereading every created subtitle remains part of the safety model: undo must not delete a subtitle that was edited or replaced after creation.

## Remaining performance policy

Generic scan and apply performance are accepted on the measured workload. Undo is now instrumented and bounded-parallel in v0.1.22; its first real-device result determines whether concurrency 4 is sufficient.

Further work should be evidence-driven:

- investigate solid-7z batch/streaming extraction only when a real solid-7z source is measurably slow;
- investigate Android-native document operations only if a provider/device shows substantial SAF wait after bounded concurrency;
- do not trade away eager archive validation, exact no-overwrite behavior, or SHA-verified undo for headline timing numbers.

## Safety invariants

Performance work must continue to preserve:

- video paths, names and bytes are never changed;
- source subtitles/archives are retained;
- existing destination subtitles are not overwritten;
- exact previewed destination names are not silently changed by provider collision handling;
- partial or merely pre-created outputs from failed/cancelled apply items are deleted best-effort;
- undo only targets files created by the app and verifies their full SHA-256 before deletion.
