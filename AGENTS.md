# Virtual Ride Unity project guide

## 入口

Virtual Ride Unity は、ルームバイクの漕ぐ速さに合わせて自動操舵の 3D 田園コースを走る実験用プロトタイプです。利用・起動手順は `README.md`、専用センサーの境界は `Docs/SENSOR_INTEGRATION.md` を参照してください。

作業開始時は、必要な範囲で次を確認します。

- `Docs/current-state.md`：Unity 版、入力方式、既知の実験上の注意点
- `Docs/architecture.md`：走行・入力・世界生成・UI の境界
- `Docs/decisions.md`：カメラ計測、プライバシー、センサー交換の判断
- `Docs/dev/logs/`：関係する最近の作業記録
- 既存の `Logs/`：Unity 実行ログであり、Codex 作業ログとは分ける

## 変更時の前提

- カメラ映像を保存・送信せず、差分から速度・回転数を PC 内で推定する。
- 走行側は `IRideInputSource` の契約だけを使い、カメラ・キーボード・将来の Bluetooth 固有処理を混ぜない。
- Unity 生成ワールドと自動操舵の体験を壊さず、入力境界への最小変更を優先する。
- 専用センサーの機種や通信方式を決める前に、特定メーカーへ依存するコードを追加しない。

## 検証と記録

- Unity 6000.5.7f1 で `Assets/Scenes/VirtualRide.unity` を開き、キーボード入力、カメラ入力、ヘルプ、リセットを確認する。
- Windows ビルドを変更した場合は `Tools > Virtual Ride > Build Windows` と smoke QA を確認する。
- 変更後は `git diff --check` を実行する。
- 意味のある変更では `Docs/current-state.md` と `Docs/dev/logs/` のログを更新する。
