# Android / Quest CSC input

Unity 6000.5.7f1 向けの独立した Android Library プラグイン。Java の Android BLE API を使用し、追加の Maven 依存、Unity activity の置き換え、外部サービスは不要です。

## 統合

`VirtualRideApp` がAndroid実機ビルドで `AndroidBluetoothCadenceInput` を自動追加します。クラスはWindows版と共通の `IBluetoothCadenceInput` を実装し、同じBLE接続画面から使えます。

```csharp
var input = gameObject.AddComponent<AndroidBluetoothCadenceInput>();
input.BeginScan(); // 権限要求・検索開始の受付。完了は IsScanning / Status / Error を参照。
// UI の接続操作で input.Connect(input.Devices[index].Address);
// 利用する入力として選ぶ時に app.AttachExternalInput(input);
// 所有側の終了時に input.Shutdown();
```

必要な using は `VirtualRide.Input`。API は Unity メインスレッドから呼んでください。`Update` が非選択時も状態を読み取り、既存アプリからの `Tick(float)` と重なっても通常の JNI 読み取りは毎秒10回までです。`Current` の値は直近のスナップショットで、切断検知は通常最大0.1秒遅れます。回転数の期限はネイティブ受信時刻から計算し、Unity 側の読み取りが止まっても `Current` は期限後に0 rpmを返します。

`BeginScan()` は10秒間、周辺BLE機器を最大64件メモリに保持し、RSSIの強い順で返します。検索中の再要求は同じ検索を継続し、検出済み機器を消しません。サービスUUIDを広告に含めない機器も発見するため既定はフィルターなしです。`BeginScan(false)` は CSC `1816` 広告のみを対象にします。どちらも選択後に実際の GATT サービス `1816` と Measurement `2A5B` を確認し、CCCD `2902` の書き込み成功後に `IsConnected = true` にします。notify を優先し、indicate のみの機器にも対応します。ホイール専用モードではクランク回転数を取得できません。

`BeginScan` / `Connect` の true は非同期処理の受付で、接続成功ではありません。接続・サービス探索・購読の合計は20秒で時間切れにします。明示切断、`Deactivate`、component無効化・破棄、アプリ終了・バックグラウンド移行で購読とGATT接続を解放し、一覧と選択アドレスを消去します。復帰後は再検索・接続が必要です。権限ダイアログによる一時停止中は、結果を待ってからフォアグラウンドで検索します。古いGATT・スキャンから届くコールバックは無視します。

## データと権限

- `Address` / `SelectedAddress` は接続用の一時的なOSアドレスです。JSONによるJNI転送も含め、RAM内だけで扱います。これは匿名IDではありません。統合側も一覧・JSON・アドレスを PlayerPrefs、研究記録、ログへ出力しないでください。
- 機器の生の広告名は一覧表示専用です。接続名・走行状態には固定の一般名または既知モデル名だけを渡します。例外メッセージもログや状態に渡しません。
- Android 12以降は `BLUETOOTH_SCAN` / `BLUETOOTH_CONNECT` を実行時に要求します。SCANには `neverForLocation` を宣言します。Android 6〜11は `ACCESS_FINE_LOCATION` の実行時許可と端末の位置情報設定が必要です。位置を取得する処理はありません。
- クランク累積回転数・イベント時刻はそれぞれ uint16 の周回を処理し、`deltaRevolutions * 60 * 1024 / deltaTime` でrpmを求めます。初回・2.5秒以上の中断後は基準取得のみです。2.5秒無通知、または同一クランク値の通知が続く停止状態で0 rpmにします。220 rpm超や不正な差分は0として次回の基準を取り直します。
- 仮想速度は既存契約の `rpm * 4.2 * 60 / 1000`、上限45 km/h。基準センサーとの照合前なので `IsUnvalidatedMeasurement` は true、未測定・期限切れの confidence は0です。

## ビルド構成

- `.androidlib` とその Android 専用 PluginImporter メタデータが Unity によるライブラリ取り込みを指定します。ルート `AndroidManifest.xml` はライブラリ内の `sourceSets` に指定し、アプリ側へ権限をマージします。既存のManifest/Gradleテンプレートは書き換えません。
- Unity の `unity.compileSdkVersion` / `unity.targetSdkVersion` / `unity.minSdkVersion` / `unity.buildToolsVersion` を使用します。compile SDK >=33、target SDK >=31、min SDK >=23が必要です。アプリの既存SDK値は下げません。Javaソース互換性は8です。
- `consumer-rules.pro` がJNI経由のクラス名とメソッドをR8削除・改名から保護します。C#のAndroid呼び出しは `UNITY_ANDROID && !UNITY_EDITOR` に限定しています。
- QuestのXR/ARM64設定、ビルドターゲット、UIの配線は統合側の担当です。BLEプラグイン単体では既存シーンやWindows入力を切り替えません。

## 検証

この追加時に確認した内容:

- Unity 6000.5.7f1 の実際のマネージド参照DLLを使った、Android条件とWindows/Editor条件のC#コンパイル。
- AOSPのAndroid API 35公開 `android.jar` に対する全Java本体のコンパイル（Java 8互換、警告をエラー扱い）。
- JVMで `CscCadenceTest` の29項目が成功。uint16同時周回、ホイール付きパケット、正確な2.5秒境界、重複通知による停止、長時間停止・再開、カウンタリセット、不正パケットを確認。
- ManifestのXML構文、権限、JNI公開メソッド、担当ファイルの空白チェック。

Android Build Support / SDK / Gradleを含むUnityのAndroidツールチェーンがこの環境にないため、UnityのAndroid Gradleビルド・APK作成・マージ済みManifest・R8/IL2CPP結果は未確認です。Java API検証用の参照jarだけをプロジェクト外へ取得しており、SDKはインストールしていません。Quest/BK9C実機の検索、権限拒否・再許可、notify/indicate、rpm精度、切断・復帰は未検証です。

JVMテストはリポジトリルートから、JDKの `javac` / `java` で実行できます（出力先はプロジェクト外を推奨）。

```powershell
$lib = 'Assets/Plugins/Android/VirtualRideBle.androidlib'
$outDir = 'C:/myproject/.tmp-android-ble-test'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
javac --release 8 '-Xlint:all,-options' -Werror -d $outDir "$lib/src/main/java/com/virtualride/ble/CscCadence.java" "$lib/src/test/java/com/virtualride/ble/CscCadenceTest.java"
java -cp $outDir com.virtualride.ble.CscCadenceTest
# Android API参照jarがある場合のJava全体のコンパイル:
javac --release 8 '-Xlint:all,-options' -Werror -cp '<Android SDK>/platforms/android-35/android.jar' -d $outDir "$lib/src/main/java/com/virtualride/ble/CscCadence.java" "$lib/src/main/java/com/virtualride/ble/BleCscClient.java"
```

参考: [Unity 6000.5 Android Library](https://docs.unity.com/en-us/engine/6000.5/manual/platform-specific/android/developing/plugins-for/plugin-types/aarplugins/library-plugin-create)、[Android Bluetooth権限](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)、[Bluetooth SIG CSC仕様](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/CSCS_v1.0/out/en/index-en.html)。
