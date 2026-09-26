package com.virtualride.ble;

import android.Manifest;
import android.annotation.SuppressLint;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanRecord;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.pm.PackageManager;
import android.location.LocationManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.os.SystemClock;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/** Per-provider foreground BLE client. No Unity dependency, logging or persistence.
 * All state, callbacks and JNI entry points share this object's monitor.
 * Unity polls an atomic snapshot; no Unity API is called from Android threads.
 */
@SuppressLint("MissingPermission") // Entry checks + SecurityException handling cover revocation races.
public final class BleCscClient {
    private static final UUID CSC = UUID.fromString("00001816-0000-1000-8000-00805f9b34fb");
    private static final UUID MEASUREMENT = UUID.fromString("00002a5b-0000-1000-8000-00805f9b34fb");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static final int MAX_DEVICES = 64;
    private final Context context;
    private final Handler timer = new Handler(Looper.getMainLooper());
    private final CscCadence cadence = new CscCadence();
    private final Map<String, DeviceEntry> devices = new LinkedHashMap<>();
    private BluetoothAdapter adapter;
    private BluetoothLeScanner scanner;
    private ScanCallback scanCallback;
    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic measurement;
    private BluetoothGattDescriptor cccd;
    private Runnable scanDeadline;
    private Runnable connectionDeadline;
    private boolean scanning;
    private int receivedScanCallbacks;
    private boolean connected;
    private boolean closed;
    private String state = "offline";
    private String error = "";
    private String selectedAddress = "";
    private String deviceName = "";

    private static final class DeviceEntry {
        long lastSeenAt;
        final BluetoothDevice device;
        String name;
        int rssi;
        boolean advertisesCsc;
        DeviceEntry(BluetoothDevice device) { this.device = device; }
    }

    public BleCscClient(Context context) {
        this.context = context.getApplicationContext();
    }

