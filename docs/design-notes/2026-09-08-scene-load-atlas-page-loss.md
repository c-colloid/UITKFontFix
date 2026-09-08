# 設計ノート: シーンロード等で遅延生成アトラスページが破棄され UI が崩れる問題

- 日付: 2026-09-08 / ステータス: **採用・実装**(0.4.1)
- 発端: 下流ツールからの報告。エディタ操作中(New Scene、ベイク失敗など「新しい
  読み込み」が入ったタイミング)に本パッケージのフォントで描画しているウィンドウが
  崩れる。診断ウィンドウの Re-probe(`ResetCaches`)で復帰する。報告に添付された
  例外は `MissingReferenceException` ではなく **`NullReferenceException`** で、
  発生箇所は TextCore ではなく **UI Toolkit のレンダラ**
  (`UIRStylePainter.DrawTextInfo`)。下流側の自前ガードは「アトラスまたは
  material が破棄された」と検出し同一名で再構築しているが、それでも表示は
  戻らず Re-probe が要る。

前回のノート(`2026-08-30-playmode-fontasset-lifecycle.md`)はプレイモード遷移を
主因として設計した。本ノートはその設計の**3つの穴**を実証し、埋める。
一次ソースは UnityCsReference `2022.3` ブランチ(raw 取得。行番号は取得時点)と
2022.3 スクリプトリファレンス。

## 1. 根本原因(3点、いずれもソースで実証)

### 1.1 例外の正体: material は生きていて、その `_MainTex` が死んでいる

`UIRStylePainter.DrawTextInfo`(`UIRStylePainter.cs:454`):

```
if (((Texture2D)textInfo.meshInfo[i].material.mainTexture).format != TextureFormat.Alpha8)
```

`Material.mainTexture` は、割り当て済みテクスチャが**破棄済み**のとき C# の
**本物の null** を返す(ネイティブ側の参照解決結果)。それを `Texture2D` に
キャストして `.format` を読むので `NullReferenceException` になる。
material 自体が破棄されていれば `MissingReferenceException`(前回ノートの症状)。
つまり今回の症状は「**material 生存 + アトラスページ破棄**」という組合せである。

どの material か。TextCore はページ 0 以外のグリフを描くとき、アセットの material
から派生させた **fallback material** を使う
(`TextGenerator.cs:5793-5795` → `MaterialManager.GetFallbackMaterial(fontAsset,
sourceMaterial, atlasIndex)`)。この fallback material は `HideAndDontSave`
(`MaterialManager.cs:107`)なので**遷移を生き残る**が、`_MainTex` に持つ
ページ N(`fontAsset.atlasTextures[atlasIndex]`, `:85`)は TextCore が
`SetupNewAtlasTexture` で作ったままの `HideFlags.None`(`FontAsset.cs:2769-2790`、
`hideFlags` への代入なし)。よって**ページ N だけが死に、material は残る**。

### 1.2 遅延生成ページが無防備な時間帯がある(予防の穴)

- 本パッケージが再スタンプするのは (a) 生成時(ページ 0 のみ存在)、
  (b) `PlayModeStateChange.Exiting*`、(c) getter のキャッシュアクセス時、の3つ
  (`FontFix.cs` `StampOwned` / `VerifyOwned`)。
- 報告元の消費者は `CreateGUI` で `ApplyCjkUi` を1回呼ぶだけなので (c) は起きない。
  (b) はプレイモード限定。**New Scene / シーンを開く / ベイク**には何のフックも無い。
- 一方 `HideFlags.DontSave` の定義は「新しいシーンがロードされても破棄されない」
  (2022.3 リファレンス原文)。裏返せば `HideFlags.None` の非永続オブジェクトは
  シーンロードで破棄されうる。New Scene はまさにこれ。
