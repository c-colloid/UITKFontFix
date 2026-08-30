# 設計ノート: キット生成アセットの命名+CJKマルチフェイス対応(v0.2.0)

- 日付: 2026-08-30 / ステータス: 採用済み
- 発端: ユーザー要望 (1) UITK Debuggerで本パッケージ生成物と判別できる命名
  (2) Regular以外(Thin/Semibold/Bold等)のフェイス選択対応

## 事実確認(一次ソース+実測。詳細な実測ログは verify 記録参照)

- `FontAsset.fontWeightTable` は public `FontWeightPair[10]`
  (`{ regularTypeface, italicTypeface }`)。TextGenerator は
  `-unity-font-style: bold` を `TextFontWeight.Bold`(=index 7)へ写像し、
  `FontAssetUtilities.GetCharacterFromFontAsset_Internal` が
  `fontWeightTable[7].regularTypeface` を参照、DynamicOS代替フェイスへの
  遅延グリフ追加まで実装済み(UnityCsReference 2022.3:
  TextGenerator.cs L549/L629、FontAssetUtilities.cs L43-)
- `CreateFontAsset(family, style)` は FontAsset自体の `.name` を設定しない
  (空のまま。material/atlas[0] には "{family} - {style} Material/Atlas" を設定)
  — Debugger で判別できない根本原因
- スタイル解決は**フェイス名の完全一致**。不一致は Debug.Log 1行+null
  (実測: DejaVu Sans の Regular相当は "Book"、"Regular" 指定は null)
- `GetOSInstalledFontNames` はスタイル展開された表示名("DejaVu Sans Bold"等)
  を列挙するが、**その表示名は family 入力として無効**(実測で全滅)。
  正解経路は常に (真のfamily, style)("Inter"+"Bold" は成功)
- `fontWeightTable` ゲッターはライブ配列を返し、要素の書き換えが有効(実測)。
  配線先アセットを先に破棄してもスロットは fake-null となり、
  FontAssetUtilities の null チェックで faux bold に落ちるだけで無害(実測+ソース)
- UITKのスタイルシステムから選択できるのは bold/italic のみ。
  Thin/Semibold 等は要素への明示的な FontAsset 割り当てでしか到達できない

## 選択肢と裁定(設計批評ワークフロー: APIクリティック+消費者代弁者)

| 論点 | 採用 | 棄却案と理由 |
|---|---|---|
| 命名形式(FontAsset系) | `"{family} - {style} [UITK Font Fix]"`(識別情報先頭+末尾タグ、公開const `CreatedObjectNameTag`)。material/atlas[0]にも波及 | 接頭辞形式は狭いパネルで肝心のfamily/styleが省略される(消費者指摘)。`(DynamicOS)`挿入はタグと重複するため削除 |
| 命名形式(所有OSフォント) | `"{name} [UITK Font Fix]"`(classic Fontはstyle分離フィールドを持たないため`- {style}`セグメントなし) | |
| 命名対象 | **キット所有オブジェクトのみ**(生成した全FontAsset+material+atlas[0]+所有OSフォント) | 共有エディタアセット(同梱RobotoMono・ラベルフォント)への命名は他UIへ漏染するため禁止(回帰テストで固定) |
| atlas追加ページ | 命名しない(生成時にはpage0のみ存在)。診断レポートに全ページ名を出して帰属可能にする | ページ追跡ポーリングは複雑さに見合わない |
| フェイス取得API | `GetCjkUiFontAsset(string styleName)`: base勝者familyに**ロック**、単一のスタイルキー辞書(ordinal-ci)で正/負両キャッシュ。null/空/base同名はbaseインスタンスへエイリアス | family横断フォールバック(Noto等へ)は棄却 — ファミリー混在はGT#9の病そのもの。分割表示名探索は実測で無効と確定し削除 |
| 葉向け適用API | **別名 `ApplyCjkUiFace(element, styleName)`**(スタイルミスはno-op=継承維持) | `ApplyCjkUi`オーバーロードは「ApplyCjkUi=コンテナルート」の記憶則を壊す(消費者裁定を採用、APIクリティックのオーバーロード案を棄却) |
| Bold自動配線 | base解決直後に `CjkUiBoldStyleName`(既定"Bold")のフェイスを**スタイル辞書経由で**取得し `fontWeightTable[7].regularTypeface` へ。**既定ON** | 既定OFFは「ユーザーが求めた修正を隠す」。フェイス無し→null→従来のfaux boldへ自然劣化+killスイッチあり+0.xセマンティクスで許容。配線を辞書経由にしないとBoldアトラス二重化 |
| 自己配線ガード | boldスタイル==baseスタイル(ordinal-ci)なら配線スキップ | 自分自身をweightTableへ入れるのは無意味で診断を汚す |
| 設定セマンティクス | `CjkUiBoldStyleName`: null→既定"Bold"復元 / **空文字→配線無効**(空配列=段無効の規約をミラー)。等値代入はキャッシュ維持 | 「null/空とも無効」は既存規約(null=既定復元)と矛盾 |
| ライフサイクル | InvalidateCaches: base→スタイル辞書の順で破棄、辞書と束縛family をクリア。スタイル生成時にHideAndDontSave+HookCleanup | family束縛の未リセットは「候補変更後に旧familyのフェイスが混ざる」最悪の潜在バグ(クリティック指摘) |
| italic配線 | 見送り(既定CJK候補にitalicフェイスが無い。bold-and-italicはfauxのまま — ドキュメント化) | |
| mono側マルチフェイス | 見送り(classic Font経路。fontWeightTableはFontAsset機構であり、v0.3+のアーキテクチャ変更) | |
| weight→style汎用辞書 | 見送り(USSから到達可能なのは700のみ。`fontWeightTable`はpublicで、`GetCjkUiFontAsset("Medium")`の戻りを手動配線すれば安全に合成可能) | |
| バージョン | 0.2.0(Changed: boldの実フェイス描画。`CjkUiBoldStyleName=""` で従来動作) | |

## 既知のコスト・注意(ドキュメント化)

- Bold配線はbold文字が描画されなくても第2のDynamicOSアセット+アトラスを
  即時生成する(USS boldはキットのフックなしにweightTableへ到達するため先行生成が不可避)
- 実Boldフェイスはfaux膨張と字送りが異なるため、既存bold表示の折返しが変わりうる
- スタイル名はフェイス名の完全一致(例: DejaVuは"Book")。ミスマッチ検出のため
  診断レポートに requested style と faceInfo.styleName を併記する
