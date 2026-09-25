using System.Collections.Generic;
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
        private GUIStyle _compactButtonStyle;
        private GUIStyle _compactPrimaryButtonStyle;
        private GUIStyle _compactDangerButtonStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _cameraNameStyle;
        private GUIStyle _cueStyle;
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
        private string _relayAddressText;
        private int _bluetoothPage;
        private string _keyboardFieldName = string.Empty;
        private TouchScreenKeyboard _touchScreenKeyboard;
        private QuestHudSurface _questSurface;

        private void Awake()
        {
            _app = GetComponent<VirtualRideApp>();
            if (QuestHudSurface.IsSupported)
            {
                _questSurface = gameObject.AddComponent<QuestHudSurface>();
            }
        }

        private void Update()
        {
            _questSurface?.UpdatePointer();
        }

        private void OnGUI()
        {
            if (_app == null)
            {
                return;
            }

            EnsureStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            RenderTexture previousTarget = null;
            bool drawingToQuestPanel = false;
            float width;
            float height;
            if (_questSurface != null && _questSurface.Texture != null)
            {
                // Layout in a fixed 1600x900 space. IMGUI maps screen coordinates onto the
                // whole active target, so this non-uniform matrix fills the panel texture.
                width = QuestHudSurface.VirtualWidth;
                height = QuestHudSurface.VirtualHeight;
                GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / width, Screen.height / height, 1f));
                if (Event.current.type == EventType.Repaint)
                {
                    previousTarget = RenderTexture.active;
                    RenderTexture.active = _questSurface.Texture;
                    GL.Clear(true, true, Color.clear);
                    drawingToQuestPanel = true;
                }
            }
            else
            {
                float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 760f), 0.35f, 1.35f);
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
                width = Screen.width / scale;
                height = Screen.height / scale;
            }

            ResponseTestRunner responseTest = _app.ResponseTest;
            bool modal = _app.HelpVisible || _app.ResearchPanelVisible || responseTest.HasResults ||
                _app.BluetoothPanelVisible;
            GUI.enabled = !modal && !responseTest.IsRunning;
            if (!modal && !_app.MinimalHud)
            {
                DrawTopHud(width);
                DrawRouteProgress(width);
                DrawStatus(width, height);
                DrawControls(width, height);
            }
            else if (!modal) DrawImmersiveControls(width, height);

            if (!modal && !_app.MinimalHud && ReferenceEquals(_app.ActiveInput, _app.CameraInput))
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

            if (_app.BluetoothPanelVisible)
            {
                DrawBluetoothPanel(width, height);
            }

            if (responseTest.IsRunning)
            {
                DrawResponseTestCue(width, responseTest);
            }

            if (responseTest.HasResults)
            {
                DrawResponseTestResults(width, height, responseTest);
            }

            if (!modal) DrawResearchBadge(width);
            DrawBlockedActionBanner(width);
            if (_app.MinimalHud && !modal &&
                (_app.ActiveSample.State == RideInputState.Error || _app.ActiveSample.State == RideInputState.Offline))
                DrawStatus(width, height);

            DrawQuestControllerPointer(width, height);
            GUI.matrix = previousMatrix;
            if (drawingToQuestPanel)
            {
                RenderTexture.active = previousTarget;
            }
        }

        private bool UiButton(Rect rect, string label, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, label, style);
            if (clicked)
            {
                return true;
            }

            bool controllerClicked = _questSurface != null && _questSurface.TryConsumeClick(rect);
            if (controllerClicked)
            {
                GUI.changed = true;
            }

            return controllerClicked;
        }

        private string UiTextField(Rect rect, string value, int maxLength, GUIStyle style, string fieldName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_questSurface != null && _questSurface.TryConsumeClick(rect))
            {
                _keyboardFieldName = fieldName;
                _touchScreenKeyboard = TouchScreenKeyboard.Open(value, TouchScreenKeyboardType.Default);
            }

            if (_keyboardFieldName == fieldName && _touchScreenKeyboard != null)
            {
                value = _touchScreenKeyboard.text ?? value;
                if (_touchScreenKeyboard.status == TouchScreenKeyboard.Status.Done ||
                    _touchScreenKeyboard.status == TouchScreenKeyboard.Status.Canceled ||
                    _touchScreenKeyboard.status == TouchScreenKeyboard.Status.LostFocus)
                {
                    _touchScreenKeyboard = null;
                    _keyboardFieldName = string.Empty;
                }
            }
