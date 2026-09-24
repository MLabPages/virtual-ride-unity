# 2026-09-24 Meta Quest 3 単体版

## 実機表示の修正

- Quest 3で起動したところ背景とHUDのパネル形状は表示されたが、文字が全て欠けて操作できなかった。HUDがWindowsのOSフォント名（Yu Gothic / Meiryo / Arial）に依存していたため、Android向けにはNoto Sans CJK JP RegularをResourcesから読み込むよう変更した。
- フォントは [Noto CJK](https://github.com/notofonts/noto-cjk) の静的OTFを同梱し、同梱の `OFL.txt` にSIL Open Font License 1.1を保存した。
- Quest実機での修正版APKの文字表示と操作は再確認待ち。

## 手追跡とカメラ接続

- Touchコントローラーでポインターが上へずれる報告を受け、握り位置のポーズからOpenXRのAimポーズへ変更した。
- XR Hands 1.7.2 のHand Tracking SubsystemとMeta Hand Tracking AimをQuest向けに有効化し、左右いずれかの手で指差し、親指と人差し指のピンチでHUDボタンを選べるようにした。コントローラーのトリガーも残した。いずれも再ビルド後の実機確認が必要。
- PC内蔵カメラはQuestのカメラ一覧には出ない。Windows版で「カメラ計測」を選んでから「Questへ送信: ON」にし、Quest版は「PC中継」を選ぶ。両機を同じネットワーク（iPhoneテザリング可）につなぐ。Questの「Quest USB」はQuest本体へ接続したカメラ向けで、認識は実機未検証。
- 手追跡版のQuest APKビルド成功。APK内に `com.oculus.permission.HAND_TRACKING` と任意機能 `oculus.software.handtracking` を確認。Windowsビルド成功、`QA/quest-hands-smoke/result.json` は `passed: true`。Quest実機での手・コントローラー操作とPC中継は未確認。

## 実装

- Windows版はそのまま残し、Unityメニューに `Tools > Virtual Ride > Build Quest Android APK` と `Configure Quest Android XR` を追加した。出力は `Builds/Quest/VirtualRide.apk`（Windowsは従来どおり `Builds/Windows`）。
- Android設定: IL2CPP / ARM64 / Vulkan / minSdk 32・targetSdk 34 / OpenXR（Meta Quest Support, Quest 3, Oculus Touch）。XRローダーはAndroidだけに割り当て、Windows版はXRを使わない。パッケージは OpenXR 1.17.1、XR Management 4.6.1、Input System 1.20.0。入力方式は「両方」にして既存のキーボード操作を維持。
- 視点: `QuestHeadPoseTracking` がヘッドセットの向き・位置を、起動時の頭位置を基準にした相対値で自転車視点カメラへ反映する。Questでは人工的な揺れ・傾き・視野角変化を無効にする。
- 画面: 既存HUD（OnGUI）はヘッドセットに表示されないため、Questでは1600×900のテクスチャへ描き、自転車の前1.5 mに浮かぶパネル（幅1.9 m、頭ではなく車体に固定）として表示する。コントローラーの光線とトリガーでボタンを押す。文字欄はQuestのシステムキーボードを開く。
- 空のシェーダーを両目描画（シングルパス・マルチビュー）に対応させた。未対応だと片目にしか空が出ない。
- BK9C: Android用BLEプラグイン（Java、`Assets/Plugins/Android/VirtualRideBle.androidlib`）と `AndroidBluetoothCadenceInput` を追加。CSC 1816 / 2A5B、クランク差分からrpm、2.5秒無通知で0 rpm、Android 12以降のBluetooth権限要求。Windows版と共通の `IBluetoothCadenceInput` でHUDと接続画面を共有する。アドレスは保存・記録しない。
- 外部カメラ: Quest 3のUSBカメラはUnityのカメラ一覧に出ない報告があるため、既定構成を「PC中継」にした。外部カメラをWindows PCにつなぎ、PC版の実験設定で「Questへ送信: ON」にすると、推定した速度・回転数・信頼度・状態だけを同じLANへUDPブロードキャスト（ポート47810、毎秒約30回）する。Quest版は下部の「PC中継」で受信して走る。映像・ID・アドレスは送らない。1秒途絶えると0 km/hに戻す。PC側のBLEセンサー値も同じ方法で中継できる。
- Questの実験記録は `Android/data/com.mlabpages.virtualride.quest/files/VirtualRideResearchData` に保存する（adbやMeta Quest Developer Hubで取り出す）。

## 検証範囲

- Windows向けにUnityバッチモードでコンパイル成功。
- Quest APK: Unityバッチモードでビルド成功（`Builds/Quest/VirtualRide.apk`、約25 MB）。OpenXR検証の警告なし（GameActivity、Prioritize Input Polling設定済み）。APKの権限にBluetooth・カメラ・INTERNET・Wi-Fiマルチキャストが入っていることを確認。
- 同じプロジェクトからWindowsビルドも成功し、Windows版スモークテストが合格（`QA/quest-port-smoke/result.json`）。
- Quest実機での表示（HUDパネル、両目の空、頭の追跡）、コントローラー操作、BK9C接続、PC中継の受信、Quest直結USBカメラは未確認。

## 実機で最初に確認すること

## 学内Wi-Fiが使えない場合（iPhoneテザリング）

- 学内Wi-Fiは端末同士の通信を遮断していることが多いため、PC中継はiPhoneのインターネット共有にQuestとPCの両方をつなぐ構成を基本にする。中継の数値はiPhone内のローカル通信で、モバイルデータはほとんど使わない。
- PC版は、接続中の各ネットワークのブロードキャストアドレス（例: 172.20.10.15）と 255.255.255.255 へ送る。ブロードキャストが届かないネットワーク用に、実験設定の「QuestのIP」欄へQuestのアドレスを入れると直接送信も行う。入力値はPCに保存される。
- Quest版は受信待ちの間、自分のIPアドレスを状態表示に出す。
- iPhone側の端末同士の通信・ブロードキャストの可否は実機で未確認。

1. APKをMeta Quest Developer Hubでインストールし、景色が両目で正しく見えるか、HUDパネルが読めるか。
2. コントローラーのトリガーでボタンが押せるか。
3. 「BLEセンサー」でBK9Cを検索・接続し、rpmが出るか。
4. PC版で「Questへ送信: ON」にし、Questの「PC中継」で速度が追従するか。Windowsファイアウォールで許可を求められたら「プライベートネットワーク」を許可する。
