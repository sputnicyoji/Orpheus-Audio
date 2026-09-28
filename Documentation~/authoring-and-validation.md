# Authoring and Validation

The Host Manifest, Audio Events, Catalog, Settings, Runtime Host prefab, and
Listener Scenes form one validated authoring set. Runtime never scans the
project to discover content.

Public Contract v1 in package `0.3.5` freezes the serialized field identities
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

Authoring automation additionally uses:

```text
Orpheus > Audio Module Recipe
Orpheus > Audio Authoring Profile
```

These assets are Editor inputs. Runtime consumes only the generated Event
assets and Catalog.

## Authoring automation

This is the M1 authoring workflow. Its recipe, enrollment, output, and command
contracts remain stable across the `0.3` package line.

Exactly one Validation Profile in the project may be pending or enrolled,
regardless of whether it is enabled. Every other profile remains manual.
Enabled manual profiles must use the same Manifest and already validate
against the proposed Manifest state.

Before the first Analyze:

- assign every numeric Audio Key ID in the Host Manifest; the compiler never
  allocates IDs;
- create an empty current-schema Catalog at
  `<generatedRoot>/OrpheusAudioCatalog.asset`;
- assign that same Catalog to the Authoring Profile and Validation Profile;
- leave the Validation Profile enrollment GUID empty;
- select the enabled `OrpheusAudioValidationProfile` asset before invoking an
  authoring menu command.

1. Create Module Recipe assets.
2. Create one Audio Authoring Profile.
3. Assign its Manifest, Recipes, Catalog, and generated root.
4. Assign that profile to the enabled Validation Profile.
5. Run `Tools > Orpheus > Analyze Enrolled Authoring`.
6. Review the report under
   `Library/Orpheus/AuthoringAnalysis/<validation-profile-guid>.json`.
7. Run `Tools > Orpheus > Accept And Compile Enrollment`.

Enrollment is explicit and sticky. The confirmation shows the Validation
Profile path, Authoring Profile GUID, numeric Manifest snapshot, and generated
root. After acceptance, the custom inspector hides the raw enrollment GUID and
locks Authoring Profile replacement. M1 has no de-enrollment command.

The exact commands are:

```text
Tools > Orpheus > Analyze Enrolled Authoring
Tools > Orpheus > Accept And Compile Enrollment
Tools > Orpheus > Compile Enrolled Authoring
Tools > Orpheus > Compile And Delete Tracked Orphans
```

Analyze is read-only. Accept is available only for pending enrollment. Compile
and orphan deletion are available only after enrollment. Orphan deletion
permanently removes only tracked generated orphans and requires a second
confirmation showing the generated root and orphan count.

Committed output paths are exact:

```text
<generatedRoot>/Events/AE_<symbol>.asset
<generatedRoot>/OrpheusAudioCatalog.asset
<generatedRoot>/OrpheusAuthoringOwnership.json
<generatedRoot>/OrpheusAuthoringClosure.json
Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef
Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs
```

Commit the Authoring Profile, enrolled Validation Profile, Recipes, Manifest,
Catalog, generated Events, typed-key files, ownership and closure reports, and
every generated `.meta` file together. Normal Compile reports tracked orphans
but does not delete them. Only `Compile And Delete Tracked Orphans` may remove
an orphan whose recorded GUID, path, type, generated root, and current recipe
ownership all agree. Foreign or identity-ambiguous files are never deleted.

Batch automation uses Unity's internal `-executeMethod` carrier:

```powershell
Unity.exe -batchmode -nographics `
  -projectPath <project> `
  -executeMethod Orpheus.Audio.Editor.OrpheusAudioAuthoringBatch.Run `
  -orpheusProfileGuid <lowercase-32-character-guid> `
  -orpheusMode <Analyze|Compile> `
  -orpheusBuildTarget <StandaloneWindows|StandaloneWindows64|Android>
