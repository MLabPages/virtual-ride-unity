# Quest 3 BLE検索の実機診断（2026-09-25）

ユーザーはBK9Cをスマホのサイクリングアプリから接続できた。Quest 3をADBで確認したところ、`com.mlabpages.virtualride.quest` の `BLUETOOTH_SCAN` と `BLUETOOTH_CONNECT` は許可済みで、BluetoothとBLE機能も有効だった。BluetoothLeScanner登録は成功した。一方、Quest画面は「検出0件」で、OSログには短時間に検索を繰り返したことによる `scanning too frequently` 拒否があった。Bluetoothサービスの統計ではアプリの複数の検索に広告結果が届いていた。画面0件の原因は、連続再検索による結果消去・頻度制限と、アプリ内の受信処理のどちらか、現時点では確定していない。

対策として検索中の再要求では進行中の検索を維持し、検出済み機器を消さないようC#とJavaの両方にガードを追加した。状態文に広告コールバック数とJava側の候補機器数を表示し、次回の実機検索で無線受信とUnityへの受け渡しを切り分ける。Quest APKのビルドは成功した。

ADBで上書きインストールを試みた際にADBサーバーとの接続が切れ、サーバー復旧後もQuestが端末一覧に現れなかったため、修正版APKの端末への反映と検索結果の確認は未完了。USB再接続後に `adb devices` を確認し、`adb install -r Builds/Quest/VirtualRide.apk` でデータを維持して更新する。
