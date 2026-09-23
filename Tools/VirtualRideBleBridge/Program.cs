using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace VirtualRideBleBridge
{
    internal static class Program
    {
        private static int Main()
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            using (var bridge = new BleBridge())
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    try
                    {
                        if (!bridge.HandleCommand(line)) break;
                    }
                    catch (Exception exception)
                    {
                        BleBridge.Write("ERROR\t" + exception.Message);
                    }
                }
            }

            return 0;
        }
    }

    internal sealed class BleBridge : IDisposable
    {
        private static readonly Guid CscServiceUuid = new Guid("00001816-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CscMeasurementUuid = new Guid("00002a5b-0000-1000-8000-00805f9b34fb");
        private static readonly object OutputLock = new object();

        private readonly ConcurrentDictionary<ulong, long> _lastAdvertisementAt =
            new ConcurrentDictionary<ulong, long>();
        private BluetoothLEAdvertisementWatcher _watcher;
        private TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs>
            _receivedHandler;
        private TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementWatcherStoppedEventArgs>
            _stoppedHandler;
        private object _receivedToken;
        private object _stoppedToken;
        private BluetoothLEDevice _device;
        private GattDeviceService _service;
        private GattCharacteristic _measurement;
        private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> _measurementHandler;
        private object _measurementToken;
        private int _scanGeneration;
        private bool _hasPreviousCrank;
        private ushort _previousCrankRevolutions;
        private ushort _previousCrankEventTime;
        private string _connectedName = "BLE cadence sensor";

        public static void Write(string message)
        {
            lock (OutputLock)
            {
                Console.WriteLine(message.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
                Console.Out.Flush();
            }
        }

        public bool HandleCommand(string line)
        {
            string[] parts = (line ?? string.Empty).Split('\t');
            string command = parts[0].Trim().ToUpperInvariant();
            if (command == "SCAN")
            {
                StartScan();
            }
            else if (command == "CONNECT" && parts.Length >= 2)
            {
                ulong address;
                if (!ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
                {
                    Write("ERROR\tInvalid Bluetooth address");
                    return true;
                }

                Task.Run(() => ConnectAsync(address));
            }
            else if (command == "DISCONNECT")
            {
                DisconnectCurrent();
                Write("DISCONNECTED");
            }
            else if (command == "QUIT")
            {
                Dispose();
                return false;
            }
            else
            {
                Write("ERROR\tUnknown bridge command");
            }
            return true;
        }

        private void StartScan()
        {
            try
            {
                if (_watcher == null)
                {
                    _watcher = new BluetoothLEAdvertisementWatcher
                    {
                        ScanningMode = BluetoothLEScanningMode.Active
                    };
                    _receivedHandler = OnAdvertisementReceived;
                    _stoppedHandler = OnWatcherStopped;
                    _receivedToken = AddWinRtEvent(_watcher, "Received", _receivedHandler);
                    _stoppedToken = AddWinRtEvent(_watcher, "Stopped", _stoppedHandler);
                }

                if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _watcher.Stop();
                }

                int generation = Interlocked.Increment(ref _scanGeneration);
                _lastAdvertisementAt.Clear();
                _watcher.Start();
                Write("STATE\tSCANNING\tSearching for nearby Bluetooth LE devices");
                Task.Delay(TimeSpan.FromSeconds(12)).ContinueWith(_ =>
                {
                    if (Interlocked.CompareExchange(ref _scanGeneration, generation, generation) == generation &&
                        _watcher != null && _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                    {
                        _watcher.Stop();
                        Write("SCAN_DONE");
                    }
                });
            }
            catch (Exception exception)
            {
                Write("ERROR\tBLE scan failed: " + exception.Message);
            }
        }

        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long previous;
            if (_lastAdvertisementAt.TryGetValue(args.BluetoothAddress, out previous) &&
                (now - previous) * 1000.0 / System.Diagnostics.Stopwatch.Frequency < 650.0)
            {
                return;
            }

            _lastAdvertisementAt[args.BluetoothAddress] = now;
            string name = args.Advertisement.LocalName ?? string.Empty;
            Write(string.Join("\t", "DEVICE", args.BluetoothAddress.ToString("X12", CultureInfo.InvariantCulture),
                args.RawSignalStrengthInDBm.ToString(CultureInfo.InvariantCulture), name));
        }

        private void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (args.Error != BluetoothError.Success)
            {
                Write("ERROR\tBLE scan stopped: " + args.Error);
            }
        }

        private async Task ConnectAsync(ulong address)
        {
            try
            {
                Interlocked.Increment(ref _scanGeneration);
                if (_watcher != null && _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _watcher.Stop();
                }

                DisconnectCurrent();
                Write("CONNECTING\t" + address.ToString("X12", CultureInfo.InvariantCulture));

                _device = await AwaitWinRt<BluetoothLEDevice>(BluetoothLEDevice.FromBluetoothAddressAsync(address));
                if (_device == null)
                {
                    Write("ERROR\tWindows could not open this Bluetooth LE device");
                    return;
                }

                _connectedName = string.IsNullOrWhiteSpace(_device.Name) ? "BLE cadence sensor" : _device.Name;
                GattDeviceServicesResult servicesResult = await AwaitWinRt<GattDeviceServicesResult>(
                    _device.GetGattServicesAsync(BluetoothCacheMode.Uncached));
                if (servicesResult.Status != GattCommunicationStatus.Success)
                {
                    Write("ERROR\tCould not read sensor services: " + servicesResult.Status);
                    DisconnectCurrent();
                    return;
                }

                _service = servicesResult.Services.FirstOrDefault(service => service.Uuid == CscServiceUuid);
                if (_service == null)
                {
                    Write("ERROR\tCSC service 1816 was not found on this device");
                    DisconnectCurrent();
                    return;
                }

                GattCharacteristicsResult characteristicsResult = await AwaitWinRt<GattCharacteristicsResult>(
                    _service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached));
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    Write("ERROR\tCould not read CSC characteristics: " + characteristicsResult.Status);
                    DisconnectCurrent();
                    return;
                }

                _measurement = characteristicsResult.Characteristics
                    .FirstOrDefault(characteristic => characteristic.Uuid == CscMeasurementUuid);
                if (_measurement == null)
                {
                    Write("ERROR\tCSC Measurement 2A5B was not found on this device");
                    DisconnectCurrent();
                    return;
                }

                _hasPreviousCrank = false;
                _measurementHandler = OnMeasurementChanged;
                _measurementToken = AddWinRtEvent(_measurement, "ValueChanged", _measurementHandler);
                GattCharacteristicProperties properties = _measurement.CharacteristicProperties;
                if ((properties & GattCharacteristicProperties.Notify) == 0 &&
                    (properties & GattCharacteristicProperties.Indicate) == 0)
                {
                    Write("ERROR\tCSC Measurement does not support notifications");
                    DisconnectCurrent();
                    return;
                }
                GattClientCharacteristicConfigurationDescriptorValue notifyValue =
                    (properties & GattCharacteristicProperties.Notify) != 0
                        ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                        : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                GattCommunicationStatus subscribeStatus = await AwaitWinRt<GattCommunicationStatus>(
                    _measurement.WriteClientCharacteristicConfigurationDescriptorAsync(notifyValue));
                if (subscribeStatus != GattCommunicationStatus.Success)
                {
                    Write("ERROR\tCould not subscribe to cadence notifications: " + subscribeStatus);
                    DisconnectCurrent();
                    return;
                }

                Write("CONNECTED\t" + _connectedName);
            }
            catch (Exception exception)
            {
                DisconnectCurrent();
                Write("ERROR\tBLE connection failed: " + exception.Message);
            }
        }

        private void OnMeasurementChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                reader.Dispose();

                if (data.Length < 1)
                {
                    return;
                }

                byte flags = data[0];
                int offset = 1;
                if ((flags & 0x01) != 0) offset += 6; // Wheel revolutions and event time.
                if ((flags & 0x02) == 0 || data.Length < offset + 4) return;

                ushort revolutions = (ushort)(data[offset] | (data[offset + 1] << 8));
                ushort eventTime = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                if (!_hasPreviousCrank)
                {
                    _hasPreviousCrank = true;
                    _previousCrankRevolutions = revolutions;
                    _previousCrankEventTime = eventTime;
                    return;
                }

                int revolutionDelta = (revolutions - _previousCrankRevolutions) & 0xffff;
                int eventTimeDelta = (eventTime - _previousCrankEventTime) & 0xffff;
                _previousCrankRevolutions = revolutions;
                _previousCrankEventTime = eventTime;
                if (revolutionDelta == 0 || eventTimeDelta == 0) return;

                double rpm = revolutionDelta * 60.0 * 1024.0 / eventTimeDelta;
                if (rpm < 0.0 || rpm > 220.0) return;
                double receivedAtMs = (double)System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;
                Write(string.Join("\t", "CADENCE", rpm.ToString("0.00", CultureInfo.InvariantCulture),
                    receivedAtMs.ToString("0.00", CultureInfo.InvariantCulture)));
            }
            catch (Exception exception)
            {
                Write("ERROR\tInvalid CSC measurement: " + exception.Message);
            }
        }

        private void DisconnectCurrent()
        {
            if (_measurement != null)
            {
                RemoveWinRtEvent(_measurement, "ValueChanged", _measurementHandler, _measurementToken);
                _measurementHandler = null;
                _measurementToken = null;
                _measurement = null;
            }

            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            if (_device != null)
            {
                _device.Dispose();
                _device = null;
            }

            _hasPreviousCrank = false;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _scanGeneration);
            if (_watcher != null)
            {
                RemoveWinRtEvent(_watcher, "Received", _receivedHandler, _receivedToken);
                RemoveWinRtEvent(_watcher, "Stopped", _stoppedHandler, _stoppedToken);
                if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _watcher.Stop();
                }
                _watcher = null;
            }

            DisconnectCurrent();
        }

        private static object AddWinRtEvent(object target, string eventName, Delegate handler)
        {
            MethodInfo add = target.GetType().GetMethod("add_" + eventName);
            if (add == null) throw new MissingMethodException(target.GetType().FullName, "add_" + eventName);
            return add.Invoke(target, new object[] { handler });
        }

        private static void RemoveWinRtEvent(object target, string eventName, Delegate handler, object token)
        {
            if (target == null || handler == null || token == null) return;
            MethodInfo remove = target.GetType().GetMethod("remove_" + eventName);
            if (remove != null) remove.Invoke(target, new[] { token });
        }

        private static async Task<T> AwaitWinRt<T>(object operation)
        {
            MethodInfo asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods()
                .First(method => method.Name == "AsTask" && method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsGenericType &&
                    method.GetParameters()[0].ParameterType.GetGenericTypeDefinition().FullName ==
                    "Windows.Foundation.IAsyncOperation`1");
            var task = (Task<T>)asTask.MakeGenericMethod(typeof(T)).Invoke(null, new[] { operation });
            return await task.ConfigureAwait(false);
        }
    }
}
