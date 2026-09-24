using UnityEngine;
using UnityEngine.XR;

namespace VirtualRide.UI
{
    /// <summary>
    /// Quest only: the existing IMGUI HUD is drawn into a render texture shown on a panel
    /// that rides with the bicycle (it follows the route, not the head). The controller ray
    /// hits that panel and its trigger clicks HUD buttons. Windows keeps the screen HUD.
    /// </summary>
    public sealed class QuestHudSurface : MonoBehaviour
    {
        public const int VirtualWidth = 1600;
        public const int VirtualHeight = 900;
        private const float PanelDistance = 1.5f;
        private const float PanelWidthMetres = 1.9f;
        private const float TriggerThreshold = 0.72f;

        private RenderTexture _texture;
        private Transform _panel;
        private Vector2 _pointer;
        private bool _hasPointer;
        private bool _wasTriggerPressed;
        private bool _clickPending;
        private bool _clickConsumed;
        private int _updatedFrame = -1;

        public static QuestHudSurface Current { get; private set; }
        public RenderTexture Texture => _texture;

        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            Current = this;
            _texture = new RenderTexture(VirtualWidth, VirtualHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Quest HUD",
                useMipMap = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            _texture.Create();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Current, this)) Current = null;
            if (_texture != null) _texture.Release();
        }

        /// <summary>Places the panel once the ride camera exists (its parent is the bicycle rig).</summary>
        private bool EnsurePanel()
        {
            if (_panel != null) return true;
            Camera camera = Camera.main;
            if (camera == null || camera.transform.parent == null) return false;
            Shader shader = Resources.Load<Shader>("VirtualRideHudPanel");
            if (shader == null) return false;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Quest HUD Panel";
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _panel = quad.transform;
            _panel.SetParent(camera.transform.parent, false);
            _panel.localPosition = new Vector3(0f, -0.08f, PanelDistance);
            _panel.localRotation = Quaternion.identity;
            _panel.localScale = new Vector3(PanelWidthMetres, PanelWidthMetres * VirtualHeight / VirtualWidth, 1f);
            Renderer renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(shader) { name = "Quest HUD", mainTexture = _texture };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        public void UpdatePointer()
        {
            if (_updatedFrame == Time.frameCount) return;
            _updatedFrame = Time.frameCount;
            _clickPending = false;
            _clickConsumed = false;
            _hasPointer = false;
            if (!EnsurePanel()) return;

            Camera camera = Camera.main;
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            InputDevice hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!hand.isValid) hand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (camera == null || !head.isValid || !hand.isValid ||
                !head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition) ||
                !head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation) ||
                !hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 handPosition) ||
                !hand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion handRotation))
            {
                _wasTriggerPressed = false;
                return;
            }

            // The controller pose relative to the head, re-expressed from the tracked camera.
            Quaternion headInverse = Quaternion.Inverse(headRotation);
            Transform cameraTransform = camera.transform;
            Vector3 origin = cameraTransform.TransformPoint(headInverse * (handPosition - headPosition));
            Vector3 direction = cameraTransform.rotation * (headInverse * (handRotation * Vector3.forward));

            Plane plane = new Plane(-_panel.forward, _panel.position);
            if (plane.Raycast(new Ray(origin, direction), out float distance) && distance > 0f)
            {
                Vector3 local = _panel.InverseTransformPoint(origin + direction * distance);
                if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f)
                {
                    _pointer = new Vector2((local.x + 0.5f) * VirtualWidth, (0.5f - local.y) * VirtualHeight);
                    _hasPointer = true;
                }
            }

            bool pressed = hand.TryGetFeatureValue(CommonUsages.triggerButton, out bool button)
                ? button
                : hand.TryGetFeatureValue(CommonUsages.trigger, out float value) && value >= TriggerThreshold;
            _clickPending = pressed && !_wasTriggerPressed;
            _wasTriggerPressed = pressed;
        }

        /// <summary>Pointer position in the HUD's virtual 1600x900 layout space.</summary>
        public bool TryGetPointer(out Vector2 position)
        {
            position = _pointer;
            return _hasPointer;
        }

        /// <summary>One trigger press activates at most one control, during the repaint pass.</summary>
        public bool TryConsumeClick(Rect rect)
        {
            if (!_clickPending || _clickConsumed || !GUI.enabled || Event.current == null ||
                Event.current.type != EventType.Repaint || !_hasPointer || !rect.Contains(_pointer))
            {
                return false;
            }

            _clickConsumed = true;
            return true;
        }
    }
}
