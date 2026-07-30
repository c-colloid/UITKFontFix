# M1 検証手順(サンドボックス: AITemp / Unity 2022.3.22f1)

このセッション(クラウドLinuxコンテナ)からはUnityを実行できないため、
M1のバッチ検証は以下の手順でWindows環境から実施する。

## 前提

- サンドボックス: `C:\Unity\UnityProjects\AITemp`(2022.3.22f1、既存)
- 既存の `jp.colloid.unity-agent-panel` ジャンクションと共存させる(触らない)
- 同一プロジェクトへの同時2インスタンス起動禁止

## 手順

1. リポジトリを `C:\Users\colloid\Dev\UitkFontKit` 等にクローンし、
   ブランチ `claude/uitk-font-kit-dev-t0o23n` をチェックアウト
2. ジャンクション作成(管理者不要):

   ```
   mklink /J C:\Unity\UnityProjects\AITemp\Packages\jp.colloid.uitk-font-kit ^
       <repo>\jp.colloid.uitk-font-kit
   ```

3. コンパイル確認(同期待機・タイムアウト20分・失敗時はそのPID限定で `taskkill /T /F`):

   ```
   & "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" `
       -batchmode -quit -projectPath C:\Unity\UnityProjects\AITemp `
       -logFile C:\Unity\UnityProjects\AITemp\Logs\uitkfontkit-compile.log
   ```

   ログに `CompilerOutput` エラーが無いこと。

4. EditModeテスト(`-quit` は付けない):

   ```
   & "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" `
       -batchmode -projectPath C:\Unity\UnityProjects\AITemp `
       -runTests -testPlatform EditMode `
       -testResults C:\Unity\UnityProjects\AITemp\Logs\uitkfontkit-results.xml `
       -logFile C:\Unity\UnityProjects\AITemp\Logs\uitkfontkit-tests.log
   ```

5. 結果XMLで確認する期待値:
   - `Colloid.UitkFontKit.Tests` 配下: 失敗0
   - Skipped が許されるのは以下のみ(理由もXMLに記録される):
     - `CjkUiFontAsset_ResolvesOnWindows_AndReportsWinner`
       (ヘッドレスでDynamicOS生成が不可な環境のみ)
     - `BothInlineOnSameElement_LastWriteWins`(CJK/monoどちらかが未解決の環境のみ)
     - Windows以外での `*_OnWindows` 系
   - ログ内の実測記録: `[FontKitResolveTests] mono source = editor:Fonts/RobotoMono/RobotoMono-Regular.ttf`
     となること(2022.3の期待値)
   - UAP側のテスト(同居)がM1導入により壊れていないこと

## 完了条件

上記すべてグリーンでM1完了。結果XMLとログの要約を
`docs/verify/M1-results.md` として記録する。
