using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
using UnityEngine.Scripting;
#endif

namespace VirtualRide.Input
{
    /// <summary>
    /// Foreground Android/Quest CSC adapter. Call the public API on Unity's main thread.
    /// Add as a component and pass to VirtualRideApp.AttachExternalInput when desired.
    /// Devices/SelectedAddress contain temporary scan identifiers: never persist or log them.
    /// A successful BeginScan/Connect means the asynchronous request was accepted.
    /// Update also polls while this provider is not yet the active ride input.
    /// </summary>
    public sealed class AndroidBluetoothCadenceInput : MonoBehaviour, IBluetoothCadenceInput
    {
        private const double CadenceTimeoutSeconds = 2.5;
        private const double PollIntervalSeconds = 0.1;
        private readonly List<BluetoothCadenceDevice> _devices = new List<BluetoothCadenceDevice>();
        private bool _isActive;
        private bool _isScanning;
        private bool _isConnected;
        private bool _paused;
        private string _status = "Bluetoothセンサーは未接続です";
        private string _error = string.Empty;
        private string _deviceName = string.Empty;
        private string _selectedAddress = string.Empty;
        private float _rpm;
        private double _expiresAt;

        public string DisplayName => "Bluetoothセンサー";
        public bool IsActive => _isActive;
        public bool IsUnvalidatedMeasurement => true;
        public bool IsConnected => _isConnected;
        public bool IsScanning => _isScanning;
        public string Status => _status;
        public string Error => _error;
        public string ConnectedDeviceName => _deviceName;
        public string SelectedAddress => _selectedAddress;

        // A copy protects the provider's transient scan cache from UI modifications.
        public List<BluetoothCadenceDevice> Devices => new List<BluetoothCadenceDevice>(_devices);

        public RideInputSample Current
        {
            get
            {
                if (!string.IsNullOrEmpty(_error))
                    return new RideInputSample(0f, -1f, 0f, RideInputState.Error, _error);
                if (_isConnected)
                {
                    bool fresh = Time.realtimeSinceStartupAsDouble < _expiresAt;
                    float rpm = fresh ? _rpm : 0f;
                    float speed = Mathf.Clamp(rpm * 4.2f * 60f / 1000f, 0f, 45f);
                    // Only a normalized model name can reach ride/research status.
                    string status = fresh ? _deviceName + " ・ " + Mathf.RoundToInt(rpm) + " rpm"
                        : _deviceName + " 接続中 ・ ペダリング待ち";
                    return new RideInputSample(speed, rpm, fresh ? 1f : 0f, RideInputState.Detected, status);
                }
                return new RideInputSample(0f, -1f, 0f,
                    _isScanning ? RideInputState.Searching : RideInputState.Offline, _status);
            }
        }

        public void Activate() { _isActive = true; }

        public void Deactivate()
        {
            _isActive = false;
            Disconnect();
        }

        // Match Windows discovery, including sensors that omit CSC in advertisements.
        public bool BeginScan() { return BeginScan(true); }

        /// <summary>Pass false to list only advertisements explicitly declaring CSC 1816.</summary>
        public bool BeginScan(bool includeOtherDevices)
        {
            if (_paused || !isActiveAndEnabled) return false;
            Disconnect();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!EnsureClient()) return false;
                _includeOtherDevices = includeOtherDevices;
                if (HasPermissions()) return StartScan();
                _permissionPending = true;
                _permissionDenied = false;
                _status = "Bluetooth検索の権限を許可してください。";
                int generation = ++_permissionGeneration;
                _permissionCallbacks = new PermissionCallbacks();
                _permissionCallbacks.PermissionDenied += permission => PermissionWasDenied(generation);
#pragma warning disable CS0618 // Older Unity/device versions may use this separate denial callback.
                _permissionCallbacks.PermissionDeniedAndDontAskAgain += permission => PermissionWasDenied(generation);
#pragma warning restore CS0618
                Permission.RequestUserPermissions(RequiredPermissions(), _permissionCallbacks);
                return true;
            }
            catch (Exception)
            {
                SetError("Bluetooth権限を要求できません。端末のアプリ設定を確認してください。");
                return false;
            }
#else
            SetError("Android版でBluetooth入力を利用できます。Editorでは実機BLEに接続しません。");
            return false;
#endif
        }

        public bool Connect(string address)
        {
            if (_paused || !isActiveAndEnabled || string.IsNullOrWhiteSpace(address)) return false;
            // Validate against this scan's RAM-only cache before crossing JNI.
            if (!_devices.Exists(device => string.Equals(device.Address, address, StringComparison.OrdinalIgnoreCase)))
                return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            CancelPermissionRequest();
            if (!EnsureClient()) return false;
            try
            {
                bool accepted = _client.Call<bool>("connect", address);
                Poll();
                return accepted;
            }
            catch (Exception)
            {
                SetError("Bluetooth接続を開始できません。権限と端末のBluetooth設定を確認してください。");
            }
#endif
            return false;
        }

        public void Disconnect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CancelPermissionRequest();
            if (_client != null)
            {
                try { _client.Call("disconnect"); }
                catch (Exception) { CloseClient(); }
            }
