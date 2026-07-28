using UnityEngine;

namespace Orpheus.Audio.Tests.Fixtures
{
    public sealed class OrpheusAudioFixtureSceneBinding : MonoBehaviour
    {
        [SerializeField] private AudioListener _listener;

        public bool BindListener(OrpheusAudioManager manager)
        {
            return manager != null && manager.BindListener(_listener);
        }

        public bool ReplaceListener(
            OrpheusAudioManager manager,
            AudioListener replacementListener)
        {
            if (manager == null ||
                !manager.ReplaceListener(_listener, replacementListener))
            {
                return false;
            }

            _listener = replacementListener;
            return true;
        }

        public bool RemoveListener(OrpheusAudioManager manager)
        {
            return manager != null && manager.RemoveListener(_listener);
        }
    }
}
