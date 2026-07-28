# Runtime Integration

An `OrpheusAudioManager` is one non-static, single-use Audio Session. The Host
Composition Root owns creation, binding, readiness, Scene handoff, persistence,
and teardown.

All public calls are main-thread-only. Orpheus never marshals a wrong-thread
call.

## Assembly references

A Host runtime asmdef references:

```text
Orpheus.Audio
Orpheus.Audio.Core
```

A gameplay assembly using generated keys also references:

```text
Orpheus.Audio.Generated
```

Generate typed keys before adding that final reference to a new Host.

## Public Contract v1

Package `0.2.2` freezes the supported owners listed below, their method
signatures, serialized Host asset fields, and the deterministic generated
`OrpheusAudioKeys` shape. Types not exposed by those owners are implementation
details. Do not construct Catalog entries, authoring policy values, clip
selection results, or lookup indexes from gameplay code.

The package contract tests compare the compiled surface with checked-in
baselines. A deliberate breaking change requires a new package compatibility
line and migration notes.

## Session startup

Use this order:

```csharp
var createResult = OrpheusAudioFactory.Create(
    runtimeHost,
    settings,
    catalog,
    initialUserGains,
    out var manager);

if (!createResult.Success)
{
    return;
}

if (!runtimeHost.Bind(manager) || !manager.BindListener(listener))
{
    manager.Dispose();
    runtimeHost.Unbind(manager);
    return;
}

var authority = manager.CaptureBootstrapAuthority();
if (!manager.CompleteBootstrapHydration(authority))
{
    manager.RemoveListener(listener);
    manager.Dispose();
    runtimeHost.Unbind(manager);
    return;
}

_manager = manager;
if (!OrpheusAudioBridge.Bind(manager))
{
    Teardown();
    return;
}

// Bind OrpheusAudioRawBridge only when the Host has a raw-int consumer.
```

Call `CompleteHostReady` only after the Runtime Host has observed `Start`.

```csharp
private IEnumerator Start()
{
    yield return null;

    var result = _manager == null
        ? OrpheusCompleteHostReadyResult.Invalid
        : _manager.CompleteHostReady();
    if (result != OrpheusCompleteHostReadyResult.Completed &&
        result != OrpheusCompleteHostReadyResult.AlreadyCompleted)
    {
        Teardown();
    }
}
```

`HostReady` and `BootstrapHydrated` are independent barriers. Playback Ready
requires both barriers and active transport. A bound, enabled Listener keeps
transport active. Clip load state is separate; each playback request still
requires Loaded content.

Check `OrpheusAudioInitResult.ErrorCode`, `RelatedKey`, and `RelatedIndex` on a
failed create. Failure always returns a null Manager.

## Public API by owner

| Owner | Surface |
| --- | --- |
| Gameplay one-shot | `OrpheusAudioBridge.Play`, `PlayAt` |
| Raw-int integration | `OrpheusAudioRawBridge.Play`, `PlayAt` |
| Host content state | `ApplyProfile`, `SetOverlay`, `PlayLoop`, `StopLoop`, `StopAll` |
| Host loading | `Prepare`, `GetLoadState` |
| Host preferences | `SetUserGain`, `TryGetUserGain` |
| Host lifecycle | bootstrap authority, Host Ready, Listener binding, Diagnostics, `Dispose` |

Bridges expose only one-shot playback. They do not expose Profile Intent,
gain, loops, `StopAll`, loading, Diagnostics, or lifecycle.

## One-shot playback

The names below illustrate Host-generated keys. The bundled sample generates
only `OrpheusAudioKeys.PlayableOneShot`.

```csharp
manager.Play(OrpheusAudioKeys.UiConfirm);
manager.PlayAt(OrpheusAudioKeys.WorldExplosion, worldPosition);
```

`Play` accepts only `OneShot2D`. `PlayAt` accepts only `OneShot3D` and a finite
position. Both return `void`; gameplay never branches on audio admission.

