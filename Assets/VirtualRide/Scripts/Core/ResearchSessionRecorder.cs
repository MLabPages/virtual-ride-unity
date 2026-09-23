using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VirtualRide.Input;
using VirtualRide.World;

namespace VirtualRide.Core
{
    /// <summary>
    /// Writes numerical ride samples for research use. Camera frames are never accessed or saved here.
    /// </summary>
    public sealed class ResearchSessionRecorder
    {
        public const string SchemaVersion = "2";
        public const string MarkerInstruction = "instruction";
        public const string MarkerRest = "rest";
        public const string MarkerNote = "note";
        public const string StopReasonCompleted = "completed";
        public const string StopReasonTrialDurationElapsed = "trial_duration_elapsed";
        public const string StopReasonApplicationClosed = "application_closed";

        private const float SampleIntervalSeconds = 0.1f;
        private const float MaximumTrialDurationSeconds = 6f * 60f * 60f;
        private const int MaximumNoteLength = 200;

        private readonly List<ResearchEventMarker> _events = new List<ResearchEventMarker>();
        private readonly List<string> _pendingSampleMarkers = new List<string>();

        private StreamWriter _writer;
        private StreamWriter _eventsWriter;
        private DateTimeOffset _startedAt;
        private float _recordingElapsed;
        private float _nextSampleAt;
        private float _lastSampleAt = -1f;
        private int _sampleCount;
        private string _dataDirectory;
        private float _trialDurationSeconds;
        private string _startingInputMode = string.Empty;
        private string _applicationVersion = string.Empty;
        private string _unityVersion = string.Empty;
        private string _productName = string.Empty;
        private string _companyName = string.Empty;
        private string _platform = string.Empty;
        private bool _cameraBothLegsVisible = true;
        private string _cameraSensitivity = string.Empty;
        private float _cameraMetersPerRevolution;
        private string _cameraDeviceName = string.Empty;
        private string _cameraRegion = string.Empty;
        private string _videoSpeedMode = string.Empty;
        private float _fixedVideoSpeedKph;
        private bool _pedalPreviewVisible = true;
        private bool _comfortMode;
        private bool _minimalHud;
        private bool _windEnabled;

        public ResearchSessionRecorder(string dataDirectory = null)
        {
            _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? ResolveDefaultDataDirectory()
                : Path.GetFullPath(dataDirectory);
            LastMessage = "実験記録は停止中です";
        }

        public bool IsRecording => _writer != null;
        public string ParticipantId { get; private set; } = string.Empty;
        public string Condition { get; private set; } = string.Empty;
        public string SessionId { get; private set; } = string.Empty;
        public string CsvPath { get; private set; } = string.Empty;
        public string EventsPath { get; private set; } = string.Empty;
        public string SummaryPath { get; private set; } = string.Empty;
        public string DataDirectory => _dataDirectory;
        public string LastMessage { get; private set; }
        public bool HasError { get; private set; }
        public string StopReason { get; private set; } = string.Empty;
        public string StartingInputMode => _startingInputMode;
        public float RecordingElapsedSeconds => _recordingElapsed;
        public float TrialDurationSeconds => _trialDurationSeconds;
        public bool HasTrialDuration => _trialDurationSeconds > 0f;
        public float RemainingTrialSeconds => HasTrialDuration
            ? Mathf.Max(0f, _trialDurationSeconds - _recordingElapsed)
            : 0f;
        public int SampleCount => _sampleCount;
        public int EventCount => _events.Count;
        public IReadOnlyList<ResearchEventMarker> Events => _events;

        public void SetDataDirectoryForTesting(string dataDirectory)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("記録中は保存先を変更できません。");
            }

