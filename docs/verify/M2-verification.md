# M2 検証手順(実機ビジュアル + バッチ)

M2の成果(GlyphAudit・診断ウィンドウ)の検証手順。バッチ部分はM1手順
(`M1-verification.md`)と同じコマンドで、追加テスト
(`GlyphAuditTests` / `DiagnosticsTests`)がグリーンになることを確認する。

## バッチ(Windowsサンドボックス)

- `PackageShippedSources_PassGlyphAudit` が通ること
  (異体字セレクタは多くのエディタ上で不可視のため、ソースへの混入は
  目視レビューをすり抜ける。このテストがその再発ガード)
- `BuildReport_NeverThrows_AndContainsSections` のログに出る診断レポート
  全文を確認し、`mono   : editor:Fonts/RobotoMono/...` となっていること

## 実機ビジュアル(起動中のエディタ)

1. 手元のエディタ自動化ツール等で `FontFixDiagnosticsWindow.Open()` 相当を
   呼ぶ(パッケージ導入済みならメニュー
   `Window/UITK Font Fix/Diagnostics` を直接開いてもよい)
2. ウィンドウ名 "Font Fix Diagnostics" をスクリーンショット撮影
3. 確認観点:
   - 日本語環境でウィンドウ全体がまだら太字になっていないこと
     (ルートへの ApplyCjkUi が効いている)
   - レポート本文がモノスペースで表示されること(ApplyMono)
   - `cjk-ui : osasset:Yu Gothic UI` 等、期待の解決結果
   - atlas pages の値(DynamicOSでの複数ページは正常。mark罠の注記が
     表示されること)
   - Re-probe / Copy report ボタンが機能すること
4. **開いたウィンドウは必ず閉じる**

注意: 検証対象のエディタに対しては読み取り系の操作に留め、同居する他の
ローカルプロジェクトのファイルには書き込まないこと(自動化ツールでの
フルシンク/再起動系の操作はエディタを長時間ブロックしうるため避ける)。
