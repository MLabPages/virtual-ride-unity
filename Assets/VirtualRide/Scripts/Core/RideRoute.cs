using System.Collections.Generic;
using UnityEngine;

namespace VirtualRide.Core
{
    /// <summary>
    /// Deterministic, closed scenic route shared by world generation and rider movement.
    /// Distances are represented in Unity metres so measured speed maps directly to motion.
    /// </summary>
    public sealed class RideRoute
    {
        private const int PointCount = 512;
        private readonly List<Vector3> _points = new List<Vector3>(PointCount);
        private readonly float[] _segmentStarts = new float[PointCount + 1];

        public RideRoute()
        {
            BuildPoints();
            BuildDistanceTable();
        }

        public IReadOnlyList<Vector3> Points => _points;
        public int Count => _points.Count;
        public float TotalLength { get; private set; }
        public float RoadHalfWidth => 3.1f;

        public Vector3 GetPoint(int index)
        {
            int wrapped = ((index % Count) + Count) % Count;
            return _points[wrapped];
        }

        public Vector3 GetForwardAtIndex(int index)
        {
            Vector3 previous = GetPoint(index - 1);
            Vector3 next = GetPoint(index + 1);
            Vector3 forward = next - previous;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        public Vector3 GetRightAtIndex(int index)
        {
            return Vector3.Cross(Vector3.up, GetForwardAtIndex(index)).normalized;
        }

        public void Evaluate(float distance, out Vector3 position, out Vector3 forward, out Vector3 right)
        {
            float wrappedDistance = Mathf.Repeat(distance, TotalLength);
            int low = 0;
            int high = Count - 1;

            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (_segmentStarts[middle + 1] <= wrappedDistance)
                {
                    low = middle + 1;
                }
                else if (_segmentStarts[middle] > wrappedDistance)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle;
                    break;
                }
            }

            int index = Mathf.Clamp(low, 0, Count - 1);
            int nextIndex = (index + 1) % Count;
            float segmentLength = _segmentStarts[index + 1] - _segmentStarts[index];
            float amount = segmentLength > 0.0001f
                ? (wrappedDistance - _segmentStarts[index]) / segmentLength
                : 0f;

            position = Vector3.Lerp(_points[index], _points[nextIndex], amount);
            Vector3 forwardA = GetForwardAtIndex(index);
            Vector3 forwardB = GetForwardAtIndex(nextIndex);
            forward = Vector3.Slerp(forwardA, forwardB, amount).normalized;
            right = Vector3.Cross(Vector3.up, forward).normalized;
        }

        public float GetProgress01(float distance)
        {
            return TotalLength > 0f ? Mathf.Repeat(distance, TotalLength) / TotalLength : 0f;
        }

        public string GetAreaName(float distance)
        {
            float progress = GetProgress01(distance);
            if (progress < 0.22f)
            {
                return "木漏れ日の森";
            }

            if (progress < 0.43f)
            {
                return "湖畔の道";
            }

            if (progress < 0.66f)
            {
                return "小さな村";
            }

            if (progress < 0.84f)
            {
                return "風の草原";
            }

            return "丘へ続く道";
        }

        private void BuildPoints()
        {
            for (int i = 0; i < PointCount; i++)
            {
                float progress = i / (float)PointCount;
                float angle = progress * Mathf.PI * 2f;

                float organicRadius = 1f +
                    0.075f * Mathf.Sin(angle * 3f + 0.35f) +
                    0.035f * Mathf.Sin(angle * 5f - 0.8f);
                float x = Mathf.Cos(angle) * 178f * organicRadius + 14f * Mathf.Sin(angle * 2f);
                float z = Mathf.Sin(angle) * 132f * organicRadius + 9f * Mathf.Cos(angle * 3f);

                // Keep the first route level so every generated roadside object remains
                // grounded. Elevation can be introduced later with a matching terrain mesh.
                _points.Add(new Vector3(x, 0f, z));
            }
        }

        private void BuildDistanceTable()
        {
            _segmentStarts[0] = 0f;
            float length = 0f;
            for (int i = 0; i < PointCount; i++)
            {
                length += Vector3.Distance(GetPoint(i), GetPoint(i + 1));
                _segmentStarts[i + 1] = length;
            }

            TotalLength = length;
        }
    }
}