            _dataDirectory = Path.GetFullPath(dataDirectory);
        }

        public bool Start(string participantId, string condition, VirtualRideApp app, float trialDurationSeconds = 0f)
        {
            if (IsRecording)
            {
                LastMessage = "すでに実験記録中です";
                return false;
            }

            if (app == null)
            {
                LastMessage = "走行アプリを確認できません";
                return false;
            }

            if (float.IsNaN(trialDurationSeconds) || float.IsInfinity(trialDurationSeconds)
                || trialDurationSeconds < 0f || trialDurationSeconds > MaximumTrialDurationSeconds)
            {
                LastMessage = "試行時間は0（手動終了）から6時間までです";
                return false;
            }

            string safeId = SanitizeIdentifier(participantId);
            string safeCondition = SanitizeIdentifier(condition);
            if (string.IsNullOrEmpty(safeId))
            {
                LastMessage = "匿名の参加者IDを入力してください";
                return false;
            }

            if (string.IsNullOrEmpty(safeCondition))
            {
                LastMessage = "実験条件を入力してください";
                return false;
            }

            if (safeId != participantId.Trim() || safeCondition != condition.Trim())
            {
                LastMessage = "IDと条件には40文字以内の文字・数字・ハイフン・下線を使ってください";
                return false;
            }

            try
            {
                if (!EnsureDataDirectory())
                {
                    LastMessage = "記録フォルダを作成できません";
                    return false;
                }

                CaptureStartMetadata(app);
                ParticipantId = safeId;
                Condition = safeCondition;
                _trialDurationSeconds = trialDurationSeconds;
                _startedAt = DateTimeOffset.Now;
                SessionId = _startedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                            "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string fileStem = ParticipantId + "_" + Condition + "_" + SessionId;
                CsvPath = Path.Combine(_dataDirectory, fileStem + ".csv");
                EventsPath = Path.Combine(_dataDirectory, fileStem + "_events.csv");
                SummaryPath = Path.Combine(_dataDirectory, fileStem + "_summary.json");
                _events.Clear();
                _pendingSampleMarkers.Clear();
                StopReason = string.Empty;

                _writer = new StreamWriter(CsvPath, false, new UTF8Encoding(false));
                _writer.WriteLine(
                    "schema_version,session_id,participant_id,condition,recorded_at_utc,elapsed_seconds," +
                    "input_mode,input_state,input_status,input_speed_kph,display_speed_kph,cadence_rpm," +
                    "confidence,distance_metres,moving_seconds,route_distance_metres,route_progress," +
                    "area_name,is_paused,event_marker");
                _eventsWriter = new StreamWriter(EventsPath, false, new UTF8Encoding(false));
                _eventsWriter.WriteLine(
                    "schema_version,session_id,participant_id,condition,recorded_at_utc,elapsed_seconds," +
                    "marker_type,note");
                _eventsWriter.Flush();
                _recordingElapsed = 0f;
                _nextSampleAt = 0f;
                _sampleCount = 0;
                _lastSampleAt = -1f;
                // The first sample represents the common starting line without mutating the ride.
                // Only a fully opened/flushed recording lets the app reset its live state.
                WriteSample(app, true);
                _writer.Flush();
                _nextSampleAt = SampleIntervalSeconds;
                HasError = false;
                LastMessage = HasTrialDuration
                    ? "実験記録中: " + ParticipantId + " / " + Condition + "  （" +
                      Mathf.RoundToInt(_trialDurationSeconds) + "秒で自動保存）"
                    : "実験記録中: " + ParticipantId + " / " + Condition;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail("記録ファイルを作成できません", exception);
                return false;
            }
        }

        public void Tick(float unscaledDeltaTime, VirtualRideApp app)
        {
            if (!IsRecording || app == null || unscaledDeltaTime <= 0f)
            {
                return;
            }

            _recordingElapsed += unscaledDeltaTime;
            if (_recordingElapsed + 0.0001f >= _nextSampleAt)
            {
                try
                {
                    WriteSample(app);
                    _nextSampleAt += SampleIntervalSeconds;
                    if (_nextSampleAt < _recordingElapsed - SampleIntervalSeconds)
                    {
                        _nextSampleAt = _recordingElapsed + SampleIntervalSeconds;
                    }

                    if (_sampleCount % 10 == 0)
                    {
                        _writer.Flush();
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Fail("記録を継続できません", exception);
                    return;
                }
            }

            if (HasTrialDuration && _recordingElapsed + 0.0001f >= _trialDurationSeconds)
            {
                Stop(app, StopReasonTrialDurationElapsed);
            }
        }

        public bool Stop(VirtualRideApp app, string reason = StopReasonCompleted)
        {
            if (!IsRecording)
            {
                return false;
            }

            try
            {
                if (app != null && (_recordingElapsed > _lastSampleAt + .0001f || _pendingSampleMarkers.Count > 0))
                {
                    WriteSample(app);
                }

                _writer.Flush();
                _eventsWriter?.Flush();
                CloseWriter();
                StopReason = string.IsNullOrEmpty(reason) ? StopReasonCompleted : reason;
                WriteSummary(app, StopReason);
                LastMessage = StopReason == StopReasonTrialDurationElapsed
                    ? "制限時間に達したため記録を保存しました: " + Path.GetFileName(CsvPath)
                    : "記録を保存しました: " + Path.GetFileName(CsvPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail("記録の終了処理に失敗しました", exception);
                return false;
            }
        }

        public bool AddEventMarker(string markerType, string note = "")
        {
            if (!IsRecording)
            {
                LastMessage = "記録中のみイベントを追加できます";
                return false;
            }

            string type = SanitizeIdentifier(markerType);
            if (string.IsNullOrEmpty(type))
            {
                LastMessage = "イベントの種類を指定してください";
                return false;
            }

            string safeNote = SanitizeNote(note);
            string recordedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var marker = new ResearchEventMarker(
                _recordingElapsed,
                recordedAtUtc,
                type,
                safeNote);

            try
            {
                _eventsWriter.WriteLine(string.Join(",", new[]
                {
                    SchemaVersion,
                    Csv(SessionId),
                    Csv(ParticipantId),
                    Csv(Condition),
                    recordedAtUtc,
                    Number(_recordingElapsed),
                    Csv(type),
                    Csv(safeNote)
                }));
                _eventsWriter.Flush();
                _events.Add(marker);
                _pendingSampleMarkers.Add(type);
                LastMessage = string.IsNullOrEmpty(safeNote)
                    ? "イベントを記録: " + type + "  (" + Number(_recordingElapsed) + "秒)"
                    : "イベントを記録: " + type + " / " + safeNote;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Fail("イベントを保存できません", exception);
                return false;
            }
        }

        private void CaptureStartMetadata(VirtualRideApp app)
        {
            _startingInputMode = app.InputModeName ?? string.Empty;
            _applicationVersion = string.IsNullOrWhiteSpace(Application.version)
                ? "unspecified"
                : Application.version;
            _unityVersion = string.IsNullOrWhiteSpace(Application.unityVersion)
                ? "unspecified"
                : Application.unityVersion;
            _productName = Application.productName ?? string.Empty;
            _companyName = Application.companyName ?? string.Empty;
            _platform = Application.platform.ToString();
            _comfortMode = app.ComfortMode;
            _minimalHud = app.MinimalHud;
            _windEnabled = app.WindEnabled;
            _videoSpeedMode = app.IsVideoSpeedFixed ? "fixed" : "pedal_linked";
            _fixedVideoSpeedKph = app.FixedVideoSpeedKph;
            _pedalPreviewVisible = app.PedalPreviewVisible;

            CameraCadenceInput camera = app.CameraInput;
            if (camera != null)
            {
                _cameraBothLegsVisible = camera.BothLegsVisible;
                _cameraSensitivity = camera.Sensitivity.ToString();
                _cameraMetersPerRevolution = camera.MetersPerRevolution;
                _cameraDeviceName = camera.SelectedDeviceName ?? string.Empty;
                _cameraRegion = camera.Region.ToString();
            }
            else
            {
                _cameraBothLegsVisible = true;
                _cameraSensitivity = string.Empty;
                _cameraMetersPerRevolution = 0f;
                _cameraDeviceName = string.Empty;
                _cameraRegion = string.Empty;
            }
        }

        private void WriteSample(VirtualRideApp app, bool initial = false)
        {
            RideInputSample sample = app.ActiveSample;
            string recordedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            string eventMarker = _pendingSampleMarkers.Count > 0
                ? string.Join("|", _pendingSampleMarkers.ToArray())
                : string.Empty;
            _pendingSampleMarkers.Clear();
            _writer.WriteLine(string.Join(",", new[]
            {
                SchemaVersion,
                Csv(SessionId),
                Csv(ParticipantId),
                Csv(Condition),
                recordedAtUtc,
                Number(_recordingElapsed),
                Csv(app.InputModeName),
                Csv(sample.State.ToString()),
                Csv(sample.Status),
                Number(sample.SpeedKph),
                Number(initial ? 0f : app.DisplaySpeedKph),
                sample.HasCadence ? Number(sample.CadenceRpm) : string.Empty,
                Number(sample.Confidence),
                Number(initial ? 0f : app.Session.DistanceMetres),
                Number(initial ? 0f : app.Session.MovingSeconds),
                Number(initial ? 0f : app.RouteDistance),
                Number(initial ? 0f : app.RouteProgress),
                Csv(initial ? app.Route.GetAreaName(0f) : app.AreaName),
                !initial && app.IsPaused ? "true" : "false",
                Csv(eventMarker)
            }));
            _sampleCount++;
            _lastSampleAt = _recordingElapsed;
        }

        private void WriteSummary(VirtualRideApp app, string reason)
        {
            float distance = app != null ? app.Session.DistanceMetres : 0f;
            float movingSeconds = app != null ? app.Session.MovingSeconds : 0f;
            float averageSpeed = app != null ? app.Session.AverageSpeedKph : 0f;
            float maximumSpeed = app != null ? app.Session.MaximumSpeedKph : 0f;
            string finalInputMode = app != null ? app.InputModeName : string.Empty;
            string json =
                "{\n" +
                "  \"schemaVersion\": \"" + SchemaVersion + "\",\n" +
                "  \"sessionId\": \"" + Json(SessionId) + "\",\n" +
                "  \"participantId\": \"" + Json(ParticipantId) + "\",\n" +
                "  \"condition\": \"" + Json(Condition) + "\",\n" +
                "  \"startedAt\": \"" + _startedAt.ToString("O", CultureInfo.InvariantCulture) + "\",\n" +
                "  \"endedAt\": \"" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + "\",\n" +
                "  \"stopReason\": \"" + Json(reason) + "\",\n" +
                "  \"sampleIntervalSeconds\": " + Number(SampleIntervalSeconds) + ",\n" +
                "  \"sampleCount\": " + _sampleCount.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"elapsedSeconds\": " + Number(_recordingElapsed) + ",\n" +
                "  \"trialDurationSeconds\": " + TrialDurationJson() + ",\n" +
                "  \"movingSeconds\": " + Number(movingSeconds) + ",\n" +
                "  \"distanceMetres\": " + Number(distance) + ",\n" +
                "  \"averageSpeedKph\": " + Number(averageSpeed) + ",\n" +
                "  \"maximumSpeedKph\": " + Number(maximumSpeed) + ",\n" +
                "  \"startingInputMode\": \"" + Json(_startingInputMode) + "\",\n" +
                "  \"finalInputMode\": \"" + Json(finalInputMode) + "\",\n" +
                "  \"inputLocked\": true,\n" +
                "  \"applicationVersion\": \"" + Json(_applicationVersion) + "\",\n" +
                "  \"unityVersion\": \"" + Json(_unityVersion) + "\",\n" +
                "  \"productName\": \"" + Json(_productName) + "\",\n" +
                "  \"companyName\": \"" + Json(_companyName) + "\",\n" +
                "  \"platform\": \"" + Json(_platform) + "\",\n" +
                "  \"visualRevision\": \"" + ScenicWorldBuilder.VisualRevision + "\",\n" +
                "  \"comfortMode\": " + (_comfortMode ? "true" : "false") + ",\n" +
                "  \"minimalHud\": " + (_minimalHud ? "true" : "false") + ",\n" +
                "  \"windEnabled\": " + (_windEnabled ? "true" : "false") + ",\n" +
                "  \"videoSpeedMode\": \"" + Json(_videoSpeedMode) + "\",\n" +
                "  \"fixedVideoSpeedKph\": " + (_videoSpeedMode == "fixed" ? Number(_fixedVideoSpeedKph) : "null") + ",\n" +
                "  \"pedalPreviewVisible\": " + (_pedalPreviewVisible ? "true" : "false") + ",\n" +
                "  \"routeStartMetres\": 0,\n" +
                "  \"elapsedIncludesPauses\": true,\n" +
                "  \"camera\": {\n" +
                "    \"bothLegsVisible\": " + (_cameraBothLegsVisible ? "true" : "false") + ",\n" +
                "    \"sensitivity\": \"" + Json(_cameraSensitivity) + "\",\n" +
                "    \"metersPerRevolution\": " + Number(_cameraMetersPerRevolution) + ",\n" +
                "    \"deviceName\": \"" + Json(_cameraDeviceName) + "\",\n" +
                "    \"measurementRegion\": \"" + Json(_cameraRegion) + "\"\n" +
                "  },\n" +
                "  \"measurementValidated\": false,\n" +
                "  \"cameraFramesSaved\": false,\n" +
                "  \"csvFile\": \"" + Json(Path.GetFileName(CsvPath)) + "\",\n" +
                "  \"eventsFile\": \"" + Json(Path.GetFileName(EventsPath)) + "\",\n" +
                "  \"eventCount\": " + _events.Count.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"events\": " + WriteEventsJson() + "\n" +
                "}\n";
            File.WriteAllText(SummaryPath, json, new UTF8Encoding(false));
        }

        private string TrialDurationJson()
        {
            return HasTrialDuration ? Number(_trialDurationSeconds) : "null";
        }

        private string WriteEventsJson()
        {
            if (_events.Count == 0)
            {
                return "[]";
            }

            var builder = new StringBuilder();
            builder.Append("[\n");
            for (int i = 0; i < _events.Count; i++)
            {
                ResearchEventMarker marker = _events[i];
                builder.Append("    {\n");
                builder.Append("      \"elapsedSeconds\": " + Number(marker.ElapsedSeconds) + ",\n");
                builder.Append("      \"recordedAtUtc\": \"" + Json(marker.RecordedAtUtc) + "\",\n");
                builder.Append("      \"markerType\": \"" + Json(marker.MarkerType) + "\",\n");
                builder.Append("      \"note\": \"" + Json(marker.Note) + "\"\n");
                builder.Append(i == _events.Count - 1 ? "    }\n" : "    },\n");
            }

            builder.Append("  ]");
            return builder.ToString();
        }

        private void CloseWriter()
        {
            StreamWriter samples = _writer;
            StreamWriter events = _eventsWriter;
            _writer = null;
            _eventsWriter = null;
            try { samples?.Dispose(); }
            finally { events?.Dispose(); }
        }

        private void Fail(string message, Exception exception)
        {
            try { CloseWriter(); }
            catch (Exception closeError) when (closeError is IOException || closeError is UnauthorizedAccessException)
            { Debug.LogWarning("Research stream close failed: " + closeError.Message); }
            HasError = true;
            StopReason = "write_failed";
            LastMessage = message + ": " + exception.Message + "\n保存先を確認してください。途中のファイルは残しています。";
        }

        private bool EnsureDataDirectory()
        {
            if (TryCreateDirectory(_dataDirectory))
            {
                return true;
            }

            string fallback = Path.Combine(Application.persistentDataPath, "VirtualRideResearchData");
            if (!string.Equals(Path.GetFullPath(_dataDirectory), Path.GetFullPath(fallback), StringComparison.OrdinalIgnoreCase)
                && TryCreateDirectory(fallback))
            {
                _dataDirectory = fallback;
                return true;
            }

            return false;
        }

        private static bool TryCreateDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                Directory.CreateDirectory(path);
                return Directory.Exists(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static string ResolveDefaultDataDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                return Path.Combine(documents, "VirtualRideResearchData");
            }

            return Path.Combine(Application.persistentDataPath, "VirtualRideResearchData");
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (char character in value.Trim())
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                {
                    builder.Append(character);
                }

                if (builder.Length >= 40)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        private static string SanitizeNote(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string flattened = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
            return flattened.Length <= MaximumNoteLength
                ? flattened
                : flattened.Substring(0, MaximumNoteLength);
        }

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "null";
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            // Quoting alone does not prevent spreadsheets interpreting notes as formulas.
            string trimmed = safe.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) safe = "'" + safe;
            return "\"" + safe.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static string Json(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? string.Empty)
            {
                if (c == '\\') result.Append("\\\\");
                else if (c == '"') result.Append("\\\"");
                else if (c < 32) result.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.ToString();
        }
    }

    public sealed class ResearchEventMarker
    {
        public ResearchEventMarker(float elapsedSeconds, string recordedAtUtc, string markerType, string note)
        {
            ElapsedSeconds = elapsedSeconds;
            RecordedAtUtc = recordedAtUtc ?? string.Empty;
            MarkerType = markerType ?? string.Empty;
            Note = note ?? string.Empty;
        }

        public float ElapsedSeconds { get; }
        public string RecordedAtUtc { get; }
        public string MarkerType { get; }
        public string Note { get; }
    }
}
