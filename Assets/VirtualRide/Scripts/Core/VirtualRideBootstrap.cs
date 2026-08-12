using UnityEngine;

namespace VirtualRide.Core
{
    public static class VirtualRideBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartVirtualRide()
        {
            if (Object.FindAnyObjectByType<VirtualRideApp>() != null)
            {
                return;
            }

            GameObject appObject = new GameObject("Virtual Ride App");
            appObject.AddComponent<VirtualRideApp>();
        }
    }
}
