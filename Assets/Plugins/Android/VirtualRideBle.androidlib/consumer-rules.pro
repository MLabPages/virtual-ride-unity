# JNI uses these names from C#. Preserve entry points when the app enables R8.
-keep class com.virtualride.ble.BleCscClient {
    public <init>(android.content.Context);
    public boolean beginScan(boolean);
    public void stopScan();
    public boolean connect(java.lang.String);
    public java.lang.String snapshot();
    public void disconnect();
    public void close();
}
