using UnityEngine;
using VirtualRide.Core;
using VirtualRide.Input;

namespace VirtualRide.UI
{
    public sealed class RideHud : MonoBehaviour
    {
        private VirtualRideApp _app;
        private Font _font;
        private GUIStyle _speedStyle;
        private GUIStyle _unitStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _textFieldStyle;
        private Texture2D _whiteTexture;
        private Texture2D _roundedTexture;
        private Texture2D _primaryTexture;
        private Texture2D _secondaryTexture;
        private Texture2D _dangerTexture;
        private bool _stylesReady;
        private string _participantId = "P001";
        private string _condition = "baseline";

        private void Awake()
        {
            _app = GetComponent<VirtualRideApp>();
        }

        private void OnGUI()
        {
            if (_app == null)
            {
                return;
            }

            EnsureStyles();
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.72f, 1.35f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            DrawTopHud(width);
            DrawRouteProgress(width);
            DrawResearchBadge(width);
            DrawStatus(width, height);
            DrawControls(width, height);

            if (ReferenceEquals(_app.ActiveInput, _app.CameraInput))
            {
                DrawCameraPanel(width, height);
            }

            if (_app.IsPaused)
            {
                DrawPausedOverlay(width, height);
            }

            if (_app.HelpVisible)
            {
                DrawHelp(width, height);
            }

            if (_app.ResearchPanelVisible)
            {
                DrawResearchPanel(width, height);
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawTopHud(float width)
        {
            Rect panel = new Rect(28f, 24f, 460f, 151f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.88f));

            GUI.Label(new Rect(50f, 36f, 280f, 75f), _app.DisplaySpeedKph.ToString("0.0"), _speedStyle);
            GUI.Label(new Rect(277f, 75f, 90f, 30f), "km/h", _unitStyle);

            RideInputSample sample = _app.ActiveSample;
            string cadence = sample.HasCadence ? Mathf.RoundToInt(sample.CadenceRpm) + " rpm" : "-- rpm";
            string rideTime = FormatTime(_app.Session.MovingSeconds);
            string distance = (_app.Session.DistanceMetres / 1000f).ToString("0.00") + " km";
            GUI.Label(new Rect(52f, 109f, 410f, 28f), $"{cadence}    {distance}    {rideTime}", _bodyStyle);
            GUI.Label(new Rect(52f, 139f, 410f, 25f),
                $"平均 {_app.Session.AverageSpeedKph:0.0}  ・  最高 {_app.Session.MaximumSpeedKph:0.0} km/h",
                _smallStyle);
        }

        private void DrawRouteProgress(float width)
        {
            Rect panel = new Rect(width - 462f, 24f, 434f, 116f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.88f));
            GUI.Label(new Rect(panel.x + 22f, panel.y + 14f, 250f, 30f), _app.AreaName, _headingStyle);
            GUI.Label(new Rect(panel.xMax - 98f, panel.y + 18f, 76f, 26f),
                Mathf.RoundToInt(_app.RouteProgress * 100f) + "%", _bodyStyle);

            Rect track = new Rect(panel.x + 22f, panel.y + 57f, panel.width - 44f, 12f);
            GUI.DrawTexture(track, _secondaryTexture, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * _app.RouteProgress, track.height),
                _primaryTexture, ScaleMode.StretchToFill);
            GUI.Label(new Rect(panel.x + 22f, panel.y + 77f, panel.width - 44f, 25f),
                $"周回 {_app.Route.TotalLength / 1000f:0.00} km  ・  入力: {_app.InputModeName}", _smallStyle);
        }

        private void DrawStatus(float width, float height)
        {
            RideInputSample sample = _app.ActiveSample;
            Color statusColor = sample.State == RideInputState.Detected
                ? new Color(0.21f, 0.78f, 0.53f, 0.94f)
                : sample.State == RideInputState.Error
                    ? new Color(0.96f, 0.36f, 0.27f, 0.95f)
                    : new Color(0.06f, 0.16f, 0.18f, 0.88f);

            float statusWidth = Mathf.Min(650f, width - 56f);
            Rect statusRect = new Rect((width - statusWidth) * 0.5f, height - 126f, statusWidth, 38f);
            DrawPanel(statusRect, statusColor);
            GUI.Label(statusRect, sample.Status, _statusStyle);
        }

        private void DrawControls(float width, float height)
        {
            float panelWidth = Mathf.Min(1050f, width - 56f);
            Rect panel = new Rect((width - panelWidth) * 0.5f, height - 79f, panelWidth, 58f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.91f));

            float x = panel.x + 13f;
            float y = panel.y + 10f;
            if (GUI.Button(new Rect(x, y, 142f, 38f), "カメラ計測",
                    ReferenceEquals(_app.ActiveInput, _app.CameraInput) ? _primaryButtonStyle : _buttonStyle))
            {
                _app.UseCameraInput();
            }

