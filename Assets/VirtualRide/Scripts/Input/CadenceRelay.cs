using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VirtualRide.Input
{
    /// <summary>
    /// Local-network relay of numeric ride input from the Windows app (external USB camera
    /// or BLE on the PC) to the Quest app. Only speed, cadence, confidence and a state code
    /// travel as a UDP broadcast on the local network. No video, IDs or addresses are sent.
    /// Packet: VRCAD1|sequence|speedKph|cadenceRpm|confidence|state
    /// </summary>
    public static class CadenceRelayProtocol
    {
        public const int Port = 47810;
        public const string Header = "VRCAD1";

        public static string Encode(int sequence, RideInputSample sample)
        {
            return string.Join("|", Header,
                sequence.ToString(CultureInfo.InvariantCulture),
                sample.SpeedKph.ToString("0.###", CultureInfo.InvariantCulture),
                sample.CadenceRpm.ToString("0.###", CultureInfo.InvariantCulture),
                sample.Confidence.ToString("0.###", CultureInfo.InvariantCulture),
                ((int)sample.State).ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryDecode(string packet, out int sequence, out RideInputSample sample)
        {
            sequence = 0;
            sample = default;
            string[] parts = (packet ?? string.Empty).Split('|');
            if (parts.Length != 6 || parts[0] != Header) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float rpm) ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float confidence) ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state) ||
                !float.IsFinite(speed) || !float.IsFinite(rpm) || !float.IsFinite(confidence) ||
                !Enum.IsDefined(typeof(RideInputState), state))
            {
                return false;
            }

            sample = new RideInputSample(Mathf.Clamp(speed, 0f, 45f), Mathf.Clamp(rpm, -1f, 220f),
                Mathf.Clamp01(confidence), (RideInputState)state, string.Empty);
            return true;
        }

        /// <summary>Accepts only a plain IPv4 address such as 172.20.10.3.</summary>
        public static bool TryParseIPv4(string text, out IPAddress address)
        {
            address = null;
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Split('.').Length != 4 || !IPAddress.TryParse(trimmed, out IPAddress parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            address = parsed;
            return true;
        }

        /// <summary>This device's IPv4 address on the active network, for display only.</summary>
        public static string LocalIPv4()
        {
            try
            {
                // No packet is sent: connecting a UDP socket only selects the outgoing interface.
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("10.255.255.255", Port);
                    if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address) &&
                        !endPoint.Address.Equals(IPAddress.Any))
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation info in network.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(info.Address))
                            return info.Address.ToString();
                    }
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Windows side: sends the active input about 30 times per second while enabled, as a broadcast
    /// on every connected network and, when the Quest's IP is entered, directly to that address.
    /// Direct sending still works on networks that drop broadcasts, such as some phone hotspots.
    /// </summary>
    public sealed class CadenceRelaySender : IDisposable
    {
        private const string TargetPreferenceKey = "VirtualRide.QuestRelayAddress";
        private const float IntervalSeconds = 1f / 30f;
        private const float TargetRefreshSeconds = 5f;
        private readonly List<IPEndPoint> _targets = new List<IPEndPoint>();
        private UdpClient _client;
        private float _nextSendAt;
        private float _nextTargetRefreshAt;
        private int _sequence;
        private IPAddress _questAddress;

        public CadenceRelaySender()
        {
            string saved = PlayerPrefs.GetString(TargetPreferenceKey, string.Empty);
            if (CadenceRelayProtocol.TryParseIPv4(saved, out IPAddress address)) _questAddress = address;
        }

        public bool IsEnabled { get; private set; }
        public string Error { get; private set; } = string.Empty;
        public string QuestAddressText => _questAddress != null ? _questAddress.ToString() : string.Empty;

        /// <summary>Empty clears the direct address and leaves broadcast only. Returns false if invalid.</summary>
        public bool SetQuestAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _questAddress = null;
            }
            else if (CadenceRelayProtocol.TryParseIPv4(text, out IPAddress address))
            {
                _questAddress = address;
            }
            else
            {
                return false;
            }

            PlayerPrefs.SetString(TargetPreferenceKey, QuestAddressText);
            PlayerPrefs.Save();
            _nextTargetRefreshAt = 0f;
            return true;
        }

        public bool SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                IsEnabled = false;
                CloseClient();
                return true;
            }

            try
            {
                if (_client == null)
                {
                    _client = new UdpClient { EnableBroadcast = true };
                }
                Error = string.Empty;
                IsEnabled = true;
                _nextTargetRefreshAt = 0f;
                return true;
            }
            catch (Exception)
            {
                Error = "Questへの送信を開始できません。Windowsのネットワーク設定を確認してください。";
                IsEnabled = false;
                CloseClient();
                return false;
            }
        }

        public void Tick(RideInputSample sample)
        {
            if (!IsEnabled || _client == null || Time.unscaledTime < _nextSendAt) return;
            _nextSendAt = Time.unscaledTime + IntervalSeconds;
            if (Time.unscaledTime >= _nextTargetRefreshAt)
            {
                _nextTargetRefreshAt = Time.unscaledTime + TargetRefreshSeconds;
                RefreshTargets();
            }

            byte[] bytes = Encoding.ASCII.GetBytes(CadenceRelayProtocol.Encode(++_sequence, sample));
            foreach (IPEndPoint target in _targets)
            {
                try
                {
                    _client.Send(bytes, bytes.Length, target);
                }
                catch (Exception)
                {
                    // A transient network change should not stop the PC-side ride.
                }
            }
        }

        private void RefreshTargets()
        {
            _targets.Clear();
            if (_questAddress != null) _targets.Add(new IPEndPoint(_questAddress, CadenceRelayProtocol.Port));
            _targets.Add(new IPEndPoint(IPAddress.Broadcast, CadenceRelayProtocol.Port));
            try
            {
                // 255.255.255.255 leaves through one adapter only; also broadcast on each connected network
                // (e.g. 172.20.10.15 on an iPhone hotspot) in case the PC also has wired or campus networks.
                foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up ||
                        network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation info in network.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask == null) continue;
                        byte[] address = info.Address.GetAddressBytes();
                        byte[] mask = info.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4 || (mask[0] == 255 && mask[1] == 255 && mask[2] == 255 && mask[3] == 255)) continue;
                        for (int i = 0; i < 4; i++) address[i] = (byte)(address[i] | ~mask[i]);
                        var broadcast = new IPEndPoint(new IPAddress(address), CadenceRelayProtocol.Port);
                        if (!_targets.Contains(broadcast)) _targets.Add(broadcast);
                    }
                }
            }
            catch (Exception)
            {
                // The limited broadcast and any direct address above remain.
            }
        }

        public void Dispose()
        {
            IsEnabled = false;
            CloseClient();
        }

        private void CloseClient()
        {
            try { _client?.Close(); } catch (Exception) { }
            _client = null;
        }
    }

    /// <summary>Quest side: rides on the numbers relayed from the Windows app.</summary>
    public sealed class PcRelayCadenceInput : MonoBehaviour, IRideInputSource
    {
        private const float StaleSeconds = 1.0f;
        private readonly object _lock = new object();
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private bool _isActive;
        private RideInputSample _latest;
        private int _latestSequence;
        private double _receivedAt = -1;
        private int _packetCount;
        private string _error = string.Empty;
        private string _localAddress = string.Empty;
        private float _nextAddressCheckAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _multicastLock;
#endif

        public string DisplayName => "PC中継";
        public bool IsActive => _isActive;
        public bool IsUnvalidatedMeasurement => true;

        public RideInputSample Current
        {
            get
            {
                if (!string.IsNullOrEmpty(_error))
                    return new RideInputSample(0f, -1f, 0f, RideInputState.Error, _error);
                RideInputSample latest;
                double receivedAt;
                lock (_lock)
                {
                    latest = _latest;
                    receivedAt = _receivedAt;
                }

                double age = receivedAt < 0 ? double.MaxValue : Time.realtimeSinceStartupAsDouble - receivedAt;
                if (age > StaleSeconds)
                {
                    string address = string.IsNullOrEmpty(_localAddress)
                        ? "QuestのIPアドレスを確認中です"
                        : "QuestのIP: " + _localAddress;
                    string waiting = receivedAt < 0
                        ? "PCからの回転数を待っています。" + address + "（届かない場合はPC版の送信先に入力）"
                        : "PCからの受信が途切れています。" + address;
                    return new RideInputSample(0f, -1f, 0f, RideInputState.Searching, waiting);
                }

                string status = latest.HasCadence
                    ? "PC中継 ・ " + Mathf.RoundToInt(latest.CadenceRpm) + " rpm"
                    : "PC中継 ・ " + latest.SpeedKph.ToString("0.0") + " km/h";
                if (latest.State == RideInputState.Error) status = "PC側の計測にエラーがあります。PCの画面を確認してください。";
                return new RideInputSample(latest.SpeedKph, latest.CadenceRpm, latest.Confidence, latest.State, status);
            }
        }

        public void Activate()
        {
            if (_isActive) return;
            _isActive = true;
            _error = string.Empty;
            lock (_lock) { _receivedAt = -1; }
            try
            {
                AcquireMulticastLock();
                _client = new UdpClient(new IPEndPoint(IPAddress.Any, CadenceRelayProtocol.Port));
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "VirtualRide relay" };
                _thread.Start();
            }
            catch (Exception)
            {
                _error = "PC中継を受信できません。アプリを再起動してください。";
                StopReceiving();
            }
        }

        public void Deactivate()
        {
            _isActive = false;
            StopReceiving();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (Time.unscaledTime >= _nextAddressCheckAt)
            {
                // Refresh occasionally: the address changes when the Quest joins another network.
                _nextAddressCheckAt = Time.unscaledTime + 3f;
                _localAddress = CadenceRelayProtocol.LocalIPv4();
            }

            int count;
            lock (_lock) { count = _packetCount; _packetCount = 0; }
            if (count > 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                lock (_lock) { _receivedAt = now; }
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            int lastPacketTick = 0;
            while (_running)
            {
                try
                {
                    byte[] bytes = _client.Receive(ref remote);
                    string packet = Encoding.ASCII.GetString(bytes);
                    if (!CadenceRelayProtocol.TryDecode(packet, out int sequence, out RideInputSample sample)) continue;
                    int now = Environment.TickCount;
                    bool afterGap = now - lastPacketTick > 1000;
                    lastPacketTick = now;
                    lock (_lock)
                    {
                        // Ignore late, out-of-order packets; a pause means the PC app may have restarted.
                        if (!afterGap && sequence <= _latestSequence) continue;
                        _latestSequence = sequence;
                        _latest = sample;
                        _packetCount++;
                    }
                }
                catch (Exception)
                {
                    if (!_running) return;
                }
            }
        }

        private void StopReceiving()
        {
            _running = false;
            try { _client?.Close(); } catch (Exception) { }
            _client = null;
            _thread = null;
            lock (_lock) { _latestSequence = 0; _packetCount = 0; }
            ReleaseMulticastLock();
        }

        private void AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Some Android Wi-Fi drivers drop broadcast packets unless an app holds this lock.
            try
            {
                using (var wifi = UnityEngine.Android.AndroidApplication.currentActivity
                           .Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    _multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "VirtualRideRelay");
                    _multicastLock.Call("setReferenceCounted", false);
                    _multicastLock.Call("acquire");
                }
            }
            catch (Exception)
            {
                _multicastLock = null;
            }
#endif
        }

        private void ReleaseMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _multicastLock?.Call("release"); } catch (Exception) { }
            _multicastLock?.Dispose();
            _multicastLock = null;
#endif
        }

        private void OnDestroy()
        {
            StopReceiving();
        }
    }
}
