# Codex context baseline

日付: 2026-08-20

## 目的

Unity シーン、入力境界、カメラのプライバシー、専用センサー拡張の前提を次回の Codex 作業へ引き継ぐ。

## 確認したこと

- `README.md`、`Docs/SENSOR_INTEGRATION.md`、`Packages/manifest.json`、`ProjectSettings/ProjectVersion.txt`、`Assets/`、`QA/` を確認した。
- `IRideInputSource` が走行側と入力側の主要な境界である。
- 作業前の作業ツリーにコード変更はなかった。

## 変更

- ルート `AGENTS.md` と既存 `Docs/` 配下の引き継ぎ文書を追加した。
- Unity シーン、C#、ProjectSettings、QA 成果物は変更していない。

## 検証

- Git の所有者設定を永続変更せず `git pull --ff-only` が最新状態であることを確認した。
- `git diff --check`
- Unity 実環境の再生確認は未実施。
