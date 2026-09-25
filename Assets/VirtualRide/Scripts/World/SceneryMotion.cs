using UnityEngine;

namespace VirtualRide.World
{
    /// <summary>
    /// Constant, input-independent scenery motion (windmill sails). It runs identically in
    /// the pedal-linked and fixed video-speed conditions.
    /// </summary>
    internal sealed class SceneryMotion : MonoBehaviour
    {
        public Vector3 LocalAxis = Vector3.forward;
        public float DegreesPerSecond = 24f;

        private void Update()
        {
            transform.localRotation *= Quaternion.AngleAxis(DegreesPerSecond * Time.deltaTime, LocalAxis);
        }
    }
}
