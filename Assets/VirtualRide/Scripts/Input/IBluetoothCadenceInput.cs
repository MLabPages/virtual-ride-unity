using System.Collections.Generic;

namespace VirtualRide.Input
{
    /// <summary>
    /// Platform-specific Bluetooth CSC implementations exposed through one app/UI contract.
    /// </summary>
    public interface IBluetoothCadenceInput : IRideInputSource
    {
        bool IsConnected { get; }
        bool IsScanning { get; }
        string Status { get; }
        string Error { get; }
        string ConnectedDeviceName { get; }
        string SelectedAddress { get; }
        List<BluetoothCadenceDevice> Devices { get; }

        bool BeginScan();
        bool Connect(string address);
        void Disconnect();
        void Shutdown();
    }
}
