# 対話確認チェックリスト: シーンロード時のアトラスページ破棄対策(0.4.1)

設計ノート: `docs/design-notes/2026-09-08-scene-load-atlas-page-loss.md`

Linux バッチ(EditMode)で確認済みの範囲はテスト結果の節に記す。ここに挙げるのは
実機の UI Toolkit パネルが要る項目(`resolvedStyle` はパネル内でしか計算されない
ため、再描画走査はヘッドレスで検証できない)。

## 手順(CJK フォントが解決する Windows 実機)

1. **再現の確認(修正前 0.4.0)**
   - `ApplyCjkUi` したウィンドウに、かな・漢字を 100 字以上含むテキストを表示
     (診断ウィンドウの "atlas pages" が 2 以上になることを確認)
   - File > New Scene → ウィンドウの表示が崩れ、Console に
     `NullReferenceException ... UIRStylePainter.DrawTextInfo` が出ること
   - 診断ウィンドウの Re-probe で復帰すること
2. **修正後(0.4.1)の予防**
   - 同じ状態を作ったあと、診断ウィンドウで全ページの hideFlags が `DontSave`
     になっていること(ページ追加から 1 ティック以内)
   - File > New Scene / 既存シーンを開く → 崩れず、例外が出ないこと
3. **修正後の回復(予防を迂回して壊す)**
   - 診断ウィンドウを開いた状態で、別のエディタスクリプトから
     `FontFix.CjkUiFontAsset.atlasTextures[1]` を `DestroyImmediate`
   - 0.25 秒以内に修復され(Console に例外が出ても 1 回まで)、当該ページの
     グリフを含むラベルが**触らなくても**描き直されること(再描画走査)
   - 修復後 `material` が別インスタンスになっていること(診断の material 名は
     同じでよい)
4. **ベイク失敗時**
   - ライトマップベイクを失敗させる(例: 対象なしで Generate Lighting)→ 崩れず、
     崩れた場合は Console のスタックトレースを設計ノート §6-1 に追記する
5. **プレイモード往復**(0.4.0 のチェックリスト項目 1, 3, 4 の再確認)
   - 修復経路が material 差し替えになったため、往復後に文字が空白/化けに
     ならないことを再確認する

## Linux バッチ(EditMode)の結果

セッション内で再構築した検証サンドボックス(Unity 2022.3.22f1 Linux、
`ci/TestProject`、検証専用アカウントで Personal シートを有効化 → 実行 → 返却)。

- 結果: **`total="136" passed="133" failed="0" inconclusive="0" skipped="3"`**
  (0.4.0 時点は total=131 / passed=128 / skipped=3。スキップ3件は従来どおりの
  Windows 前提ゲート)
- 新規5件はすべて実行され合格。うち2件は設計ノートの因果連鎖の実証を兼ねる:
  - `NewScene_KeepsStampedChildren_AndKillsUnflaggedObjects`:
    `EditorSceneManager.NewScene` が `HideFlags.None` の `Texture2D` を破棄し、
    `DontSave` を貼った material とページ 0 は生き残る(§1.2 の「シーンロードが
    フラグ無しを殺す」を EditMode から実証)
  - `LazyPages_AreBornUnflagged_AndTheGuardTickStampsThem`: `TryAddCharacters`
    で育てた 2 ページ目以降が `HideFlags.None` で生まれ、ガードティック 1 回で
    `DontSave` になる(§1.2 の「遅延ページは無防備」を実証)

## 対話確認の記録

(未実施。実行後にここへ結果を追記する)
