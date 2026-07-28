using Orpheus.Audio.Core;
using Orpheus.Audio.Generated;

namespace Orpheus.Audio.ContractConsumer.Generated
{
    internal static class OrpheusAudioGeneratedConsumerContract
    {
        internal static OrpheusAudioKey ConsumeGeneratedContract(
            out int schemaVersion,
            out string contentHash)
        {
            schemaVersion = OrpheusAudioKeys.ManifestSchemaVersion;
            contentHash = OrpheusAudioKeys.ManifestContentHash;
            return OrpheusAudioKeys.ActiveCue;
        }
    }
}
