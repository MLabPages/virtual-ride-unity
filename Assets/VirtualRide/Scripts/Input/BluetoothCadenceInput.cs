using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VirtualRide.Input
{
    public struct BluetoothCadenceDevice
    {
        public BluetoothCadenceDevice(string address, string name, int signalStrength)
        {
            Address = address ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? "Bluetooth cadence sensor" : name;
            SignalStrength = signalStrength;
        }

        public string Address { get; }
        public string Name { get; }
        public int SignalStrength { get; }
    }

    /// <summary>
    /// Windows BLE Cycling Speed and Cadence (CSC) input.
    /// BLE access is isolated in the small Windows Runtime bridge process.
    /// </summary>
    public sealed class BluetoothCadenceInput : MonoBehaviour, IBluetoothCadenceInput
    {
        private const string BridgeRelativePath = "Bluetooth/VirtualRideBleBridge.exe";
        private const float CadenceTimeoutSeconds = 2.5f;
        private readonly ConcurrentQueue<string> _messages = new ConcurrentQueue<string>();
        private readonly Dictionary<string, BluetoothCadenceDevice> _devices =
            new Dictionary<string, BluetoothCadenceDevice>(StringComparer.OrdinalIgnoreCase);

        private Process _bridge;
        private StreamWriter _bridgeInput;
        private bool _isActive;
        private bool _isScanning;
        private bool _isConnected;
        private bool _hasCadence;
        private string _selectedAddress = string.Empty;
        private string _deviceName = string.Empty;
        private string _status = "Bluetoothセンサーは未接続です";
        private string _error = string.Empty;
        private float _cadenceRpm;
        private float _lastCadenceAt = -1f;

        public string DisplayName => "Bluetoothセンサー";
        public bool IsActive => _isActive;
        public bool IsUnvalidatedMeasurement => true;
        public bool IsConnected => _isConnected;
        public bool IsScanning => _isScanning;
        public string Status => _status;
        public string Error => _error;
        public string ConnectedDeviceName => _deviceName;
        public string SelectedAddress => _selectedAddress;

        public List<BluetoothCadenceDevice> Devices
        {
            get
            {
                var result = new List<BluetoothCadenceDevice>(_devices.Values);
                result.Sort((left, right) => right.SignalStrength.CompareTo(left.SignalStrength));
                return result;
            }
        }

        public RideInputSample Current
        {
            get
            {
                if (!string.IsNullOrEmpty(_error))
                {
                    return new RideInputSample(0f, -1f, 0f, RideInputState.Error, _error);
                }

                if (_isConnected)
                {
                    bool fresh = _hasCadence && Time.unscaledTime - _lastCadenceAt <= CadenceTimeoutSeconds;
                    float rpm = fresh ? _cadenceRpm : 0f;
                    float speed = Mathf.Clamp(rpm * 4.2f * 60f / 1000f, 0f, 45f);
                    string status = fresh
                        ? _deviceName + " ・ " + Mathf.RoundToInt(rpm) + " rpm"
                        : _deviceName + " 接続中 ・ ペダリング待ち";
                    return new RideInputSample(speed, fresh ? rpm : 0f, 1f, RideInputState.Detected, status);
                }

                if (_isScanning)
                {
                    return new RideInputSample(0f, -1f, 0f, RideInputState.Searching, _status);
                }

                return new RideInputSample(0f, -1f, 0f, RideInputState.Offline, _status);
            }
        }

        public void Activate()
        {
            _isActive = true;
        }

        public void Deactivate()
        {
            _isActive = false;
        }

        public void Tick(float unscaledDeltaTime)
        {
            string line;
            while (_messages.TryDequeue(out line))
            {
                HandleBridgeMessage(line);
            }

            if (_bridge != null && _bridge.HasExited && string.IsNullOrEmpty(_error))
            {
                _isConnected = false;
                _isScanning = false;
                _error = "Bluetooth接続ブリッジが終了しました。アプリを再起動してください。";
            }
        }

        public bool BeginScan()
        {
            _error = string.Empty;
            _devices.Clear();
            if (!EnsureBridge()) return false;
            _isScanning = true;
            _status = "近くのBluetoothセンサーを検索しています…";
            return SendCommand("SCAN");
        }

        public bool Connect(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            _error = string.Empty;
            if (!EnsureBridge()) return false;
            _selectedAddress = address;
            _isScanning = false;
            _isConnected = false;
            _hasCadence = false;
            _lastCadenceAt = -1f;
            _status = "センサーに接続しています…";
            return SendCommand("CONNECT\t" + address);
        }

        public void Disconnect()
        {
            if (_bridge != null && !_bridge.HasExited)
            {
                SendCommand("DISCONNECT");
            }
            _isConnected = false;
            _hasCadence = false;
            _selectedAddress = string.Empty;
            _deviceName = string.Empty;
            _status = "Bluetoothセンサーは未接続です";
            _error = string.Empty;
        }

        public void Shutdown()
        {
            if (_bridge == null) return;
            try
            {
                if (!_bridge.HasExited)
                {
                    SendCommand("QUIT");
                    if (!_bridge.WaitForExit(600)) _bridge.Kill();
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Bluetooth bridge shutdown: " + exception.Message);
            }
            finally
            {
                _bridge.Dispose();
                _bridge = null;
                _bridgeInput = null;
            }
        }

        private bool EnsureBridge()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_bridge != null && !_bridge.HasExited) return true;
            if (_bridge != null)
            {
                _bridge.Dispose();
                _bridge = null;
                _bridgeInput = null;
            }
            string path = Path.Combine(Application.streamingAssetsPath, BridgeRelativePath);
            if (!File.Exists(path))
            {
                _error = "Bluetooth接続ブリッジがありません。Windows版をビルドし直してください。";
                _status = _error;
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                _bridge = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _bridge.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null) _messages.Enqueue(args.Data);
                };
                _bridge.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data)) _messages.Enqueue("ERROR\t" + args.Data);
                };
                _bridge.Start();
                _bridgeInput = _bridge.StandardInput;
                _bridge.BeginOutputReadLine();
                _bridge.BeginErrorReadLine();
                return true;
            }
            catch (Exception exception)
            {
                _error = "Bluetoothブリッジを起動できません: " + exception.Message;
                _status = _error;
                return false;
            }