```

`-orpheusProfileGuid` is the lowercase 32-character asset GUID of the
Validation Profile. Mode and target values are case-sensitive. Each Orpheus
argument is required exactly once. Unknown, repeated, missing, interactive
confirmation modes, or unsupported input is rejected. Enrollment acceptance
and orphan deletion remain menu-only operations.

| Exit code | Meaning |
| --- | --- |
| `0` | `SucceededChanged` or `SucceededUnchanged` |
| `2` | Invalid usage, internal carrier failure, or unknown status |
| `3` | Compile rejected before mutation |
| `4` | Mutation failed and rollback completed |
| `5` | Rollback could not be proven; manual repair is required |

The compiler, modes, reports, batch carrier, and transaction orchestration are
package-internal. Host code must not call them as an API.

### Validation and build behavior

Analyze, the shared Validator, and build preprocessing are read-only. They do
not repair, compile, enroll, or delete content.

- pending enrollment blocks a player build;
- stale generated output or stale ownership/closure blocks a build;
- invalid, non-terminal, or `RollbackFailed` transaction journals fail closed;
- target-specific Clip importer mismatch blocks the active build target;
- build preprocessing never invokes Compile or transaction recovery.

### Recipe field contract

Module IDs and non-empty metadata slugs use lowercase ASCII:

```text
[a-z0-9]+(?:-[a-z0-9]+)*
```

Leading or trailing hyphens, repeated hyphens, uppercase letters, underscores,
and other characters are invalid.

`OrpheusAudioModuleRecipe` stores:

| Serialized field | Contract |
| --- | --- |
| `_schemaVersion` | Must be `1` |
| `_moduleId` | Required project-unique slug |
| `_events` | Non-null array of Event Recipes |

Each `OrpheusAudioModuleEventRecipe` stores:

| Serialized field | Contract |
| --- | --- |
| `_symbol` | Active Manifest symbol owned by exactly one recipe |
| `_playbackKind`, `_category`, `_loadPolicy` | Must satisfy the Audio Event playback matrix |
| `_clips` | `1..16` unique, non-null clips with valid importer policy |
| `_volumeMin`, `_volumeMax` | Finite ordered range inside `[0,1]` |
| `_pitchMin`, `_pitchMax` | Finite ordered range inside `[0.5,2]` |
| `_priority` | `0..255`; `0` is highest |
| `_polyphonyCap` | Must satisfy the selected Playback Kind |
| `_cooldownSeconds` | Finite and `>= 0` |
| `_minimumDistance`, `_maximumDistance` | Required valid range for `OneShot3D` |
| `_rolloffMode` | Valid rolloff for the selected Playback Kind |
| `_profileHint` | Empty or a valid slug; report-only in M1 |
| `_candidateContentBankId` | Empty or a valid slug; report-only in M1 |

Report-only fields appear in analysis and future-delivery projection evidence.
They do not choose runtime profiles, load a content bank, or change Event
playback behavior in M1.

`OrpheusAudioAuthoringProfile` stores:

| Serialized field | Contract |
| --- | --- |
| `_schemaVersion` | Must be `1` |
| `_keyManifest` | The Host identity and lifecycle authority |
| `_moduleRecipes` | Non-null set of project-owned Module Recipes |
| `_catalog` | Host-owned Catalog whose Event list is transactionally generated |
| `_generatedRoot` | Canonical non-root `Assets/...` path owned by this enrollment |

### Manual-to-enrolled migration

Existing manual Events stay untouched until enrollment is explicitly
accepted:

```text
keep existing manual Events untouched
  -> create recipes and generated root
  -> assign Authoring Profile with empty enrollment GUID
  -> Analyze
  -> review proposed Manifest baseline, ownership and diff
  -> Accept And Compile Enrollment
  -> review generated diff
  -> run Validation Profile and build lint
  -> commit both profiles, recipes, Manifest, generated outputs, reports and .meta files together
```

Enrollment has no v1 undo. Do not point a pending profile at manual Event
paths that the compiler does not own.

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

A Manifest-authored same-ID symbol rename is source-breaking but does not
change Audio Key identity. Run Analyze, review the breaking rename, then run
Compile. The Event asset and `.meta` move together, preserving Event GUID and
numeric key. The old generated member disappears, the new member keeps the
same value, and no compatibility alias is emitted.

## Audio Event

Create `Orpheus > Audio Event`. Name the asset exactly:

```text
AE_<ManifestSymbol>.asset
```

All Events require:

- one valid Active key;
- `1..16` unique, non-null Clips;
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
| `MissingClipReference`, `DuplicateClipReference` | Assign `1..16` distinct non-null Clips |
| `MissingAudioImporter`, `InvalidAudioImporterPolicy` | Reimport the Clip and apply the importer matrix |
| `InvalidBgmCrossfadeDuration`, `InvalidProfileAmbienceCrossfadeDuration`, `InvalidSnapshotTransitionDuration` | Use finite Settings durations in `0.015s..5s` |
| `MissingMixer`, `MissingMixerGroup`, `MissingMixerSnapshot`, `MismatchedMixerReference` | Assign one Mixer identity and every required group/snapshot from it |
| `InvalidMixerUpdateMode`, `InvalidMixerGroupContract`, `InvalidMixerExposedParameterContract` | Use `UnscaledTime` and the exact hierarchy/parameter names above |
| `InvalidMixerSnapshotContract`, `InvalidMixerSnapshotOverride`, `InvalidMixerAttenuation`, `InaudibleStateUi`, `UnsupportedMixerSerialization` | Restore the sample Mixer topology, finite `[-80,0] dB` overrides, and audible UI state |
| `InvalidRuntimeHostPrefab`, `InvalidRuntimeHostCount`, `MissingSourceBank`, `InvalidSourceBankCount`, `MismatchedSourceBankReference` | Use one Runtime Host and its one serialized Source Bank |
| `InvalidSourceBankRoleCount`, `MissingSourceBankLeaf`, `InvalidSourceBankLeafName`, `DuplicateSourceBankLeaf`, `SourceBankLeafOutsideBank` | Restore the exact 24 named role leaves |
| `InvalidSourceBankLeafAudioSourceCount`, `UnexpectedSourceBankAudioSource`, `SourceBankPlayOnAwake`, `SourceBankPresetClip`, `SourceBankStaleRoute` | Keep one clean `AudioSource` on each expected leaf and no others |
| `NullListenerScene`, `UnresolvedListenerScene`, `InvalidListenerSceneType`, `UnloadableListenerScene` | Assign a real loadable Scene asset containing the Host-selected Listener |
| `InvalidAuthoringProfile`, `AuthoringEnrollmentIdentityMismatch`, `LostAuthoringOwnership` | Restore the assigned Authoring Profile and its accepted asset identity; pending enrollment must be explicitly accepted |
| `StaleGeneratedOwnership`, `GeneratedCatalogMismatch` | Restore tracked generated assets, then run `Compile Enrolled Authoring` |
| `StaleAuthoringInput`, `AuthoringOutputFingerprintMismatch` | Recompile the enrolled profile for the active target and commit the resulting closure |

Validation is deterministic and collects independent errors. Fix the first
structural error in each asset, rerun generation when the Manifest changes,
then validate again.
