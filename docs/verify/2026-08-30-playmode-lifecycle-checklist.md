# プレイモード・ライフサイクル修正(0.4.0)の検証チェックリスト

対象: `docs/design-notes/2026-08-30-playmode-fontasset-lifecycle.md` の実装。
静的ゲート(ASCII監査・meta整合・JSON検証・一次ソースでのAPI確認)はクラウド側で
実施済み。以下は **Unity 実機/CI でしか確認できない項目**。

## A. CI(compile + EditMode)= **合格**

- 実行: ユーザーによる `workflow_dispatch`
  ([run 33338463574](https://github.com/c-colloid/UITKFontFix/actions/runs/33338463574)、
  対象コミット `c12a82b` = 実装コミット、2026-08-30)。
  release ジョブは想定どおり skipped(`github.ref == 'refs/heads/main'` ガード)。
- 結果: **`total="131" passed="128" failed="0" inconclusive="0" skipped="3"`**
  - 直前の main 実測は total=115 / passed=112 / skipped=3
    ([run 33299071172](https://github.com/c-colloid/UITKFontFix/actions/runs/33299071172))
  - **+16 件がすべて追加分で、すべて実走して合格**。新規
    `FontAssetLifecycleTests` は16件ちょうどで、`Assert.Ignore` は0件
    → Linux コンテナ(CJKフォント無し)でも DejaVu / Liberation に束縛でき、
    フラグ付与・破壊シミュレーション・**その場修復**・イベント契約が
    実オブジェクトに対して検証されたことになる
  - skipped は据え置き3件(Windows限定2件+CJK/mono両解決が要る1件)で、
    今回の変更による新規スキップは無い
- これで CLAUDE.md §2 のゲート(コンパイル+EditMode 全グリーン)は満たした。
  ただし下の B は依然として未実施。

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