    /** includeOtherDevices helps sensors whose advertisements omit the service UUID. */
    public synchronized boolean beginScan(boolean includeOtherDevices) {
        if (closed) return false;
        if (scanning) return true;
        stopScanInternal();
        disconnectInternal();
        // Retain candidates between searches within this foreground session.
        long cutoff = SystemClock.elapsedRealtime() - 120000;
        devices.values().removeIf(entry -> entry.lastSeenAt < cutoff);
        receivedScanCallbacks = 0;
        error = "";
        if (!ready(true)) return false;
        try {
            scanner = adapter.getBluetoothLeScanner();
            if (scanner == null) return fail("bluetooth_off");
            scanCallback = new ScanCallback() {
                @Override public void onScanResult(int type, ScanResult result) {
                    receiveScan(this, result);
                }
                @Override public void onBatchScanResults(List<ScanResult> results) {
                    for (ScanResult result : results) receiveScan(this, result);
                }
                @Override public void onScanFailed(int code) { scanFailed(this); }
            };
            List<ScanFilter> filters = includeOtherDevices ? Collections.emptyList()
                : Collections.singletonList(new ScanFilter.Builder().setServiceUuid(new ParcelUuid(CSC)).build());
            scanning = true;
            state = "scanning";
            scanner.startScan(filters, new ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).setReportDelay(0).build(), scanCallback);
            if (!scanning) return false;
            final ScanCallback started = scanCallback;
            scanDeadline = () -> finishScan(started);
            timer.postDelayed(scanDeadline, 30000);
            return true;
        } catch (SecurityException ignored) { return fail("permission"); }
        catch (RuntimeException ignored) { return fail("scan_failed"); }
    }

    public synchronized void stopScan() {
        stopScanInternal();
        if ("scanning".equals(state)) state = devices.isEmpty() ? "empty" : "select";
    }

    private synchronized void finishScan(ScanCallback source) {
        if (source == scanCallback && !closed) stopScan();
    }

    private synchronized void scanFailed(ScanCallback source) {
        if (source == scanCallback && !closed) fail("scan_failed");
    }

    private synchronized void receiveScan(ScanCallback source, ScanResult result) {
        if (closed || !scanning || source != scanCallback || result == null) return;
        receivedScanCallbacks++;
        try {
            BluetoothDevice device = result.getDevice();
            String address = device.getAddress(); // RAM only. Never used in an error or state message.
            DeviceEntry entry = devices.get(address);
            if (entry == null) {
                if (devices.size() >= MAX_DEVICES) return;
                entry = new DeviceEntry(device);
                devices.put(address, entry);
            }
            ScanRecord record = result.getScanRecord();
            String name = record == null ? null : record.getDeviceName();
            if (name == null || name.trim().isEmpty()) name = device.getName();
            if (name != null && !name.trim().isEmpty()) entry.name = displayName(name);
            if (entry.name == null) entry.name = "BLE sensor";
            entry.lastSeenAt = SystemClock.elapsedRealtime();
            entry.rssi = result.getRssi();
            List<ParcelUuid> services = record == null ? null : record.getServiceUuids();
            entry.advertisesCsc |= services != null && services.contains(new ParcelUuid(CSC));
        } catch (SecurityException ignored) { fail("permission"); }
        catch (RuntimeException ignored) { fail("scan_failed"); }
    }

    /** Only devices observed by this instance can be connected. No arbitrary MAC lookup. */
    public synchronized boolean connect(String address) {
        if (closed) return false;
        DeviceEntry entry = devices.get(address);
        stopScanInternal();
        disconnectInternal();
        error = "";
        if (entry == null) return fail("select_device");
        if (!ready(false)) return false;
        selectedAddress = address;
        // Do not return arbitrary advertised names in ride status / research output.
        deviceName = modelName(entry.name);
        state = "connecting";
        try {
            gatt = entry.device.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE);
            if (gatt == null) return fail("connect_failed");
            final BluetoothGatt started = gatt;
            connectionDeadline = () -> connectionTimedOut(started);
            timer.postDelayed(connectionDeadline, 20000);
            return true;
        } catch (SecurityException ignored) { return fail("permission"); }
        catch (RuntimeException ignored) { return fail("connect_failed"); }
    }

    private synchronized void connectionTimedOut(BluetoothGatt source) {
        if (source == gatt && !connected && !closed) fail("connect_timeout");
    }

    private final BluetoothGattCallback callback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt source, int status, int newState) {
            connectionChanged(source, status, newState);
        }
        @Override public void onServicesDiscovered(BluetoothGatt source, int status) {
            servicesDiscovered(source, status);
        }
        @Override public void onDescriptorWrite(BluetoothGatt source, BluetoothGattDescriptor descriptor, int status) {
            subscribed(source, descriptor, status);
        }
        @Override public void onCharacteristicChanged(BluetoothGatt source, BluetoothGattCharacteristic characteristic, byte[] value) {
            received(source, characteristic, value);
        }
        @Override @SuppressWarnings("deprecation")
        public void onCharacteristicChanged(BluetoothGatt source, BluetoothGattCharacteristic characteristic) {
            // API 33+ supplies an immutable-at-callback byte[] in the overload above.
            if (Build.VERSION.SDK_INT < 33) received(source, characteristic, characteristic.getValue());
        }
    };

    private synchronized void connectionChanged(BluetoothGatt source, int status, int newState) {
        if (closed || source != gatt) return; // Ignore callbacks from replaced/closed GATTs.
        if (status != BluetoothGatt.GATT_SUCCESS) { fail("connection_lost"); return; }
        if (newState == BluetoothProfile.STATE_DISCONNECTED) {
            disconnectInternal();
            devices.clear();
            state = "disconnected";
            return;
        }
        if (newState != BluetoothProfile.STATE_CONNECTED || !"connecting".equals(state)) return;
        state = "discovering";
        try {
            if (!source.discoverServices()) fail("service_failed");
        } catch (SecurityException ignored) { fail("permission"); }
        catch (RuntimeException ignored) { fail("service_failed"); }
    }

    @SuppressWarnings("deprecation")
    private synchronized void servicesDiscovered(BluetoothGatt source, int status) {
        if (closed || source != gatt || !"discovering".equals(state)) return;
        if (status != BluetoothGatt.GATT_SUCCESS) { fail("service_failed"); return; }
        try {
            BluetoothGattService service = source.getService(CSC);
            if (service == null) { fail("no_csc"); return; }
            measurement = service.getCharacteristic(MEASUREMENT);
            if (measurement == null) { fail("no_measurement"); return; }
            int properties = measurement.getProperties();
            byte[] subscription;
            if ((properties & BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0)
                subscription = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE;
            else if ((properties & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0)
                subscription = BluetoothGattDescriptor.ENABLE_INDICATION_VALUE;
            else { fail("subscribe_failed"); return; }
            cccd = measurement.getDescriptor(CCCD);
            if (cccd == null || !source.setCharacteristicNotification(measurement, true)) {
                fail("subscribe_failed"); return;
            }
            state = "subscribing";
            boolean started;
            if (Build.VERSION.SDK_INT >= 33) started = source.writeDescriptor(cccd, subscription) == 0;
            else started = cccd.setValue(subscription) && source.writeDescriptor(cccd);
            if (!started) fail("subscribe_failed");
        } catch (SecurityException ignored) { fail("permission"); }
        catch (RuntimeException ignored) { fail("subscribe_failed"); }
    }

    private synchronized void subscribed(BluetoothGatt source, BluetoothGattDescriptor descriptor, int status) {
        if (closed || source != gatt || descriptor != cccd || !"subscribing".equals(state)) return;
        if (status != BluetoothGatt.GATT_SUCCESS) { fail("subscribe_failed"); return; }
        cancelConnectionDeadline();
        cadence.reset();
        connected = true; // Only declare connected after the peripheral confirms CCCD.
        state = "connected";
    }

    private synchronized void received(BluetoothGatt source, BluetoothGattCharacteristic characteristic, byte[] value) {
        if (closed || source != gatt || !connected || characteristic != measurement) return;
        cadence.accept(value, SystemClock.elapsedRealtime());
    }

    /** Small atomic RAM-only JSON snapshot. Caller must not log/persist this payload. */
    public synchronized String snapshot() {
        if (!closed && (scanning || gatt != null)) ready(scanning);
        try {
            long now = SystemClock.elapsedRealtime();
            JSONObject result = new JSONObject();
            result.put("state", state);
            result.put("error", error);
            result.put("scanning", scanning);
            result.put("connected", connected);
            result.put("selectedAddress", selectedAddress);
            result.put("deviceName", deviceName);
            result.put("rpm", connected ? cadence.rpm(now) : 0);
            result.put("ageMs", connected ? cadence.ageMs(now) : -1);
            result.put("receivedScanCallbacks", receivedScanCallbacks);
            result.put("detectedDeviceCount", devices.size());
            JSONArray list = new JSONArray();
            for (Map.Entry<String, DeviceEntry> item : devices.entrySet()) {
                DeviceEntry device = item.getValue();
                JSONObject json = new JSONObject();
                json.put("address", item.getKey());
                json.put("name", device.name);
                json.put("rssi", device.rssi);
                json.put("advertisesCsc", device.advertisesCsc);
                list.put(json);
            }
            result.put("devices", list);
            return result.toString();
        } catch (JSONException ignored) {
            fail("internal");
            return "{\"state\":\"error\",\"error\":\"internal\",\"connected\":false,\"devices\":[]}";
        }
    }

    public synchronized void disconnect() {
        stopScanInternal();
        disconnectInternal();
        devices.clear();
        error = "";
        state = "offline";
    }

    public synchronized void close() {
        if (closed) return;
        closed = true;
        disconnect();
        timer.removeCallbacksAndMessages(null);
        adapter = null;
    }

    private boolean ready(boolean scanningRequested) {
        try {
            if (Build.VERSION.SDK_INT >= 31) {
                if (!granted(Manifest.permission.BLUETOOTH_CONNECT) ||
                    (scanningRequested && !granted(Manifest.permission.BLUETOOTH_SCAN))) return fail("permission");
            } else if (scanningRequested && !granted(Manifest.permission.ACCESS_FINE_LOCATION)) return fail("permission");
            if (!context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_BLUETOOTH_LE)) return fail("unsupported");
            BluetoothManager manager = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
            adapter = manager == null ? null : manager.getAdapter();
            if (adapter == null) return fail("unsupported");
            if (!adapter.isEnabled()) return fail("bluetooth_off");
            if (scanningRequested && Build.VERSION.SDK_INT < 31) {
                LocationManager location = (LocationManager) context.getSystemService(Context.LOCATION_SERVICE);
                boolean enabled = location != null && (Build.VERSION.SDK_INT >= 28 ? location.isLocationEnabled()
                    : location.isProviderEnabled(LocationManager.GPS_PROVIDER) || location.isProviderEnabled(LocationManager.NETWORK_PROVIDER));
                if (!enabled) return fail("location_off");
            }
            return true;
        } catch (SecurityException ignored) { return fail("permission"); }
        catch (RuntimeException ignored) { return fail("unavailable"); }
    }

    private boolean granted(String permission) {
        return context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED;
    }

    private boolean fail(String code) {
        stopScanInternal();
        disconnectInternal();
        devices.clear();
        error = code; // Fixed identifiers only: no OS exception strings or MACs.
        state = "error";
        return false;
    }

    private void stopScanInternal() {
        if (scanDeadline != null) timer.removeCallbacks(scanDeadline);
        scanDeadline = null;
        ScanCallback oldCallback = scanCallback;
        BluetoothLeScanner oldScanner = scanner;
        scanCallback = null;
        scanner = null;
        scanning = false;
        if (oldScanner != null && oldCallback != null) {
            try { oldScanner.stopScan(oldCallback); } catch (RuntimeException ignored) { }
        }
    }

    private void cancelConnectionDeadline() {
        if (connectionDeadline != null) timer.removeCallbacks(connectionDeadline);
        connectionDeadline = null;
    }

    private void disconnectInternal() {
        cancelConnectionDeadline();
        BluetoothGatt previous = gatt;
        BluetoothGattCharacteristic previousMeasurement = measurement;
        gatt = null; // Invalidate before disconnect/close can cause another callback.
        measurement = null;
        cccd = null;
        connected = false;
        cadence.reset();
        selectedAddress = deviceName = "";
        if (previous == null) return;
        // Ending this GATT connection ends the remote subscription. Do not enqueue
        // a CCCD write just before close: it may race an outstanding discovery/write.
        try { if (previousMeasurement != null) previous.setCharacteristicNotification(previousMeasurement, false); }
        catch (RuntimeException ignored) { }
        try { previous.disconnect(); } catch (RuntimeException ignored) { }
        try { previous.close(); } catch (RuntimeException ignored) { }
    }

    private static String displayName(String name) {
        String clean = name.replaceAll("[\\p{Cntrl}<>]", "").trim();
        return clean.isEmpty() ? "BLE sensor" : clean.substring(0, Math.min(clean.length(), 64));
    }

    private static String modelName(String name) {
        String upper = name == null ? "" : name.toUpperCase(java.util.Locale.ROOT);
        if (upper.contains("BK9C")) return "COOSPO BK9C";
        if (upper.contains("CAD70")) return "iGPSPORT CAD70";
        if (upper.contains("S314")) return "Magene S314";
        return "Bluetooth cadence sensor";
    }
}
