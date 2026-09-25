# Quest HUD文字配置の調整（2026-09-25）

Quest実機のスクリーンショットで、下部の状態案内と操作列、ヘルプの説明、BLEボタン付近が窮屈だった。`RideHud` の速度表示下の行、カメラ案内、実験設定の注記、ヘルプの行を整理した。下部操作列はQuest向けに文字サイズとボタン余白を調整し、状態文と警告文の高さは実際のフォントによる `GUIStyle.CalcHeight` から決める。ヘルプなどを開いたときは背後のHUDを描かず、説明文の下に状態案内が透けて重なるのを防ぐ。

PC中継の操作手順もREADMEに明記した。PC側は `Builds/Windows/VirtualRide.exe` を使い、Quest側は `Builds/Quest/VirtualRide.apk` をインストールして起動する。中継は数値のみで、映像は送らない。

検証: Unity 6000.5.7f1 でWindows版とQuest版のビルドが成功。Windowsスモークテスト `QA/quest-ui-fit-final/result.json` は `passed: true`。同テストの `help.png` を目視し、「すぐ試す」3行目と次の見出しの重なりがないことを確認した。Quest実機での最終表示と中継接続は、修正版APKのインストール後に確認が必要。