            x += 150f;
            if (GUI.Button(new Rect(x, y, 142f, 38f), "キーボード",
                    ReferenceEquals(_app.ActiveInput, _app.KeyboardInput) ? _primaryButtonStyle : _buttonStyle))
            {
                _app.UseKeyboardInput();
            }

            x += 150f;
            if (GUI.Button(new Rect(x, y, 48f, 38f), "−", _buttonStyle))
            {
                _app.AdjustKeyboardSpeed(-2f);
            }

            x += 54f;
            if (GUI.Button(new Rect(x, y, 48f, 38f), "＋", _buttonStyle))
            {
                _app.AdjustKeyboardSpeed(2f);
            }

            x += 60f;
            if (GUI.Button(new Rect(x, y, 116f, 38f), _app.IsPaused ? "再開" : "一時停止", _buttonStyle))
            {
                _app.TogglePause();
            }

            x += 124f;
            if (GUI.Button(new Rect(x, y, 90f, 38f), "全画面", _buttonStyle))
            {
                _app.ToggleFullscreen();
            }

            x += 98f;
            if (GUI.Button(new Rect(x, y, 90f, 38f), "使い方", _buttonStyle))
            {
                _app.ToggleHelp();
            }

            x += 98f;
            if (GUI.Button(new Rect(x, y, 46f, 38f), _app.WindEnabled ? "音" : "消音", _buttonStyle))
            {
                _app.ToggleWind();
            }

