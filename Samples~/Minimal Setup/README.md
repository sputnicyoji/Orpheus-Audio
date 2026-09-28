# Orpheus Minimal Setup

This package `0.3.5` sample is an empty, consumer-owned Orpheus Audio Session
for Unity `2022.3.62f2c1`. It proves the supported Factory, Manager, Runtime
Host, asset, and lifecycle contract. It contains no Audio Event, no playable
Audio Key, and no production content.

## Installation

1. Install `com.orpheus.audio` through Unity Package Manager.
2. Open the package in Package Manager.
3. Import the `Minimal Setup` sample.
4. Select `Settings/EmptyKeyManifest.asset` in the imported sample.
5. Run `Tools > Orpheus > Generate Typed Keys`.
6. Run `Tools > Orpheus > Validate Audio Profiles`.
7. Open `MinimalSetup.unity` from the imported sample directory.
8. Enter Play Mode.

Unity copies the sample into `Assets/Samples`. Imported assets are Host assets. Package updates do not silently replace the imported copy.
The generated empty projection belongs at `Assets/OrpheusGenerated`. Build validation requires it even though the Manifest has no entries.

## Ownership

`MinimalCompositionRoot` explicitly creates and owns one `OrpheusAudioManager`. The serialized Runtime Host owns the fixed Source Bank. `MinimalSceneBinding` forwards only Listener binding operations. `PlayerPrefsGainStore` belongs to this sample Host, not to package Runtime Settings.

There is no package singleton, Service Locator, Scene scan, or package-owned persistence policy. The Runtime Host is the only component that calls Manager `Tick` and `LateTick`.

## Mixer Duplication

The sample Mixer is a validated template. Duplicate it into a Host-owned project directory before production tuning, then update the Host Settings asset to reference the duplicate Mixer, groups, exposed user-gain parameters, and all four snapshots. Preserve the required topology and names. Do not share one Mixer identity between live Audio Sessions.

The template uses `UnscaledTime`. Its state attenuation rows are:

| Snapshot | Music | Combat | World | UI | Ambience |
| --- | ---: | ---: | ---: | ---: | ---: |
| Peace | 0 dB | 0 dB | 0 dB | 0 dB | 0 dB |
| Combat | -3 dB | 0 dB | -2 dB | 0 dB | -6 dB |
| Menu | -3 dB | -6 dB | -6 dB | 0 dB | -12 dB |
| Pause | -8 dB | -80 dB | -80 dB | 0 dB | -8 dB |

## Lifecycle

The sample uses this order:

1. `OrpheusAudioFactory.Create` creates the session with six persisted linear gains.
2. `OrpheusAudioRuntimeHost.Bind` binds the Manager identity to its carrier.
3. `MinimalSceneBinding.BindListener` attaches the explicit Scene Listener.
4. The Composition Root captures bootstrap authority from that Manager.
5. It completes bootstrap hydration with the captured authority.
6. `CompleteHostReady` runs after the Runtime Host `Start` callback.
7. Overlay calls go through `SetMenuOverlay` and `SetPauseOverlay`.
8. Teardown saves gains, removes the Listener, disposes the Manager, then identity-safely unbinds the Runtime Host.

Call `Teardown` before unloading the Scene. `OnDestroy` is only best-effort cleanup because Unity does not define destruction order across Scene roots; it is not the clean shutdown contract.

## Bootstrap

Host Ready and Bootstrap Hydrated are independent barriers. Bootstrap completion requires authority captured from the current Manager. An old callback cannot activate a replacement Audio Session.

This empty Catalog deliberately uses the Manager's internal no-profile default. It does not call `ApplyProfile(0)`. Audio Key `0` is invalid.

## Scene Binding

The package never searches for a Listener. `MinimalSceneBinding` exposes only `BindListener`, `ReplaceListener`, and `RemoveListener`, and forwards them to the current Manager.

For an additive Scene handoff, Host code validates and enables the replacement Listener, disables the old Listener, and calls identity-safe replacement before the next frame. Orpheus does not change `AudioListener.enabled`.

## Gain Persistence

All six gains are linear values in `[0,1]`. `PlayerPrefsGainStore` loads them before Factory creation and saves them before teardown. Invalid persisted values fall back to `1`.

The six ASCII Host-owned keys are:

- `Orpheus.MinimalSetup.Gain.Master`
- `Orpheus.MinimalSetup.Gain.Music`
- `Orpheus.MinimalSetup.Gain.SfxCombat`
- `Orpheus.MinimalSetup.Gain.SfxWorld`
- `Orpheus.MinimalSetup.Gain.SfxUi`
- `Orpheus.MinimalSetup.Gain.Ambience`

Production games may replace this store. Preference keys do not belong in `OrpheusAudioSettings`.

## Empty means no playback

Activation Ready and Playback Ready describe lifecycle and transport readiness. They do not imply that content exists. This sample has no playable event, so `Play` and `PlayAt` cannot start a cue. Use a Host-owned non-empty Catalog to test playback.

## Scene survival policy

This sample is scene-scoped. It does not call `DontDestroyOnLoad`, so its Runtime Host and Audio Session do not survive Scene replacement. A production Host may choose a different carrier lifetime in its own Composition Root. The package does not impose that choice.

## xLua Integration

The package and this sample do not depend on xLua. If a Host uses xLua, Host codegen may expose only the intended bridge surface. xLua wrappers remain outside Orpheus.
