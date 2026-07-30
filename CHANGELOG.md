# Changelog

## [Unreleased]

## [0.3.3] - 2026-07-30

- Added a discoverable M1 authoring quickstart to the package README and
  manual.
- Documented enrollment prerequisites, exact generated output and commit
  closure, tracked-orphan safety, batch exit codes, and fail-closed build
  behavior.
- Clarified the public Editor authoring ABI types without changing public API,
  serialized asset ABI, runtime behavior, or platform support.

## [0.3.2] - 2026-07-30

- Raised the transient Audio Event clip-count ceiling from `8` to `16` for
  `OneShot2D` and `OneShot3D`.
- Kept persistent `GlobalLoop2D`, `Bgm`, and `ProfileAmbience` Events at
  exactly one Clip.
- Changed no public API, serialized asset ABI, generated-key shape, existing
  clip-selection sequence, or platform support claim.

## [0.3.1] - 2026-07-30

- Bounded successful authoring transaction journal retention to one terminal
  record between compiler invocations.
- Pruned strictly validated `Committed` and `RolledBack` journals during
  recovery while preserving `RollbackFailed`, foreign, and reparse-point
  evidence fail-closed.

## [0.3.0] - 2026-07-30

- Added public Editor-only Audio Module Recipe and Audio Authoring Profile
  assets.
- Added deterministic Analyze, explicit enrollment acceptance, transactional
  Compile, ownership, closure, and machine-report workflows.
- Added Manifest-authored same-ID symbol rename support. Generated source and
  Event paths change while Audio Key identity, numeric value, and Event GUID
  remain stable.
- Added repeat-run, rollback recovery, `1024`-key compiler, and clean-consumer
  authoring evidence.
- Kept compiler orchestration internal and retained existing runtime behavior
  and platform support claims.

## [0.2.2] - 2026-07-28

- Corrected Settings duration documentation to the enforced `0.015s..5s`
  range.
- Clarified that Playback Ready does not include per-Clip load state.
- Corrected runtime error ownership for Mixer and Clip validation failures.
- Replaced an invalid generated-key declaration example and documented public
  enum sentinels.
- Added the Unity Git dependency prerequisite.

## [0.2.1] - 2026-07-28

- Added copy-ready Git installation, public API, diagnostics, troubleshooting,
  upgrading, and complete Composition Root documentation.
- Added Package Manager author metadata.
- Added the MIT license and public package-root distribution metadata.

## [0.2.0] - 2026-07-27

- Completed the Audio Core v1 runtime, pure policy, Editor validation, build
  lint, diagnostics, and Windows support evidence.
- Declared Public Contract v1 for the Factory, Manager, Bridge, Host asset, and
  generated Audio Key surfaces. Added deterministic public API, serialization
  ABI, and generated-source baselines.
- Internalized authoring, Catalog lookup, clip-selection, and runtime policy
  types that were never supported consumer APIs.
- Added package onboarding documentation for installation, content authoring,
  validation, runtime integration, and failure diagnosis.
- Added the `Playable One Shot` sample with one validated Audio Event and a
  deterministic audible cue.
- Retained `Minimal Setup` as the intentionally empty lifecycle sample.
- Classified Android as Experimental pending complete multi-device closing
  evidence. Windows Editor and Windows Standalone remain Supported.

## [0.1.0] - 2026-07-16

- Added the initial embedded UPM package scaffold.
- Added separate runtime and pure-policy assembly definitions.
