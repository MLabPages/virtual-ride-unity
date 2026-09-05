using System.Globalization;
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
        private GUIStyle _warningStyle;
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
        private string _trialDurationText = "";
        private string _eventNote = "";
        private string _formMessage = "";

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
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 760f), 0.35f, 1.35f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            bool modal = _app.HelpVisible || _app.ResearchPanelVisible;
            GUI.enabled = !modal;
            if (!_app.MinimalHud)
            {
                DrawTopHud(width);
                DrawRouteProgress(width);
                DrawStatus(width, height);
                DrawControls(width, height);
            }
            else DrawImmersiveControls(width, height);

            if (!_app.MinimalHud && ReferenceEquals(_app.ActiveInput, _app.CameraInput))
            {
                DrawCameraPanel(width, height);
            }

            if (_app.IsPaused && !modal)
            {
                DrawPausedOverlay(width, height);
            }

            GUI.enabled = true;
            if (_app.HelpVisible)
            {
                DrawHelp(width, height);
            }

            if (_app.ResearchPanelVisible)
            {
                DrawResearchPanel(width, height);
            }

            if (!modal) DrawResearchBadge(width);
            DrawBlockedActionBanner(width);
            if (_app.MinimalHud && !modal &&
                (_app.ActiveSample.State == RideInputState.Error || _app.ActiveSample.State == RideInputState.Offline))
                DrawStatus(width, height);

            GUI.matrix = previousMatrix;
        }

        private void DrawImmersiveControls(float width, float height)
        {
            if (_app.ActiveInputIsUnvalidatedMeasurement)
            {
                Rect note = new Rect(24, 24, 284, 36);
                DrawPanel(note, new Color(.025f,.055f,.065f,.78f));
                GUI.Label(new Rect(34,30,264,26), "カメラ値は未検証の推定です", _warningStyle);
            }
            Rect panel = new Rect((width - 680f) * .5f, height - 60f, 680f, 42f);
            DrawPanel(panel, new Color(.025f, .055f, .065f, .72f));
            GUI.Label(new Rect(panel.x+12, panel.y+5, 260, 32), "景色に集中  ·  Spaceで停止 / 再開", _smallStyle);
            if (GUI.Button(new Rect(panel.x+274,panel.y+4,105,34), _app.IsPaused ? "再開" : "一時停止", _buttonStyle)) _app.TogglePause();
            if (GUI.Button(new Rect(panel.x+387,panel.y+4,140,34), "実験設定 (F7)", _buttonStyle)) _app.ToggleResearchPanel();
            if (GUI.Button(new Rect(panel.x+535,panel.y+4,132,34), "通常表示 (Tab)", _buttonStyle)) _app.ToggleMinimalHud();
        }

        private void DrawTopHud(float width)
        {
            bool unvalidated = _app.ActiveInputIsUnvalidatedMeasurement;
            Rect panel = new Rect(28f, 24f, 460f, unvalidated ? 176f : 151f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.88f));

            GUI.Label(new Rect(50f, 36f, 280f, 75f), _app.DisplaySpeedKph.ToString("0.0"), _speedStyle);
            GUI.Label(new Rect(277f, 75f, 90f, 30f), "km/h", _unitStyle);

            RideInputSample sample = _app.ActiveSample;
            string cadence = sample.HasCadence ? Mathf.RoundToInt(sample.CadenceRpm) + " rpm" : "-- rpm";
            if (unvalidated)
            {
                cadence = "推定 " + cadence;
            }

            string rideTime = FormatTime(_app.Session.MovingSeconds);
            string distance = (_app.Session.DistanceMetres / 1000f).ToString("0.00") + " km";
            GUI.Label(new Rect(52f, 109f, 410f, 28f), $"{cadence}    {distance}    {rideTime}", _bodyStyle);
            GUI.Label(new Rect(52f, 139f, 410f, 25f),
                $"平均 {_app.Session.AverageSpeedKph:0.0}  ・  最高 {_app.Session.MaximumSpeedKph:0.0} km/h",
                _smallStyle);

            if (unvalidated)
            {
                GUI.Label(new Rect(52f, 164f, 410f, 24f), "カメラ値は未検証の推定です", _warningStyle);
            }
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

            string inputLine = $"周回 {_app.Route.TotalLength / 1000f:0.00} km  ・  入力: {_app.InputModeName}";
            if (_app.IsInputLocked)
            {
                inputLine += "（固定）";
            }

            if (_app.ActiveInputIsUnvalidatedMeasurement)
            {
                inputLine += "  ※未検証";
            }

            GUI.Label(new Rect(panel.x + 22f, panel.y + 77f, panel.width - 44f, 25f), inputLine, _smallStyle);
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

            float badgeY = !_app.MinimalHud && width < 1520f ? 183f : 24f;
            Rect badge = new Rect(width * 0.5f - 210f, badgeY, 420f, 40f);
            DrawPanel(badge, new Color(0.78f, 0.16f, 0.13f, 0.94f));
            string label = $"● 実験記録中  {FormatTime(recorder.RecordingElapsedSeconds)}";
            if (recorder.HasTrialDuration)
            {
                label += "  残り " + FormatTime(recorder.RemainingTrialSeconds);
            }

            label += "  " + recorder.ParticipantId;
            GUI.Label(badge, label, _statusStyle);
        }

        private void DrawBlockedActionBanner(float width)
        {
            if (!_app.IsBlockedActionMessageVisible)
            {
                return;
            }

            float y = _app.ResearchPanelVisible || _app.HelpVisible ? 8f
                : _app.ResearchRecorder.IsRecording && !_app.MinimalHud && width < 1520f ? 232f : 210f;
            Rect banner = new Rect(width * 0.5f - 300f, y, 600f, 44f);
            DrawPanel(banner, new Color(0.82f, 0.45f, 0.10f, 0.95f));
            GUI.Label(banner, _app.BlockedActionMessage, _statusStyle);
        }

        private void DrawCameraPanel(float width, float height)
        {
            Rect panel = new Rect(28f, 210f, 318f, 348f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.91f));
            GUI.Label(new Rect(panel.x + 18f, panel.y + 13f, panel.width - 36f, 27f), "ペダル確認", _headingStyle);

            Rect preview = new Rect(panel.x + 18f, panel.y + 48f, panel.width - 36f, 168f);
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

            Rect motionTrack = new Rect(panel.x + 18f, panel.y + 224f, panel.width - 36f, 8f);
            GUI.DrawTexture(motionTrack, _secondaryTexture, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(motionTrack.x, motionTrack.y, motionTrack.width * camera.MotionLevel, motionTrack.height),
                _primaryTexture, ScaleMode.StretchToFill);

            if (GUI.Button(new Rect(panel.x + 18f, panel.y + 244f, 131f, 38f),
                camera.BothLegsVisible ? "両足が映る" : "片足だけ映る", _buttonStyle))
            {
                _app.TryToggleCameraLegView();
            }

            if (GUI.Button(new Rect(panel.x + 158f, panel.y + 244f, 142f, 38f),
                "感度: " + camera.SensitivityLabel, _buttonStyle))
            {
                _app.TryCycleCameraSensitivity();
            }

            GUI.Label(new Rect(panel.x + 18f, panel.y + 288f, panel.width - 36f, 20f),
                "映像は保存・送信しません", _smallStyle);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 308f, panel.width - 36f, 28f),
                "回転数・速度は未検証の推定値です", _warningStyle);
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
            float cardHeight = Mathf.Min(640f, height - 70f);
            Rect card = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f, cardWidth, cardHeight);
            DrawPanel(card, new Color(0.035f, 0.075f, 0.08f, 0.98f));

            GUI.Label(new Rect(card.x + 38f, card.y + 30f, card.width - 76f, 46f),
                "VIRTUAL RIDE  —  仮想空間サイクリング", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 80f, card.width - 76f, 52f),
                "漕ぐ速さに合わせて、Unityで生成した田園コースを進みます。\nまずはキーボードで体験し、その後カメラ計測へ切り替えられます。", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 146f, card.width - 76f, 29f), "すぐ試す", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 176f, card.width - 76f, 72f),
                "1. 「キーボードで試す」を押します\n2. ↑ または W で速度を上げます\n3. ↓ または S で速度を下げます（Spaceで停止／再開）", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 258f, card.width - 76f, 29f), "ルームバイクで使う", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 288f, card.width - 76f, 120f),
                "1. PCカメラにペダルと足元が横から映るよう固定します\n2. 「カメラ計測を始める」を押します\n3. 3〜5秒、一定のペースで漕ぎます\n4. 検出中と rpm が表示されたら、その速さで仮想空間を進みます", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + cardHeight - 168f, card.width - 76f, 58f),
                "カメラの回転数・速度は未検証の推定値です。実験記録中は入力方式を切り替えられません。\nカメラ映像はこのPC内だけで計算し、保存も送信もしません。",
                _smallStyle);

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
            float cardWidth = Mathf.Min(740f, width - 80f);
            float cardHeight = Mathf.Min(recorder.IsRecording ? 620f : 600f, height - 50f);
            Rect card = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f, cardWidth, cardHeight);
            DrawPanel(card, new Color(0.035f, 0.075f, 0.08f, 0.98f));

            GUI.Label(new Rect(card.x + 38f, card.y + 24f, card.width - 76f, 38f),
                "実験セッション記録", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 64f, card.width - 76f, 44f),
                "数値とイベントを記録します。映像は保存しません。",
                _bodyStyle);

            if (!recorder.IsRecording)
            {
                GUI.Label(new Rect(card.x + 38f, card.y + 124f, 220f, 28f), "匿名の参加者ID", _bodyStyle);
                GUI.SetNextControlName("participantId");
                _participantId = GUI.TextField(
                    new Rect(card.x + 270f, card.y + 118f, card.width - 308f, 38f),
                    _participantId, 40, _textFieldStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 172f, 220f, 28f), "実験条件", _bodyStyle);
                GUI.SetNextControlName("condition");
                _condition = GUI.TextField(
                    new Rect(card.x + 270f, card.y + 166f, card.width - 308f, 38f),
                    _condition, 40, _textFieldStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 220f, 220f, 28f), "試行時間（秒）", _bodyStyle);
                GUI.SetNextControlName("trialDuration");
                _trialDurationText = GUI.TextField(
                    new Rect(card.x + 270f, card.y + 214f, card.width - 308f, 38f),
                    _trialDurationText, 8, _textFieldStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 262f, card.width - 76f, 52f),
                    "氏名は使わず、P001のような匿名IDにしてください。空欄または0秒は手動終了です。\n時間が来ると保存して走行を停止します。開始時は同じスタート地点に戻ります。",
                    _smallStyle);
                if (GUI.Button(new Rect(card.x+38,card.y+328,210,38), _app.ComfortMode ? "視点: 揺れなし" : "視点: ゆるやかな揺れ", _buttonStyle)) _app.ToggleComfortMode();
                if (GUI.Button(new Rect(card.x+260,card.y+328,210,38), _app.MinimalHud ? "表示: 景色に集中" : "表示: 計器あり", _buttonStyle)) _app.ToggleMinimalHud();
                if (GUI.Button(new Rect(card.x+482,card.y+328,card.width-520,38), _app.WindEnabled ? "走行音: ON" : "走行音: OFF", _buttonStyle)) _app.ToggleWind();
                GUI.Label(new Rect(card.x+38,card.y+372,card.width-76,24), "視点・表示・音は記録中固定。試行時間には一時停止中の時間も含みます。", _smallStyle);
            }
            else
            {
                string remaining = recorder.HasTrialDuration
                    ? "  ・  残り " + FormatTime(recorder.RemainingTrialSeconds)
                    : "  ・  手動終了";
                GUI.Label(new Rect(card.x + 38f, card.y + 122f, card.width - 76f, 28f),
                    $"参加者ID: {recorder.ParticipantId}    条件: {recorder.Condition}", _bodyStyle);
                GUI.Label(new Rect(card.x + 38f, card.y + 154f, card.width - 76f, 28f),
                    $"経過: {FormatTime(recorder.RecordingElapsedSeconds)}{remaining}  ・  {recorder.SampleCount} 件",
                    _bodyStyle);
                GUI.Label(new Rect(card.x + 38f, card.y + 186f, card.width - 76f, 24f),
                    "入力: " + recorder.StartingInputMode + "（記録中は変更できません）", _smallStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 220f, card.width - 76f, 26f), "イベントマーカー", _headingStyle);
                if (GUI.Button(new Rect(card.x + 38f, card.y + 252f, 150f, 40f), "指示 (F8)", _buttonStyle))
                {
                    _formMessage = "";
                    _app.AddResearchEventMarker(ResearchSessionRecorder.MarkerInstruction);
                }

                if (GUI.Button(new Rect(card.x + 198f, card.y + 252f, 150f, 40f), "休息 (F9)", _buttonStyle))
                {
                    _formMessage = "";
                    _app.AddResearchEventMarker(ResearchSessionRecorder.MarkerRest);
                }

                GUI.Label(new Rect(card.x + 38f, card.y + 302f, 80f, 28f), "メモ", _bodyStyle);
                GUI.SetNextControlName("eventNote");
                _eventNote = GUI.TextField(
                    new Rect(card.x + 118f, card.y + 296f, card.width - 286f, 38f),
                    _eventNote, 200, _textFieldStyle);
                if (GUI.Button(new Rect(card.xMax - 158f, card.y + 296f, 120f, 38f), "メモを記録", _buttonStyle))
                {
                    if (string.IsNullOrWhiteSpace(_eventNote))
                    {
                        _formMessage = "メモの内容を入力してください";
                    }
                    else if (_app.AddResearchEventMarker(ResearchSessionRecorder.MarkerNote, _eventNote))
                    {
                        _eventNote = "";
                        _formMessage = "";
                    }
                }
            }

            string statusText = !string.IsNullOrEmpty(_formMessage)
                ? _formMessage
                : recorder.LastMessage;
            Rect status = new Rect(card.x + 38f, card.y + cardHeight - 196f, card.width - 76f, 54f);
            DrawPanel(status, recorder.IsRecording || recorder.HasError
                ? new Color(0.52f, 0.12f, 0.10f, 0.92f)
                : new Color(0.06f, 0.16f, 0.18f, 0.92f));
            GUI.Label(status, statusText, _statusStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + cardHeight - 132f, card.width - 76f, 46f),
                "保存先: " + recorder.DataDirectory, _smallStyle);

            float buttonY = card.y + cardHeight - 72f;
            if (!recorder.IsRecording)
            {
                if (GUI.Button(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を開始", _primaryButtonStyle))
                {
                    if (!TryParseTrialDuration(out float duration))
                    {
                        _formMessage = "試行時間は0（手動終了）から21600秒までです";
                    }
                    else
                    {
                        _formMessage = "";
                        if (_app.BeginResearchSession(_participantId, _condition, duration)) GUI.FocusControl(null);
                    }
                }
            }
            else if (GUI.Button(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を終了して保存", _dangerButtonStyle))
            {
                _app.EndResearchSession();
            }

            if (GUI.Button(new Rect(card.x+272f,buttonY,190f,46f), "保存フォルダを開く", _buttonStyle))
            {
                if (System.IO.Directory.Exists(recorder.DataDirectory))
                    Application.OpenURL(new System.Uri(recorder.DataDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
                else _formMessage = "記録を開始すると保存フォルダを作成します";
            }

            if (GUI.Button(new Rect(card.xMax - 158f, buttonY, 120f, 46f), "閉じる", _buttonStyle))
            {
                _app.HideResearchPanel();
                GUI.FocusControl(null);
            }
        }

        private bool TryParseTrialDuration(out float seconds)
        {
            if (string.IsNullOrWhiteSpace(_trialDurationText))
            {
                seconds = 0f;
                return true;
            }

            if (!float.TryParse(_trialDurationText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            {
                seconds = 0f;
                return false;
            }

            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds > 6f * 60f * 60f)
            {
                return false;
            }

            return true;
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
            _warningStyle = MakeLabelStyle(14, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.98f, 0.78f, 0.38f));
            _warningStyle.wordWrap = true;
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
