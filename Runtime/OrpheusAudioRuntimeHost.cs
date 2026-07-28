using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    public sealed class OrpheusAudioRuntimeHost : MonoBehaviour
    {
        [SerializeField] private OrpheusAudioSourceBank _sourceBank;

        private OrpheusAudioManager _reservedManager;
        private OrpheusAudioManager _boundManager;
        private WeakReference<OrpheusAudioManager> _lastUnboundManager;
        private bool _hasLastUnboundManager;
        private bool _hasStarted;
        private OrpheusRecoveryPendingReason _configurationPendingReasons;
        private bool _configurationSubscribed;
        private bool _ownedResetInFlight;

        internal OrpheusAudioSourceBank SourceBank => _sourceBank;
        internal bool HasStarted => _hasStarted;

        internal double ReadRealtime()
        {
            return Time.realtimeSinceStartupAsDouble;
        }

        public bool Bind(OrpheusAudioManager manager)
        {
            if (manager == null)
            {
                return false;
            }

            if (!OrpheusMainThread.IsCurrent)
            {
                manager.RejectWrongThread();
                return false;
            }

            if (ReferenceEquals(_boundManager, manager))
            {
                return true;
            }

            if (!manager.IsAvailableForBinding(this))
            {
                return false;
            }

            if (_boundManager != null || !ReferenceEquals(_reservedManager, manager) ||
                !manager.TryMarkBound(this, Time.realtimeSinceStartupAsDouble))
            {
                return false;
            }

            _boundManager = manager;
            SubscribeAudioConfiguration();
            return true;
        }

        public bool Unbind(OrpheusAudioManager expectedManager)
        {
            if (expectedManager == null)
            {
                return false;
            }

            if (!OrpheusMainThread.IsCurrent)
            {
                expectedManager.RejectWrongThread();
                return false;
            }

            if (_boundManager == null && _reservedManager == null &&
                WasLastUnbound(expectedManager))
            {
                return true;
            }

            if (!ReferenceEquals(_reservedManager, expectedManager) ||
                !ReferenceEquals(_boundManager, expectedManager) ||
                !expectedManager.TryReleaseAfterHostUnbind(this))
            {
                return false;
            }

            UnsubscribeAudioConfiguration();
            _configurationPendingReasons = OrpheusRecoveryPendingReason.None;
            _ownedResetInFlight = false;
            _boundManager = null;
            _reservedManager = null;
            RememberLastUnbound(expectedManager);
            return true;
        }

        // Requires PrepareUnboundIdentitySlot(manager) on this Host immediately beforehand.
        internal bool TryReserve(OrpheusAudioManager manager)
        {
            if (_reservedManager != null || manager == null || _lastUnboundManager == null)
            {
                return false;
            }

            _reservedManager = manager;
            return true;
        }

        internal void PrepareUnboundIdentitySlot(OrpheusAudioManager manager)
        {
            if (_lastUnboundManager == null)
            {
                _lastUnboundManager = new WeakReference<OrpheusAudioManager>(manager);
            }
        }

        internal void ClearUnboundReservation(OrpheusAudioManager expectedManager)
        {
            if (ReferenceEquals(_reservedManager, expectedManager) && _boundManager == null)
            {
                _reservedManager = null;
                RememberLastUnbound(expectedManager);
            }
        }

        private void Start()
        {
            _hasStarted = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HandleApplicationFocusChanged(hasFocus);
        }

        private void OnApplicationPause(bool paused)
        {
            HandleApplicationPauseChanged(paused);
        }

        internal void HandleApplicationFocusChanged(bool hasFocus)
        {
            HandleApplicationSuspensionChanged(
                OrpheusSuspensionReason.FocusLost,
                !hasFocus);
        }

        internal void HandleApplicationPauseChanged(bool paused)
        {
            HandleApplicationSuspensionChanged(
                OrpheusSuspensionReason.ApplicationPaused,
                paused);
        }

        private void HandleApplicationSuspensionChanged(
            OrpheusSuspensionReason reason,
            bool active)
        {
            var manager = _reservedManager;
            if (manager == null)
            {
                return;
            }

            if (!OrpheusMainThread.IsCurrent)
            {
                manager.RejectWrongThread();
                return;
            }

            manager.HandleRuntimeHostSuspensionInput(
                this,
                reason,
                active,
                Application.platform == RuntimePlatform.Android);
        }

        private void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            RecordAudioConfigurationChanged(deviceWasChanged);
        }

        internal void RecordAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (_boundManager == null)
            {
                return;
            }

            var incoming = !deviceWasChanged && _ownedResetInFlight
                ? OrpheusRecoveryPendingReason.SelfResetNotification
                : OrpheusRecoveryPendingReason.ExternalConfiguration;
            _configurationPendingReasons = OrpheusAudioRecoveryPolicy.Coalesce(
                _configurationPendingReasons,
                incoming);
        }

        internal void BeginOwnedAudioReset(OrpheusAudioManager expectedManager)
        {
            if (ReferenceEquals(_boundManager, expectedManager))
            {
                _ownedResetInFlight = true;
            }
        }

        internal void EndOwnedAudioReset(OrpheusAudioManager expectedManager)
        {
            if (ReferenceEquals(_boundManager, expectedManager))
            {
                _ownedResetInFlight = false;
            }
        }

        internal void ConsumePendingAudioConfigurationChanges()
        {
            var manager = _boundManager;
            if (manager == null)
            {
                return;
            }

            if (!OrpheusMainThread.IsCurrent)
            {
                manager.RejectWrongThread();
                return;
            }

            var pending = _configurationPendingReasons;
            _configurationPendingReasons = OrpheusRecoveryPendingReason.None;
            manager.HandleRuntimeHostAudioConfiguration(this, pending);
        }

        internal OrpheusRecoveryPendingReason PeekAudioConfigurationPending()
        {
            return _configurationPendingReasons;
        }

        private void SubscribeAudioConfiguration()
        {
            if (_configurationSubscribed)
            {
                return;
            }

            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;
            _configurationSubscribed = true;
        }

        private void UnsubscribeAudioConfiguration()
        {
            if (!_configurationSubscribed)
            {
                return;
            }

            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
            _configurationSubscribed = false;
        }

        private void Update()
        {
            var manager = _boundManager;
            if (manager == null)
            {
                return;
            }

            ConsumePendingAudioConfigurationChanges();
            manager.Tick(Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            var manager = _boundManager;
            if (manager == null)
            {
                return;
            }

            manager.LateTick();
        }

        private bool WasLastUnbound(OrpheusAudioManager expectedManager)
        {
            return _hasLastUnboundManager && _lastUnboundManager != null &&
                   _lastUnboundManager.TryGetTarget(out var manager) &&
                   ReferenceEquals(manager, expectedManager);
        }

        private void RememberLastUnbound(OrpheusAudioManager manager)
        {
            if (_lastUnboundManager != null)
            {
                _lastUnboundManager.SetTarget(manager);
                _hasLastUnboundManager = true;
            }
        }

        private void OnDestroy()
        {
            var manager = _reservedManager;
            try
            {
                if (manager != null)
                {
                    manager.HandleRuntimeHostDestroyed(this);
                }
            }
            finally
            {
                UnsubscribeAudioConfiguration();
                _configurationPendingReasons = OrpheusRecoveryPendingReason.None;
                _ownedResetInFlight = false;
                if (manager != null)
                {
                    manager.ForceReleaseLeases(this);
                }
                _boundManager = null;
                _reservedManager = null;
                if (manager != null)
                {
                    RememberLastUnbound(manager);
                }
            }
        }
    }
}
