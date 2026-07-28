# Authoring and Validation

The Host Manifest, Audio Events, Catalog, Settings, Runtime Host prefab, and
Listener Scenes form one validated authoring set. Runtime never scans the
project to discover content.

Public Contract v1 in package `0.2.2` freezes the serialized field identities
for these Host assets. Upgrade through the package changelog and migration
guide. Do not rename fields or rewrite package assets through reflection.

Create the five Host asset types from the `Assets > Create` menu:

```text
Orpheus > Audio Key Manifest
Orpheus > Audio Event
Orpheus > Audio Catalog
Orpheus > Audio Settings
Orpheus > Audio Validation Profile
```

## Stable Audio Keys

Create `Orpheus > Audio Key Manifest`.

Each entry contains:

| Field | Contract |
| --- | --- |
| ID | Unique `ushort`; `0` is invalid |
| Symbol | Unique ASCII C# identifier; no keyword, generated member name, or repeated underscore |
| Status | `Active`, `Reserved`, or `Retired` |

IDs are Host-assigned identities. Retired IDs are never reused. Category is not
derived from an ID range.

Every Active ID has exactly one Catalog Event. Reserved and Retired IDs have
none. Select the Manifest and run `Tools > Orpheus > Generate Typed Keys` after
every edit.

Generation writes:

```text
Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef
Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs
```

Gameplay assemblies that use generated members reference
`Orpheus.Audio.Generated` in addition to the runtime assemblies.

## Audio Event

Create `Orpheus > Audio Event`. Name the asset exactly:

```text
AE_<ManifestSymbol>.asset
```

All Events require:

- one valid Active key;
- `1..8` unique, non-null Clips;
- priority `0..255`, where `0` is highest and `255` is lowest;
- volume range inside `[0,1]`;
- pitch range inside `[0.5,2]`;
- finite cooldown `>= 0`;
- a legal Playback Kind, Category, and Load Policy tuple.

### Playback matrix

| Playback Kind | Allowed Category | Load Policy | Additional contract |
| --- | --- | --- | --- |
| `OneShot2D` | Any valid Category | `BootstrapTransient` or `ExplicitTransient` | Polyphony `1..4` |
| `OneShot3D` | `SfxCombat`, `SfxWorld`, `Ambience` | `BootstrapTransient` or `ExplicitTransient` | Polyphony `1..12`; `0 <= minDistance < maxDistance`; logarithmic or linear rolloff |
| `GlobalLoop2D` | `SfxCombat`, `SfxWorld`, `SfxUi`, `Ambience` | `PersistentStream` | One Clip; polyphony `1`; fixed volume; pitch `1`; cooldown `0` |
| `Bgm` | `Music` | `PersistentStream` | One Clip; polyphony `1`; fixed volume; pitch `1`; cooldown `0` |
| `ProfileAmbience` | `Ambience` | `PersistentStream` | One Clip; polyphony `1`; fixed volume; pitch `1`; cooldown `0` |

### Clip importer matrix

| Load Policy | Unity Load Type | Preload Audio Data | Load In Background |
| --- | --- | --- | --- |
| `BootstrapTransient` | Decompress On Load | On | Off |
| `ExplicitTransient` | Decompress On Load | Off | On |
| `PersistentStream` | Streaming | Off | On |

`OneShot3D` additionally requires `Force To Mono`.

## Catalog

Create `Orpheus > Audio Catalog`.

- Add Events explicitly.
- Sort them by raw Audio Key.
- Do not duplicate a key.
- Do not leave null entries.
- Do not register or replace Events at runtime.

Manager creation validates the entire Catalog and then binds one immutable
Runtime Catalog Snapshot to that Audio Session.

## Mixer and Settings

Duplicate the Mixer from either sample into a Host-owned `Assets/Audio`
directory. Do not edit the package copy.

Required group hierarchy:

```text
Master
  Music_User
    Music_State
  SFX_Combat_User
    SFX_Combat_State
  SFX_World_User
    SFX_World_State
  SFX_UI_User
    SFX_UI_State
  Ambience_User
    Ambience_State
```

Required exposed parameters:

```text
MasterVolume
MusicVolume
SfxCombatVolume
SfxWorldVolume
SfxUiVolume
AmbienceVolume
```

Required snapshots:

```text
Peace
Combat
Menu
Pause
```

Use `UnscaledTime`. Assign the Mixer, all eleven groups, all four snapshots,
and transition durations to one `OrpheusAudioSettings` asset.

Default transition values:

| Setting | Default |
| --- | --- |
| BGM crossfade | `0.4s` |
| Profile Ambience crossfade | `0.4s` |
| Snapshot transition | `0.6s` |

