# Architecture

## 概要

Unity 6000.5.7f1 で、起動時に道路・草地・湖・木々・村・山・自転車視点を生成するプロトタイプです。外部アセットを待たず、`VirtualRide.unity` から開始します。

## 主な構成

- `Core/`：`VirtualRideApp`、走行セッション、速度・距離・時間、周回制御
- `Input/`：`IRideInputSource`、キーボード入力、カメラ差分によるケイデンス入力、Windows BLE CSC入力
- `World/`：周回経路と景観の生成
- `UI/`：速度、回転数、信頼度、計測状態、ヘルプの表示
- `Editor/`：Windows ビルドメニュー
- `Resources/`：景観シェーダーなど起動時に使う資産

## データの流れ

1. `VirtualRideApp` が入力ソースを選び、`IRideInputSource.Current` から速度・回転数・信頼度・状態を受け取る。
2. `RideController` / `RideSession` が入力を移動速度、視野角、ハンドル揺れ、走行値へ変換する。
3. `ScenicWorldBuilder` と周回経路が生成した世界を自転車視点で表示する。
4. カメラ入力はペダル周辺の周期運動をメモリ上で処理し、映像ファイルを作らない。Windows BLE入力は補助プロセスからCSCケイデンスを受け取る。

## 交換境界

専用センサーは `IRideInputSource` を実装し、`VirtualRideApp.AttachExternalInput(...)` に渡す。センサー固有の通信・値の変換は入力クラスに閉じ込め、走行コースと HUD は変更しない。
