using UnityEngine;

namespace Orpheus.Audio.Samples.MinimalSetup
{
    public sealed class MinimalSceneBinding : MonoBehaviour
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
            if (manager == null || !manager.RemoveListener(_listener))
            {
                return false;
            }

            _listener = null;
            return true;
        }
    }
}