Rejected playback stays silent and increments exactly one Diagnostics counter.

## Explicit loading

`BootstrapTransient` content preloads during session creation.

For `ExplicitTransient`:

```csharp
var result = manager.Prepare(OrpheusAudioKeys.MissionComplete);
var state = manager.GetLoadState(OrpheusAudioKeys.MissionComplete);
```

Wait for `Loaded` before expecting playback. `Prepare` returns a structured
result. `GetLoadState` returns `Invalid` for an invalid, unavailable, or
wrong-thread key.

Persistent BGM, Profile Ambience, and Global Loops use `PersistentStream`.
Their intent may be recorded while loading or suspended; physical playback
waits for Playback Ready and Loaded state.

## Profile Intent and overlays

Profile Intent is the only write surface for Profile ID, Base State, BGM, and
Profile Ambience:

```csharp
manager.ApplyProfile(new OrpheusAudioProfileIntent(
    profileId,
    OrpheusBaseState.Combat,
    OrpheusAudioKeys.CombatBgm,
    OrpheusAudioKeys.BattlefieldAmbience));
```

Use `OrpheusAudioKey.Invalid` to request no BGM or no Profile Ambience.
`profileId` must be non-zero for public intent. Invalid intent rejects
atomically. Repeating identical intent is a no-op except for the defined
failed-load retry.

Overlays are independent:

```csharp
manager.SetOverlay(OrpheusOverlay.Menu, true);
manager.SetOverlay(OrpheusOverlay.Pause, true);
```

Pass exactly one selector per call. Pause wins effective-snapshot arbitration
when Menu and Pause are both enabled.

## Global loops and StopAll

```csharp
manager.PlayLoop(OrpheusAudioKeys.RainLoop);
manager.StopLoop(OrpheusAudioKeys.RainLoop);
manager.StopAll(OrpheusBus.SfxWorld);
```

Global Loop registry capacity is four. `StopAll` is a Host operation by Bus.
It does not clear unrelated intent.

## User gain

All six gains are linear values in `[0,1]`:

```csharp
manager.SetUserGain(OrpheusBus.Music, 0.8f);
manager.TryGetUserGain(OrpheusBus.Music, out var musicGain);
```

The Host loads gains before Factory creation and saves them before teardown.
Preference keys and storage policy do not belong in package Settings.

## Listener handoff

Orpheus never searches for a Listener and never changes
`AudioListener.enabled`.

For additive Scene handoff:

1. validate and enable the replacement Listener;
2. disable the old Listener;
3. call `ReplaceListener(expectedOld, replacement)` before the next frame.

Removal suspends transport with `ListenerMissing`. It does not globally pause
Unity audio.

## Diagnostics and silent degradation

```csharp
if (manager.TryGetDiagnostics(out var diagnostics))
{
    var ready = diagnostics.PlaybackReady;
    var reason = diagnostics.DisableReason;
    var rejected = diagnostics.Counters.InvalidKeyRejected;
}
```

Diagnostics is the fixed snapshot truth for lifecycle, transport, readiness,
Profile, Overlay, persistent keys, execution counts, and saturating counters.
It remains available after Disabled and Disposed.

Audio failure never blocks gameplay, subtitles, rewards, damage, tasks, or
cutscenes. Do not wait on audio completion to advance game state.

## Teardown

Use explicit Host order:

```csharp
saveGains();
OrpheusAudioRawBridge.Unbind(manager); // only if bound
OrpheusAudioBridge.Unbind(manager);
manager.RemoveListener(listener);
manager.Dispose();
runtimeHost.Unbind(manager);
```

Identity-safely unbind any typed or raw Bridge before disposal.
`OnDestroy` may provide best-effort cleanup, but it is not the clean shutdown
contract because Unity does not define destruction order across Scene roots.

A disposed Manager is terminal. Create a new Audio Session instead of resetting
or reinitializing it.