- ページ追加は頻発する。`CreateFontAsset(family, style)` は**サンプリング 90pt、
  1024x1024、padding 9** で生成する(`FontAsset.cs:522-525`)。CJK グリフ1つが
  約 110px 角を占めるので **1ページに 80 字前後**しか入らない。かな・漢字を
  含む日本語 UI なら 2〜10 ページは普通で、ページ追加はレンダリング中に
  (誰も getter を呼ばないまま)起きる。

### 1.3 その場修復しても UI Toolkit が古い TextInfo を描き続ける(回復の穴)

- `TextHandle.Update`(`TextHandle.cs:633-642`)は **`IsDirty()` が真のときだけ**
  `GenerateText` を実行し、それ以外はキャッシュ済み `textInfo` を返す。
  `IsDirty` は `TextGenerationSettings.GetHashCode()` の変化で判定する
  (`:79-88`)。
- そのハッシュに含まれるのは `fontAsset` **と `material`**
  (`TextGenerator.cs` 内 `TextGenerationSettings.GetHashCode`, `:127-128`)。
  UITK は毎回 `tgs.material = tgs.fontAsset.material` を代入する
  (`UITKTextHandle.cs:322`)。
- 前回のその場修復は「material が死んでいた場合のみ新しい material を作る」。
  プレイモード往復(material も死ぬ)では**偶然**ハッシュが変わって再生成されて
  いたが、今回のケース(material 生存)では同一インスタンス・同一 material の
  ままなのでハッシュ不変 → 古い `meshInfo`(死んだページを指す fallback
  material)を描き続け、NRE が再発し続ける。
- Re-probe(`ResetCaches`)で直るのは、FontAsset が**別インスタンス**になり
  消費者が再適用してハッシュが変わるから。下流の「同一名で再構築」が効かない
  理由も同じ。
- 加えて、例外を投げた要素はダーティフラグが既に落ちているので、何かが再描画を
  要求しない限りその要素は描き直されない。修復後に**明示的な再描画要求**が要る。

## 2. 修正(3層それぞれに1つ)

### 2.1 予防: ページ数の増加をエディタ更新ティックで検知して即スタンプ

`EditorApplication.update` に軽量ガードを載せる。各所有アセットについて
`atlasTextureCount`(マネージドフィールド由来、ネイティブ呼び出し無し)を前回
スタンプ時と比較し、**増えたときだけ**新ページへ `DontSave` を貼る。定常状態の
コストはアセット数回の整数比較のみ。露出は最大1ティック(ページ追加は描画中、
ユーザー操作によるシーンロードは別イベントで起きるので、実質ゼロ)。

### 2.2 検出: シーンイベントでの push 型スイープ + 低頻度の生存確認

- `EditorSceneManager.newSceneCreated` / `sceneOpened` で `VerifyOwned`。
  ロード直後・再描画前に同期で走る。
- ガードティックでも 0.25 秒間隔で生存確認(ベイク失敗のように、破棄経路を
  特定できていないトリガーの受け皿)。`IsUsable` は material 1 回 + 使用ページ数
  回の `== null` だけなので十分に安い。

### 2.3 回復: 修復時は material を必ず差し替え、利用要素に再描画を要求

- `TryRepair` は material が生きていても**新しい material に差し替えて**旧
  material を破棄する。`fontAsset.material` が変わることが UITK 側の
  `IsDirty` を真にする唯一の外部から可能な手段(§1.3)。次の再描画で TextInfo が
  再生成され、新しい fallback material(新 material ID × 新ページ ID のキー)が
  作られる。旧 fallback material は `MaterialManager` の静的辞書に残る
  (`HideAndDontSave`、修復1回につき数個。ドメインリロードで消える。許容)。
- 修復が実際に行われたアセットについて、全 `EditorWindow` の
  `rootVisualElement` 配下の `TextElement` のうち
  `resolvedStyle.unityFontDefinition.fontAsset` が当該アセットのものへ
  `MarkDirtyRepaint()` を呼ぶ。これで `OnGenerateVisualContent` → `Update()` →
  再生成が走る。継承経由の要素も `resolvedStyle` で拾える。修復は稀な事象なので
  全ウィンドウ走査のコストは問題にならない。

