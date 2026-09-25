# BLE一覧が空になる問題（2026-09-25）

WindowsとQuestの両方でBK9Cが一覧に出ないとの実機報告を受けた。Windows版では `VirtualRideBleBridge.Write` がプロトコルのタブ区切りを空白に置換しており、Unity側の `Split('\t')` が `DEVICE` 等を認識できなかった。区切りを保持し、外部から届く機器名・例外文だけを個別に無害化して補助exeを再生成した。

修正後のWindows補助exeを単独で実行し、13秒の検索で `STATE` 1行、`DEVICE` 154行をタブ区切りで受信した。これは周辺BLE広告の受信とプロトコル形式の検証であり、BK9Cの検出・接続・rpmの検証ではない。Windows・QuestのビルドとWindowsスモークテスト（`QA/ble-discovery-smoke/result.json`: `passed: true`）は成功した。

一覧は従来上位5件だけを表示していた。BK9Cなど既知のケイデンス名を優先し、4件ずつページ切替で全件に到達できるようにした。[BK9C日本語説明書](https://cdn.shopify.com/s/files/1/0636/7749/7600/files/COOSPO_BK9C_JP.pdf?v=1695455428)にある絶縁シート、回転による起動と青点滅の確認を画面と手順書に追加した。

QuestのAndroid BLE実装は別経路。Androidプラグインはフィルタなしでスキャンするが、Quest実機は今回ADB未接続でログを取れず、原因は確定していない。端末のNearby devices権限、センサーの起動、他アプリとの接続状況、Quest画面の状態文を確認する必要がある。
