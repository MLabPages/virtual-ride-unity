using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VirtualRide.Core;

namespace VirtualRide.World
{
    /// <summary>
    /// Roadside boards every 200 m showing the distance ridden since the start, e.g. "1.4 km".
    /// On a later lap the same board shows the new cumulative distance at which the rider
    /// will pass it. Digits are drawn as flat strokes (no font), so they render in both eyes
    /// on Quest. Visibility is an experiment setting that is fixed during a recording.
    /// </summary>
    public sealed class DistanceSigns : MonoBehaviour
    {
        public const float SpacingMetres = 200f;
        private const float LateralOffset = 5.8f;
        private const float CharacterHeight = 0.46f;
        private const float DigitWidth = 0.25f;
        private const float LetterMWidth = 0.36f;
        private const float Gap = 0.075f;
        private const float Stroke = 0.07f;
        private const float PanelCenterHeight = 2.25f;

        private static readonly string[] Segments =
        {
            "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg"
        };

        private sealed class Board
        {
            public float RouteDistance;
            public Vector3 FaceCenter;
            public Vector3 Right;
            public Transform Panel;
            public MeshFilter Text;
            public int ShownTenths = -1;
        }

        private readonly List<Board> _boards = new List<Board>();
        private VirtualRideApp _app;
        private RideRoute _route;
        private GameObject _boardRoot;
        private Material _textMaterial;

        public void Initialize(VirtualRideApp app, RideRoute route)
        {
            _app = app;
            _route = route;
            _boardRoot = new GameObject("Distance boards");
            _boardRoot.transform.SetParent(transform, false);

            Shader shader = Resources.Load<Shader>("VirtualRideScenic");
            Material postMaterial = new Material(shader) { name = "Distance board post", color = new Color(0.55f, 0.57f, 0.56f) };
            Material panelMaterial = new Material(shader) { name = "Distance board panel", color = new Color(0.07f, 0.38f, 0.27f) };
            _textMaterial = new Material(shader) { name = "Distance board text", color = new Color(0.97f, 0.97f, 0.94f) };

            for (float distance = SpacingMetres; distance < route.TotalLength - 10f; distance += SpacingMetres)
            {
                route.Evaluate(distance, out Vector3 point, out Vector3 forward, out Vector3 right);
                Vector3 foot = point + right * LateralOffset;
                foot.y = 0f;
                Transform board = new GameObject("Distance board").transform;
                board.SetParent(_boardRoot.transform, false);
                board.SetPositionAndRotation(foot, Quaternion.LookRotation(forward, Vector3.up));

                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post";
                post.transform.SetParent(board, false);
                post.transform.localPosition = new Vector3(0f, 1.1f, 0.06f);
                post.transform.localScale = new Vector3(0.12f, 2.2f, 0.12f);
                Prepare(post, postMaterial);

                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Panel";
                panel.transform.SetParent(board, false);
                panel.transform.localPosition = new Vector3(0f, PanelCenterHeight, 0f);
                panel.transform.localScale = new Vector3(1.7f, 0.88f, 0.06f);
                Prepare(panel, panelMaterial);

                GameObject text = new GameObject("Distance text");
                text.transform.SetParent(_boardRoot.transform, false);
                MeshFilter filter = text.AddComponent<MeshFilter>();
                MeshRenderer renderer = text.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _textMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                _boards.Add(new Board
                {
                    RouteDistance = distance,
                    FaceCenter = foot + Vector3.up * PanelCenterHeight - forward * 0.045f,
                    Right = right,
                    Panel = panel.transform,
                    Text = filter
                });
            }

            Refresh();
        }

        private void LateUpdate()
        {
            if (_app == null) return;
            bool visible = _app.DistanceSignsVisible;
            if (_boardRoot.activeSelf != visible) _boardRoot.SetActive(visible);
            if (visible) Refresh();
        }

