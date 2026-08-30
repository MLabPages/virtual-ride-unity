using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VirtualRide.Input;

namespace VirtualRide.Core
{
    /// <summary>
    /// Writes numerical ride samples for research use. Camera frames are never accessed or saved here.
    /// </summary>
    public sealed class ResearchSessionRecorder
    {
        private const float SampleIntervalSeconds = 0.1f;
        private const string SchemaVersion = "1";

        private StreamWriter _writer;
        private DateTimeOffset _startedAt;
        private float _recordingElapsed;
        private float _nextSampleAt;
        private int _sampleCount;
        private string _dataDirectory;

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
        public string SummaryPath { get; private set; } = string.Empty;
        public string DataDirectory => _dataDirectory;
        public string LastMessage { get; private set; }
        public float RecordingElapsedSeconds => _recordingElapsed;
        public int SampleCount => _sampleCount;

        public void SetDataDirectoryForTesting(string dataDirectory)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("記録中は保存先を変更できません。");
            }

            _dataDirectory = Path.GetFullPath(dataDirectory);
        }

        public bool Start(string participantId, string condition, VirtualRideApp app)
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

            ParticipantId = SanitizeIdentifier(participantId);
            Condition = SanitizeIdentifier(condition);
            if (string.IsNullOrEmpty(ParticipantId))
            {
                LastMessage = "匿名の参加者IDを入力してください";
                return false;
            }

            if (string.IsNullOrEmpty(Condition))
            {
                LastMessage = "実験条件を入力してください";
                return false;
            }

            try
            {
                Directory.CreateDirectory(_dataDirectory);
                _startedAt = DateTimeOffset.Now;
                SessionId = _startedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                            "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string fileStem = ParticipantId + "_" + Condition + "_" + SessionId;
                CsvPath = Path.Combine(_dataDirectory, fileStem + ".csv");
                SummaryPath = Path.Combine(_dataDirectory, fileStem + "_summary.json");
                _writer = new StreamWriter(CsvPath, false, new UTF8Encoding(false));
                _writer.WriteLine(
                    "schema_version,session_id,participant_id,condition,recorded_at_utc,elapsed_seconds," +
                    "input_mode,input_state,input_status,input_speed_kph,display_speed_kph,cadence_rpm," +
                    "confidence,distance_metres,moving_seconds,route_distance_metres,route_progress," +
                    "area_name,is_paused");
                _recordingElapsed = 0f;
                _nextSampleAt = 0f;
                _sampleCount = 0;
                WriteSample(app);
                _nextSampleAt = SampleIntervalSeconds;
                LastMessage = "実験記録中: " + ParticipantId + " / " + Condition;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                CloseWriter();
                LastMessage = "記録ファイルを作成できません: " + exception.Message;
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
            if (_recordingElapsed + 0.0001f < _nextSampleAt)
            {
                return;
            }

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
                CloseWriter();
                LastMessage = "記録を継続できません: " + exception.Message;
            }
        }

        public bool Stop(VirtualRideApp app, string reason = "completed")
        {
            if (!IsRecording)
            {
                return false;
            }

            try
            {
                if (app != null)
                {
                    WriteSample(app);
                }

                _writer.Flush();
                CloseWriter();
                WriteSummary(app, reason);
                LastMessage = "記録を保存しました: " + Path.GetFileName(CsvPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                CloseWriter();
                LastMessage = "記録の終了処理に失敗しました: " + exception.Message;
                return false;
            }
        }

        private void WriteSample(VirtualRideApp app)
        {
            RideInputSample sample = app.ActiveSample;
            string recordedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
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
                Number(app.DisplaySpeedKph),
                sample.HasCadence ? Number(sample.CadenceRpm) : string.Empty,
                Number(sample.Confidence),
                Number(app.Session.DistanceMetres),
                Number(app.Session.MovingSeconds),
                Number(app.RouteDistance),
                Number(app.RouteProgress),
                Csv(app.AreaName),
                app.IsPaused ? "true" : "false"
            }));
            _sampleCount++;
        }

        private void WriteSummary(VirtualRideApp app, string reason)
        {
            float distance = app != null ? app.Session.DistanceMetres : 0f;
            float movingSeconds = app != null ? app.Session.MovingSeconds : 0f;
            float averageSpeed = app != null ? app.Session.AverageSpeedKph : 0f;
            float maximumSpeed = app != null ? app.Session.MaximumSpeedKph : 0f;
            string inputMode = app != null ? app.InputModeName : string.Empty;
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
                "  \"movingSeconds\": " + Number(movingSeconds) + ",\n" +
                "  \"distanceMetres\": " + Number(distance) + ",\n" +
                "  \"averageSpeedKph\": " + Number(averageSpeed) + ",\n" +
                "  \"maximumSpeedKph\": " + Number(maximumSpeed) + ",\n" +
                "  \"finalInputMode\": \"" + Json(inputMode) + "\",\n" +
                "  \"csvFile\": \"" + Json(Path.GetFileName(CsvPath)) + "\",\n" +
                "  \"cameraFramesSaved\": false\n" +
                "}\n";
            File.WriteAllText(SummaryPath, json, new UTF8Encoding(false));
        }

        private void CloseWriter()
        {
            _writer?.Dispose();
            _writer = null;
        }

        private static string ResolveDefaultDataDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(Application.persistentDataPath, "ResearchData")
                : Path.Combine(documents, "VirtualRideResearchData");
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

        private static string Number(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            return "\"" + safe.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static string Json(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
