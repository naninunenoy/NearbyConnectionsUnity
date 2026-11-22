using UnityEngine;

namespace NearbyConnections
{
    internal sealed class NearbyPump : MonoBehaviour
    {
        internal NearbyClient Client;

        private void Update() => Client?.Update();

        private void OnApplicationPause(bool paused)
        {
            if (paused) Client?.Close();
        }

        private void OnDestroy() => Client?.Close();
    }
}
