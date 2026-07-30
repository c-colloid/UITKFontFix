# M1 検証手順(Windowsローカルサンドボックス / Unity 2022.3.22f1)

クラウド環境からはWindows実機に触れないため、M1のバッチ検証は以下の手順で
ローカルのWindows環境から実施する。

## 前提

- 検証用サンドボックスプロジェクト(2022.3.22f1、以下 `<sandbox>` と表記)
- 同じサンドボックスに他のローカルパッケージがジャンクションされている場合は
  共存させ、そちらには触れない
- 同一プロジェクトへの同時2インスタンス起動禁止

## 手順

1. リポジトリをローカルにクローンし、対象ブランチをチェックアウト
2. ジャンクション作成(管理者不要):

   ```
   mklink /J <sandbox>\Packages\jp.colloid.uitk-font-fix ^
       <repo>\jp.colloid.uitk-font-fix
   ```

3. コンパイル確認(同期待機・タイムアウト20分・失敗時はそのPID限定で `taskkill /T /F`):

   ```
   & "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" `
       -batchmode -quit -projectPath <sandbox> `
       -logFile <sandbox>\Logs\uitkfontfix-compile.log
   ```

   ログに `CompilerOutput` エラーが無いこと。

4. EditModeテスト(`-quit` は付けない):

   ```
   & "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" `
       -batchmode -projectPath <sandbox> `
       -runTests -testPlatform EditMode `
       -testResults <sandbox>\Logs\uitkfontfix-results.xml `
       -logFile <sandbox>\Logs\uitkfontfix-tests.log
   ```

5. 結果XMLで確認する期待値:
   - `Colloid.UitkFontFix.Tests` 配下: 失敗0
   - Skipped が許されるのは以下のみ(理由もXMLに記録される):
     - `CjkUiFontAsset_ResolvesOnWindows_AndReportsWinner`
       (ヘッドレスでDynamicOS生成が不可な環境のみ)
     - `BothInlineOnSameElement_LastWriteWins`(CJK/monoどちらかが未解決の環境のみ)
     - Windows以外での `*_OnWindows` 系
   - ログ内の実測記録: `[FontFixResolveTests] mono source = editor:Fonts/RobotoMono/RobotoMono-Regular.ttf`
     となること(2022.3の期待値)
   - サンドボックスに同居する他パッケージのテストが壊れていないこと

## 完了条件

上記すべてグリーンでM1完了。結果XMLとログの要約を
`docs/verify/M1-results.md` として記録する。