        private void Refresh()
        {
            float ridden = _app.Session.DistanceMetres;
            float position = _app.RouteDistance;
            float length = _route.TotalLength;
            // Session distance and route position are reset together, so their difference is a
            // whole number of laps. Rounding keeps the labels sensible after a test preview jump.
            int lapsDone = Mathf.Max(0, Mathf.RoundToInt((ridden - position) / length));
            foreach (Board board in _boards)
            {
                float passAt = lapsDone * length + board.RouteDistance + (board.RouteDistance < position ? length : 0f);
                int tenths = Mathf.RoundToInt(passAt / 100f);
                if (tenths == board.ShownTenths) continue;
                board.ShownTenths = tenths;
                Rebuild(board, (tenths / 10f).ToString("0.0", CultureInfo.InvariantCulture) + " km");
            }
        }

        private void Rebuild(Board board, string label)
        {
            float width = MeasureWidth(label);
            Vector3 panelScale = board.Panel.localScale;
            panelScale.x = Mathf.Max(1.6f, width + 0.5f);
            board.Panel.localScale = panelScale;

            var mesh = new LandscapeMeshes();
            Vector3 up = Vector3.up;
            Vector3 origin = board.FaceCenter - board.Right * (width * 0.5f) - up * (CharacterHeight * 0.5f);
            float x = 0f;
            foreach (char character in label)
            {
                x += DrawGlyph(mesh, origin, board.Right, up, x, character);
            }

            Mesh previous = board.Text.sharedMesh;
            board.Text.sharedMesh = mesh.Finish("Distance text " + label);
            if (previous != null) Destroy(previous);
        }

        private static float MeasureWidth(string label)
        {
            float width = 0f;
            foreach (char character in label) width += Advance(character);
            return width - Gap;
        }

        private static float Advance(char character)
        {
            if (character == '.') return 0.08f + Gap;
            if (character == ' ') return 0.1f + Gap;
            if (character == 'm') return LetterMWidth + Gap;
            return DigitWidth + Gap;
        }

        private static float DrawGlyph(LandscapeMeshes mesh, Vector3 origin, Vector3 right, Vector3 up, float x, char character)
        {
            float h = CharacterHeight;
            float w = DigitWidth;
            void Line(float x0, float y0, float x1, float y1)
            {
                Vector2 from = new Vector2(x + x0, y0);
                Vector2 to = new Vector2(x + x1, y1);
                Vector2 direction = (to - from).normalized;
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                Vector2 middle = (from + to) * 0.5f;
                Vector3 center = origin + right * middle.x + up * middle.y;
                Vector3 axisX = (right * direction.x + up * direction.y).normalized;
                Vector3 axisY = (right * perpendicular.x + up * perpendicular.y).normalized;
                mesh.Quad(center, axisX, axisY, (to - from).magnitude * 0.5f + Stroke * 0.5f, Stroke * 0.5f, Color.white);
            }

            if (character >= '0' && character <= '9')
            {
                foreach (char segment in Segments[character - '0'])
                {
                    switch (segment)
                    {
                        case 'a': Line(0f, h, w, h); break;
                        case 'b': Line(w, h * 0.5f, w, h); break;
                        case 'c': Line(w, 0f, w, h * 0.5f); break;
                        case 'd': Line(0f, 0f, w, 0f); break;
                        case 'e': Line(0f, 0f, 0f, h * 0.5f); break;
                        case 'f': Line(0f, h * 0.5f, 0f, h); break;
                        case 'g': Line(0f, h * 0.5f, w, h * 0.5f); break;
                    }
                }
            }
            else if (character == '.')
            {
                Line(0.01f, 0f, 0.04f, 0f);
            }
            else if (character == 'k')
            {
                Line(0f, 0f, 0f, h);
                Line(0f, h * 0.28f, w * 0.8f, h * 0.66f);
                Line(w * 0.3f, h * 0.42f, w * 0.85f, 0f);
            }
            else if (character == 'm')
            {
                float top = h * 0.64f;
                float half = LetterMWidth * 0.5f;
                Line(0f, 0f, 0f, top);
                Line(half, 0f, half, top);
                Line(LetterMWidth, 0f, LetterMWidth, top);
                Line(0f, top, LetterMWidth, top);
            }

            return Advance(character);
        }

        private static void Prepare(GameObject primitive, Material material)
        {
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }
    }
}
