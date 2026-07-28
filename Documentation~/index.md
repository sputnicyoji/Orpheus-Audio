# Orpheus Audio Manual

Orpheus is a Unity-native game-audio runtime. The consuming game owns content,
session creation, Scene binding, preferences, and teardown.

This manual describes package `0.2.2` and Public Contract v1. Its Factory,
Manager, Bridge, Host asset, generated Audio Key, and required Core value
surfaces form the versioned consumer contract.

## Task guides

| Task | Guide |
| --- | --- |
| Install the package and hear a cue | [Getting Started](getting-started.md) |
| Create Events, Keys, Catalogs, Mixers, and profiles | [Authoring and Validation](authoring-and-validation.md) |
| Integrate lifecycle, playback, loading, state, gains, and diagnostics | [Runtime Integration](runtime-integration.md) |
| Look up supported public types and methods | [API Reference](api-reference.md) |
| Diagnose initialization, readiness, and fail-closed state | [Diagnostics and Troubleshooting](diagnostics-and-troubleshooting.md) |
| Upgrade a pinned package dependency | [Upgrading](upgrading.md) |

Import `Playable One Shot` for an audible end-to-end Host. Import
`Minimal Setup` only when an intentionally silent lifecycle example is needed.

## Supported platforms

| Surface | Status |
| --- | --- |
| Unity | `2022.3.62f2c1`; minimum and verified target |
| Windows Editor | Supported |
| Windows Standalone | Supported |
| Android | Experimental |
| iOS, macOS, WebGL, other platforms | Unsupported |

Android builds and device observations do not become a support claim until the
normative multi-device, lifecycle, route, performance, allocation, listening,
and long-background evidence is complete.

## Product boundary

- `Orpheus.Audio.Core` contains the public value contracts required by Host
  integration. All other pure policy types remain internal.
- `Orpheus.Audio` is the Unity runtime adapter.
- One non-static Manager owns one terminal Audio Session.
- Orpheus owns only its leased `AudioSource` instances.
- Audio failure is silent gameplay degradation.
- The package has no singleton, Scene scan, global Listener pause, or content
  delivery pipeline.
