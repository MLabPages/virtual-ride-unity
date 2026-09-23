# 専用センサー接続の設計

## 結論

Windows版にBluetooth LEのCSC（Cycling Speed and Cadence）接続を追加しました。画面の「BLEセンサー」から機器を検索し、一覧から選んで接続します。センサーがBluetooth SIG標準のCSCサービスを公開していれば、メーカー固有のSDKなしで読み取れます。

## 入力の契約

`Assets/VirtualRide/Scripts/Input/IRideInputSource.cs` が、走行側と計測側の境界です。Bluetooth入力は `BluetoothCadenceInput` が値を変換し、走行側は同じ入力契約を使います。

- `SpeedKph`: 走行に使う速度（km/h）
- `CadenceRpm`: ペダル回転数（rpm）
- `Confidence`: 0〜1の計測信頼度
- `Status`: 参加者や実験者へ見せる状態文
- `IsUnvalidatedMeasurement`: 基準機器との妥当性確認前なら true（カメラとBluetoothセンサーは true、キーボードは false）

センサー固有の通信処理や値の変換は、この境界より入力側に閉じ込めます。

## Windows版の接続手順

1. WindowsのBluetoothをオンにし、センサーをクランクに取り付けます。
2. センサーを数回回して起動します。
3. アプリ下部の「BLEセンサー」→「再検索」を押します。
4. 一覧からBK9Cを選び、「接続」を押します。
5. rpmが表示されたことを確認してから実験記録を開始します。記録中は入力方式を切り替えられません。

UnityのWindowsビルドには `Assets/StreamingAssets/Bluetooth/VirtualRideBleBridge.exe` が同梱されます。この小さな補助プロセスがWindowsのBLE APIを使い、Unityとは標準入力・出力で通信します。BLEの処理と値はPC内だけで扱い、Bluetoothアドレスは保存しません。

補助プログラムのソースは `Tools/VirtualRideBleBridge/Program.cs` にあり、変更後は `Tools/VirtualRideBleBridge/Build.ps1` を実行して同梱ファイルを再生成します。

## 対応するデータ

- CSCサービス: `0x1816`
- CSC Measurement: `0x2A5B`
- クランク累積回転数と最後のクランクイベント時刻からrpmを計算します。イベント時刻は1/1024秒単位です。
- 走行速度は既存の換算と合わせ、`rpm × 4.2 m/回転 × 60 ÷ 1000` km/h とします。
- 通知が2.5秒途絶えた場合、表示・走行入力を0 rpmにします。
- 検索一覧には周辺BLE機器も表示されます。CSCサービスがない機器を選ぶと接続エラーになります。

BK9Cの実機がまだないため、スキャン、接続、通知周期、rpm値、切断時の挙動は未確認です。最初の接続時にWindowsのBluetooth許可やアダプター状態も確認してください。実験前にカメラ入力と同時記録し、基準値との一致と反応時間を測ってください。
