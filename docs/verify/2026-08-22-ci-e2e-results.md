# CI エンドツーエンド検証結果(2026-08-22)

`.github/workflows/ci.yml`(Licensing Client直叩き構成)のGitHub Actions
ランナー上での実測記録。設計ノート
`2026-07-31-ci-release-automation.md` の「残る未検証点」を解消。

## 結果

- Run #2(`2bbaf6f`, attempt 2, シークレット登録後の再実行): **success**
- Run #3(`fba9001`, mainへのpush起点): **success**(所要 約2分)
  - unityci/editor イメージ上での認証アクティベーション
    (`--activate-all --include-personal`)→ EditModeテスト → シート返却
    (`--deactivate-all`)まで全ステップ動作
- release ジョブ: `v0.1.0` タグ既存のため**スキップ経路を正常動作**
  (タグ/リリース自体は 2026-07-31 にユーザーがWeb UIから作成済み。
  対象コミット `1516eb6` = パッケージ0.1.0の内容として正しい)

## 未走行の経路

- release ジョブの**タグ新規作成経路**は、次回のバージョンbump
  (package.json の version 変更 → main反映)で初めて実走する。
  ロジックは `gh release create` 1コマンドで、権限(contents: write)と
  スキップ判定は今回実証済み

## 次回リリースの手順(全自動)

1. `jp.colloid.uitk-font-fix/package.json` の `version` を上げる
2. `CHANGELOG.md` に項を追加
3. mainへ反映 → CIがテスト→タグ`v{version}`→Release作成まで自動実行
