using UnityEngine;

namespace Orpheus.Audio.Samples.PlayableOneShot
{
    public sealed class PlayableOneShotSceneBinding : MonoBehaviour
    {
        [SerializeField] private AudioListener _listener;

        public bool BindListener(OrpheusAudioManager manager)
        {
            return manager != null && manager.BindListener(_listener);
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