            x += 54f;
            if (GUI.Button(new Rect(x, y, 126f, 38f),
                _app.ResearchRecorder.IsRecording ? "● 記録中" : "実験記録",
                _app.ResearchRecorder.IsRecording ? _dangerButtonStyle : _buttonStyle))
            {
                _app.ToggleResearchPanel();
            }
        }

        private void DrawResearchBadge(float width)
        {
            ResearchSessionRecorder recorder = _app.ResearchRecorder;
            if (!recorder.IsRecording)
            {
                return;
            }

            Rect badge = new Rect(width * 0.5f - 160f, 24f, 320f, 40f);
            DrawPanel(badge, new Color(0.78f, 0.16f, 0.13f, 0.94f));
            GUI.Label(badge,
                $"● 実験記録中  {FormatTime(recorder.RecordingElapsedSeconds)}  {recorder.ParticipantId}",
                _statusStyle);
        }

        private void DrawCameraPanel(float width, float height)
        {
            Rect panel = new Rect(28f, 194f, 318f, 322f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.91f));
            GUI.Label(new Rect(panel.x + 18f, panel.y + 13f, panel.width - 36f, 27f), "ペダル確認", _headingStyle);

            Rect preview = new Rect(panel.x + 18f, panel.y + 48f, panel.width - 36f, 178f);
            CameraCadenceInput camera = _app.CameraInput;
            GUI.DrawTexture(preview, _secondaryTexture, ScaleMode.StretchToFill);
            if (camera.HasPreview)
            {
                Matrix4x4 oldMatrix = GUI.matrix;
                Vector2 pivot = preview.center;
                GUIUtility.RotateAroundPivot(-camera.PreviewTextureRotation(), pivot);
                Rect rotatedRect = preview;
                if (Mathf.Abs(camera.PreviewTextureRotation()) == 90f)
                {
                    rotatedRect = new Rect(
                        pivot.x - preview.height * 0.5f,
                        pivot.y - preview.width * 0.5f,
                        preview.height,
                        preview.width);
                }

                GUI.DrawTexture(rotatedRect, camera.PreviewTexture, ScaleMode.ScaleAndCrop, true);
                GUI.matrix = oldMatrix;
            }
            else
            {
                GUI.Label(preview, "カメラ準備中…", _statusStyle);
            }

            Rect motionTrack = new Rect(panel.x + 18f, panel.y + 235f, panel.width - 36f, 8f);
            GUI.DrawTexture(motionTrack, _secondaryTexture, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(motionTrack.x, motionTrack.y, motionTrack.width * camera.MotionLevel, motionTrack.height),
                _primaryTexture, ScaleMode.StretchToFill);

            if (GUI.Button(new Rect(panel.x + 18f, panel.y + 258f, 131f, 38f),
                camera.BothLegsVisible ? "両足が映る" : "片足だけ映る", _buttonStyle))
            {
                camera.ToggleLegView();
            }

            if (GUI.Button(new Rect(panel.x + 158f, panel.y + 258f, 142f, 38f),
                "感度: " + camera.SensitivityLabel, _buttonStyle))
            {
                camera.CycleSensitivity();
            }

            GUI.Label(new Rect(panel.x + 18f, panel.y + 300f, panel.width - 36f, 20f),
                "映像は保存・送信しません", _smallStyle);
        }

        private void DrawPausedOverlay(float width, float height)
        {
            Rect card = new Rect(width * 0.5f - 230f, height * 0.5f - 86f, 460f, 172f);
            DrawPanel(card, new Color(0.02f, 0.05f, 0.06f, 0.94f));
            GUI.Label(new Rect(card.x + 25f, card.y + 24f, card.width - 50f, 46f), "一時停止", _headingStyle);
            GUI.Label(new Rect(card.x + 25f, card.y + 67f, card.width - 50f, 30f),
                "Spaceキーまたは下のボタンで再開します", _bodyStyle);
            if (GUI.Button(new Rect(card.x + 145f, card.y + 111f, 170f, 42f), "ライドを再開", _primaryButtonStyle))
            {
                _app.TogglePause();
            }
        }

        private void DrawHelp(float width, float height)
        {
            GUI.DrawTexture(new Rect(0f, 0f, width, height), _whiteTexture, ScaleMode.StretchToFill,
                true, 0f, new Color(0.01f, 0.025f, 0.03f, 0.78f), 0f, 0f);

            float cardWidth = Mathf.Min(760f, width - 80f);
            float cardHeight = Mathf.Min(600f, height - 70f);
            Rect card = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f, cardWidth, cardHeight);
            DrawPanel(card, new Color(0.035f, 0.075f, 0.08f, 0.98f));

            GUI.Label(new Rect(card.x + 38f, card.y + 30f, card.width - 76f, 46f),
                "VIRTUAL RIDE  —  仮想空間サイクリング", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 80f, card.width - 76f, 52f),
                "漕ぐ速さに合わせて、Unityで生成した田園コースを進みます。\nまずはキーボードで体験し、その後カメラ計測へ切り替えられます。", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 151f, card.width - 76f, 29f), "すぐ試す", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 184f, card.width - 76f, 93f),
                "1. 「キーボードで試す」を押します\n2. ↑ または W で速度を上げます\n3. ↓ または S で速度を下げます（Spaceで停止／再開）", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 291f, card.width - 76f, 29f), "ルームバイクで使う", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 324f, card.width - 76f, 112f),
                "1. PCカメラにペダルと足元が横から映るよう固定します\n2. 「カメラ計測を始める」を押します\n3. 3〜5秒、一定のペースで漕ぎます\n4. 検出中と rpm が表示されたら、その速さで仮想空間を進みます", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + cardHeight - 135f, card.width - 76f, 40f),
                "カメラ映像はこのPC内だけで計算し、保存も送信もしません。", _smallStyle);

            float buttonY = card.y + cardHeight - 80f;
            if (GUI.Button(new Rect(card.x + 38f, buttonY, 218f, 48f), "キーボードで試す", _primaryButtonStyle))
            {
                _app.UseKeyboardInput();
                _app.HideHelp();
            }

            if (GUI.Button(new Rect(card.x + 272f, buttonY, 238f, 48f), "カメラ計測を始める", _buttonStyle))
            {
                _app.UseCameraInput();
                _app.HideHelp();
            }

            if (GUI.Button(new Rect(card.xMax - 150f, buttonY, 112f, 48f), "閉じる", _buttonStyle))
            {
                _app.HideHelp();
            }
        }

        private void DrawResearchPanel(float width, float height)
        {
            GUI.DrawTexture(new Rect(0f, 0f, width, height), _whiteTexture, ScaleMode.StretchToFill,
                true, 0f, new Color(0.01f, 0.025f, 0.03f, 0.78f), 0f, 0f);

            ResearchSessionRecorder recorder = _app.ResearchRecorder;
            float cardWidth = Mathf.Min(720f, width - 80f);
            float cardHeight = Mathf.Min(540f, height - 70f);
            Rect card = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f, cardWidth, cardHeight);
            DrawPanel(card, new Color(0.035f, 0.075f, 0.08f, 0.98f));

            GUI.Label(new Rect(card.x + 38f, card.y + 28f, card.width - 76f, 42f),
                "実験セッション記録", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 73f, card.width - 76f, 48f),
                "速度・回転数・信頼度・距離などを10HzでCSVへ保存します。\nカメラ映像は記録しません。",
                _bodyStyle);

            if (!recorder.IsRecording)
            {
                GUI.Label(new Rect(card.x + 38f, card.y + 139f, 220f, 28f), "匿名の参加者ID", _bodyStyle);
                _participantId = GUI.TextField(
                    new Rect(card.x + 270f, card.y + 134f, card.width - 308f, 38f),
                    _participantId, 40, _textFieldStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 193f, 220f, 28f), "実験条件", _bodyStyle);
                _condition = GUI.TextField(
                    new Rect(card.x + 270f, card.y + 188f, card.width - 308f, 38f),
                    _condition, 40, _textFieldStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 239f, card.width - 76f, 44f),
                    "氏名・メールアドレスは入力せず、P001のような匿名IDを使ってください。\n記録開始時に距離・時間を0へ戻します。",
                    _smallStyle);
            }
            else
            {
                GUI.Label(new Rect(card.x + 38f, card.y + 139f, card.width - 76f, 34f),
                    $"参加者ID: {recorder.ParticipantId}", _bodyStyle);
                GUI.Label(new Rect(card.x + 38f, card.y + 181f, card.width - 76f, 34f),
                    $"条件: {recorder.Condition}", _bodyStyle);
                GUI.Label(new Rect(card.x + 38f, card.y + 223f, card.width - 76f, 34f),
                    $"経過: {FormatTime(recorder.RecordingElapsedSeconds)}  ・  {recorder.SampleCount} 件",
                    _bodyStyle);
            }

            Rect status = new Rect(card.x + 38f, card.y + cardHeight - 206f, card.width - 76f, 54f);
            DrawPanel(status, recorder.IsRecording
                ? new Color(0.52f, 0.12f, 0.10f, 0.92f)
                : new Color(0.06f, 0.16f, 0.18f, 0.92f));
            GUI.Label(status, recorder.LastMessage, _statusStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + cardHeight - 139f, card.width - 76f, 46f),
                "保存先: " + recorder.DataDirectory, _smallStyle);

            float buttonY = card.y + cardHeight - 76f;
            if (!recorder.IsRecording)
            {
                if (GUI.Button(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を開始", _primaryButtonStyle))
                {
                    _app.BeginResearchSession(_participantId, _condition);
                }
            }
            else if (GUI.Button(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を終了して保存", _dangerButtonStyle))
            {
                _app.EndResearchSession();
            }

            if (GUI.Button(new Rect(card.xMax - 158f, buttonY, 120f, 46f), "閉じる", _buttonStyle))
            {
                _app.HideResearchPanel();
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _whiteTexture = MakeTexture(new Color(1f, 1f, 1f, 1f));
            _roundedTexture = MakeTexture(new Color(0.07f, 0.13f, 0.14f, 1f));
            _primaryTexture = MakeTexture(new Color(0.13f, 0.72f, 0.69f, 1f));
            _secondaryTexture = MakeTexture(new Color(0.15f, 0.22f, 0.23f, 1f));
            _dangerTexture = MakeTexture(new Color(0.78f, 0.22f, 0.17f, 1f));

            string[] preferredFonts = { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "Arial" };
            _font = Font.CreateDynamicFontFromOSFont(preferredFonts, 22);

            _speedStyle = MakeLabelStyle(62, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            _unitStyle = MakeLabelStyle(20, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.72f, 0.88f, 0.87f));
            _headingStyle = MakeLabelStyle(24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            _bodyStyle = MakeLabelStyle(18, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.91f, 0.96f, 0.95f));
            _bodyStyle.wordWrap = true;
            _smallStyle = MakeLabelStyle(14, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.69f, 0.81f, 0.80f));
            _smallStyle.wordWrap = true;
            _statusStyle = MakeLabelStyle(17, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _statusStyle.wordWrap = true;

            _buttonStyle = MakeButtonStyle(_roundedTexture, new Color(0.96f, 1f, 0.99f));
            _primaryButtonStyle = MakeButtonStyle(_primaryTexture, Color.white);
            _dangerButtonStyle = MakeButtonStyle(_dangerTexture, Color.white);
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                font = _font,
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 7, 7)
            };
            _stylesReady = true;
        }

        private GUIStyle MakeLabelStyle(int size, FontStyle fontStyle, TextAnchor alignment, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = size,
                fontStyle = fontStyle,
                alignment = alignment,
                normal = { textColor = color },
                richText = false
            };
        }

        private GUIStyle MakeButtonStyle(Texture2D background, Color textColor)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                font = _font,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(12, 12, 8, 8)
            };
            style.normal.background = background;
            style.hover.background = _secondaryTexture;
            style.active.background = _primaryTexture;
            style.focused.background = background;
            style.normal.textColor = textColor;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = textColor;
            return style;
        }

        private static void DrawPanel(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }
    }

    internal static class CameraPreviewExtensions
    {
        public static float PreviewTextureRotation(this CameraCadenceInput input)
        {
            WebCamTexture texture = input.PreviewTexture as WebCamTexture;
            return texture != null ? texture.videoRotationAngle : 0f;
        }
    }
}
