# End-to-end workflow example

This example shows the intended Android storage model and which layer is responsible for each decision.

## Input

```text
/storage/emulated/0/Download/
├─ Torrent/
│  └─ [ANi] Sousou no Frieren [01-28]/
│     ├─ [ANi] Sousou no Frieren - 01.mkv
│     ├─ [ANi] Sousou no Frieren - 02.mkv
│     └─ ...
├─ Sousou.no.Frieren.CHS.01-28.zip
└─ unrelated.apk
```

The video directory already exists and may belong to an active torrent. It is not reorganized.

## Discovery

Android scans `Download/Torrent/**` and creates one `VideoTarget` for the physical folder that directly contains the episode video files.

Android separately inspects direct children of `Download` and recognizes the ZIP as a subtitle source because it contains subtitle entries.

## Work attribution

Android-specific attribution ranks:

```text
Sousou.no.Frieren.CHS.01-28.zip
    -> Torrent/[ANi] Sousou no Frieren [01-28]
```

If confidence is not sufficient, the user selects the target torrent directory manually.

This step answers **which show/release directory the source belongs to**. It does not decide episode mapping.

## Episode mapping

Only the direct video filenames from the selected target and the subtitle entry filenames from the selected source are passed to the original `SubRenamer.Core` matcher.

Conceptually:

```text
video filenames + subtitle filenames
    -> SubRenamer.Core diff/extract/mapping
    -> episode pairs
```

## Preview

Android converts the Core result into exact destination filenames and checks for conflicts.

Example:

```text
subtitle entry 01.ass
  -> [ANi] Sousou no Frieren - 01.ass

subtitle entry 02.ass
  -> [ANi] Sousou no Frieren - 02.ass
```

When several same-extension language tracks map to one episode, a recognized language suffix is retained, for example:

```text
[ANi] Sousou no Frieren - 01.chs.ass
[ANi] Sousou no Frieren - 01.cht.ass
```

Nothing is written during preview.

## Apply

After confirmation:

```text
/storage/emulated/0/Download/Torrent/[ANi] Sousou no Frieren [01-28]/
├─ [ANi] Sousou no Frieren - 01.mkv      # untouched
├─ [ANi] Sousou no Frieren - 01.ass      # created
├─ [ANi] Sousou no Frieren - 02.mkv      # untouched
├─ [ANi] Sousou no Frieren - 02.ass      # created
└─ ...
```

The ZIP remains in `Download`.

Existing destination subtitles are skipped rather than overwritten.

## Undo

For each created subtitle, Android records its destination name and SHA-256.

If **撤销上次处理** is used later:

1. the app resolves the same torrent target directory;
2. it finds each recorded output;
3. it hashes the current file;
4. it deletes the file only when the hash still matches what Android originally created.

If a subtitle has been edited or replaced after processing, undo leaves it in place.

## Responsibility summary

```text
Android storage layer
  -> discover Torrent target folders and Download subtitle sources

Android attribution layer
  -> choose which subtitle source belongs to which torrent target

Original SubRenamer.Core
  -> map episode filenames within that selected work

Android planning/apply layer
  -> derive safe destination names, check conflicts, create subtitles

Android undo layer
  -> content-verified removal of the most recent created batch
```
