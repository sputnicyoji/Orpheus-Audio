using System;
using System.Runtime.CompilerServices;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    public readonly struct OrpheusBootstrapAuthority : IEquatable<OrpheusBootstrapAuthority>
    {
        private readonly OrpheusAudioManager _manager;
        private readonly uint _generation;

        internal OrpheusBootstrapAuthority(OrpheusAudioManager manager, uint generation)
        {
            _manager = manager;
            _generation = generation;
        }

        internal bool Matches(OrpheusAudioManager manager, uint generation)
        {
            return ReferenceEquals(_manager, manager) && _generation == generation && _generation != 0;
        }

        internal bool IsDefault => ReferenceEquals(_manager, null) && _generation == 0;

        public bool Equals(OrpheusBootstrapAuthority other)
        {
            return ReferenceEquals(_manager, other._manager) && _generation == other._generation;
        }

        public override bool Equals(object obj)
        {
            return obj is OrpheusBootstrapAuthority other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked(((_manager == null ? 0 : RuntimeHelpers.GetHashCode(_manager)) * 397) ^
                             (int)_generation);
        }

        public static bool operator ==(OrpheusBootstrapAuthority left, OrpheusBootstrapAuthority right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OrpheusBootstrapAuthority left, OrpheusBootstrapAuthority right)
        {
            return !left.Equals(right);
        }
    }

    public sealed partial class OrpheusAudioManager
    {
        private const string MasterVolumeParameter = "MasterVolume";
        private const string MusicVolumeParameter = "MusicVolume";
        private const string SfxCombatVolumeParameter = "SfxCombatVolume";
        private const string SfxWorldVolumeParameter = "SfxWorldVolume";
        private const string SfxUiVolumeParameter = "SfxUiVolume";
        private const string AmbienceVolumeParameter = "AmbienceVolume";

        public void SetUserGain(OrpheusBus bus, float linearGain)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (!OrpheusAudioUserGainPolicy.TrySet(_userGains, bus, linearGain, out var updated))
            {
                SaturatingIncrement(ref _invalidValueRejectedBits);
                return;
            }

            if (_activationState.HostReady &&
                (!ValidateLiveCarrierOrFailClosed() ||
                 !ApplyUserGain(linearGain, GetParameterName(bus))))
            {
                return;
            }

            _userGains = updated;
        }

        public bool TryGetUserGain(OrpheusBus bus, out float linearGain)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                linearGain = 0f;
                return false;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                linearGain = 0f;
                return false;
            }

            return OrpheusAudioUserGainPolicy.TryGet(_userGains, bus, out linearGain);
        }

        public OrpheusBootstrapAuthority CaptureBootstrapAuthority()
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return default;
            }

            return _lifecycle == OrpheusAudioLifecycle.Running
                ? new OrpheusBootstrapAuthority(this, _bootstrapGeneration)
                : default;
        }

        public bool CompleteBootstrapHydration(OrpheusBootstrapAuthority authority)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return false;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return false;
            }

            if (authority.IsDefault)
            {
                return false;
            }

            if (!authority.Matches(this, _bootstrapGeneration))
            {
                SaturatingIncrement(ref _staleBootstrapTokenRejectedBits);
                return false;
            }

            if (_activationState.BootstrapHydrated)
            {
                return true;
            }

            if (_activationState.HostReady &&
                (!ValidateLiveCarrierOrFailClosed() || !ApplyInitialSnapshot()))
            {
                return false;
            }

            _activationState.CompleteBootstrapHydration();
            return true;
        }

        public OrpheusCompleteHostReadyResult CompleteHostReady()
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return OrpheusCompleteHostReadyResult.Invalid;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return OrpheusCompleteHostReadyResult.InvalidLifecycle;
            }

            if (_activationState.HostReady)
            {
                return OrpheusCompleteHostReadyResult.AlreadyCompleted;
            }

            if (!_runtimeHost.HasStarted)
            {
                return OrpheusCompleteHostReadyResult.RuntimeHostNotStarted;
            }

            if (!_isBound)
            {
                return OrpheusCompleteHostReadyResult.RuntimeHostNotBound;
            }

            var listener = _listener;
            if (listener == null)
            {
                SetListenerAvailable(false);
                return OrpheusCompleteHostReadyResult.ListenerNotBound;
            }

            var position = listener.transform.position;
            if (!IsFinite(position))
            {
                FailClosed(OrpheusAudioDisableReason.ListenerPositionNonFinite);
                return OrpheusCompleteHostReadyResult.DisabledByValidationFailure;
            }

            if (!listener.isActiveAndEnabled)
            {
                SetListenerAvailable(false);
                return OrpheusCompleteHostReadyResult.ListenerNotBound;
            }

            _cachedListenerPosition = position;
            SetListenerAvailable(true);

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return OrpheusCompleteHostReadyResult.DisabledByValidationFailure;
            }

            if (!ApplyAllUserGains())
            {
                return OrpheusCompleteHostReadyResult.DisabledByValidationFailure;
            }

            if (_activationState.BootstrapHydrated && !ApplyInitialSnapshot())
            {
                return OrpheusCompleteHostReadyResult.DisabledByValidationFailure;
            }

            _activationState.CompleteHostReady();
            return OrpheusCompleteHostReadyResult.Completed;
        }

        public bool BindListener(AudioListener listener)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return false;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || listener == null)
            {
                return false;
            }

            if (_listener != null)
            {
                return ReferenceEquals(_listener, listener);
            }

            if (listener.isActiveAndEnabled)
            {
                if (_activationState.HostReady)
                {
                    var position = listener.transform.position;
                    if (!IsFinite(position))
                    {
                        FailClosed(OrpheusAudioDisableReason.ListenerPositionNonFinite);
                        return false;
                    }

                    _cachedListenerPosition = position;
                }

                _listener = listener;
                SetListenerAvailable(true);
            }
            else
            {
                _listener = listener;
                SetListenerAvailable(false);
            }

            return _lifecycle == OrpheusAudioLifecycle.Running &&
                   ReferenceEquals(_listener, listener);
        }

        public bool ReplaceListener(AudioListener expectedOldListener, AudioListener replacementListener)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return false;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running ||
                expectedOldListener == null || replacementListener == null ||
                !ReferenceEquals(_listener, expectedOldListener) ||
                !replacementListener.isActiveAndEnabled)
            {
                return false;
            }

            var position = replacementListener.transform.position;
            if (!IsFinite(position))
            {
                FailClosed(OrpheusAudioDisableReason.ListenerPositionNonFinite);
                return false;
            }

            _listener = replacementListener;
            _cachedListenerPosition = position;
            SetListenerAvailable(true);
            return _lifecycle == OrpheusAudioLifecycle.Running &&
                   ReferenceEquals(_listener, replacementListener);
        }

        public bool RemoveListener(AudioListener expectedListener)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return false;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || expectedListener == null ||
                !ReferenceEquals(_listener, expectedListener))
            {
                return false;
            }

            _listener = null;
            _cachedListenerPosition = default;
            SetListenerAvailable(false);
            return true;
        }

        internal void Tick(double realtime, float unscaledDeltaTime)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || !_isBound)
            {
                return;
            }

            if (!ValidateLiveCarrierOrFailClosed())
            {
                return;
            }

            if (!IsFiniteNonNegative(realtime) ||
                float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                FailClosed(OrpheusAudioDisableReason.RuntimeTimeInvalid);
                return;
            }

            if (realtime < _lastRealtime)
            {
                FailClosed(OrpheusAudioDisableReason.RealtimeMovedBackwards);
                return;
            }

            _lastRealtime = realtime;
            TickLoadObservations(realtime);
            var transportActive = GetTransportState() == OrpheusTransportState.Active;
            var activeDelta = transportActive && _wasTransportActive
                ? unscaledDeltaTime
                : 0f;
            _wasTransportActive = transportActive;
            AdvanceTransient3D(activeDelta);
            AdvanceTransient2D(activeDelta);
            TickBgm(realtime, activeDelta);
            TickProfileAmbience(realtime, activeDelta);
            TickGlobalLoops(realtime, activeDelta);
        }

        internal void LateTick()
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || !_isBound)
            {
                return;
            }

            if (!ValidateLiveCarrierOrFailClosed())
            {
                return;
            }

            RefreshTransient3DWorldPositions();
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            var listener = _listener;
            if (listener == null)
            {
                SetListenerAvailable(false);
                return;
            }

            var position = listener.transform.position;
            if (!IsFinite(position))
            {
                FailClosed(OrpheusAudioDisableReason.ListenerPositionNonFinite);
                return;
            }

            if (!listener.isActiveAndEnabled)
            {
                SetListenerAvailable(false);
                return;
            }

            _cachedListenerPosition = position;
            SetListenerAvailable(true);
        }

        private OrpheusAudioDisableReason ValidateCarrier()
        {
            if (!HasSourceBankLease() ||
                !ReferenceEquals(_runtimeHost.SourceBank, _sourceBank))
            {
                return OrpheusAudioDisableReason.SourceBankLeaseLost;
            }

            var sourceReason = _sourceBank.ValidateCapturedReferences(_ownedSources);
            if (sourceReason != OrpheusAudioDisableReason.None)
            {
                return sourceReason;
            }

            if (!HasMixerLease())
            {
                return OrpheusAudioDisableReason.MixerLeaseLost;
            }

            try
            {
                var currentIdentity = _mixer.LeaseIdentity;
                if (currentIdentity == null || !ReferenceEquals(currentIdentity, _mixerIdentity))
                {
                    return OrpheusAudioDisableReason.MixerReferenceInvalid;
                }

                if (!_mixerGroups.HasValidMixerIdentity(_mixerIdentity))
                {
                    return OrpheusAudioDisableReason.MixerGroupReferenceInvalid;
                }

                if (_mixer is OrpheusUnityMixerPort unityMixer)
                {
                    unityMixer.ValidateSnapshotReferences();
                }

                return OrpheusAudioDisableReason.None;
            }
            catch (OrpheusAudioCarrierException exception)
            {
                return exception.DisableReason;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return OrpheusAudioDisableReason.UnexpectedRuntimeException;
            }
        }

        private bool ValidateLiveCarrierOrFailClosed()
        {
            var reason = ValidateCarrier();
            if (reason == OrpheusAudioDisableReason.None)
            {
                return true;
            }

            FailClosed(reason);
            return false;
        }

        private bool ApplyAllUserGains()
        {
            return ApplyUserGain(_userGains.Master, MasterVolumeParameter) &&
                   ApplyUserGain(_userGains.Music, MusicVolumeParameter) &&
                   ApplyUserGain(_userGains.SfxCombat, SfxCombatVolumeParameter) &&
                   ApplyUserGain(_userGains.SfxWorld, SfxWorldVolumeParameter) &&
                   ApplyUserGain(_userGains.SfxUi, SfxUiVolumeParameter) &&
                   ApplyUserGain(_userGains.Ambience, AmbienceVolumeParameter);
        }

        private bool ApplyUserGain(float linearGain, string parameterName)
        {
            try
            {
                if (_mixer.SetFloat(parameterName, OrpheusAudioUserGainPolicy.ToDecibels(linearGain)))
                {
                    return true;
                }

                SaturatingIncrement(ref _setFloatFailedBits);
                FailClosed(OrpheusAudioDisableReason.MixerSetFloatFailed);
                return false;
            }
            catch (OrpheusAudioCarrierException exception)
            {
                FailClosed(exception.DisableReason);
                return false;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return false;
            }
        }

        private bool ApplyInitialSnapshot()
        {
            return ApplySnapshot(_profileState.EffectiveSnapshot, 0f);
        }

        private bool ApplySnapshot(
            OrpheusEffectiveSnapshot snapshot,
            float transitionSeconds)
        {
            try
            {
                _mixer.TransitionTo(snapshot, transitionSeconds);
                return true;
            }
            catch (OrpheusAudioCarrierException exception)
            {
                FailClosed(exception.DisableReason);
                return false;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return false;
            }
        }

        private static string GetParameterName(OrpheusBus bus)
        {
            switch (bus)
            {
                case OrpheusBus.Master:
                    return MasterVolumeParameter;
                case OrpheusBus.Music:
                    return MusicVolumeParameter;
                case OrpheusBus.SfxCombat:
                    return SfxCombatVolumeParameter;
                case OrpheusBus.SfxWorld:
                    return SfxWorldVolumeParameter;
                case OrpheusBus.SfxUi:
                    return SfxUiVolumeParameter;
                case OrpheusBus.Ambience:
                    return AmbienceVolumeParameter;
                default:
                    return null;
            }
        }

        private static bool IsFinite(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z);
        }
    }
}
