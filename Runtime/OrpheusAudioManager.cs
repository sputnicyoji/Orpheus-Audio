using System;
using System.Threading;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    internal readonly struct OrpheusAudioSessionContext
    {
        internal OrpheusAudioSessionContext(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSourceBank sourceBank,
            AudioSource[] ownedSources,
            OrpheusAudioSettings settings,
            OrpheusRuntimeCatalogSnapshot catalogSnapshot,
            OrpheusAudioUserGains initialUserGains,
            uint randomSeed,
            IOrpheusAudioClipReadiness clipReadiness,
            IOrpheusAudioMixerPort mixer,
            object mixerIdentity,
            IOrpheusAudioSystemPort audioSystem,
            OrpheusAudioSystemConfiguration configuration,
            bool initialFocus)
        {
            RuntimeHost = runtimeHost;
            SourceBank = sourceBank;
            OwnedSources = ownedSources;
            Settings = settings;
            MixerGroups = settings.CaptureMixerGroups();
            SnapshotTransitionSeconds = settings.SnapshotTransitionSeconds;
            CatalogSnapshot = catalogSnapshot;
            InitialUserGains = initialUserGains;
            RandomSeed = randomSeed;
            ClipReadiness = clipReadiness;
            Mixer = mixer;
            MixerIdentity = mixerIdentity;
            AudioSystem = audioSystem;
            Configuration = configuration;
            InitialFocus = initialFocus;
        }

        internal OrpheusAudioRuntimeHost RuntimeHost { get; }
        internal OrpheusAudioSourceBank SourceBank { get; }
        internal AudioSource[] OwnedSources { get; }
        internal OrpheusAudioSettings Settings { get; }
        internal OrpheusAudioMixerGroupSet MixerGroups { get; }
        internal float SnapshotTransitionSeconds { get; }
        internal OrpheusRuntimeCatalogSnapshot CatalogSnapshot { get; }
        internal OrpheusAudioUserGains InitialUserGains { get; }
        internal uint RandomSeed { get; }
        internal IOrpheusAudioClipReadiness ClipReadiness { get; }
        internal IOrpheusAudioMixerPort Mixer { get; }
        internal object MixerIdentity { get; }
        internal IOrpheusAudioSystemPort AudioSystem { get; }
        internal OrpheusAudioSystemConfiguration Configuration { get; }
        internal bool InitialFocus { get; }
    }

    public sealed partial class OrpheusAudioManager : IDisposable
    {
        private readonly OrpheusAudioRuntimeHost _runtimeHost;
        private readonly OrpheusAudioSourceBank _sourceBank;
        private readonly AudioSource[] _ownedSources;
        private readonly OrpheusAudioSettings _settings;
        private readonly OrpheusRuntimeCatalogSnapshot _catalogSnapshot;
        private readonly OrpheusAudioCategoryRoutes _categoryRoutes;
        private readonly OrpheusAudioMixerGroupSet _mixerGroups;
        private readonly IOrpheusAudioClipReadiness _clipReadiness;
        private readonly IOrpheusAudioMixerPort _mixer;
        private readonly object _mixerIdentity;
        private readonly IOrpheusAudioSystemPort _audioSystem;
        private readonly OrpheusLoadStallTracker[] _loadStallTrackers;
        private readonly int[] _activeLoadEntryIndices;
        private readonly int[] _loadEntryActivePositions;
        private readonly OrpheusOneShotSlot[] _transient3DSlots;
        private readonly OrpheusOneShotSlot[] _transient2DSlots;
        private readonly double[] _lastAcceptedRealtime;
        private readonly byte[] _shuffleOrder;
        private readonly byte[] _shuffleCursors;
        private readonly byte[] _shufflePreviousLast;
        private readonly double[] _transient3DDistanceRatios;
        private readonly OrpheusTransientExecutionState[] _transient3DExecutionStates;
        private readonly OrpheusTransientExecutionState[] _transient2DExecutionStates;
        private readonly OrpheusGlobalLoopEntryState[] _globalLoops;
        private readonly int _configuredRealVoiceLimit;
        private readonly int _configuredVirtualVoiceLimit;
        private readonly bool _voiceBudgetDegraded;
        private readonly float _bgmCrossfadeSeconds;
        private readonly float _profileAmbienceCrossfadeSeconds;
        private readonly float _snapshotTransitionSeconds;
        private OrpheusAudioUserGains _userGains;
        private OrpheusAudioProfileState _profileState;
        private OrpheusAudioProfileState _lastCommittedProfileState;
        private bool _bgmFailedBlocked;
        private bool _profileAmbienceFailedBlocked;
        private bool _bgmRetryRequested;
        private bool _profileAmbienceRetryRequested;
        private OrpheusAudioActivationState _activationState;
        private AudioListener _listener;
        private Vector3 _cachedListenerPosition;
        private OrpheusSuspensionReason _suspensionReasons;
        private uint _bootstrapGeneration;
        private double _lastRealtime;

        private OrpheusAudioLifecycle _lifecycle;
        private OrpheusAudioDisableReason _disableReason;
        private bool _isBound;
        private bool _sourceLeaseHeld;
        private bool _mixerLeaseHeld;
        private OrpheusAudioLeaseRegistry.OrpheusAudioLeaseWitness _leaseWitness;
        private long _wrongThreadRejectedBits;
        private long _poolCapacityRejectedBits;
        private long _stolenBits;
        private long _cooldownRejectedBits;
        private long _polyphonyRejectedBits;
        private long _distanceRejectedBits;
        private long _preReadyRejectedBits;
        private long _suspendedRejectedBits;
        private long _unavailableRejectedBits;
        private long _loadNotReadyRejectedBits;
        private long _loadFailedBits;
        private long _invalidKeyRejectedBits;
        private long _invalidRawKeyRejectedBits;
        private long _invalidPositionRejectedBits;
        private long _playbackKindRejectedBits;
        private long _loopRegistryFullBits;
        private long _invalidValueRejectedBits;
        private long _setFloatFailedBits;
        private long _recoveryFailedBits;
        private long _staleBootstrapTokenRejectedBits;
        private long _unexpectedExceptionBits;
        private long _loadStalledBits;
        private int _activeLoadObservationCount;
        private byte _transient3DActiveCount;
        private byte _transient2DActiveCount;
        private byte _fadingCount;
        private byte _pendingCount;
        private OrpheusXorshift32 _random;
        private bool _wasTransportActive;
        private OrpheusRecoveryPendingReason _recoveryPendingReasons;
        private uint _recoveryGeneration;
        private uint _bgmRecoveryRetryGeneration;
        private uint _profileAmbienceRecoveryRetryGeneration;
        private bool _isRecovering;
        private bool _androidManualRecoveryRequested;
        private byte _failedGlobalLoopRecoveryMask;
        private bool _allowRecoveryPersistentStart;

        private OrpheusAudioManager(OrpheusAudioSessionContext context)
        {
            _runtimeHost = context.RuntimeHost;
            _sourceBank = context.SourceBank;
            _ownedSources = context.OwnedSources;
            _settings = context.Settings;
            _catalogSnapshot = context.CatalogSnapshot;
            _categoryRoutes = context.Settings.CaptureCategoryRoutes();
            _mixerGroups = context.MixerGroups;
            _clipReadiness = context.ClipReadiness;
            _mixer = context.Mixer;
            _mixerIdentity = context.MixerIdentity;
            _audioSystem = context.AudioSystem;
            var catalogEntryCount = context.CatalogSnapshot.CoreIndex.Count;
            _loadStallTrackers = new OrpheusLoadStallTracker[catalogEntryCount];
            _activeLoadEntryIndices = new int[catalogEntryCount];
            _loadEntryActivePositions = new int[catalogEntryCount];
            _transient3DSlots = new OrpheusOneShotSlot[OrpheusAudioSourceBank.Transient3DCount];
            _transient2DSlots = new OrpheusOneShotSlot[OrpheusAudioSourceBank.Transient2DCount];
            _lastAcceptedRealtime = new double[catalogEntryCount];
            _shuffleOrder = new byte[context.CatalogSnapshot.FlattenedClipCount];
            _shuffleCursors = new byte[catalogEntryCount];
            _shufflePreviousLast = new byte[catalogEntryCount];
            _transient3DDistanceRatios = new double[OrpheusAudioSourceBank.Transient3DCount];
            _transient3DExecutionStates =
                new OrpheusTransientExecutionState[OrpheusAudioSourceBank.Transient3DCount];
            _transient2DExecutionStates =
                new OrpheusTransientExecutionState[OrpheusAudioSourceBank.Transient2DCount];
            _globalLoops = new OrpheusGlobalLoopEntryState[OrpheusAudioSourceBank.GlobalLoopCount];
            for (var entryIndex = 0; entryIndex < catalogEntryCount; entryIndex++)
            {
                _loadEntryActivePositions[entryIndex] = -1;
                _lastAcceptedRealtime[entryIndex] = -1d;
                _shuffleCursors[entryIndex] = OrpheusTransientVariationPolicy.UninitializedCursor;
                _shufflePreviousLast[entryIndex] = byte.MaxValue;
            }
            for (var slotIndex = 0; slotIndex < OrpheusAudioSourceBank.Transient3DCount; slotIndex++)
            {
                _transient3DExecutionStates[slotIndex].Clear();
            }
            for (var slotIndex = 0; slotIndex < OrpheusAudioSourceBank.Transient2DCount; slotIndex++)
            {
                _transient2DExecutionStates[slotIndex].Clear();
            }
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                _globalLoops[slotIndex].Initialize();
            }
            _random = new OrpheusXorshift32(context.RandomSeed);
            _userGains = context.InitialUserGains;
            _configuredRealVoiceLimit = context.Configuration.NumRealVoices;
            _configuredVirtualVoiceLimit = context.Configuration.NumVirtualVoices;
            _voiceBudgetDegraded = context.Configuration.NumRealVoices < OrpheusAudioSourceBank.TotalSourceCount;
            _bgmCrossfadeSeconds = context.Settings.BgmCrossfadeSeconds;
            _profileAmbienceCrossfadeSeconds = context.Settings.ProfileAmbienceCrossfadeSeconds;
            _snapshotTransitionSeconds = context.SnapshotTransitionSeconds;
            _profileState = OrpheusAudioProfileState.Default;
            _lastCommittedProfileState = OrpheusAudioProfileState.Default;
            InitializeBgmDirector();
            InitializeProfileAmbienceDirector();
            _suspensionReasons = OrpheusAudioSuspensionPolicy.CreateInitialReasons(
                context.InitialFocus,
                false);
            _bootstrapGeneration = 1;
            _lifecycle = OrpheusAudioLifecycle.Running;
            _disableReason = OrpheusAudioDisableReason.None;
        }

        internal static OrpheusAudioManager CreateUnpublished(OrpheusAudioSessionContext context)
        {
            return new OrpheusAudioManager(context);
        }

        public bool TryGetDiagnostics(out OrpheusAudioDiagnostics diagnostics)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                diagnostics = default;
                return false;
            }

            var transport = GetTransportState();
            var suspension = _lifecycle == OrpheusAudioLifecycle.Running
                ? _suspensionReasons
                : OrpheusSuspensionReason.None;
            var readiness = _lifecycle == OrpheusAudioLifecycle.Running
                ? _activationState.GetReadiness(transport)
                : OrpheusReadiness.None;
            var recoveryPending = _lifecycle == OrpheusAudioLifecycle.Running
                ? OrpheusAudioRecoveryPolicy.Coalesce(
                    _recoveryPendingReasons,
                    _runtimeHost.PeekAudioConfigurationPending())
                : OrpheusRecoveryPendingReason.None;
            var counters = new OrpheusAudioDiagnosticsCounters(
                ReadCounter(ref _poolCapacityRejectedBits),
                ReadCounter(ref _stolenBits),
                ReadCounter(ref _cooldownRejectedBits),
                ReadCounter(ref _polyphonyRejectedBits),
                ReadCounter(ref _distanceRejectedBits),
                ReadCounter(ref _preReadyRejectedBits),
                ReadCounter(ref _suspendedRejectedBits),
                ReadCounter(ref _unavailableRejectedBits),
                ReadCounter(ref _wrongThreadRejectedBits),
                ReadCounter(ref _loadNotReadyRejectedBits),
                ReadCounter(ref _loadFailedBits),
                ReadCounter(ref _loadStalledBits),
                ReadCounter(ref _invalidKeyRejectedBits),
                ReadCounter(ref _invalidRawKeyRejectedBits),
                ReadCounter(ref _invalidPositionRejectedBits),
                ReadCounter(ref _invalidValueRejectedBits),
                ReadCounter(ref _playbackKindRejectedBits),
                ReadCounter(ref _loopRegistryFullBits),
                ReadCounter(ref _setFloatFailedBits),
                ReadCounter(ref _recoveryFailedBits),
                ReadCounter(ref _staleBootstrapTokenRejectedBits),
                ReadCounter(ref _unexpectedExceptionBits));
            diagnostics = new OrpheusAudioDiagnostics(
                _lastCommittedProfileState.Intent.ProfileId,
                _lifecycle == OrpheusAudioLifecycle.Running
                    ? _profileState.Intent.BgmKey
                    : OrpheusAudioKey.Invalid,
                CurrentBgmKey,
                TargetBgmKey,
                _lifecycle == OrpheusAudioLifecycle.Running
                    ? _profileState.Intent.ProfileAmbienceKey
                    : OrpheusAudioKey.Invalid,
                CurrentProfileAmbienceKey,
                TargetProfileAmbienceKey,
                _lastCommittedProfileState.Overlay,
                _lastCommittedProfileState.EffectiveSnapshot,
                recoveryPending,
                _lastCommittedProfileState.Intent.BaseState,
                _lifecycle,
                _disableReason,
                transport,
                suspension,
                readiness,
                _configuredRealVoiceLimit,
                _configuredVirtualVoiceLimit,
                _voiceBudgetDegraded,
                _transient3DActiveCount,
                _transient2DActiveCount,
                _fadingCount,
                _pendingCount,
                BgmActiveCount,
                ProfileAmbienceActiveCount,
                GlobalLoopActiveCount,
                counters);
            return true;
        }

        public void Dispose()
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle == OrpheusAudioLifecycle.Disposed)
            {
                UnbindStaticBridges();
                return;
            }

            var disposingRunningManager = _lifecycle == OrpheusAudioLifecycle.Running;
            if ((disposingRunningManager || _lifecycle == OrpheusAudioLifecycle.Disabled) &&
                CanNormalizeOwnedSources() && !TryNormalizeAllOwnedSources() &&
                disposingRunningManager)
            {
                _disableReason = OrpheusAudioDisableReason.UnexpectedRuntimeException;
                SaturatingIncrement(ref _unexpectedExceptionBits);
            }

            if (_lifecycle == OrpheusAudioLifecycle.Running)
            {
                ClearLiveProfileIntent();
                ClearGlobalLoopRegistryState();
                InvalidateActivationState();
            }

            _lifecycle = OrpheusAudioLifecycle.Disposed;
            UnbindStaticBridges();

            if (!_isBound)
            {
                ReleaseLeases();
                // Unity fake-null: OnDestroy may already have torn down the host.
                if (_runtimeHost != null)
                {
                    _runtimeHost.ClearUnboundReservation(this);
                }
            }
        }

        internal bool TryMarkBound(OrpheusAudioRuntimeHost runtimeHost, double realtime)
        {
            if (!IsAvailableForBinding(runtimeHost))
            {
                return false;
            }

            if (!IsFiniteNonNegative(realtime))
            {
                FailClosed(OrpheusAudioDisableReason.RuntimeTimeInvalid);
                return false;
            }

            _lastRealtime = realtime;
            _isBound = true;
            return true;
        }

        internal bool IsAvailableForBinding(OrpheusAudioRuntimeHost runtimeHost)
        {
            return ReferenceEquals(_runtimeHost, runtimeHost) &&
                   _lifecycle == OrpheusAudioLifecycle.Running &&
                   HasSourceBankLease() && HasMixerLease();
        }

        internal bool TryReleaseAfterHostUnbind(OrpheusAudioRuntimeHost runtimeHost)
        {
            if (!ReferenceEquals(_runtimeHost, runtimeHost) || !_isBound ||
                _lifecycle != OrpheusAudioLifecycle.Disposed)
            {
                return false;
            }

            _isBound = false;
            ReleaseLeases();
            return true;
        }

        internal void MarkLeasesHeld()
        {
            _leaseWitness = OrpheusAudioLeaseRegistry.GetLeaseWitness(this);
            _sourceLeaseHeld = _leaseWitness != null && _leaseWitness.SourceHeld;
            _mixerLeaseHeld = _leaseWitness != null && _leaseWitness.MixerHeld;
        }

        internal void HandleRuntimeHostDestroyed(OrpheusAudioRuntimeHost runtimeHost)
        {
            if (!ReferenceEquals(_runtimeHost, runtimeHost) ||
                _lifecycle == OrpheusAudioLifecycle.Disposed)
            {
                return;
            }

            FailClosed(OrpheusAudioDisableReason.RuntimeHostDestroyed);
        }

        internal void ForceReleaseLeases(OrpheusAudioRuntimeHost runtimeHost)
        {
            if (!ReferenceEquals(_runtimeHost, runtimeHost))
            {
                return;
            }

            _isBound = false;
            ReleaseLeases();
        }

        internal void FailClosed(OrpheusAudioDisableReason reason)
        {
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (reason < OrpheusAudioDisableReason.MixerSetFloatFailed ||
                reason > OrpheusAudioDisableReason.RuntimeHostDestroyed)
            {
                reason = OrpheusAudioDisableReason.UnexpectedRuntimeException;
            }

            _lifecycle = OrpheusAudioLifecycle.Disabled;
            _disableReason = reason;
            InvalidateTerminalGenerations();
            if (reason == OrpheusAudioDisableReason.UnexpectedRuntimeException)
            {
                SaturatingIncrement(ref _unexpectedExceptionBits);
            }

            if (CanNormalizeOwnedSources() && !TryNormalizeAllOwnedSources())
            {
                SaturatingIncrement(ref _unexpectedExceptionBits);
            }

            ClearLiveProfileIntent(false);
            ClearGlobalLoopRegistryState(false);
            InvalidateActivationState(false);
            UnbindStaticBridges();
        }

        internal void RejectWrongThread()
        {
            SaturatingIncrement(ref _wrongThreadRejectedBits);
        }

        private OrpheusTransportState GetTransportState()
        {
            if (_lifecycle == OrpheusAudioLifecycle.Disposed)
            {
                return OrpheusTransportState.Disposed;
            }

            if (_lifecycle == OrpheusAudioLifecycle.Disabled)
            {
                return OrpheusTransportState.Invalid;
            }

            if (_isRecovering ||
                (OrpheusAudioRecoveryPolicy.HasExternal(_recoveryPendingReasons) &&
                 _suspensionReasons == OrpheusSuspensionReason.None))
            {
                return OrpheusTransportState.Recovering;
            }

            return _suspensionReasons == OrpheusSuspensionReason.None
                ? OrpheusTransportState.Active
                : OrpheusTransportState.Suspended;
        }

        private void InvalidateTerminalGenerations()
        {
            AdvanceBootstrapGeneration();
            AdvanceBgmGeneration();
            AdvanceProfileAmbienceGeneration();
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                _globalLoops[slotIndex].Generation =
                    OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(
                        _globalLoops[slotIndex].Generation);
            }

            AdvanceRecoveryGeneration();
        }

        private void InvalidateActivationState(bool advanceGenerations = true)
        {
            if (advanceGenerations)
            {
                AdvanceBootstrapGeneration();
            }

            _activationState = default;
            _listener = null;
            _cachedListenerPosition = default;
            _suspensionReasons = OrpheusSuspensionReason.None;
            ClearLoadObservations();
            ClearTransient3DSlots();
            ClearTransient2DSlots();
            _fadingCount = 0;
            _pendingCount = 0;
            _wasTransportActive = false;
            ClearRecoveryState(advanceGenerations);
        }

        private void AdvanceBootstrapGeneration()
        {
            _bootstrapGeneration = unchecked(_bootstrapGeneration + 1u);
            if (_bootstrapGeneration == 0)
            {
                _bootstrapGeneration = 1;
            }
        }

        private void ClearLiveProfileIntent(bool advanceGenerations = true)
        {
            _profileState = OrpheusAudioProfileState.Default;
            ClearBgmDirectorState(advanceGenerations);
            ClearProfileAmbienceDirectorState(advanceGenerations);
            _bgmFailedBlocked = false;
            _profileAmbienceFailedBlocked = false;
            _bgmRetryRequested = false;
            _profileAmbienceRetryRequested = false;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static ulong ReadCounter(ref long counterBits)
        {
            return unchecked((ulong)Volatile.Read(ref counterBits));
        }

        private bool TryNormalizeAllOwnedSources()
        {
            var succeeded = true;
            for (var sourceIndex = 0; sourceIndex < _ownedSources.Length; sourceIndex++)
            {
                try
                {
                    if (_ownedSources[sourceIndex] != null)
                    {
                        OrpheusAudioSourceNormalizer.Normalize(_ownedSources[sourceIndex]);
                    }
                }
                catch (Exception exception) when (!IsCatastrophic(exception))
                {
                    succeeded = false;
                }
            }

            return succeeded;
        }

        private bool HasSourceBankLease()
        {
            return _sourceLeaseHeld && _leaseWitness != null && _leaseWitness.SourceHeld;
        }

        private bool HasMixerLease()
        {
            return _mixerLeaseHeld && _leaseWitness != null && _leaseWitness.MixerHeld;
        }

        private bool CanNormalizeOwnedSources()
        {
            return HasSourceBankLease();
        }

        private void ReleaseLeases()
        {
            if (_sourceLeaseHeld)
            {
                OrpheusAudioLeaseRegistry.ReleaseSourceBank(this);
                _sourceLeaseHeld = false;
            }

            if (_mixerLeaseHeld)
            {
                OrpheusAudioLeaseRegistry.ReleaseMixerAndHost(this);
                _mixerLeaseHeld = false;
            }
        }

        private static void SaturatingIncrement(ref long counterBits)
        {
            while (true)
            {
                var current = Volatile.Read(ref counterBits);
                var next = OrpheusAudioSaturatingCounterPolicy.Increment(
                    unchecked((ulong)current));
                if (next == unchecked((ulong)current))
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                        ref counterBits,
                        unchecked((long)next),
                        current) == current)
                {
                    return;
                }
            }
        }

        internal static bool IsCatastrophic(Exception exception)
        {
            return exception is OutOfMemoryException ||
                   exception is StackOverflowException ||
                   exception is AccessViolationException ||
                   exception is ThreadAbortException;
        }
    }
}
