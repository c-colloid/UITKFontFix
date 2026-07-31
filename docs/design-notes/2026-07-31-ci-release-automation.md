# 設計ノート: CI自動検証+自動リリース(タグ付け)

- 日付: 2026-07-31 / ステータス: 採用済み
- 発端: セッションのGitHubプロキシがタグ書き込みを遮断しており(git push 403 /
  REST API書き込み禁止)、リリースタグ付与に手作業が必要だった。ユーザー提案:
  「CIで自動検証/リリースすれば手作業なしでタグ付けできるのではないか」

## 選択肢

| 案 | 内容 | 判定 | 根拠 |
|---|---|---|---|
| A | タグ作成のみのworkflow(検証なし) | 棄却 | 「コミット/リリースは検証グリーン時のみ」(CLAUDE.md 2項)に反する。未検証のmainがそのまま配布タグになる |
| B | GameCIで検証+テスト成功を条件にversion変更を自動タグ化 | **採用** | 検証ゲートとリリースが機械的に連動。タグ作成はActionsの`GITHUB_TOKEN`(contents: write)で行うためプロキシ制約の影響を受けない |
| C | 手動trigger(workflow_dispatch)のみのリリース | 併用 | 自動判定に加えて手動再実行の口も残す(Bのworkflowにdispatchを追加) |

## 構成

- `.github/workflows/ci.yml`
  - `test` ジョブ: `game-ci/unity-test-runner@v4` / editor 2022.3.22f1 /
    EditMode。対象は `ci/TestProject`(リポジトリ内の最小プロジェクト。
    `Packages/manifest.json` がパッケージを相対 `file:../../jp.colloid.uitk-font-fix`
    参照+`testables` 登録。ローカルLinuxサンドボックスと同じ構成)
  - `release` ジョブ: mainへのpush時のみ。`package.json` の version に対応する
    `v{version}` タグが未存在なら、`test` 成功を前提に `gh release create` で
    タグ+GitHub Releaseを作成(認証は `GITHUB_TOKEN` のみ)
- ライセンス: 検証専用Unityアカウントの認証情報をリポジトリシークレット
  `UNITY_EMAIL` / `UNITY_PASSWORD` に登録(GameCIのPersonalライセンス
  アクティベーション方式。本コンテナで同方式の動作実績あり)。
  認証情報そのものはリポジトリに置かない

## 制約・注意

- シークレット登録(2件)だけは初回にユーザーのGitHub設定操作が必要。
  以後は `package.json` のversionをbumpしてmainへ反映するだけで
  「検証→タグ→リリース」が全自動
- シークレット未登録の間は `test` が失敗し、`release` は実行されない
  (未検証リリースを作らない安全側の既定)
- フォークからのPRにはシークレットが渡らないため `test` は失敗する
  (現状コントリビュータ不在のため許容。必要になったら
  `pull_request_target` 等を別途設計する)
- `ci/TestProject` はコンテナのLinux Unityで実コンパイル+テストを通してから
  コミットする(CIと同一構成の事前検証)

## 追記(2026-07-31): GameCIアクションを廃し、Licensing Client直叩きに変更

初版CI(run #1)は `game-ci/unity-test-runner@v4` を採用したが、実行ログで
即時失敗を確認:

```
Missing Unity License File and no Serial was found.
```

- 根本原因: 同アクションの事前検証は **UNITY_LICENSE(ULF)または UNITY_SERIAL
  が必須**で、メール+パスワードのみの認証活性化に非対応。PersonalのULF新規取得は
  廃止済み・他マシンULFはバインディング不一致(本コンテナで実証)のため、
  Personal+シークレット2件という前提とアクションの要求が両立しない
- 対応: `unityci/editor:ubuntu-2022.3.22f1-base-3` コンテナ上の自前ステップに変更。
  Licensing Client `--activate-all --include-personal`(認証)→ `-runTests`
  (EditMode)→ `--deactivate-all`(シート返却、`if: always()`)。
  活性化・返却・再取得のサイクルは本コンテナで動作検証済み
- テスト失敗はUnityの終了コード(0=成功)でステップが自然に失敗する。
  results.xml/ログはartifactとして常時アップロード
- 残る未検証点: GitHub Actionsランナー上でのエンドツーエンド実行のみ
  (シークレット登録後の初回runで確定する)
