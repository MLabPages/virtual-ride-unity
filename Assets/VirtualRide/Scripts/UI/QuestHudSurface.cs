using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputDevice = UnityEngine.XR.InputDevice;
using OculusTouchController = UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile.OculusTouchController;

namespace VirtualRide.UI
{
    /// <summary>
    /// Quest only: the existing IMGUI HUD is drawn into a render texture shown on a panel
    /// that rides with the bicycle (it follows the route, not the head). The controller ray
    /// hits that panel and its trigger or index-finger pinch clicks HUD buttons.
    /// Windows keeps the screen HUD.
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
        private bool _wasPressed;
        private int _activeSource;
        private bool _clickPending;
        private bool _clickConsumed;
        private int _updatedFrame = -1;
        private float _recenterHold;
        private bool _recenterHeld;

        private void Update()
        {
            // Works even when the bicycle HUD is behind the user's head.
            bool bothPinching = TryGetHandAim(MetaAimHand.left, 2, out _, out _, out bool left, out _) && left &&
                TryGetHandAim(MetaAimHand.right, 1, out _, out _, out bool right, out _) && right;
            InputDevice controller = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool xPressed = controller.isValid &&
                controller.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool x) && x;
            if (!bothPinching && !xPressed)
            {
                _recenterHold = 0f;
                _recenterHeld = false;
                return;
            }
            _recenterHold += Time.unscaledDeltaTime;
            if (!_recenterHeld && _recenterHold >= 1.5f)
            {
                _recenterHeld = true;
                GetComponent<VirtualRide.Core.VirtualRideApp>()?.RequestRecenter();
            }
        }

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
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (camera == null || !head.isValid ||
                !TryGetHeadPose(head, out Vector3 headPosition, out Quaternion headRotation) ||
                !TryGetAimPose(out Vector3 aimPosition, out Quaternion aimRotation,
                    out bool pressed, out int source))
            {
                _wasPressed = false;
                _activeSource = 0;
                return;
            }

            // All OpenXR poses share tracking space. Re-express the aim pose from the
            // tracked head camera, whose local pose is recentered for seated riding.
            Quaternion headInverse = Quaternion.Inverse(headRotation);
            Transform cameraTransform = camera.transform;
            Vector3 origin = cameraTransform.TransformPoint(headInverse * (aimPosition - headPosition));
            Vector3 direction = cameraTransform.rotation * (headInverse * (aimRotation * Vector3.forward));

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

            _clickPending = source == _activeSource && pressed && !_wasPressed && _hasPointer;
            _wasPressed = pressed;
            _activeSource = source;
        }

        private static bool TryGetHeadPose(InputDevice head, out Vector3 position, out Quaternion rotation)
        {
            rotation = default;
            if (head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out position) &&
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out rotation))
                return true;
            return head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position) &&
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
        }

        private static bool TryGetAimPose(out Vector3 position, out Quaternion rotation,
            out bool pressed, out int source)
        {
            // Meta's aim pose points where the index finger is aimed; the grip pose
            // previously used for Touch controllers points above the physical controller.
            if (TryGetHandAim(MetaAimHand.right, 1, out position, out rotation, out pressed, out source) ||
                TryGetHandAim(MetaAimHand.left, 2, out position, out rotation, out pressed, out source))
                return true;

            foreach (var device in InputSystem.devices)
            {
                if (!(device is OculusTouchController controller) ||
                    controller.pointer == null || !controller.pointer.isTracked.isPressed)
                    continue;

                int side = 0;
                foreach (var usage in controller.usages)
                {
                    if (usage == UnityEngine.InputSystem.CommonUsages.RightHand) side = 3;
                    if (usage == UnityEngine.InputSystem.CommonUsages.LeftHand) side = 4;
                }
                if (side == 0) continue;
                position = controller.pointer.position.ReadValue();
                rotation = controller.pointer.rotation.ReadValue();
                pressed = controller.triggerPressed.isPressed ||
                    controller.trigger.ReadValue() >= TriggerThreshold;
                source = side;
                return true;
            }

            position = default;
            rotation = default;
            pressed = false;
            source = 0;
            return false;
        }

        private static bool TryGetHandAim(MetaAimHand hand, int side,
            out Vector3 position, out Quaternion rotation, out bool pressed, out int source)
        {
            if (hand != null && hand.added &&
                ((MetaAimFlags)hand.aimFlags.ReadValue() & MetaAimFlags.Valid) != 0 &&
                ((MetaAimFlags)hand.aimFlags.ReadValue() & MetaAimFlags.SystemGesture) == 0)
            {
                position = hand.devicePosition.ReadValue();
                rotation = hand.deviceRotation.ReadValue();
                pressed = hand.indexPressed.isPressed;
                source = side;
                return true;
            }

            position = default;
            rotation = default;
            pressed = false;
            source = 0;
            return false;
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
