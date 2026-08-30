# プレイモード・ライフサイクル修正(0.4.0)の検証チェックリスト

対象: `docs/design-notes/2026-08-30-playmode-fontasset-lifecycle.md` の実装。
静的ゲート(ASCII監査・meta整合・JSON検証・一次ソースでのAPI確認)はクラウド側で
実施済み。以下は **Unity 実機/CI でしか確認できない項目**。

## A. CI(compile + EditMode)

- 手順: GitHub の Actions タブ → CI → Run workflow → ブランチ
  `claude/playmode-missing-reference-exception-ngmi6z` を選んで実行
  (エージェントからの `workflow_dispatch` は 403: Actions 書き込み権限なし)。
  release ジョブは `github.ref == 'refs/heads/main'` で守られているため、
  ブランチ実行ではタグもリリースも作られない。
- 期待: 失敗0。新規 `FontAssetLifecycleTests` は Linux コンテナに DejaVu /
  Liberation があるため実走する(直近 main 実測: total=115 / passed=112 /
  failed=0 / skipped=3。スキップはWindows限定2件+CJK/mono両解決が要る1件)。
  0.4.0 では 15 件前後増える見込み。
- 記録先: 結果(total/passed/failed/skipped と run URL)を本ファイルに追記する。

## B. インタラクティブ・エディタ(実機でのみ確認可能)

1. **再現と修正の確認(本丸)**
   - CJK フォントが解決する環境で、`ApplyCjkUi` したエディタウィンドウを開く
   - プレイモードに入る → 抜ける → ウィンドウの日本語表示を確認
   - 期待: `MissingReferenceException` が出ず、日本語が崩れない
   - Enter Play Mode Options の4通り(Reload Domain × Reload Scene の
     on/off)それぞれで確認する。設計ノート §1.6-2/3 の未実証点はここで解消する
2. **どの構成で元々壊れていたか**(修正前の挙動確認。0.3.0 に戻して再現を取る)
   - 既定構成(Domain/Scene 両方 reload)で壊れるのは「終了時」か「開始時」か
   - Reload Domain 無効時に開始時から壊れるか
3. **その場修復後の再描画**
   - 壊れた状態を作ったあと(または修復後)、UIR が古いテクスチャを描画コマンドに
     抱えていないか。文字が空白/化けにならないか
   - 修復時に `Unable to load font face for [...]` 警告が出ないか
4. **多ページ・アトラス**
   - 大量の異なる漢字を表示させてアトラスページを2枚以上作らせてから
     プレイモード往復 → 遅延生成ページも保護されているか
     (診断ウィンドウの "atlas pages" と各ページの hideFlags 表示で確認)
5. **設定変更の再適用**
   - 診断ウィンドウを開いた状態で "Re-probe" / "Apply (this session)" /
     "Use package defaults" を押す → ウィンドウ自身のフォントが壊れず、
     日本語表示が維持されること(`CachesInvalidated` の購読が効いている)
   - Project Settings > UITK Font Fix でも同様。ペインを閉じて開き直しても
     多重購読にならないこと
6. **OS モノスペースフォント経路**(設計ノート §8 の残課題)
   - `FontFixSettings.EditorMonoFontPaths = new string[0]` で Tier1 を無効化し、
     OS mono フォントが選ばれる状態にする(診断で `mono : os:<name>` を確認)
   - その状態でプレイモード往復 → `ApplyMono` した要素が壊れないか
   - 壊れる場合、クラシック `Font` の material にも同じ保護が要る(要追加設計)

## 記録

(未実施。実行後にここへ結果を追記する)
