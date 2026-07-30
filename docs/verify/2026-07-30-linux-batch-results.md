# Linux バッチ検証結果(2026-07-30)

クラウドLinuxコンテナ上に構築した検証環境での実測記録。
Windows実機(AITemp)検証の**代替ではなく前段のコンパイル/回帰ゲート**。

## 環境

- Ubuntu 24.04 コンテナ(4コア/15GB RAM)
- Unity 2022.3.22f1 Linux Editor(download.unity3d.com から取得、`/opt/unity`)
- ライセンス: 検証専用Unityアカウント+Licensing Client
  `--activate-all --include-personal` でPersonalシートをアクティベーション
  (旧 .alf/.ulf 手動経路はPersonal廃止済み。他マシンの .ulf は
  "Machine bindings don't match" で不可 — 実測)
- サンドボックス: `/opt/FontFixSandbox`(パッケージはローカルパス参照+testables。
  com.unity.test-framework 1.1.33 はエディタ同梱キャッシュから解決 —
  packages.unity3d.com への疎通は不要だった)

## 結果

- コンパイル: **エラー0**(Runtime/Editor/Tests 全アセンブリ)
- EditModeテスト: **total=53 / passed=50 / failed=0 / skipped=3**
- スキップ3件は全て設計どおりの環境ゲート:
  1. `CjkUi_InstalledCandidateNames_ContainYuGothicUi_OnWindows`(Windows前提)
  2. `CjkUiFontAsset_ResolvesOnWindows_AndReportsWinner`(Windows前提)
  3. `BothInlineOnSameElement_LastWriteWins`(CJK+mono両解決が前提。
     本コンテナはCJKフォント未導入のため正しくスキップ)

## 実測ログ(抜粋)

```
[FontFixResolveTests] mono source = editor:Fonts/RobotoMono/RobotoMono-Regular.ttf, font = RobotoMono-Regular
```

- GT#5(RobotoMono同梱パス)は**LinuxエディタでもTier1で解決**することを確認
- 診断レポートも設計どおり動作: mono解決結果、CJK "(none)" 表示、
  候補インストール状況([x] DejaVu Sans Mono を正検出)、既知の罠一覧

## 残作業(このゲートでは検証不能なもの)

- Windows実機(AITemp 2022.3.22f1)でのフルラン:
  CJK解決(Yu Gothic UI)・DynamicOS FontAsset生成・スキップ3件の解消
  → `M1-verification.md` / `M2-verification.md` の手順
- 実機ビジュアル(uloop): 診断ウィンドウのまだら太字なし/モノスペース表示
