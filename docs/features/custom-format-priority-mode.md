# Custom Format Priority Mode

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** Settings → Profiles → Quality

## What it does

Adds a per-custom-format **Priority** checkbox to the quality-profile editor. A custom
format flagged **Priority** is compared **before** quality when Sonarr ranks releases
and decides on upgrades.

This lets a *language* custom format (the motivating case: **VFQ / Quebec French**)
take precedence over quality — Sonarr prefers the VFQ release even if a non-VFQ release
is higher quality — while still allowing normal quality upgrades **within** the same
priority tier.

## Why it exists

Upstream Sonarr ranks releases by **quality first, then custom-format score**. With a
plain (non-priority) VFQ custom format you can add points, but a higher-quality
non-VFQ release still out-ranks a VFQ one, and can even replace a VFQ file you already
have. There was no way to say "language matters more than quality, but I still want the
best quality *for that language*."

> This is a v4 port of the same feature in [KrZ-W/Radarr](https://github.com/KrZ-W/Radarr).
> The v4 frontend uses JS class components + the Redux connector pattern, so the UI
> mirrors the Radarr structure.

## How it works

The per-CF Priority flag splits a profile's custom-format score into two numbers:

- **Priority score** — sum of scores from CFs flagged Priority
- **Regular score** — sum of scores from all other CFs

Comparison order becomes: **Priority score → quality → revision → regular CF score**.

This is applied consistently in three places that previously disagreed:

| Stage | Where | Behavior |
|---|---|---|
| **Initial grab** | `DownloadDecisionComparer` | Priority tier is compared ahead of quality, so a priority release wins the first grab even at lower quality. |
| **Upgrade decision** | `UpgradableSpecification` | Higher priority score upgrades regardless of quality; lower priority score is rejected regardless of quality; equal priority falls through to the normal quality/revision/CF chain. |
| **Import** | `EpisodeImport/.../UpgradeSpecification` | Mirrors the grab/upgrade logic so a file grabbed for a priority language isn't refused at import by a quality-only check. Respects `CutoffFormatScore` and `MinUpgradeFormatScore`. |

When a priority comparison causes a rejection, the queue/activity message now lists the
**matched custom formats and absolute scores** for both the new and existing file,
instead of only a bare delta.

## Configuration

1. **Settings → Custom Formats** — create the custom format you want to prioritize
   (e.g. a VFQ format).
2. **Settings → Profiles → Quality → (edit a profile)** — find that custom format in
   the format list and:
   - give it a **score** (as usual), and
   - tick the new **Priority** checkbox.
3. Save.

> Multiple formats can be flagged Priority; their scores are summed into the priority
> tier. A profile with **no** Priority-flagged formats behaves exactly like stock
> Sonarr (priority score is always 0).

## Behavior & edge cases

- **Backward compatible.** With nothing flagged Priority, every release scores 0 in the
  priority tier and the original quality-first ordering is unchanged.
- **Quality still matters within a tier.** Two VFQ releases are still compared by
  quality/revision — you get the best VFQ copy, not just the first one.
- **Interacts with Season-Pack Partial Fill.** Per-episode evaluation inside the
  season-pack logic routes through the same priority-aware `IsUpgradable`, so priority
  behavior is preserved when grabbing packs. See
  [Season-Pack Partial Fill](season-pack-partial-fill.md).

## Related

- [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — pair Priority with the
  Audio Title condition so VFQ is detected from file content.
- [Import-time Enforcement](import-time-enforcement.md) — complementary import-side
  MinFormatScore check (Sonarr has no profile Language field).
- User Guide: [Make VFQ win over higher-quality audio](../user-guide.md#recipe-make-vfq-win-over-higher-quality-audio).

## Source

Commits: `91c955a20` (per-CF flag), `38a5f9b68` (grab), `453ff6cd3` (import),
`98154b879` (rejection messages), `7114d0fab` (tests). Key files:
`Profiles/ProfileFormatItem.cs`, `Profiles/Qualities/QualityProfile.cs`,
`DecisionEngine/Specifications/UpgradableSpecification.cs`,
`DecisionEngine/DownloadDecisionComparer.cs`,
`MediaFiles/EpisodeImport/Specifications/UpgradeSpecification.cs`.
