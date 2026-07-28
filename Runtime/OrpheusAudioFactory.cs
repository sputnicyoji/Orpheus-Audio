using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    public static class OrpheusAudioFactory
    {
        public static OrpheusAudioInitResult Create(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSettings settings,
            OrpheusAudioCatalog catalog,
            OrpheusAudioUserGains initialUserGains,
            out OrpheusAudioManager manager)
        {
            manager = null;
            if (!OrpheusMainThread.IsCurrent)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (runtimeHost == null)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (OrpheusAudioLeaseRegistry.IsRuntimeHostReserved(runtimeHost))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (!OrpheusAudioFactoryCore.AreValid(initialUserGains))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidInitialGain);
            }

            if (settings == null || settings.SchemaVersion != OrpheusAudioAuthoringSchema.Current ||
                !settings.HasValidDurations)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSettings);
            }

            IOrpheusAudioMixerPort mixer;
            try
            {
                if (settings.Mixer == null)
                {
                    return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidMixerContract);
                }

                mixer = new OrpheusUnityMixerPort(settings);
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidMixerContract);
            }

            return OrpheusAudioFactoryCore.Create(
                runtimeHost,
                settings,
                catalog,
                initialUserGains,
                unchecked((uint)Guid.NewGuid().GetHashCode()),
                Application.isFocused,
                null,
                mixer,
                new OrpheusUnityAudioSystemPort(),
                out manager);
        }
    }

    internal static class OrpheusAudioTestFactory
    {
        internal static OrpheusAudioInitResult Create(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSettings settings,
            OrpheusAudioCatalog catalog,
            OrpheusAudioUserGains initialUserGains,
            uint randomSeed,
            bool initialFocus,
            IOrpheusAudioClipReadiness clipReadiness,
            IOrpheusAudioMixerPort mixer,
            IOrpheusAudioSystemPort audioSystem,
            out OrpheusAudioManager manager)
        {
            manager = null;
            if (!OrpheusMainThread.IsCurrent)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (mixer == null || audioSystem == null)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            return OrpheusAudioFactoryCore.Create(
                runtimeHost,
                settings,
                catalog,
                initialUserGains,
                randomSeed,
                initialFocus,
                clipReadiness,
                mixer,
                audioSystem,
                out manager);
        }
    }

    internal static class OrpheusAudioFactoryCore
    {
        internal static OrpheusAudioInitResult Create(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSettings settings,
            OrpheusAudioCatalog catalog,
            OrpheusAudioUserGains initialUserGains,
            uint randomSeed,
            bool initialFocus,
            IOrpheusAudioClipReadiness injectedClipReadiness,
            IOrpheusAudioMixerPort mixer,
            IOrpheusAudioSystemPort audioSystem,
            out OrpheusAudioManager manager)
        {
            manager = null;
            if (runtimeHost == null || mixer == null || audioSystem == null)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (OrpheusAudioLeaseRegistry.IsRuntimeHostReserved(runtimeHost))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            if (!AreValid(initialUserGains))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidInitialGain);
            }

            if (settings == null || settings.SchemaVersion != OrpheusAudioAuthoringSchema.Current ||
                !settings.HasValidDurations)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSettings);
            }

            object mixerIdentity;
            try
            {
                mixerIdentity = mixer.LeaseIdentity;
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidMixerContract);
            }

            if (mixerIdentity == null)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidMixerContract);
            }

            var mixerConflict = OrpheusAudioLeaseRegistry.PreflightMixer(runtimeHost, mixerIdentity);
            if (mixerConflict != OrpheusAudioLeaseConflict.None)
            {
                return OrpheusAudioInitResult.Failed(MapLeaseConflict(mixerConflict));
            }

            OrpheusAudioSourceBank sourceBank;
            AudioSource[] ownedSources;
            try
            {
                sourceBank = runtimeHost.SourceBank;
                if (sourceBank == null || !sourceBank.TryCapture(out ownedSources))
                {
                    return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSourceBank);
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSourceBank);
            }

            if (OrpheusAudioLeaseRegistry.PreflightSources(sourceBank, ownedSources) !=
                OrpheusAudioLeaseConflict.None)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSourceBank);
            }

            if (catalog == null)
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidCatalog);
            }

            if (!catalog.TryBuildSnapshot(
                    out var snapshot,
                    out var catalogError,
                    out var relatedKey,
                    out var relatedIndex))
            {
                return OrpheusAudioInitResult.Failed(catalogError, relatedKey, relatedIndex);
            }

            OrpheusAudioSystemConfiguration configuration;
            try
            {
                configuration = audioSystem.GetConfiguration();
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidAudioConfiguration);
            }

            if (!IsValid(configuration))
            {
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidAudioConfiguration);
            }

            var clipReadiness = injectedClipReadiness ?? new OrpheusUnityClipReadiness(snapshot);
            var sessionContext = new OrpheusAudioSessionContext(
                runtimeHost,
                sourceBank,
                ownedSources,
                settings,
                snapshot,
                initialUserGains,
                randomSeed,
                clipReadiness,
                mixer,
                mixerIdentity,
                audioSystem,
                configuration,
                initialFocus);
            var unpublishedManager = OrpheusAudioManager.CreateUnpublished(sessionContext);
            runtimeHost.PrepareUnboundIdentitySlot(unpublishedManager);

            if (!OrpheusAudioLeaseRegistry.TryAcquire(
                    runtimeHost,
                    sourceBank,
                    ownedSources,
                    mixerIdentity,
                    unpublishedManager,
                    out var conflict))
            {
                return OrpheusAudioInitResult.Failed(MapLeaseConflict(conflict));
            }

            unpublishedManager.MarkLeasesHeld();
            if (!runtimeHost.TryReserve(unpublishedManager))
            {
                unpublishedManager.Dispose();
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidArguments);
            }

            try
            {
                OrpheusAudioSourceNormalizer.NormalizeAll(ownedSources);
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                unpublishedManager.Dispose();
                return OrpheusAudioInitResult.Failed(OrpheusAudioInitErrorCode.InvalidSourceBank);
            }

            unpublishedManager.PreloadBootstrapTransient(runtimeHost.ReadRealtime());
            manager = unpublishedManager;
            return OrpheusAudioInitResult.Succeeded;
        }

        private static OrpheusAudioInitErrorCode MapLeaseConflict(OrpheusAudioLeaseConflict conflict)
        {
            switch (conflict)
            {
                case OrpheusAudioLeaseConflict.Mixer:
                    return OrpheusAudioInitErrorCode.InvalidMixerContract;
                case OrpheusAudioLeaseConflict.SourceBank:
                    return OrpheusAudioInitErrorCode.InvalidSourceBank;
                default:
                    return OrpheusAudioInitErrorCode.InvalidArguments;
            }
        }

        internal static bool AreValid(OrpheusAudioUserGains gains)
        {
            return OrpheusAudioUserGainPolicy.AreValid(gains);
        }

        private static bool IsValid(OrpheusAudioSystemConfiguration configuration)
        {
            return configuration.DspBufferSize > 0 && configuration.SampleRate > 0 &&
                   configuration.NumRealVoices > 0 &&
                   configuration.NumVirtualVoices >= configuration.NumRealVoices;
        }
    }
}