#else
            _error = "Bluetoothセンサー入力はWindows版で利用できます。";
            _status = _error;
            return false;
#endif
        }

        private bool SendCommand(string command)
        {
            try
            {
                _bridgeInput.WriteLine(command);
                _bridgeInput.Flush();
                return true;
            }
            catch (Exception exception)
            {
                _error = "Bluetoothブリッジへ指示を送れません: " + exception.Message;
                _status = _error;
                return false;
            }
        }

        private void HandleBridgeMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string[] parts = line.Split('\t');
            switch (parts[0])
            {
                case "DEVICE":
                    if (parts.Length >= 4)
                    {
                        int signal;
                        if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out signal))
                        {
                            _devices[parts[1]] = new BluetoothCadenceDevice(parts[1], parts[3], signal);
                        }
                    }
                    break;
                case "STATE":
                    _status = "近くのBluetoothセンサーを検索しています…";
                    break;
                case "SCAN_DONE":
                    _isScanning = false;
                    _status = _devices.Count > 0
                        ? "センサーを選んで接続してください。"
                        : "見つかりません。WindowsのBluetoothをオンにし、センサーを動かして再検索してください。";
                    break;
                case "CONNECTING":
                    _isScanning = false;
                    _status = "センサーに接続しています…";
                    break;
                case "CONNECTED":
                    _isConnected = true;
                    _error = string.Empty;
                    _deviceName = NormalizeDeviceName(parts.Length > 1 ? parts[1] : string.Empty);
                    _status = _deviceName + " に接続しました。ペダルを回してください。";
                    break;
                case "CADENCE":
                    if (parts.Length >= 2)
                    {
                        float rpm;
                        if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rpm) &&
                            !float.IsNaN(rpm) && !float.IsInfinity(rpm))
                        {
                            _cadenceRpm = Mathf.Clamp(rpm, 0f, 220f);
                            _hasCadence = true;
                            _lastCadenceAt = Time.unscaledTime;
                        }
                    }
                    break;
                case "DISCONNECTED":
                    _isConnected = false;
                    _hasCadence = false;
                    _status = "Bluetoothセンサーを切断しました。";
                    break;
                case "ERROR":
                    _isConnected = false;
                    _isScanning = false;
                    _error = TranslateBridgeError(parts.Length > 1 ? parts[1] : string.Empty);
                    _status = _error;
                    break;
            }
        }

        private static string TranslateBridgeError(string message)
        {
            if (message.IndexOf("CSC service 1816", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("CSC Measurement 2A5B", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "この機器からケイデンス値を読めません。BK9Cを選んでいるか確認してください。";
            }
            if (message.IndexOf("BLE scan", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Bluetooth検索に失敗しました。WindowsのBluetooth設定とアダプターを確認してください。";
            }
            if (message.IndexOf("subscribe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("notifications", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "ケイデンス通知を開始できません。センサーを起動し、他アプリとの接続を解除して再試行してください。";
            }
            return string.IsNullOrWhiteSpace(message) ? "Bluetooth接続エラー" : message;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private static string NormalizeDeviceName(string name)
        {
            if (name.IndexOf("BK9C", StringComparison.OrdinalIgnoreCase) >= 0) return "COOSPO BK9C";
            if (name.IndexOf("CAD70", StringComparison.OrdinalIgnoreCase) >= 0) return "iGPSPORT CAD70";
            if (name.IndexOf("S314", StringComparison.OrdinalIgnoreCase) >= 0) return "Magene S314";
            return "Bluetooth cadence sensor";
        }
    }
}
