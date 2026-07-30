# Orpheus Playable One Shot

This package `0.3.2` sample is the shortest verified path from package import
to audible playback. It owns one Audio Session, one Listener, and one validated
`OneShot2D` Event. Its code uses only the supported consumer contract.

## Run

1. Import `Playable One Shot` from Package Manager.
2. Select `Settings/PlayableOneShotKeyManifest.asset`.
3. Run `Tools > Orpheus > Generate Typed Keys`.
4. Run `Tools > Orpheus > Validate Audio Profiles`.
5. Open `Scenes/PlayableOneShot.unity`.
6. Enter Play Mode.

The Composition Root waits for Host Ready and Loaded state, then plays the
short tone once. Click `Replay Orpheus Cue` in Game view to replay it. The
button does not depend on the legacy Input Manager or Input System package.

If playback is silent, clear `Mute Audio` in Game view and verify the operating
system output device. The sample does not use `AudioListener.pause`.

If `Minimal Setup` is also imported, disable or remove its
`ValidationProfile.asset`. Enabled profiles may share one Manifest, but these
two samples intentionally use different Host Manifests.

## Content

| Asset | Value |
| --- | --- |
| Key | `100`, symbol `PlayableOneShot`, Active |
| Event | `AE_PlayableOneShot.asset` |
| Playback Kind | `OneShot2D` |
| Category | `SfxUi` |
| Load Policy | `BootstrapTransient` |
| Clip | `PlayableOneShot.wav` |

The `0.75s`, `16kHz`, mono PCM tone is deterministic Orpheus sample content. It
has no third-party media source.

## Why the sample uses a numeric key

Imported sample scripts must compile before the user can run the typed-key
generator. The sample therefore keeps raw key `100` in one local constant.

After generation, Host gameplay uses:

```csharp
using Orpheus.Audio;
using Orpheus.Audio.Generated;

OrpheusAudioBridge.Play(OrpheusAudioKeys.PlayableOneShot);
```

The generated projection belongs to `Assets/OrpheusGenerated`.

## Ownership

`PlayableOneShotCompositionRoot` creates and disposes the Manager.
`PlayableOneShotSceneBinding` binds only the explicit Listener. The Runtime
Host drives Manager `Tick` and `LateTick`.

There is no singleton, Scene scan, `DontDestroyOnLoad`, global Listener pause,
or package-owned preference storage.

## Teardown

Call `Teardown` before unloading the Scene. It removes the Listener, disposes
the Manager, then identity-safely unbinds the Runtime Host. `OnDestroy` is only
best-effort cleanup.

For a silent lifecycle and persisted-gain example, import `Minimal Setup`.
For production authoring rules, read
`Documentation~/authoring-and-validation.md`.
