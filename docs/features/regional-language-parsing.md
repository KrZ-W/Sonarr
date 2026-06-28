# Regional Language Parsing

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** automatic (no settings)

## What it does

Adds `en-CA` and `fr-CA` entries to Sonarr's `IsoLanguages` table so regional language
tags in release filenames and subtitle names are parsed to the correct base language.

Example: `Show.S01E01.FRENCH-CA.mkv` now correctly maps to **French** instead of being
missed.

## Why it exists

Sonarr's language parser only knew a fixed set of language codes; Canadian regional
variants weren't represented, so regional tags in scene names didn't resolve. This is a
small, focused parsing fix — there are **no settings** and no behavior to configure.

> This is intentionally narrower than the Radarr fork's "Regional Language &
> Translations" feature. Sonarr has no quality-profile Language field and a different
> metadata pipeline, so the fork only adds the parsing entries needed for correct
> language detection. Language *preference* in Sonarr is expressed through custom
> formats — see [Import-time Enforcement](import-time-enforcement.md).

## Behavior & edge cases

- Purely additive to parsing; existing detection is unchanged for non-regional tags.
- Pairs with [VFQ Audio-Title Detection](vfq-audio-title-detection.md): filename parsing
  catches `FRENCH-CA`, while the Audio Title condition catches VFQ from the audio track
  when the filename says only `FRENCH`.

## Source

Commit: `bc175ac96`. Key file: `Parser/IsoLanguages.cs`.
