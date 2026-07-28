using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        public void ApplyProfile(OrpheusAudioProfileIntent intent)
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

            if (!OrpheusAudioProfilePolicy.TryApplyProfile(
                    _profileState,
                    intent,
                    _catalogSnapshot.CoreIndex,
                    _bgmFailedBlocked,
                    _profileAmbienceFailedBlocked,
                    out var decision))
            {
                return;
            }

            if (!decision.Commit)
            {
                _bgmRetryRequested |= decision.RetryBgm;
                _profileAmbienceRetryRequested |= decision.RetryProfileAmbience;
                RetryBlockedBgmIntent();
                RetryBlockedProfileAmbienceIntent();
                return;
            }

            var previous = _profileState;
            var bgmChanged = previous.Intent.BgmKey != decision.State.Intent.BgmKey;
            var profileAmbienceChanged =
                previous.Intent.ProfileAmbienceKey != decision.State.Intent.ProfileAmbienceKey;
            _profileState = decision.State;
            _lastCommittedProfileState = decision.State;
            if (bgmChanged)
            {
                _bgmFailedBlocked = false;
                _bgmRetryRequested = false;
                ReconcileCommittedBgmIntent(decision.State.Intent.BgmKey);
            }

            if (profileAmbienceChanged)
            {
                _profileAmbienceFailedBlocked = false;
                _profileAmbienceRetryRequested = false;
                ReconcileCommittedProfileAmbienceIntent(decision.State.Intent.ProfileAmbienceKey);
            }

            ApplyActiveSnapshotIfChanged(previous.EffectiveSnapshot);
        }

        public void SetOverlay(OrpheusOverlay selector, bool enabled)
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

            if (!OrpheusAudioProfilePolicy.TrySetOverlay(
                    _profileState,
                    selector,
                    enabled,
                    out var decision))
            {
                SaturatingIncrement(ref _invalidValueRejectedBits);
                return;
            }

            if (!decision.Commit)
            {
                return;
            }

            var previousSnapshot = _profileState.EffectiveSnapshot;
            _profileState = decision.State;
            _lastCommittedProfileState = decision.State;
            ApplyActiveSnapshotIfChanged(previousSnapshot);
        }

        internal void MarkProfilePersistentFailed(
            OrpheusPlaybackKind playbackKind,
            OrpheusAudioKey key)
        {
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (key.IsValid && playbackKind == OrpheusPlaybackKind.Bgm &&
                _profileState.Intent.BgmKey == key)
            {
                _bgmFailedBlocked = true;
                return;
            }

            if (key.IsValid && playbackKind == OrpheusPlaybackKind.ProfileAmbience &&
                _profileState.Intent.ProfileAmbienceKey == key)
            {
                _profileAmbienceFailedBlocked = true;
            }
        }

        internal bool TryConsumeProfilePersistentRetry(
            OrpheusPlaybackKind playbackKind,
            OrpheusAudioKey key)
        {
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return false;
            }

            if (playbackKind == OrpheusPlaybackKind.Bgm &&
                _bgmFailedBlocked && _bgmRetryRequested &&
                _profileState.Intent.BgmKey == key)
            {
                _bgmFailedBlocked = false;
                _bgmRetryRequested = false;
                return true;
            }

            if (playbackKind == OrpheusPlaybackKind.ProfileAmbience &&
                _profileAmbienceFailedBlocked && _profileAmbienceRetryRequested &&
                _profileState.Intent.ProfileAmbienceKey == key)
            {
                _profileAmbienceFailedBlocked = false;
                _profileAmbienceRetryRequested = false;
                return true;
            }

            return false;
        }

        private void ApplyActiveSnapshotIfChanged(OrpheusEffectiveSnapshot previousSnapshot)
        {
            if (previousSnapshot == _profileState.EffectiveSnapshot ||
                !_activationState.ActivationReady ||
                GetTransportState() != OrpheusTransportState.Active)
            {
                return;
            }

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            ApplySnapshot(_profileState.EffectiveSnapshot, _snapshotTransitionSeconds);
        }
    }
}
