# Android documentation index

The Android documentation is split by responsibility so that the top-level `Android/README.md` can stay user-facing and relatively stable.

| Document | Purpose | Update when |
|---|---|---|
| [`../README.md`](../README.md) | Android entry point, current supported workflow, basic build/install information | user-visible workflow or major capability changes |
| [`../../FORK.md`](../../FORK.md) | fork-level divergence and documentation policy | the fork's scope/relationship to upstream changes |
| [`FEATURES.md`](FEATURES.md) | upstream desktop vs Android feature inventory | a feature is added, removed, or deliberately rejected |
| [`ROADMAP.md`](ROADMAP.md) | prioritized remaining Android work | priorities or intended scope change |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | storage, matching, safety, performance and undo boundaries | an invariant or internal responsibility changes |
| [`WORKFLOW.md`](WORKFLOW.md) | concrete end-to-end example | the storage/user flow changes |
| [`VALIDATION.md`](VALIDATION.md) | CI, APK and real-device validation contract | build/test/package guarantees change |

## Documentation policy

The fork deliberately avoids rewriting the upstream root README into an Android README. Upstream desktop documentation should remain recognizable and easy to rebase.

Android-specific statements belong under `Android/` or in the root `FORK.md`.

When code changes affect multiple categories, update multiple documents in the same change rather than allowing the README to become an all-purpose changelog.

Do not put transient debugging history into the user-facing README unless it changes a durable validation rule. Durable lessons from past bugs belong in `VALIDATION.md` or `ARCHITECTURE.md`.