#endif
            ClearState();
        }

        public void Shutdown()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CancelPermissionRequest();
            CloseClient();
#endif
            ClearState();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (_paused || !isActiveAndEnabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_permissionPending)
            {
                try
                {
                    if (_permissionDenied)
                    {
                        SetError("Bluetooth権限が許可されていません。端末のアプリ設定で許可して再検索してください。");
                        return;
                    }
                    if (HasPermissions())
                    {
                        CancelPermissionRequest();
                        StartScan();
                    }
                }
                catch (Exception)
                {
                    SetError("Bluetooth権限を確認できません。端末のアプリ設定を確認してください。");
                }
                return;
            }
            if (_client != null && Time.realtimeSinceStartupAsDouble >= _nextPollAt) Poll();
#endif
        }

        private void Update() { Tick(Time.unscaledDeltaTime); }
        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { Shutdown(); }
        private void OnApplicationQuit() { Shutdown(); }

        private void OnApplicationPause(bool paused)
        {
            _paused = paused;
            if (!paused) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            // The permission dialog can itself pause Unity. No radio work has begun yet.
            if (_permissionPending) return;
#endif
            Shutdown(); // Foreground only; resume requires an explicit new scan/connect.
        }

        private void ClearState()
        {
            _isConnected = _isScanning = false;
            _rpm = 0;
            _expiresAt = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            _nextPollAt = 0;
#endif
            _devices.Clear();
            _selectedAddress = _deviceName = _error = string.Empty;
            _status = "Bluetoothセンサーは未接続です";
        }

        private void SetError(string message)
        {
            Shutdown();
            _error = _status = message; // Fixed text only, never exception.Message or JNI JSON.
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string ClientClass = "com.virtualride.ble.BleCscClient";
        private AndroidJavaObject _client;
        private PermissionCallbacks _permissionCallbacks;
        private bool _permissionPending;
        private volatile bool _permissionDenied;
        private int _permissionGeneration;
        private int _sdk;
        private bool _includeOtherDevices;
        private double _nextPollAt;

        // Private, non-component transport DTOs only. These never go to Unity assets/PlayerPrefs.
        [Serializable, Preserve]
        private sealed class DeviceJson
        {
            public string address = string.Empty;
            public string name = string.Empty;
            public int rssi = 0;
        }

        [Serializable, Preserve]
        private sealed class Snapshot
        {
            public string state = string.Empty;
            public string error = string.Empty;
            public bool scanning = false;
            public bool connected = false;
            public string selectedAddress = string.Empty;
            public string deviceName = string.Empty;
            public float rpm = 0;
            public long ageMs = -1;
            public DeviceJson[] devices = Array.Empty<DeviceJson>();
        }

        private bool EnsureClient()
        {
            if (_client != null) return true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdk = version.GetStatic<int>("SDK_INT");
                if (_sdk < 23)
                {
                    SetError("Bluetooth入力にはAndroid 6以降が必要です。");
                    return false;
                }
                // Unity-owned activity reference; valid for both Activity and GameActivity entry points.
                _client = new AndroidJavaObject(ClientClass, AndroidApplication.currentActivity);
                return true;
            }
            catch (Exception)
            {
                SetError("Android BLEプラグインを初期化できません。Androidビルド設定を確認してください。");
                return false;
            }
        }

        private string[] RequiredPermissions()
        {
            return _sdk >= 31
                ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                : new[] { Permission.FineLocation };
        }

        private bool HasPermissions()
        {
            foreach (string permission in RequiredPermissions())
                if (!Permission.HasUserAuthorizedPermission(permission)) return false;
            return true;
        }

        private void PermissionWasDenied(int generation)
        {
            // Do not call JNI or Unity from a callback. Tick consumes the result.
            if (generation == _permissionGeneration) _permissionDenied = true;
        }

        private void CancelPermissionRequest()
        {
            ++_permissionGeneration;
            _permissionPending = _permissionDenied = false;
            _permissionCallbacks = null;
        }

        private bool StartScan()
        {
            try
            {
                bool accepted = _client.Call<bool>("beginScan", _includeOtherDevices);
                Poll();
                return accepted;
            }
            catch (Exception)
            {
                SetError("Bluetooth検索を開始できません。権限と端末のBluetooth設定を確認してください。");
                return false;
            }
        }

        private void Poll()
        {
            double observedAt = Time.realtimeSinceStartupAsDouble;
            _nextPollAt = observedAt + PollIntervalSeconds;
            try
            {
                Snapshot snapshot = JsonUtility.FromJson<Snapshot>(_client.Call<string>("snapshot"));
                if (snapshot == null || string.IsNullOrEmpty(snapshot.state))
                {
                    SetError("Bluetooth入力の状態を取得できません。");
                    return;
                }
                _isConnected = snapshot.connected;
                _isScanning = snapshot.scanning;
                _selectedAddress = snapshot.selectedAddress ?? string.Empty;
                _deviceName = snapshot.deviceName ?? string.Empty;
                _error = string.IsNullOrEmpty(snapshot.error) ? string.Empty : TranslateError(snapshot.error);
                _status = string.IsNullOrEmpty(_error) ? TranslateState(snapshot.state) : _error;
                bool validRpm = !float.IsNaN(snapshot.rpm) && !float.IsInfinity(snapshot.rpm)
                    && snapshot.rpm >= 0 && snapshot.rpm <= 220;
                _rpm = validRpm ? snapshot.rpm : 0;
                _expiresAt = snapshot.connected && validRpm && snapshot.ageMs >= 0 && snapshot.ageMs < 2500
                    ? observedAt + CadenceTimeoutSeconds - snapshot.ageMs / 1000.0 : 0;
                _devices.Clear();
                if (snapshot.devices != null)
                    foreach (DeviceJson device in snapshot.devices)
                        if (device != null && !string.IsNullOrEmpty(device.address))
                            _devices.Add(new BluetoothCadenceDevice(device.address, device.name, device.rssi));
                _devices.Sort((left, right) => right.SignalStrength.CompareTo(left.SignalStrength));
            }
            catch (Exception)
            {
                SetError("Bluetooth入力の状態を取得できません。再検索してください。");
            }
        }

        private void CloseClient()
        {
            AndroidJavaObject previous = _client;
            _client = null;
            if (previous == null) return;
            try { previous.Call("close"); } catch (Exception) { }
            finally
            {
                try { previous.Dispose(); } catch (Exception) { }
            }
        }

        private static string TranslateState(string state)
        {
            switch (state)
            {
                case "scanning": return "近くのBluetoothセンサーを検索しています…";
                case "select": return "センサーを選んで接続してください。";
                case "empty": return "見つかりません。センサーを動かして再検索してください。";
                case "connecting":
                case "discovering":
                case "subscribing": return "センサーに接続しています…";
                case "connected": return "センサーに接続しました。ペダルを回してください。";
                case "disconnected": return "Bluetoothセンサーを切断しました。再検索してください。";
                default: return "Bluetoothセンサーは未接続です";
            }
        }

        private static string TranslateError(string code)
        {
            switch (code)
            {
                case "permission": return "Bluetooth権限が必要です。端末のアプリ設定で許可して再検索してください。";
                case "unsupported": return "この端末ではBluetooth LEを利用できません。";
                case "bluetooth_off": return "端末のBluetoothをオンにして再検索してください。";
                case "location_off": return "Android 11以前では検索に位置情報設定をオンにする必要があります。";
                case "scan_failed": return "Bluetooth検索に失敗しました。少し待って再検索してください。";
                case "select_device": return "再検索して機器を選び直してください。";
                case "no_csc":
                case "no_measurement": return "この機器にCSCケイデンスサービスがありません。センサーのモードを確認してください。";
                case "subscribe_failed": return "ケイデンス通知を開始できません。他アプリとの接続を解除して再試行してください。";
                case "connect_timeout": return "センサー接続が時間切れになりました。再検索してください。";
                case "connection_lost": return "センサーとの接続が切れました。再検索してください。";
                default: return "Bluetooth接続エラーです。センサーと端末の設定を確認して再検索してください。";
            }
        }
#endif
    }
}
