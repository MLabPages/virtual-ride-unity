# 実験データ記録

## 結論

画面の「実験記録」から匿名IDと条件名を指定すると、走行中の数値を10HzのCSV、イベント用サイドカーCSV、終了時の要約JSONへ保存します。カメラ映像や画像は保存しません。記録を始めると入力方式（カメラ／キーボード／外部センサー）は固定され、試行時間を指定した場合はその時点で自動保存します。

## 保存先

OSが書類フォルダ（Documents）を提供する場合は、次のフォルダです。Windowsの既存パスは変わりません。

```text
Windows:  ドキュメント\VirtualRideResearchData
macOS:    ~/Documents/VirtualRideResearchData
Linux:    Documents/VirtualRideResearchData（無い場合はホーム直下）
```

書類フォルダが空、または書き込めない場合は、Unityの `persistentDataPath/VirtualRideResearchData` へ保存します。実際の場所は実験記録画面に表示されます。

1回の記録につき、次の3ファイルを作ります。

```text
P001_baseline_20260829_143000_a1b2c3d4.csv
P001_baseline_20260829_143000_a1b2c3d4_events.csv
P001_baseline_20260829_143000_a1b2c3d4_summary.json
```

同じID・条件で繰り返しても、セッションIDが異なるため上書きしません。

## 実験セッションの進め方

1. 使う入力（キーボードまたはカメラ）を先に選び、カメラの場合は片足／両足と感度を合わせておきます。
2. 「実験記録」を開き、匿名の参加者IDと条件名を入れます。
3. 必要なら試行時間を秒で入れます。空欄または `0` は手動終了です。
4. 「記録を開始」を押します。距離・時間が0に戻り、入力方式が固定されます。
5. 記録中に指示・休息・メモのイベントを残せます（画面ボタン、または F8 / F9）。
6. 制限時間に達するか、「記録を終了して保存」でファイルが閉じます。

記録中に C / K、画面下の入力切替、走行値リセット、カメラの片足／両足・感度変更を行うと、切り替わらずに画面へ理由が表示されます。

## 入力上のルール

- `participant_id` には氏名、メールアドレス、学生番号など直接個人を特定できる値を入れず、研究側で管理する匿名IDを使います。メモ欄にも個人情報を書かないでください。
- IDと条件名に保存される文字は、英数字、日本語、`-`、`_` の最大40文字です。
- 記録開始時に、画面上の距離・時間・平均速度・最高速度を0へ戻します。
- 一時停止中も行を記録し、`is_paused=true` として区別します。
- カメラ由来の回転数・速度は未検証の推定値です。要約JSONの `measurementValidated` は常に `false` です。

## CSV列

現在の `schema_version` は `2` です。版が違うファイルは無条件に結合しないでください。

| 列 | 内容 |
|---|---|
| `schema_version` | 記録形式の版。現在は `2` |
| `session_id` | 開始日時とランダム値から作るセッション識別子 |
| `participant_id` | 画面で指定した匿名ID |
| `condition` | 実験条件 |
| `recorded_at_utc` | 各行のUTC日時（ISO 8601） |
| `elapsed_seconds` | 記録開始からの秒数 |
| `input_mode` | 開始時に固定された入力方式 |
| `input_state` | 入力の準備・探索・検出・エラー状態 |
| `input_status` | 画面にも出る入力状態の説明 |
| `input_speed_kph` | 入力が推定・取得した速度 |
| `display_speed_kph` | 仮想空間の移動に実際に使った平滑化後の速度 |
| `cadence_rpm` | ペダル回転数。取得できない場合は空欄 |
| `confidence` | 入力値の信頼度（0〜1） |
| `distance_metres` | セッション内の走行距離 |
| `moving_seconds` | 0.8km/h以上だった累積時間 |
| `route_distance_metres` | 周回ルート上の位置 |
| `route_progress` | 1周内の進捗（0〜1） |
| `area_name` | 仮想空間内のエリア名 |
| `is_paused` | 一時停止中かどうか |
| `event_marker` | 直前のサンプル以降に打ったイベント。複数は `\|` 区切り。無い行は空 |

## イベントCSV

実験者が打ったマーカーの正本です。サンプル間隔に依存せず、押した時点の経過秒で残します。

| 列 | 内容 |
|---|---|
| `schema_version` | 記録形式の版。現在は `2` |
| `session_id` | 対応するセッション |
| `participant_id` | 匿名ID |
| `condition` | 実験条件 |
| `recorded_at_utc` | UTC日時 |
| `elapsed_seconds` | 記録開始からの秒 |
| `marker_type` | `instruction`（指示）、`rest`（休息）、`note`（メモ） |
| `note` | 任意の短いメモ。指示・休息では空のことが多い |

イベントが1件も無くても、ヘッダだけの `_events.csv` を作ります。

## 要約JSON

`schemaVersion` は `2` です。主な項目は次のとおりです。

- 開始・終了日時、終了理由（`completed`、`trial_duration_elapsed`、`application_closed`）
- 試行時間 `trialDurationSeconds`（手動終了なら `null`）
- 行数、経過時間、移動時間、距離、平均・最高速度
- `startingInputMode` / `finalInputMode`（記録中は入力固定のため通常は一致）
- `inputLocked`: 常に `true`（この版のセッションは開始時に入力を固定する）
- `applicationVersion`、`unityVersion`、`productName`、`companyName`、`platform`
- `camera.bothLegsVisible`、`camera.sensitivity`、`camera.metersPerRevolution`（開始時のカメラ設定）
- `measurementValidated`: 常に `false`（カメラ推定は未検証）
- `cameraFramesSaved`: 常に `false`
- 対応するCSV / イベントファイル名、`eventCount`、`events` 配列

## 研究利用前に必要な確認

- カメラ推定の回転数・速度を、基準となるケイデンスセンサー等と同時計測して妥当性を確認する。確認できるまで `measurementValidated` を真とみなさない。
- 欠測、低信頼度、一時停止、イベント区間を分析でどう扱うか事前に決める。
- 条件名、試行時間、除外基準、匿名ID対応表の管理方法を研究計画に合わせて固定する。
- CSVとJSONの `schema_version` / `schemaVersion` を分析時に確認し、異なる版を無条件に結合しない。
