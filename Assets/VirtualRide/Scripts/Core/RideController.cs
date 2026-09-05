using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualRide.Core
{
    public sealed class RideController : MonoBehaviour
    {
        private RideRoute _route;
        private Camera _camera;
        private Transform _cameraRig;
        private Transform _handlebarRig;
        private AudioSource _windSource;
        private float _routeDistance;
        private float _speedKph;
        private float _visualRoll;
        private float _bobPhase;
        private bool _windEnabled = true;
        private bool _comfortMode = true;

        public float RouteDistance => _routeDistance;
        public Camera RideCamera => _camera;
        public bool WindEnabled => _windEnabled;
        public bool ComfortMode => _comfortMode;

        public void ToggleComfortMode() { _comfortMode = !_comfortMode; }

        public void ResetRoute(float distance = 0f)
        {
            _routeDistance = Mathf.Repeat(distance, _route.TotalLength);
            _visualRoll = 0f;
            _bobPhase = 0f;
            _speedKph = 0f;
            Advance(0f);
        }

        public void Initialize(RideRoute route)
        {
            _route = route;
            BuildCamera();
            BuildHandlebars();
            BuildWindAudio();
            SnapToRoute();
        }

        public void SetSpeed(float speedKph)
        {
            _speedKph = Mathf.Max(0f, speedKph);
        }

        public void ToggleWind()
        {
            _windEnabled = !_windEnabled;
        }

        public void Advance(float deltaTime)
        {
            if (_route == null)
            {
                return;
            }

            _routeDistance += _speedKph / 3.6f * deltaTime;
            _routeDistance = Mathf.Repeat(_routeDistance, _route.TotalLength);

            _route.Evaluate(_routeDistance, out Vector3 position, out Vector3 forward, out Vector3 right);
            _route.Evaluate(_routeDistance + 10f, out _, out Vector3 aheadForward, out _);

            transform.position = position;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            float cornerAngle = Vector3.SignedAngle(forward, aheadForward, Vector3.up);
            float desiredRoll = Mathf.Clamp(-cornerAngle * 0.55f, -8f, 8f) * Mathf.InverseLerp(3f, 26f, _speedKph);
            if (_comfortMode) desiredRoll = 0f;
            _visualRoll = Mathf.Lerp(_visualRoll, desiredRoll, 1f - Mathf.Exp(-deltaTime * 4f));

            float speedFactor = Mathf.InverseLerp(0f, 36f, _speedKph);
            float motionFactor = _comfortMode ? 0f : speedFactor;
            _bobPhase += deltaTime * Mathf.Lerp(1.2f, 8.5f, speedFactor);
            float bob = Mathf.Sin(_bobPhase) * 0.012f * motionFactor;
            float sway = Mathf.Sin(_bobPhase * 0.5f) * 0.016f * motionFactor;
            _cameraRig.localPosition = new Vector3(sway, 1.62f + bob, 0.05f);
            _cameraRig.localRotation = Quaternion.Euler(
                Mathf.Sin(_bobPhase * 0.5f) * 0.18f * motionFactor,
                0f,
                _comfortMode ? 0f : _visualRoll);

            _handlebarRig.localRotation = Quaternion.Euler(
                0f,
                Mathf.Sin(_bobPhase * 0.5f) * 0.7f * speedFactor,
                -_visualRoll * 0.25f);

            _camera.fieldOfView = _comfortMode ? 68f : Mathf.Lerp(_camera.fieldOfView, Mathf.Lerp(64f, 74f, speedFactor),
                1f - Mathf.Exp(-deltaTime * 2.5f));
            _camera.transform.localPosition = Vector3.zero;
            _camera.transform.localRotation = Quaternion.identity;

            UpdateWind(speedFactor);
        }

        private void SnapToRoute()
        {
            _route.Evaluate(0f, out Vector3 position, out Vector3 forward, out _);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void BuildCamera()
        {
            _cameraRig = new GameObject("Rider Camera Rig").transform;
            _cameraRig.SetParent(transform, false);
            _cameraRig.localPosition = new Vector3(0f, 1.62f, 0.05f);

            GameObject cameraObject = new GameObject("Ride Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(_cameraRig, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.backgroundColor = new Color(0.52f, 0.76f, 0.91f);
            _camera.fieldOfView = 64f;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 1100f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
        }

        private void BuildHandlebars()
        {
            _handlebarRig = new GameObject("Bicycle Cockpit").transform;
            _handlebarRig.SetParent(_cameraRig, false);
            _handlebarRig.localPosition = new Vector3(0f, -0.56f, 1.05f);

            Material metal = CreateMaterial("Handlebar Metal", new Color(0.08f, 0.10f, 0.11f), 0.82f, 0.72f);
            Material rubber = CreateMaterial("Handlebar Grips", new Color(0.025f, 0.035f, 0.04f), 0.12f, 0f);
            Material frame = CreateMaterial("Bicycle Frame", new Color(0.05f, 0.48f, 0.56f), 0.45f, 0.18f);

            CreateCylinder("Handlebar", _handlebarRig, new Vector3(0f, 0.02f, 0f),
                new Vector3(0.027f, 0.42f, 0.027f), Quaternion.Euler(0f, 0f, 90f), metal);
            CreateCylinder("Left grip", _handlebarRig, new Vector3(-0.48f, 0.02f, 0f),
                new Vector3(0.044f, 0.11f, 0.044f), Quaternion.Euler(0f, 0f, 90f), rubber);
            CreateCylinder("Right grip", _handlebarRig, new Vector3(0.48f, 0.02f, 0f),
                new Vector3(0.044f, 0.11f, 0.044f), Quaternion.Euler(0f, 0f, 90f), rubber);
            CreateCylinder("Stem", _handlebarRig, new Vector3(0f, -0.14f, 0.16f),
                new Vector3(0.035f, 0.24f, 0.035f), Quaternion.Euler(42f, 0f, 0f), metal);
            CreateCylinder("Top tube", _handlebarRig, new Vector3(0f, -0.34f, 0.42f),
                new Vector3(0.055f, 0.36f, 0.055f), Quaternion.Euler(68f, 0f, 0f), frame);

            GameObject bell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bell.name = "Bell";
            bell.transform.SetParent(_handlebarRig, false);
            bell.transform.localPosition = new Vector3(0.18f, 0.08f, 0.02f);
            bell.transform.localScale = new Vector3(0.09f, 0.045f, 0.09f);
            bell.GetComponent<Renderer>().sharedMaterial = metal;
            DisableCollider(bell);

            foreach (Renderer renderer in _handlebarRig.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private void BuildWindAudio()
        {
            const int sampleRate = 22050;
            const int length = sampleRate * 2;
            float[] samples = new float[length];
            float smoothed = 0f;
            var random = new System.Random(7419);
            for (int i = 0; i < length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                smoothed = Mathf.Lerp(smoothed, white, 0.055f);
                samples[i] = smoothed * 0.32f;
            }

            AudioClip clip = AudioClip.Create("Procedural Wind", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            _windSource = gameObject.AddComponent<AudioSource>();
            _windSource.clip = clip;
            _windSource.loop = true;
            _windSource.playOnAwake = false;
            _windSource.spatialBlend = 0f;
            _windSource.volume = 0f;
            _windSource.Play();
        }

        private void UpdateWind(float speedFactor)
        {
            if (_windSource == null)
            {
                return;
            }

            float targetVolume = _windEnabled ? Mathf.SmoothStep(0f, 0.24f, speedFactor) : 0f;
            _windSource.volume = Mathf.MoveTowards(_windSource.volume, targetVolume, Time.deltaTime * 0.5f);
            _windSource.pitch = Mathf.Lerp(0.72f, 1.35f, speedFactor);
        }

        private static void CreateCylinder(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = objectName;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = localScale;
            cylinder.transform.localRotation = localRotation;
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            DisableCollider(cylinder);
        }

        private static Material CreateMaterial(string materialName, Color color, float smoothness, float metallic)
        {
            Shader shader = Resources.Load<Shader>("VirtualRideScenic");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "VirtualRideScenic shader is missing from Assets/VirtualRide/Resources.");
            }

            Material material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            return material;
        }

        private static void DisableCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }
}