`Android Manual Reset Enabled` defaults to off. Keep it off while Android is
Experimental unless a later supported-device contract explicitly requires it.

## Runtime Host and Listener Scenes

Start from the sample Runtime Host prefab. Its Source Bank has fixed roles:

```text
OneShot3D_00 .. OneShot3D_11
OneShot2D_00 .. OneShot2D_03
Bgm_00 .. Bgm_01
ProfileAmbience_00 .. ProfileAmbience_01
GlobalLoop_00 .. GlobalLoop_03
```

Each leaf has exactly one `AudioSource`. Leave `playOnAwake` off, Clip empty,
and Mixer route empty. Runtime normalizes and routes Owned Sources.

List every supported Listener Scene explicitly in the Validation Profile.
Orpheus never scans for a Listener.

## Validation Profile

Create `Orpheus > Audio Validation Profile` and assign:

- Settings;
- Catalog;
- Key Manifest;
- Runtime Host prefab;
- Listener Scenes.

Enable the profile, generate keys, then run:

```text
Tools > Orpheus > Validate Audio Profiles
```

The Console error format is:

```text
Code | ProfilePath | AssetPath | event=<index> | related=<index> | detail=<value>
```

## Error repair map

| Error family | Repair |
| --- | --- |
| `InvalidProfileSchema`, `InvalidSettingsSchema`, `InvalidCatalogSchema`, `InvalidManifestSchema` | Recreate or migrate the asset with the current package version |
| `MissingSettings`, `MissingCatalog`, `MissingKeyManifest`, `MissingRuntimeHostPrefab` | Assign the missing Validation Profile reference |
| `InvalidManifestStorage`, `InvalidManifestId`, `InvalidManifestSymbol`, `InvalidManifestStatus` | Restore a non-null entry array and the key rules above |
| `DuplicateManifestId`, `DuplicateManifestSymbol`, `MultipleKeyManifests` | Keep one unique Host Manifest truth across enabled profiles |
| `MissingTypedKeyProjection`, `StaleTypedKeyProjection` | Select the active Manifest and run `Generate Typed Keys` |
| `NullEvent`, `UnsortedCatalog`, `DuplicateEventKey` | Remove nulls, sort by key, and keep one Event per key |
| `InvalidEventPolicy`, `EventKeyNotActive`, `MissingActiveEvent`, `InvalidEventAssetName` | Apply the playback matrix, activate the Manifest entry, add exactly one Event, and use `AE_<Symbol>.asset` |
| `MissingClipReference`, `DuplicateClipReference` | Assign `1..8` distinct non-null Clips |
| `MissingAudioImporter`, `InvalidAudioImporterPolicy` | Reimport the Clip and apply the importer matrix |
| `InvalidBgmCrossfadeDuration`, `InvalidProfileAmbienceCrossfadeDuration`, `InvalidSnapshotTransitionDuration` | Use finite Settings durations in `0.015s..5s` |
| `MissingMixer`, `MissingMixerGroup`, `MissingMixerSnapshot`, `MismatchedMixerReference` | Assign one Mixer identity and every required group/snapshot from it |
| `InvalidMixerUpdateMode`, `InvalidMixerGroupContract`, `InvalidMixerExposedParameterContract` | Use `UnscaledTime` and the exact hierarchy/parameter names above |
| `InvalidMixerSnapshotContract`, `InvalidMixerSnapshotOverride`, `InvalidMixerAttenuation`, `InaudibleStateUi`, `UnsupportedMixerSerialization` | Restore the sample Mixer topology, finite `[-80,0] dB` overrides, and audible UI state |
| `InvalidRuntimeHostPrefab`, `InvalidRuntimeHostCount`, `MissingSourceBank`, `InvalidSourceBankCount`, `MismatchedSourceBankReference` | Use one Runtime Host and its one serialized Source Bank |
| `InvalidSourceBankRoleCount`, `MissingSourceBankLeaf`, `InvalidSourceBankLeafName`, `DuplicateSourceBankLeaf`, `SourceBankLeafOutsideBank` | Restore the exact 24 named role leaves |
| `InvalidSourceBankLeafAudioSourceCount`, `UnexpectedSourceBankAudioSource`, `SourceBankPlayOnAwake`, `SourceBankPresetClip`, `SourceBankStaleRoute` | Keep one clean `AudioSource` on each expected leaf and no others |
| `NullListenerScene`, `UnresolvedListenerScene`, `InvalidListenerSceneType`, `UnloadableListenerScene` | Assign a real loadable Scene asset containing the Host-selected Listener |

Validation is deterministic and collects independent errors. Fix the first
structural error in each asset, rerun generation when the Manifest changes,
then validate again.
