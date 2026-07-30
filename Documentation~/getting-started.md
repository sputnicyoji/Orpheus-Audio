# Getting Started

This path installs Orpheus, imports a valid Host, and plays one audible cue.
It targets Unity `2022.3.62f2c1`.

Git installation requires Git `2.14.0` or newer and the Git executable on the
system `PATH`. Local `file:` installation does not use Git.

## 1. Install

Choose one source.

Local clone:

```json
{
  "dependencies": {
    "com.orpheus.audio": "file:../../Orpheus-Audio"
  }
}
```

This exact path assumes `<workspace>/Host` and `<workspace>/Orpheus-Audio` are
sibling directories, with the package manifest at
`<workspace>/Orpheus-Audio/package.json`. Unity resolves `file:` from
`Host/Packages`; adjust the path for another directory layout.

Pinned Git revision:

```json
{
  "dependencies": {
    "com.orpheus.audio": "https://github.com/sputnicyoji/Orpheus-Audio.git#v0.3.3"
  }
}
```

Pin a release tag or full commit SHA, not a moving branch. Wait until Package
Manager resolution and script compilation finish.

## 2. Import the audible sample

1. Open `Window > Package Manager`.
2. Select `Orpheus Audio`.
3. Open `Samples`.
4. Import `Playable One Shot`.
5. Open the imported
   `Assets/Samples/Orpheus Audio/0.3.3/Playable One Shot` directory.

Unity owns the imported copy. Package updates do not overwrite it.

## 3. Generate typed keys

1. Select `Settings/PlayableOneShotKeyManifest.asset`.
2. Run `Tools > Orpheus > Generate Typed Keys`.
3. Confirm these files exist:
   - `Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef`
   - `Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs`

Generation is deterministic. Run it again after every Manifest change. Commit
the generated projection with the Host project.

If another Orpheus sample is already imported, disable or remove its
`ValidationProfile.asset`. A production Host uses one active Manifest truth.

## 4. Validate

Run `Tools > Orpheus > Validate Audio Profiles`.

Expected Console output:

```text
Orpheus audio validation passed.
```

The same validation runs before a player build. A failing profile blocks the
build rather than shipping an invalid Mixer, Source Bank, Catalog, Manifest,
Clip importer, or Listener Scene.

## 5. Hear the cue

1. Open `Scenes/PlayableOneShot.unity`.
2. Enter Play Mode.
3. Listen for the short tone after Host Ready and clip loading complete.
4. Click `Replay Orpheus Cue` in Game view to replay it.

If no sound is heard, clear `Mute Audio` in Game view and verify the operating
system output device before changing Orpheus assets.

The sample calls:

```csharp
manager.Play(new OrpheusAudioKey(100));
```

The numeric construction exists only so the imported sample compiles before
key generation. Host gameplay code uses the generated member:

```csharp
using Orpheus.Audio;
using Orpheus.Audio.Generated;

OrpheusAudioBridge.Play(OrpheusAudioKeys.PlayableOneShot);
```

Bind the bridge to the current Manager in the Composition Root. Unbind it with
the expected Manager identity before disposal. Do not expose high-privilege
state or lifecycle methods through gameplay bridges.

## What the sample owns

- one Runtime Host prefab and fixed 24-Source Bank;
- one Host-owned Mixer and Settings asset;
- one active Key Manifest entry;
- one Bootstrap Transient 2D Audio Event;
- one Catalog;
- one enabled Validation Profile;
- one explicit Listener Scene;
- one Composition Root.

The bundled `0.75s`, `16kHz`, mono PCM tone is deterministic Orpheus sample
content. It has no third-party media source.

## Next

- Add real content with [Authoring and Validation](authoring-and-validation.md).
- Integrate lifecycle and public APIs with
  [Runtime Integration](runtime-integration.md).
