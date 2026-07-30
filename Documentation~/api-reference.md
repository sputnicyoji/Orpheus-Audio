# API Reference

This task-oriented reference covers the supported consumer owners in package
`0.3.1`. The checked-in API baseline is the exhaustive signature contract.
All runtime calls are main-thread-only unless stated otherwise. Invalid calls
reject deterministically and degrade to silence.

## Namespaces

| Namespace | Purpose |
| --- | --- |
| `Orpheus.Audio` | Unity runtime, Host assets, Manager, Factory, and Bridges |
| `Orpheus.Audio.Core` | Public value contracts used by Host integration |
| `Orpheus.Audio.Editor` | Key Manifest, Validation Profile, generation, and validation |
| `Orpheus.Audio.Generated` | Host-generated typed Audio Keys |

Other Core policy types are implementation details.

## Session creation

### `OrpheusAudioFactory.Create`

```csharp
public static OrpheusAudioInitResult Create(
    OrpheusAudioRuntimeHost runtimeHost,
    OrpheusAudioSettings settings,
    OrpheusAudioCatalog catalog,
    OrpheusAudioUserGains initialUserGains,
    out OrpheusAudioManager manager);
```

Creates one single-use Audio Session. Success returns a non-null Manager.
Failure returns a null Manager and a structured `OrpheusAudioInitResult`.

`initialUserGains` contains six linear values in `[0,1]`.

### `OrpheusAudioInitResult`

| Property | Meaning |
| --- | --- |
| `Success` | Creation completed |
| `ErrorCode` | Stable failure category |
| `RelatedKey` | Event key related to a Catalog failure, otherwise Invalid |
| `RelatedIndex` | Related Catalog index, otherwise `-1` |

`OrpheusAudioInitErrorCode` values:

```text
None
InvalidArguments
InvalidInitialGain
InvalidSettings
InvalidMixerContract
InvalidSourceBank
InvalidCatalog
DuplicateKey
InvalidEvent
InvalidClipReference
InvalidAudioConfiguration
```

## Runtime Host

### `OrpheusAudioRuntimeHost.Bind`

```csharp
public bool Bind(OrpheusAudioManager manager);
```

Binds the Manager reserved by Factory creation. Rebinding the same identity is
idempotent. Another Manager is rejected.

### `OrpheusAudioRuntimeHost.Unbind`

```csharp
public bool Unbind(OrpheusAudioManager expectedManager);
```

Releases the bound identity after Manager disposal. The expected identity is
required.

The Runtime Host owns `Update`, `LateUpdate`, application focus/pause input,
and audio-configuration callback forwarding. Do not call Manager ticks from a
second owner.

## Manager lifecycle

### `CaptureBootstrapAuthority`

```csharp
public OrpheusBootstrapAuthority CaptureBootstrapAuthority();
```

Captures the identity-scoped authority required to finish initial product
intent hydration.

### `CompleteBootstrapHydration`

```csharp
public bool CompleteBootstrapHydration(
    OrpheusBootstrapAuthority authority);
```

Completes the Bootstrap Hydrated barrier for the current Manager generation.
Stale or foreign authority is rejected.

### `CompleteHostReady`

```csharp
public OrpheusCompleteHostReadyResult CompleteHostReady();
```

Completes Host Ready after the Runtime Host has observed `Start`, the Runtime
Host is bound, and a Listener is bound.

Result values:

```text
Invalid
Completed
AlreadyCompleted
RuntimeHostNotStarted
RuntimeHostNotBound
ListenerNotBound
InvalidLifecycle
DisabledByValidationFailure
```

### Listener ownership

```csharp
public bool BindListener(AudioListener listener);
public bool ReplaceListener(
    AudioListener expectedOldListener,
    AudioListener replacementListener);
public bool RemoveListener(AudioListener expectedListener);
```

Orpheus never scans Scenes and never changes `AudioListener.enabled`.
Replacement and removal are identity-safe.

### `Dispose`

```csharp
public void Dispose();
```

Stops Owned Sources and terminates the Manager. Disposal is idempotent. A
disposed Manager cannot be reset or reused.

## Playback

```csharp
public void Play(OrpheusAudioKey key);
public void PlayAt(OrpheusAudioKey key, Vector3 position);
```

- `Play` accepts authored `OneShot2D` Events.
- `PlayAt` accepts authored `OneShot3D` Events and a finite position.
- Both return `void`.
- Rejection never changes gameplay control flow.
- Rejected requests do not advance the Manager-local RNG.