#endif
            return GUI.TextField(rect, value, maxLength, style);
        }

        private void DrawQuestControllerPointer(float width, float height)
        {
            if (_questSurface == null || Event.current == null || Event.current.type != EventType.Repaint ||
                !_questSurface.TryGetPointer(out Vector2 position))
            {
                return;
            }

            DrawPanel(new Rect(position.x - 10f, position.y - 10f, 20f, 20f), new Color(0f, 0f, 0f, 0.88f));
            DrawPanel(new Rect(position.x - 6f, position.y - 6f, 12f, 12f), new Color(0.30f, 0.91f, 0.79f, 1f));
        }

        private void DrawImmersiveControls(float width, float height)
        {
            if (_app.ActiveInputIsUnvalidatedMeasurement)
            {
                Rect note = new Rect(24, 24, 284, 36);
                DrawPanel(note, new Color(.025f,.055f,.065f,.78f));
                GUI.Label(new Rect(34,30,264,26), ValidationNotice(), _warningStyle);
            }
            Rect panel = new Rect((width - 680f) * .5f, height - 60f, 680f, 42f);
            DrawPanel(panel, new Color(.025f, .055f, .065f, .72f));
            GUI.Label(new Rect(panel.x+12, panel.y+5, 260, 32), "景色に集中  ·  Spaceで停止 / 再開", _smallStyle);
            if (UiButton(new Rect(panel.x+274,panel.y+4,105,34), _app.IsPaused ? "再開" : "一時停止", _buttonStyle)) _app.TogglePause();
            if (UiButton(new Rect(panel.x+387,panel.y+4,140,34), "実験設定 (F7)", _buttonStyle)) _app.ToggleResearchPanel();
            if (UiButton(new Rect(panel.x+535,panel.y+4,132,34), "通常表示 (Tab)", _buttonStyle)) _app.ToggleMinimalHud();
        }

        private void DrawTopHud(float width)
        {
            bool unvalidated = _app.ActiveInputIsUnvalidatedMeasurement;
            Rect panel = new Rect(28f, 24f, 460f, unvalidated ? 190f : 164f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.88f));

            GUI.Label(new Rect(50f, 36f, 280f, 75f), _app.DisplaySpeedKph.ToString("0.0"), _speedStyle);
            GUI.Label(new Rect(277f, 75f, 90f, 30f), "km/h", _unitStyle);

            RideInputSample sample = _app.ActiveSample;
            string cadence = sample.HasCadence ? Mathf.RoundToInt(sample.CadenceRpm) + " rpm" : "-- rpm";
            if (_app.ActiveInputIsCamera)
            {
                cadence = "推定 " + cadence;
            }

            string rideTime = FormatTime(_app.Session.MovingSeconds);
            string distance = (_app.Session.DistanceMetres / 1000f).ToString("0.00") + " km";
            GUI.Label(new Rect(52f, 116f, 410f, 28f), $"{cadence}    {distance}    {rideTime}", _bodyStyle);
            GUI.Label(new Rect(52f, 146f, 410f, 25f),
                $"平均 {_app.Session.AverageSpeedKph:0.0}  ・  最高 {_app.Session.MaximumSpeedKph:0.0} km/h",
                _smallStyle);

            if (unvalidated)
            {
                GUI.Label(new Rect(52f, 172f, 410f, 24f), ValidationNotice(), _warningStyle);
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

            GUI.Label(new Rect(panel.x + 22f, panel.y + 75f, panel.width - 44f, 37f), inputLine, _smallStyle);
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
            string statusText = sample.Status ?? string.Empty;
            float statusHeight = Mathf.Clamp(
                _statusStyle.CalcHeight(new GUIContent(statusText), statusWidth - 24f) + 12f, 42f, 110f);
            Rect statusRect = new Rect((width - statusWidth) * 0.5f,
                height - statusHeight - 90f, statusWidth, statusHeight);
            DrawPanel(statusRect, statusColor);
            GUI.Label(new Rect(statusRect.x + 12f, statusRect.y + 6f,
                statusRect.width - 24f, statusRect.height - 12f), statusText, _statusStyle);
        }

        private string ValidationNotice()
        {
            if (_app.ActiveInputIsBluetooth)
            {
                return "Bluetooth計測: 基準機器と未照合";
            }
            return "カメラ計測: 基準センサーと未照合";
        }

        private void DrawControls(float width, float height)
        {
            GUIStyle controlStyle = _questSurface != null ? _compactButtonStyle : _buttonStyle;
            GUIStyle activeStyle = _questSurface != null ? _compactPrimaryButtonStyle : _primaryButtonStyle;
            GUIStyle recordingStyle = _questSurface != null ? _compactDangerButtonStyle : _dangerButtonStyle;
            float panelWidth = Mathf.Min(1050f, width - 56f);
            Rect panel = new Rect((width - panelWidth) * 0.5f, height - 79f, panelWidth, 58f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.91f));

            float x = panel.x + 13f;
            float y = panel.y + 10f;
            if (UiButton(new Rect(x, y, 142f, 38f), _app.SupportsRelayInput ? "Quest USB" : "カメラ計測",
                    ReferenceEquals(_app.ActiveInput, _app.CameraInput) ? activeStyle : controlStyle))
            {
                _app.UseCameraInput();
            }

            x += 150f;
            if (UiButton(new Rect(x, y, 142f, 38f), "キーボード",
                    ReferenceEquals(_app.ActiveInput, _app.KeyboardInput) ? activeStyle : controlStyle))
            {
                _app.UseKeyboardInput();
            }

            x += 150f;
            if (UiButton(new Rect(x, y, 48f, 38f), "−", controlStyle))
            {
                _app.AdjustKeyboardSpeed(-2f);
            }

            x += 54f;
            if (UiButton(new Rect(x, y, 48f, 38f), "＋", controlStyle))
            {
                _app.AdjustKeyboardSpeed(2f);
            }

            x += 60f;
            if (UiButton(new Rect(x, y, 116f, 38f), _app.IsPaused ? "再開" : "一時停止", controlStyle))
            {
                _app.TogglePause();
            }

            x += 124f;
            if (_app.SupportsRelayInput)
            {
                if (UiButton(new Rect(x, y, 90f, 38f), "PC中継",
                        ReferenceEquals(_app.ActiveInput, _app.RelayInput) ? activeStyle : controlStyle))
                {
                    _app.UseRelayInput();
                }
            }
            else if (UiButton(new Rect(x, y, 90f, 38f), "全画面", controlStyle))
            {
                _app.ToggleFullscreen();
            }

            x += 98f;
            if (UiButton(new Rect(x, y, 90f, 38f), "使い方", controlStyle))
            {
                _app.ToggleHelp();
            }

            x += 98f;
            if (UiButton(new Rect(x, y, 46f, 38f), _app.WindEnabled ? "音" : "消音", controlStyle))
            {
                _app.ToggleWind();
            }

            x += 54f;
            if (UiButton(new Rect(x, y, 126f, 38f),
                _app.ResearchRecorder.IsRecording ? "● 記録中" : "実験記録",
                _app.ResearchRecorder.IsRecording ? recordingStyle : controlStyle))
            {
                _app.ToggleResearchPanel();
            }

            x += 134f;
            if (UiButton(new Rect(x, y, 104f, 38f), "BLEセンサー", controlStyle))
            {
                _app.ShowBluetoothPanel();
            }
        }

        private void DrawBluetoothPanel(float width, float height)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.66f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), _whiteTexture);
            GUI.color = previousColor;

            float cardWidth = Mathf.Min(760f, width - 48f);
            float cardHeight = 470f;
            Rect panel = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f,
                cardWidth, cardHeight);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.98f));
            GUI.Label(new Rect(panel.x + 28f, panel.y + 20f, cardWidth - 56f, 40f),
                "Bluetoothケイデンスセンサー", _headingStyle);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 62f, cardWidth - 56f, 48f),
                _app.BluetoothInput.Status, _bodyStyle);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 112f, cardWidth - 56f, 42f),
                "BK9Cの絶縁シートを外し、回して青点滅を確認。回しながら再検索してください。",
                _smallStyle);

            List<BluetoothCadenceDevice> devices = _app.BluetoothInput.Devices;
            devices.Sort((left, right) =>
            {
                int preferred = IsKnownCadenceSensor(right.Name).CompareTo(IsKnownCadenceSensor(left.Name));
                return preferred != 0 ? preferred : right.SignalStrength.CompareTo(left.SignalStrength);
            });
            const int pageSize = 4;
            int pageCount = Mathf.Max(1, (devices.Count + pageSize - 1) / pageSize);
            _bluetoothPage = Mathf.Clamp(_bluetoothPage, 0, pageCount - 1);
            GUI.Label(new Rect(panel.x + 30f, panel.y + 155f, 260f, 27f),
                $"検出 {devices.Count} 件  ・  {_bluetoothPage + 1}/{pageCount} ページ", _smallStyle);
            if (pageCount > 1)
            {
                if (UiButton(new Rect(panel.xMax - 132f, panel.y + 151f, 44f, 29f), "◀", _compactButtonStyle))
                    _bluetoothPage = Mathf.Max(0, _bluetoothPage - 1);
                if (UiButton(new Rect(panel.xMax - 78f, panel.y + 151f, 44f, 29f), "▶", _compactButtonStyle))
                    _bluetoothPage = Mathf.Min(pageCount - 1, _bluetoothPage + 1);
            }
            float rowY = panel.y + 185f;
            float rowHeight = 45f;
            int start = _bluetoothPage * pageSize;
            int rows = Mathf.Min(devices.Count - start, pageSize);
            for (int i = 0; i < rows; i++)
            {
                BluetoothCadenceDevice device = devices[start + i];
                Rect row = new Rect(panel.x + 24f, rowY + i * rowHeight, cardWidth - 48f, rowHeight - 3f);
                DrawPanel(row, new Color(0.08f, 0.15f, 0.17f, 0.94f));
                string shortAddress = device.Address.Length > 4
                    ? device.Address.Substring(device.Address.Length - 4)
                    : device.Address;
                GUI.Label(new Rect(row.x + 12f, row.y + 4f, row.width - 150f, 34f),
                    device.Name + "  · " + shortAddress + "  " + device.SignalStrength + " dBm", _bodyStyle);
                if (UiButton(new Rect(row.xMax - 112f, row.y + 3f, 100f, 34f), "接続", _primaryButtonStyle))
                {
                    _app.TryConnectBluetoothInput(device.Address);
                }
            }

            if (rows == 0)
            {
                string emptyMessage = _app.BluetoothInput.IsScanning
                    ? "検索中です。センサーを回して起動してください。"
                    : "近くのセンサーがここに表示されます。";
                GUI.Label(new Rect(panel.x + 30f, rowY + 12f, cardWidth - 60f, 36f), emptyMessage, _smallStyle);
            }

            if (_app.BluetoothInput.IsConnected)
            {
                GUI.Label(new Rect(panel.x + 28f, panel.y + 373f, cardWidth - 56f, 30f),
                    "接続中: " + _app.BluetoothInput.ConnectedDeviceName, _bodyStyle);
            }

            float buttonY = panel.y + cardHeight - 56f;
            if (UiButton(new Rect(panel.x + 24f, buttonY, 132f, 38f), "再検索", _buttonStyle))
            {
                _app.TryBeginBluetoothScan();
            }
            if (UiButton(new Rect(panel.x + 166f, buttonY, 126f, 38f), "カメラに戻る", _buttonStyle))
            {
                _app.UseCameraInput();
                _app.HideBluetoothPanel();
            }
            if (UiButton(new Rect(panel.x + 302f, buttonY, 132f, 38f), "キーボード", _buttonStyle))
            {
                _app.UseKeyboardInput();
                _app.HideBluetoothPanel();
            }
            if (_app.BluetoothInput.IsConnected && UiButton(
                new Rect(panel.xMax - 260f, buttonY, 126f, 38f), "センサー切断", _buttonStyle))
            {
                _app.TryDisconnectBluetoothInput();
            }
            if (UiButton(new Rect(panel.xMax - 124f, buttonY, 100f, 38f), "閉じる", _buttonStyle))
            {
                _app.HideBluetoothPanel();
            }
        }

        private static bool IsKnownCadenceSensor(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                (name.IndexOf("BK9C", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("CAD70", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("S314", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawResearchBadge(float width)
        {
            ResearchSessionRecorder recorder = _app.ResearchRecorder;
            if (!recorder.IsRecording)
            {
                return;
            }

            float badgeY = !_app.MinimalHud && width < 1520f
                ? (_app.ActiveInputIsUnvalidatedMeasurement ? 226f : 198f)
                : 24f;
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

            bool modal = _app.ResearchPanelVisible || _app.HelpVisible;
            float y = modal ? 8f
                : _app.ResearchRecorder.IsRecording && !_app.MinimalHud && width < 1520f
                    ? (_app.ActiveInputIsUnvalidatedMeasurement ? 276f : 247f)
                    : 210f;
            float bannerWidth = Mathf.Min(600f, width - 56f);
            float bannerHeight = Mathf.Clamp(
                _statusStyle.CalcHeight(new GUIContent(_app.BlockedActionMessage), bannerWidth - 24f) + 12f,
                44f, modal ? 80f : 110f);
            Rect banner = new Rect((width - bannerWidth) * 0.5f, y, bannerWidth, bannerHeight);
            DrawPanel(banner, new Color(0.82f, 0.45f, 0.10f, 0.95f));
            GUI.Label(new Rect(banner.x + 12f, banner.y + 6f,
                banner.width - 24f, banner.height - 12f), _app.BlockedActionMessage, _statusStyle);
        }

        private void DrawCameraPanel(float width, float height)
        {
            Rect panel = new Rect(28f, 226f, 318f, 424f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.065f, 0.91f));
            GUI.Label(new Rect(panel.x + 18f, panel.y + 11f, panel.width - 36f, 27f), "ペダル確認", _headingStyle);

            CameraCadenceInput camera = _app.CameraInput;
            if (UiButton(new Rect(panel.x + 18f, panel.y + 44f, 34f, 34f), "◀", _compactButtonStyle))
            {
                _app.TrySelectAdjacentCamera(-1);
            }

            GUI.Label(new Rect(panel.x + 58f, panel.y + 44f, panel.width - 116f, 34f),
                camera.SelectedDeviceLabel, _cameraNameStyle);
            if (UiButton(new Rect(panel.xMax - 52f, panel.y + 44f, 34f, 34f), "▶", _compactButtonStyle))
            {
                _app.TrySelectAdjacentCamera(1);
            }

            Rect preview = new Rect(panel.x + 18f, panel.y + 86f, panel.width - 36f, 158f);
            GUI.DrawTexture(preview, _secondaryTexture, ScaleMode.StretchToFill);
            if (!_app.PedalPreviewVisible)
            {
                GUI.Label(preview, "ペダル映像は非表示\n（計測は続けています）", _statusStyle);
            }
            else if (camera.HasPreview)
            {
                float rotation = camera.PreviewTextureRotation();
                if (Mathf.Approximately(rotation, 0f))
                {
                    // Show the whole frame so the region outline matches what is analysed.
                    Rect fitted = FitRect(preview, camera.PreviewTexture.width / (float)camera.PreviewTexture.height);
                    GUI.DrawTexture(fitted, camera.PreviewTexture, ScaleMode.StretchToFill, true);
                    DrawRegionOutline(fitted, camera.RegionRect);
                }
                else
                {
                    Matrix4x4 oldMatrix = GUI.matrix;
                    Vector2 pivot = preview.center;
                    GUIUtility.RotateAroundPivot(-rotation, pivot);
                    Rect rotatedRect = preview;
                    if (Mathf.Abs(rotation) == 90f)
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
            }
            else
            {
                GUI.Label(preview, _app.ActiveSample.State == RideInputState.Error ? "映像なし" : "カメラ準備中…",
                    _statusStyle);
            }

            Rect motionTrack = new Rect(panel.x + 18f, panel.y + 252f, panel.width - 36f, 8f);
            GUI.DrawTexture(motionTrack, _secondaryTexture, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(motionTrack.x, motionTrack.y, motionTrack.width * camera.MotionLevel, motionTrack.height),
                _primaryTexture, ScaleMode.StretchToFill);

            if (UiButton(new Rect(panel.x + 18f, panel.y + 268f, 131f, 36f),
                camera.BothLegsVisible ? "両足が映る" : "片足だけ映る", _buttonStyle))
            {
                _app.TryToggleCameraLegView();
            }

            if (UiButton(new Rect(panel.x + 158f, panel.y + 268f, 142f, 36f),
                "感度: " + camera.SensitivityLabel, _buttonStyle))
            {
                _app.TryCycleCameraSensitivity();
            }

            if (UiButton(new Rect(panel.x + 18f, panel.y + 310f, 180f, 36f),
                "計測範囲: " + camera.RegionLabel, _buttonStyle))
            {
                _app.TryCycleCameraRegion();
            }

            if (UiButton(new Rect(panel.x + 206f, panel.y + 310f, 94f, 36f), "再接続", _buttonStyle))
            {
                _app.TryRestartCamera();
            }

            GUI.Label(new Rect(panel.x + 18f, panel.y + 352f, panel.width - 36f, 40f),
                "枠内の動きだけを計測します。\n映像は保存しません。", _smallStyle);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 396f, panel.width - 36f, 24f),
                ValidationNotice(), _warningStyle);
        }

        private static Rect FitRect(Rect bounds, float aspect)
        {
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect))
            {
                return bounds;
            }

            if (bounds.width / bounds.height > aspect)
            {
                float fittedWidth = bounds.height * aspect;
                return new Rect(bounds.center.x - fittedWidth * 0.5f, bounds.y, fittedWidth, bounds.height);
            }

            float fittedHeight = bounds.width / aspect;
            return new Rect(bounds.x, bounds.center.y - fittedHeight * 0.5f, bounds.width, fittedHeight);
        }

        private void DrawRegionOutline(Rect image, Rect region)
        {
            if (region.width >= 0.999f && region.height >= 0.999f)
            {
                return;
            }

            // Texture coordinates start at the bottom-left; GUI coordinates start at the top-left.
            Rect outline = new Rect(
                image.x + region.x * image.width,
                image.y + (1f - region.y - region.height) * image.height,
                region.width * image.width,
                region.height * image.height);
            const float thickness = 3f;
            GUI.DrawTexture(new Rect(outline.x, outline.y, outline.width, thickness), _primaryTexture);
            GUI.DrawTexture(new Rect(outline.x, outline.yMax - thickness, outline.width, thickness), _primaryTexture);
            GUI.DrawTexture(new Rect(outline.x, outline.y, thickness, outline.height), _primaryTexture);
            GUI.DrawTexture(new Rect(outline.xMax - thickness, outline.y, thickness, outline.height), _primaryTexture);
        }

        private void DrawPausedOverlay(float width, float height)
        {
            Rect card = new Rect(width * 0.5f - 230f, height * 0.5f - 86f, 460f, 172f);
            DrawPanel(card, new Color(0.02f, 0.05f, 0.06f, 0.94f));
            GUI.Label(new Rect(card.x + 25f, card.y + 24f, card.width - 50f, 46f), "一時停止", _headingStyle);
            GUI.Label(new Rect(card.x + 25f, card.y + 67f, card.width - 50f, 30f),
                "Spaceキーまたは下のボタンで再開します", _bodyStyle);
            if (UiButton(new Rect(card.x + 145f, card.y + 111f, 170f, 42f), "ライドを再開", _primaryButtonStyle))
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
                "漕ぐ速さに合わせて、Unityで生成した田園コースを進みます。\nキーボード、カメラ、Bluetoothケイデンスセンサーを選べます。", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 146f, card.width - 76f, 29f), "すぐ試す", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 176f, card.width - 76f, 90f),
                "1. 「キーボードで試す」を押します\n2. ↑ または W で速度を上げます\n3. ↓ または S で速度を下げます（Spaceで停止／再開）", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + 274f, card.width - 76f, 29f), "ルームバイクで使う", _headingStyle);
            GUI.Label(new Rect(card.x + 38f, card.y + 306f, card.width - 76f, 132f),
                _app.SupportsRelayInput
                    ? "1. PCカメラ: Windows版で計測し「Questへ送信」をON\n2. Questでは下の「PC中継」を選びます\n3. BK9Cは「BLEセンサー」から直接接続します\n4. 手を向けて指をつまむと選択できます"
                    : "1. USBカメラを使う場合は「カメラ計測」を選び、左の ◀ ▶ で選択します\n2. ケイデンスセンサーは下部の「BLEセンサー」から検索して選びます\n3. カメラは計測範囲を調整し、センサーはペダルを回して起動します\n4. rpm が表示されたら、その速さでコースを進みます", _bodyStyle);

            GUI.Label(new Rect(card.x + 38f, card.y + cardHeight - 168f, card.width - 76f, 58f),
                _app.SupportsRelayInput
                    ? "カメラ推定・Bluetooth計測とも基準機器との照合前です。実験記録中は入力を切り替えられません。\nPC中継は速度・rpmだけを送り、カメラ映像は送りません。"
                    : "カメラ推定・Bluetooth計測とも基準機器との照合前です。実験記録中は入力を切り替えられません。\n入力データはこのPC内で扱います。カメラ映像は保存・送信しません。",
                _smallStyle);

            float buttonY = card.y + cardHeight - 80f;
            if (UiButton(new Rect(card.x + 38f, buttonY, 218f, 48f), "キーボードで試す", _primaryButtonStyle))
            {
                _app.UseKeyboardInput();
                _app.HideHelp();
            }

            if (UiButton(new Rect(card.x + 272f, buttonY, 238f, 48f),
                    _app.SupportsRelayInput ? "PC中継を始める" : "カメラ計測を始める", _buttonStyle))
            {
                if (_app.SupportsRelayInput) _app.UseRelayInput();
                else _app.UseCameraInput();
                _app.HideHelp();
            }

            if (UiButton(new Rect(card.xMax - 150f, buttonY, 112f, 48f), "閉じる", _buttonStyle))
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
            float cardHeight = Mathf.Min(recorder.IsRecording ? 620f : 700f, height - 50f);
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
                _participantId = UiTextField(
                    new Rect(card.x + 270f, card.y + 118f, card.width - 308f, 38f),
                    _participantId, 40, _textFieldStyle, "participantId");

                GUI.Label(new Rect(card.x + 38f, card.y + 172f, 220f, 28f), "実験条件", _bodyStyle);
                GUI.SetNextControlName("condition");
                _condition = UiTextField(
                    new Rect(card.x + 270f, card.y + 166f, card.width - 308f, 38f),
                    _condition, 40, _textFieldStyle, "condition");

                GUI.Label(new Rect(card.x + 38f, card.y + 220f, 220f, 28f), "試行時間（秒）", _bodyStyle);
                GUI.SetNextControlName("trialDuration");
                _trialDurationText = UiTextField(
                    new Rect(card.x + 270f, card.y + 214f, card.width - 308f, 38f),
                    _trialDurationText, 8, _textFieldStyle, "trialDuration");

                GUI.Label(new Rect(card.x + 38f, card.y + 262f, card.width - 76f, 52f),
                    "氏名は使わず、P001のような匿名IDにしてください。空欄または0秒は手動終了です。\n時間が来ると保存して走行を停止します。開始時は同じスタート地点に戻ります。",
                    _smallStyle);
                if (UiButton(new Rect(card.x+38,card.y+328,170,38), _app.ComfortMode ? "視点: 揺れなし" : "視点: ゆるやかな揺れ", _compactButtonStyle)) _app.ToggleComfortMode();
                if (UiButton(new Rect(card.x+216,card.y+328,170,38), _app.MinimalHud ? "表示: 景色に集中" : "表示: 計器あり", _compactButtonStyle)) _app.ToggleMinimalHud();
                if (UiButton(new Rect(card.x+394,card.y+328,150,38), _app.WindEnabled ? "走行音: ON" : "走行音: OFF", _compactButtonStyle)) _app.ToggleWind();
                if (UiButton(new Rect(card.x+552,card.y+328,card.width-590,38), _app.DistanceSignsVisible ? "距離看板: 表示" : "距離看板: なし", _compactButtonStyle)) _app.ToggleDistanceSigns();
                DrawVideoSpeedControls(card);
                if (UiButton(new Rect(card.x + 38f, card.y + 418f, 210f, 38f),
                    _app.PedalPreviewVisible ? "ペダル映像: 表示" : "ペダル映像: 非表示", _buttonStyle))
                {
                    _app.TogglePedalPreview();
                }

                if (!_app.IsQuestBuild)
                {
                    DrawRelayControls(card);
                }
                else
                {
                    GUI.Label(new Rect(card.x + 260f, card.y + 424f, card.width - 298f, 30f),
                        "非表示でも計測は続きます", _smallStyle);
                }
                GUI.Label(new Rect(card.x + 38f, card.y + 462f, card.width - 76f, 42f),
                    "記録中は表示・音・映像速度などの設定を固定します。\nペダル映像の非表示中も計測します。一時停止中も試行時間に含みます。",
                    _smallStyle);
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
                    "入力: " + recorder.StartingInputMode + "  ・  映像: " + VideoSpeedLabel() + "（記録中は変更できません）",
                    _smallStyle);

                GUI.Label(new Rect(card.x + 38f, card.y + 220f, card.width - 76f, 26f), "イベントマーカー", _headingStyle);
                if (UiButton(new Rect(card.x + 38f, card.y + 252f, 150f, 40f), "指示 (F8)", _buttonStyle))
                {
                    _formMessage = "";
                    _app.AddResearchEventMarker(ResearchSessionRecorder.MarkerInstruction);
                }

                if (UiButton(new Rect(card.x + 198f, card.y + 252f, 150f, 40f), "休息 (F9)", _buttonStyle))
                {
                    _formMessage = "";
                    _app.AddResearchEventMarker(ResearchSessionRecorder.MarkerRest);
                }

                GUI.Label(new Rect(card.x + 38f, card.y + 302f, 80f, 28f), "メモ", _bodyStyle);
                GUI.SetNextControlName("eventNote");
                _eventNote = UiTextField(
                    new Rect(card.x + 118f, card.y + 296f, card.width - 286f, 38f),
                    _eventNote, 200, _textFieldStyle, "eventNote");
                if (UiButton(new Rect(card.xMax - 158f, card.y + 296f, 120f, 38f), "メモを記録", _buttonStyle))
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
                if (UiButton(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を開始", _primaryButtonStyle))
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
            else if (UiButton(new Rect(card.x + 38f, buttonY, 220f, 46f), "記録を終了して保存", _dangerButtonStyle))
            {
                _app.EndResearchSession();
            }

            if (UiButton(new Rect(card.x+272f,buttonY,190f,46f), "保存フォルダを開く", _buttonStyle))
            {
                if (System.IO.Directory.Exists(recorder.DataDirectory))
                    Application.OpenURL(new System.Uri(recorder.DataDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
                else _formMessage = "記録を開始すると保存フォルダを作成します";
            }

            if (!recorder.IsRecording &&
                UiButton(new Rect(card.x + 472f, buttonY, card.width - 630f, 46f), "反応テスト", _buttonStyle))
            {
                _formMessage = "";
                _app.TryStartResponseTest();
            }

            if (UiButton(new Rect(card.xMax - 158f, buttonY, 120f, 46f), "閉じる", _buttonStyle))
            {
                _app.HideResearchPanel();
                GUI.FocusControl(null);
            }
        }

        private void DrawRelayControls(Rect card)
        {
            float y = card.y + 418f;
            if (UiButton(new Rect(card.x + 260f, y, 210f, 38f),
                    _app.IsRelaySending ? "Questへ送信: ON" : "Questへ送信: OFF",
                    _app.IsRelaySending ? _primaryButtonStyle : _buttonStyle))
            {
                _app.ToggleRelaySending();
            }

            if (_relayAddressText == null)
            {
                _relayAddressText = _app.RelayQuestAddress;
            }

            Rect field = new Rect(card.x + 482f, y, card.width - 520f, 38f);
            GUI.SetNextControlName("relayAddress");
            string edited = UiTextField(field, _relayAddressText, 15, _textFieldStyle, "relayAddress");
            if (edited != _relayAddressText)
            {
                _relayAddressText = edited;
                bool accepted = _app.SetRelayQuestAddress(edited);
                bool looksComplete = edited.Split('.').Length == 4 && !edited.EndsWith(".");
                _formMessage = accepted || !looksComplete ? "" : "QuestのIPアドレスの形式が正しくありません（例: 172.20.10.3）";
            }

            if (string.IsNullOrEmpty(_relayAddressText) && GUI.GetNameOfFocusedControl() != "relayAddress")
            {
                GUI.Label(new Rect(field.x + 12f, field.y + 9f, field.width - 20f, 24f), "QuestのIP（空欄で自動）", _smallStyle);
            }
        }

        private void DrawVideoSpeedControls(Rect card)
        {
            float y = card.y + 372f;
            if (UiButton(new Rect(card.x + 38f, y, 210f, 38f),
                _app.IsVideoSpeedFixed ? "映像: 一定速度" : "映像: ペダル連動", _buttonStyle))
            {
                _app.SetVideoSpeedMode(_app.IsVideoSpeedFixed
                    ? VirtualRideApp.VideoSpeedMode.PedalLinked
                    : VirtualRideApp.VideoSpeedMode.Fixed);
            }

            if (!_app.IsVideoSpeedFixed)
            {
                GUI.Label(new Rect(card.x + 260f, y + 6f, card.width - 298f, 30f),
                    "ペダルの推定速度で景色が進みます", _smallStyle);
                return;
            }

            if (UiButton(new Rect(card.x + 260f, y, 44f, 38f), "−", _buttonStyle))
            {
                _app.SetFixedVideoSpeed(_app.FixedVideoSpeedKph - 0.5f);
            }

            GUI.Label(new Rect(card.x + 308f, y, 108f, 38f), _app.FixedVideoSpeedKph.ToString("0.0") + " km/h", _statusStyle);
            if (UiButton(new Rect(card.x + 420f, y, 44f, 38f), "＋", _buttonStyle))
            {
                _app.SetFixedVideoSpeed(_app.FixedVideoSpeedKph + 0.5f);
            }

            float previousAverage = _app.Session.AverageSpeedKph;
            if (previousAverage >= VirtualRideApp.MinimumFixedVideoSpeedKph &&
                UiButton(new Rect(card.x + 474f, y, card.width - 512f, 38f),
                    $"直前の平均 {previousAverage:0.0} km/h", _buttonStyle))
            {
                _app.SetFixedVideoSpeed(previousAverage);
            }
        }

        private string VideoSpeedLabel()
        {
            return _app.IsVideoSpeedFixed
                ? "一定 " + _app.FixedVideoSpeedKph.ToString("0.0") + " km/h"
                : "ペダル連動";
        }

        private void DrawResponseTestCue(float width, ResponseTestRunner test)
        {
            Rect card = new Rect((width - 560f) * 0.5f, 210f, 560f, 176f);
            DrawPanel(card, new Color(0.025f, 0.055f, 0.065f, 0.94f));
            GUI.Label(new Rect(card.x + 22f, card.y + 12f, 360f, 28f),
                test.CurrentPhase == ResponseTestRunner.Phase.Prepare
                    ? "反応テスト  準備"
                    : $"反応テスト  {test.CycleNumber}/{test.CycleCount}", _headingStyle);

            Rect beat = new Rect(card.xMax - 52f, card.y + 14f, 30f, 30f);
            GUI.DrawTexture(beat, test.BeatVisible ? _primaryTexture : _secondaryTexture);

            string cue;
            string detail;
            switch (test.CurrentPhase)
            {
                case ResponseTestRunner.Phase.Pedal:
                    cue = $"漕いでください  {Mathf.RoundToInt(test.TargetRpm)} rpm";
                    detail = "カチッという音1回ごとに片足を踏み込みます（1回転で2回）";
                    break;
                case ResponseTestRunner.Phase.Stop:
                    cue = "止めてください";
                    detail = "足を止めたまま、次の合図を待ちます";
                    break;
                default:
                    cue = "止まったまま待ってください";
                    detail = "ペダルが枠内に映っていることを確認してください";
                    break;
            }

            GUI.Label(new Rect(card.x + 22f, card.y + 48f, card.width - 44f, 48f), cue, _cueStyle);
            GUI.Label(new Rect(card.x + 22f, card.y + 100f, card.width - 44f, 24f), detail, _smallStyle);
            GUI.Label(new Rect(card.x + 22f, card.y + 132f, 300f, 30f),
                $"残り {Mathf.CeilToInt(test.PhaseRemainingSeconds)} 秒", _bodyStyle);
            GUI.enabled = true;
            if (UiButton(new Rect(card.xMax - 150f, card.y + 126f, 128f, 38f), "中止 (Esc)", _buttonStyle))
            {
                test.Cancel();
            }
        }

        private void DrawResponseTestResults(float width, float height, ResponseTestRunner test)
        {
            GUI.DrawTexture(new Rect(0f, 0f, width, height), _whiteTexture, ScaleMode.StretchToFill,
                true, 0f, new Color(0.01f, 0.025f, 0.03f, 0.78f), 0f, 0f);
            float cardWidth = Mathf.Min(820f, width - 60f);
            float cardHeight = Mathf.Min(520f, height - 50f);
            Rect card = new Rect((width - cardWidth) * 0.5f, (height - cardHeight) * 0.5f, cardWidth, cardHeight);
            DrawPanel(card, new Color(0.035f, 0.075f, 0.08f, 0.98f));
            GUI.Label(new Rect(card.x + 32f, card.y + 22f, card.width - 64f, 34f), "反応テストの結果", _headingStyle);

            float[] columns = { 32f, 150f, 262f, 374f, 486f, 640f };
            string[] headers = { "目標", "検出まで", "景色 50%", "停止判定", "景色停止", "平均 rpm（誤差）" };
            float y = card.y + 70f;
            for (int i = 0; i < headers.Length; i++)
            {
                GUI.Label(new Rect(card.x + columns[i], y, 150f, 24f), headers[i], _smallStyle);
            }

            y += 28f;
            foreach (ResponseTestRunner.CycleResult result in test.Results)
            {
                string rpm = result.MeanRpm.HasValue
                    ? $"{result.MeanRpm.Value:0.0}（{result.RpmErrorPercent.Value:+0.0;-0.0;0.0}%）"
                    : "検出なし";
                string[] cells =
                {
                    $"{result.TargetRpm:0} rpm",
                    Seconds(result.DetectLatency),
                    Seconds(result.DisplayLatency),
                    Seconds(result.StopDetectLatency),
                    Seconds(result.DisplayStopLatency),
                    rpm
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    GUI.Label(new Rect(card.x + columns[i], y, 170f, 28f), cells[i], _bodyStyle);
                }

                GUI.Label(new Rect(card.x + columns[5], y + 24f, 170f, 20f),
                    $"検出率 {result.DetectedFraction * 100f:0}%", _smallStyle);
                y += 54f;
            }

            float? absoluteError = ResponseTestRunner.Median(test.Results,
                r => r.RpmErrorPercent.HasValue ? Mathf.Abs(r.RpmErrorPercent.Value) : (float?)null);
            GUI.Label(new Rect(card.x + 32f, y + 6f, card.width - 64f, 28f),
                "中央値: 検出まで " + Seconds(ResponseTestRunner.Median(test.Results, r => r.DetectLatency)) +
                "  ・  停止判定 " + Seconds(ResponseTestRunner.Median(test.Results, r => r.StopDetectLatency)) +
                "  ・  回転数の誤差 " + (absoluteError.HasValue ? absoluteError.Value.ToString("0.0") + "%" : "—"),
                _bodyStyle);
            GUI.Label(new Rect(card.x + 32f, y + 40f, card.width - 64f, 44f),
                "時間は合図からの秒数で、合図に反応するまでの時間（約0.3〜0.5秒）を含みます。回転数は、漕いでいた最後の6秒間の平均です。",
                _smallStyle);
            GUI.Label(new Rect(card.x + 32f, card.yMax - 118f, card.width - 64f, 44f), test.SaveMessage, _smallStyle);

            if (UiButton(new Rect(card.x + 32f, card.yMax - 66f, 200f, 46f), "もう一度テスト", _buttonStyle))
            {
                test.Dismiss();
                _app.TryStartResponseTest();
            }

            if (!string.IsNullOrEmpty(test.SavedDirectory) &&
                UiButton(new Rect(card.x + 244f, card.yMax - 66f, 200f, 46f), "保存フォルダを開く", _buttonStyle))
            {
                Application.OpenURL(new System.Uri(test.SavedDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
            }

            if (UiButton(new Rect(card.xMax - 152f, card.yMax - 66f, 120f, 46f), "閉じる", _primaryButtonStyle))
            {
                test.Dismiss();
            }
        }

        private static string Seconds(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.0") + " 秒" : "—";
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

#if UNITY_ANDROID && !UNITY_EDITOR
            // Quest does not have the Windows fonts below. Keep the font in Resources so
            // Unity includes its Japanese glyphs in the Android player.
            _font = Resources.Load<Font>("Fonts/NotoSansCJKjp-Regular");
            if (_font == null)
            {
                Debug.LogError("Quest HUD font is missing: Fonts/NotoSansCJKjp-Regular");
            }
#else
            string[] preferredFonts = { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "Arial" };
            _font = Font.CreateDynamicFontFromOSFont(preferredFonts, 22);
#endif

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
            _cameraNameStyle = MakeLabelStyle(13, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.91f, 0.96f, 0.95f));
            _cameraNameStyle.wordWrap = true;
            _cameraNameStyle.clipping = TextClipping.Clip;
            _cueStyle = MakeLabelStyle(32, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);

            _buttonStyle = MakeButtonStyle(_roundedTexture, new Color(0.96f, 1f, 0.99f));
            _primaryButtonStyle = MakeButtonStyle(_primaryTexture, Color.white);
            _dangerButtonStyle = MakeButtonStyle(_dangerTexture, Color.white);
            _compactButtonStyle = MakeCompactButtonStyle(_buttonStyle);
            _compactPrimaryButtonStyle = MakeCompactButtonStyle(_primaryButtonStyle);
            _compactDangerButtonStyle = MakeCompactButtonStyle(_dangerButtonStyle);
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

        private static GUIStyle MakeCompactButtonStyle(GUIStyle source)
        {
            return new GUIStyle(source)
            {
                fontSize = 14,
                padding = new RectOffset(4, 4, 5, 5)
            };
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
