# Diagnostics and Troubleshooting

Orpheus fails closed. Runtime failure stops or rejects audio while gameplay
continues.

## First checks

1. Run `Tools > Orpheus > Validate Audio Profiles`.
2. Confirm typed keys were regenerated after every Manifest change.
3. Confirm the Runtime Host, Listener, and active Validation Profile belong to
   the same imported Host set.
4. Confirm `CompleteHostReady` returned `Completed` or `AlreadyCompleted`.
5. Read `TryGetDiagnostics` before changing assets.

## Creation failure

```csharp
var result = OrpheusAudioFactory.Create(
    runtimeHost,
    settings,
    catalog,
    initialUserGains,
    out var manager);

if (!result.Success)
{
    var code = result.ErrorCode;
    var key = result.RelatedKey;
    var index = result.RelatedIndex;
}
```

| Error | Check |
| --- | --- |
| `InvalidArguments` | Main thread, non-null Runtime Host, and one Manager per Host |
| `InvalidInitialGain` | Six finite linear values in `[0,1]` |
| `InvalidSettings` | Current Settings schema and finite durations in `0.015s..5s` |
| `InvalidMixerContract` | Non-null Mixer reference, stable Mixer identity, and no Mixer lease conflict |
| `InvalidSourceBank` | Exact fixed roles and one clean `AudioSource` per leaf |
| `InvalidCatalog` | Non-null, sorted Catalog with valid Events |
| `DuplicateKey` | One Event per active key |
| `InvalidEvent` | Playback, category, loading, distance, variation policy, and duplicate Clip references |
| `InvalidClipReference` | Non-null, readable Clip references |
| `InvalidAudioConfiguration` | Valid DSP buffer, sample rate, and voice limits |

Failure returns a null Manager.

## Readiness

```csharp
manager.TryGetDiagnostics(out var diagnostics);
```

Playback requires:

```text
HostReady
BootstrapHydrated
ActivationReady
PlaybackReady
```

`HostReady` does not imply `BootstrapHydrated`. Complete both.

If `CompleteHostReady` returns:

| Result | Repair |
| --- | --- |
| `RuntimeHostNotStarted` | Wait until the Runtime Host has observed `Start` |
| `RuntimeHostNotBound` | Call `runtimeHost.Bind(manager)` |
| `ListenerNotBound` | Bind the explicit Scene Listener |
| `InvalidLifecycle` | Do not reuse a Disabled or Disposed Manager |
| `DisabledByValidationFailure` | Inspect `DisableReason` and authoring validation |

## Silent one-shot rejection

`Play` and `PlayAt` return `void`. Inspect the saturating counters:

- invalid or unknown key;
- wrong playback kind;
- non-finite 3D position;
- transport suspended;
- not Playback Ready;
- content not loaded;
- cooldown or polyphony rejection;
- no available source after admission.

Do not branch gameplay on those counters. They exist for diagnosis and
telemetry snapshots.

## Explicit loading

```csharp
var prepare = manager.Prepare(key);
var state = manager.GetLoadState(key);
```

- `BootstrapTransient` loads during creation.
- `ExplicitTransient` requires `Prepare`.
- persistent streams load from recorded intent.
- `GetLoadState` returns `Invalid` for invalid, unavailable, or wrong-thread
  requests.

## Suspension

`SuspensionReasons` can contain:

```text
FocusLost
ApplicationPaused
ListenerMissing
```

Orpheus suspends only Owned Sources. It never writes `AudioListener.pause`.
Restore the missing Host condition instead of manually restarting sources.

## Disabled lifecycle

`DisableReason` identifies fail-closed ownership or runtime corruption:

- destroyed or replaced Owned Source;
- Source Bank or Mixer lease loss;
- invalid Mixer, snapshot, or group reference;
- non-finite runtime time or Listener position;
- Runtime Host destruction;
- unexpected non-catastrophic runtime exception.

Disabled is terminal. Save external state if available, dispose, unbind, and
create a new Audio Session after repairing the Host.

Common Mixer-related disable reasons:

| Disable reason | Check |
| --- | --- |
| `MixerGroupReferenceInvalid` | All eleven groups belong to the configured Mixer |
| `SnapshotReferenceInvalid` | Peace, Combat, Menu, and Pause belong to the configured Mixer |
| `MixerSetFloatFailed` | All six documented exposed parameters exist and accept writes |

## No audible output

If Diagnostics reports Playback Ready and no rejection:

1. clear `Mute Audio` in Game view;
2. verify the operating-system output route and volume;
3. verify the Event Clip imports with the documented load policy;
4. verify Mixer routing and user gains;
5. replay the `Playable One Shot` sample before changing production content.

Do not use audibility alone as proof that ownership, loading, or recovery is
correct.