## Loading

```csharp
public OrpheusAudioPrepareResult Prepare(OrpheusAudioKey key);
public OrpheusClipLoadState GetLoadState(OrpheusAudioKey key);
```

`Prepare` applies to `ExplicitTransient` content.

`OrpheusAudioPrepareResult`:

```text
Invalid
LoadRequested
AlreadyLoading
AlreadyLoaded
RejectedInvalidKey
RejectedPolicy
FailedToRequest
```

`OrpheusClipLoadState`:

```text
Unloaded
Loading
Loaded
Failed
Invalid
```

## Profile, overlays, and loops

```csharp
public void ApplyProfile(OrpheusAudioProfileIntent intent);
public void SetOverlay(OrpheusOverlay selector, bool enabled);
public void PlayLoop(OrpheusAudioKey key);
public void StopLoop(OrpheusAudioKey key);
public void StopAll(OrpheusBus bus);
```

`ApplyProfile` atomically submits Profile ID, Base State, BGM, and Profile
Ambience. `ProfileId` must be non-zero. Use `OrpheusAudioKey.Invalid` for no
BGM or no Profile Ambience.

`SetOverlay` accepts exactly one selector: `Menu` or `Pause`. Do not combine
flags in one call.

`PlayLoop` and `StopLoop` address authored `GlobalLoop2D` Events. `StopAll` is
a Host-only, Bus-scoped operation.

### `OrpheusAudioProfileIntent`

```csharp
public OrpheusAudioProfileIntent(
    uint profileId,
    OrpheusBaseState baseState,
    OrpheusAudioKey bgmKey,
    OrpheusAudioKey profileAmbienceKey);
```

`OrpheusBaseState.Invalid` is the default sentinel. Valid public Base State
inputs are `Peace` and `Combat`.

## User gain

```csharp
public void SetUserGain(OrpheusBus bus, float linearGain);
public bool TryGetUserGain(OrpheusBus bus, out float linearGain);
```

Valid gains are finite linear values in `[0,1]`.

`OrpheusBus` members:

```text
Invalid
Master
Music
SfxCombat
SfxWorld
SfxUi
Ambience
```

`Invalid` is the default sentinel and is rejected as an operation target.

The Host owns persistence. Load gains before Factory creation and save them
before teardown.

### `OrpheusAudioUserGains`

```csharp
public OrpheusAudioUserGains(
    float master,
    float music,
    float sfxCombat,
    float sfxWorld,
    float sfxUi,
    float ambience);
```

Read-only properties use the same six names.

## Bridges

### Typed Bridge

```csharp
public static bool OrpheusAudioBridge.Bind(
    OrpheusAudioManager manager);
public static bool OrpheusAudioBridge.Unbind(
    OrpheusAudioManager expectedManager);
public static void OrpheusAudioBridge.Play(
    OrpheusAudioKey key);
public static void OrpheusAudioBridge.PlayAt(
    OrpheusAudioKey key,
    Vector3 position);
```

### Raw Bridge

```csharp
public static bool OrpheusAudioRawBridge.Bind(
    OrpheusAudioManager manager);
public static bool OrpheusAudioRawBridge.Unbind(
    OrpheusAudioManager expectedManager);
public static void OrpheusAudioRawBridge.Play(int rawKey);
public static void OrpheusAudioRawBridge.PlayAt(
    int rawKey,
    Vector3 position);
```

The raw Bridge accepts only values in `1..65535`. Both Bridges expose one-shot
playback only. Profile, gain, loops, loading, diagnostics, and lifecycle remain
Host-owned.

## Audio Key

```csharp
public OrpheusAudioKey(ushort value);
public static OrpheusAudioKey Invalid { get; }
public ushort Value { get; }
public bool IsValid { get; }
```

`0` is invalid. Production gameplay code uses generated members:

```csharp
OrpheusAudioKeys.PlayableOneShot
```

Do not derive category from the numeric value. Retired IDs are never reused.

Representative generated output also exposes:

```csharp
public const int ManifestSchemaVersion = 1;
public const string ManifestContentHash = "<generated SHA-256 hex>";
public static readonly OrpheusAudioKey PlayableOneShot =
    new OrpheusAudioKey(100);
```

The schema version guards generator compatibility. The content hash identifies
the Host Manifest projection and changes with Host content.

## Diagnostics