## 3. 選択肢と裁定

| 論点 | 採用 | 棄却案と理由 |
|---|---|---|
| ページ追加の検知 | `EditorApplication.update` でページ数差分 | `FontAsset.OnFontAssetTextureChanged` への購読(`FontAsset.cs:697`)は **internal** で、リフレクションは Unity 更新で壊れる。シーンイベントの直前フック(`sceneClosing` 等)は New Scene 以外のトリガーを網羅できない |
| 破損の検出 | シーンイベント + 0.25 秒ティック | ティック毎の生存確認だけでも足りるが、報告トリガー(New Scene)には遅延ゼロの経路を用意する。ベイク側は破棄経路が未特定のためイベント列挙は推測になる → 低頻度ティックで受ける |
| 再生成の誘発 | material 差し替え + `MarkDirtyRepaint` 走査 | (a) その場修復を捨てて常に再構築+`CachesInvalidated`: 未購読の消費者が壊れた参照を抱えたまま二度と直らない(0.4.0 以前への退行)。(b) 差し替えのみで走査無し: 例外を投げた要素はダーティでないため、ユーザーが触るまで崩れたまま。(c) `CachesInvalidated` を修復時にも発火: 同一インスタンスの再適用はスタイル値が等しく何もダーティにならない |
| 旧 material の扱い | 破棄 | 保持は修復毎のリークで、TextInfo は再生成時に新 material を読むため参照は残らない |

## 4. 併せて直す既存記述

- `CjkUiFontAsset` / 検証済み挙動 12 の README 記述を「プレイモード遷移の直前に
  貼り直す」から「ページが増えるたびに貼り直す」へ。`CachesInvalidated` の
  「その場修復では発火しない」は変わらない(修復は同一インスタンス)。
- 下流の自前ガード(`AtlasGuard`)は本修正で不要になる。同一名再構築は §1.3 の
  理由で表示を戻せないので、残すなら「`FontFix.CjkUiFontAsset` を読むだけ」に
  縮めるのが正しい。

## 5. 回帰ガード(Linux CI で実走)

`FontAssetLifecycleTests` に追加:

1. `TryRepair_ReplacesTheMaterial_EvenWhenOnlyAPageDied`: ページ 0 だけ破棄 →
   修復後の `material` が別インスタンスで、旧 material は破棄済み(§2.3 の契約)。
2. `LazyPages_AreBornUnflagged_AndTheGuardTickStampsThem`: `TryAddCharacters` で
   ページを 2 枚以上に育て、追加ページが `HideFlags.None` で生まれること(§1.2 の
   実証)と、ガードティック 1 回で `DontSave` になることを確認。ページが増えない
   環境では Ignore。
3. `NewScene_KeepsStampedChildren_AndKillsUnflaggedObjects`:
   `EditorSceneManager.NewScene(EmptyScene, Single)` 後、スタンプ済みの子は生存し、
   対照として作った `HideFlags.None` の `Texture2D` は破棄される(§1.2 の
   「シーンロードが `None` を殺す」の実証。EditMode テストから実行可能)。
4. `NewScene_SweepsAndRepairsWithoutAnyGetterAccess`: 子を破棄した状態で New Scene
   → getter を読まずに `IsUsable` が真(§2.2 のフック配線)。

`MarkDirtyRepaint` 走査は `resolvedStyle` がパネル内でしか計算されないため
ヘッドレスでは検証できない。対話確認項目として `docs/verify` のチェックリストへ。

## 6. 未実証(honest な線引き)

1. ベイク失敗時にどのエンジン経路が `None` オブジェクトを破棄するか。§2.2 の
   低頻度ティックは経路に依存しない受け皿であり、特定は対話確認に委ねる。
2. `EditorApplication.update` とウィンドウ再描画の同一フレーム内順序。予防(§2.1)
   が効いていればページは死なないので、順序は検出の遅延幅にしか影響しない。
3. `MaterialManager` の旧 fallback material 残留の実メモリ量(小さい見込み)。
