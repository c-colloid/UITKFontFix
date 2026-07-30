# M2 検証手順(実機ビジュアル + バッチ)

M2の成果(GlyphAudit・診断ウィンドウ)の検証手順。バッチ部分はM1手順
(`M1-verification.md`)と同じコマンドで、追加テスト
(`GlyphAuditTests` / `DiagnosticsTests`)がグリーンになることを確認する。

## バッチ(AITemp)

- `PackageShippedSources_PassGlyphAudit` が通ること
  (開発中に GlyphAudit.cs 自身のdocコメントへ生のU+FE0Fが混入する事故が
  実際に起きており、このテストがその再発ガード)
- `BuildReport_NeverThrows_AndContainsSections` のログに出る診断レポート
  全文を確認し、`mono   : editor:Fonts/RobotoMono/...` となっていること

## 実機ビジュアル(ユーザーの起動中エディタ / uloop)

1. `uloop execute-dynamic-code --code-file <cs>` で
   `FontKitDiagnosticsWindow.Open()` 相当を呼ぶ(パッケージ導入済みなら
   メニュー `Window/UITK Font Kit/Diagnostics` を直接開いてもよい)
2. `uloop screenshot --window-name "Font Kit Diagnostics" --match-mode contains --output-directory <dir>`
3. 確認観点:
   - 日本語環境でウィンドウ全体がまだら太字になっていないこと
     (ルートへの ApplyCjkUi が効いている)
   - レポート本文がモノスペースで表示されること(ApplyMono)
   - `cjk-ui : osasset:Yu Gothic UI` 等、期待の解決結果
   - atlas pages の値(DynamicOSでの複数ページは正常。mark罠の注記が
     表示されること)
   - Re-probe / Copy report ボタンが機能すること
4. **プローブウィンドウ/診断ウィンドウは必ず閉じる**

禁止事項(過去にエディタを長時間フリーズさせた実績): `uloop update` /
`uloop sync` / `uloop launch`。DevelopmentProjectのファイルへの書き込みも禁止。