```csharp
public bool TryGetDiagnostics(
    out OrpheusAudioDiagnostics diagnostics);
```

The snapshot contains lifecycle, transport, readiness, disable reason,
suspension reasons, Profile state, persistent-key state, voice counts, and
saturating counters. It remains readable after Disabled and Disposed.

Primary snapshot fields:

```text
SchemaVersion
ProfileId
ConfiguredRealVoiceLimit
ConfiguredVirtualVoiceLimit
DesiredBgmKey
CurrentBgmKey
TargetBgmKey
DesiredProfileAmbienceKey
CurrentProfileAmbienceKey
TargetProfileAmbienceKey
Lifecycle
DisableReason
TransportState
SuspensionReasons
Readiness
Overlay
EffectiveSnapshot
RecoveryPendingReasons
BaseState
VoiceBudgetDegraded
Transient3DActiveCount
Transient2DActiveCount
FadingCount
PendingCount
BgmActiveCount
ProfileAmbienceActiveCount
GlobalLoopActiveCount
Counters
```

Convenience properties:

```text
IsAvailable
IsVoiceBudgetDegraded
HostReady
BootstrapHydrated
ActivationReady
PlaybackReady
MenuOverlayEnabled
PauseOverlayEnabled
ConfigurationRecoveryPending
```

Saturating counter fields:

```text
PoolCapacityRejected
Stolen
CooldownRejected
PolyphonyRejected
DistanceRejected
PreReadyRejected
SuspendedRejected
UnavailableRejected
WrongThreadRejected
LoadNotReadyRejected
LoadFailed
LoadStalled
InvalidKeyRejected
InvalidRawKeyRejected
InvalidPositionRejected
InvalidValueRejected
PlaybackKindRejected
LoopRegistryFull
SetFloatFailed
RecoveryFailed
StaleBootstrapTokenRejected
UnexpectedException
```

Runtime state enums:

```text
OrpheusAudioLifecycle:
  Invalid, Running, Disabled, Disposed

OrpheusTransportState:
  Invalid, Active, Suspended, Recovering, Disposed

OrpheusSuspensionReason [Flags]:
  None, FocusLost, ApplicationPaused, ListenerMissing

OrpheusReadiness [Flags]:
  None, HostReady, BootstrapHydrated, ActivationReady, PlaybackReady

OrpheusEffectiveSnapshot:
  Invalid, Peace, Combat, Menu, Pause

OrpheusRecoveryPendingReason [Flags]:
  None, ExternalConfiguration, SelfResetNotification

OrpheusAudioDisableReason:
  None, MixerSetFloatFailed, ListenerPositionNonFinite,
  OwnedSourceDestroyed, OwnedSourceReferenceChanged,
  SourceBankDuplicateReference, SourceBankLeaseLost, MixerLeaseLost,
  MixerReferenceInvalid, SnapshotReferenceInvalid,
  MixerGroupReferenceInvalid, RuntimeTimeInvalid,
  RealtimeMovedBackwards, UnexpectedRuntimeException,
  RuntimeHostDestroyed
```

See [Diagnostics and Troubleshooting](diagnostics-and-troubleshooting.md).

## Authoring assets

| Type | Ownership |
| --- | --- |
| `OrpheusAudioEvent` | One authored Event definition |
| `OrpheusAudioCatalog` | Explicit Event set bound to a Session |
| `OrpheusAudioSettings` | Mixer, groups, snapshots, and durations |
| `OrpheusAudioRuntimeHost` | Session carrier and tick owner |
| `OrpheusAudioSourceBank` | Fixed 24-source physical bank |
| `OrpheusAudioKeyManifest` | Host Audio Key truth |
| `OrpheusAudioValidationProfile` | One validated Host integration set |
| `OrpheusAudioModuleRecipe` | Editor-only module grouping for generated Events |
| `OrpheusAudioModuleEventRecipe` | Serialized Event input inside a Module Recipe |
| `OrpheusAudioAuthoringProfile` | Enrollment input binding Manifest, Recipes, Catalog, and generated root |

See [Authoring and Validation](authoring-and-validation.md) for asset rules.

## Contract baselines

The exhaustive machine-readable surface is stored in:

- `Tests/Baselines/PublicApi.v1.txt`;
- `Tests/Baselines/SerializationAbi.v1.txt`;
- `Tests/Baselines/GeneratedKeys.v1.golden.cs.txt`.

Those files define compatibility. This page defines intended consumer use.
