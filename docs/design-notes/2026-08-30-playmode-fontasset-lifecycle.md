# 設計ノート: プレイモード往復で壊れる DynamicOS FontAsset のライフサイクル対策

- 日付: 2026-08-30 / ステータス: **設計提案(実装前・レビュー1巡反映済み)**
- 発端: 下流ツールからの不具合報告。プレイモードの出入り後、本パッケージが生成した
  CJK FontAsset で描画するたびに TextCore 由来の `MissingReferenceException` が発生し
  UI が崩れる。**FontAsset 本体は生きているのに、内部のアトラス `Texture2D` と
  `Material` だけが破棄されている**(報告者の観測。下流コードは本リポジトリに無いため、
  「アトラス生存時のみ適用する」暫定ガードの内容は報告ベースの記述)。

検証環境に Unity ライセンスが無いため実機再現はできない。一次ソースは
UnityCsReference の `2022.3` ブランチ(取得コミット a322ce5 = 2022.3.76f1。CI 標的は
2022.3.22f1 のため行番号は前後しうる)と Unity 2022.3 スクリプトリファレンス。

## 1. 事実確認

### 1.1 生成側: TextCore は子オブジェクトに hideFlags を一切付けない

- 本パッケージの唯一の生成経路は `FontShims.TryCreateOsFontAsset` →
  `FontAsset.CreateFontAsset(familyName, styleName)`(`FontAsset.cs:522-530`)。
  `DynamicOS` / `SDFAA` / 1024x1024 / padding 9 を**明示引数で**渡し(`:525`)、
  private オーバーロード(`:548`)経由で `CreateFontAssetInstance`(`:557` → `:608-690`)へ。
- そこで3つの UnityEngine.Object が作られる: FontAsset 本体 /
  `new Texture2D(1, 1, TextureFormat.Alpha8, false)`(`:636`)/
  `new Material(TextShaderUtilities.ShaderRef_MobileSDF)`(`:662`。`:648` は
  RASTER_MODE_BITMAP 側の兄弟で、SDFAA の本パッケージは通らない)。
- **`FontAsset.cs`(3164行)に `hideFlags` という識別子は1度も現れない。**
  つまり子2つは `HideFlags.None` のまま生まれる。
- 遅延追加ページも同様: `SetupNewAtlasTexture`(`:2769-2790`)は
  `new Texture2D(m_AtlasWidth, m_AtlasHeight, Alpha8, false)` を作るだけ。
  呼び出し元は `:2337` `:2516` `:2682`(複数ページに跨る溢れループ)`:2713`。
- 配列は**倍々に伸びる**: `Array.Resize(ref m_AtlasTextures, Length * 2)`(`:2775`)。
  よって `atlasTextures` の末尾には**本物の C# null** が並ぶ。使用中のページ数は
  public な `atlasTextureCount`(= `m_AtlasTextureIndex + 1`, `:346`)で分かる。

### 1.2 Unity 自身の同等コードは3つ全部に DontSave を付けている

`TextSettings.GetCachedFontAssetInternal`(`TextSettings.cs:306-315`、代入は `:308-311`):

```
fontAsset.hideFlags = HideFlags.DontSave;
fontAsset.atlasTextures[0].hideFlags = HideFlags.DontSave;
fontAsset.material.hideFlags = HideFlags.DontSave;
fontAsset.isMultiAtlasTexturesEnabled = true;
```

これがランタイム生成 FontAsset をキャッシュ保持する際の Unity 公式の作法である。
そして UITK は、`style.unityFontDefinition` に **FontAsset を直接渡した場合はこの経路を
通らない**: `TextUtilities.GetFontAsset`(`UITKTextHandle.cs:448-459`、短絡は `:450-451`)は
`fontAsset` が非 null ならそのまま返す。**hideFlags の責任は完全に本パッケージ側にある。**

### 1.3 本パッケージ側の欠落

`FontFix.CreateOwnedCjkAsset` は `asset.hideFlags = HideFlags.HideAndDontSave;`
(`Editor/FontFix.cs:443`)を **FontAsset 本体にだけ** 設定し、`NameCreatedAsset`
(`:460-483`)は material(`:471`)と atlasTextures[0](`:476`)を**リネームするだけ**で
hideFlags に触れていない。`HideAndDontSave` は `DontSave` のビット群
(`DontSaveInEditor|DontSaveInBuild|DontUnloadUnusedAsset`)を含むため本体だけが生き残る。
報告症状(本体は生存、子だけ破棄)と一致する。

この欠落は base だけでなく `GetCjkUiFontAsset(style)` が作る**全フェイス**に及ぶ。
既定で `CjkUiBoldStyleName = "Bold"`(`Runtime/FontFixDefaults.cs:61`)であり、
`WireBoldFace` が `fontWeightTable[7].regularTypeface` に配線する Bold フェイスも
同じく無防備な2つ目の FontAsset である。

### 1.4 破棄されると何が起きるか

- `TextAsset.material` は `get => m_Material; set => m_Material = value;`
  (`TextAsset.cs:56-60`)。ゲッターは**保持している参照をそのまま返すだけで、
  再生成も null 回復もしない**(`atlasTexture` は `m_AtlasTexture` が null のとき
  `atlasTextures[0]` を読み直す分だけマシ: `FontAsset.cs:297-308`)。
  なお **public セッターがある**ことが、後述のその場修復を合法にしている。
- 破棄済み参照は**静かに伝播し、最初のネイティブメンバアクセスで落ちる**。
  `UITKTextHandle.cs:322` の `tgs.material = tgs.fontAsset.material;` は managed な
  フィールド読みなので投げない。`Object.GetInstanceID()` も
  `m_InstanceID` を返すだけで投げない(`UnityEngineObject.bindings.cs:97-104`)。
  実際に投げるのは `Object.name`(`GetName` extern, `:659-660`)、
  `Material.ComputeCRC()`、`new Material(source)`、`Texture.isReadable` などの
  extern アクセスで、`MaterialManager.GetFallbackMaterial` はその密集地帯
  (`MaterialManager.cs:79`, `:91`, `:96`)。**厳密な発生フレームは実機スタックトレース
  でしか確定できない**ため、本ノートでは「下流のどこかのネイティブアクセスで落ちる」
  以上には主張しない。
- 同じ理由で **`hideFlags` 自体も extern**(`UnityEngineObject.bindings.cs:474`)である。
  破棄済みオブジェクトへスタンプしようとすると、それ自体が例外になる。
  → 実装順序の制約(§2.2)。

### 1.5 いつ破棄されるのか

- `HideFlags.DontSave` の定義(2022.3 スクリプトリファレンス、原文):
  「The object will not be saved to the Scene. **It will not be destroyed when a new
  Scene is loaded.**」= `HideFlags.None` のオブジェクトはシーンロードで破棄されうる。
- プレイモード開始はシーンをリロードし、終了時はバックアップからシーンを復元する。
- ドメインリロード**有効**(既定)の場合、開始時に `beforeAssemblyReload` が発火し、
  本パッケージの `DestroyOwned` が自分のアセットを破棄する
  (`FontAsset.OnDestroy` が `DestroyAtlasTextures()` と `DestroyImmediate(m_Material)`
  を行う: `FontAsset.cs:722-727`)。したがってこの経路は自己修復的である。
- Enter Play Mode Options で **Reload Domain を無効**にしている場合、static も購読も
  維持される(`Manual/DomainReloading.html`)ため `beforeAssemblyReload` は発火せず、
  同一インスタンスが遷移を越えて生き残る。報告症状に最も素直に対応するのはこの構成。

### 1.6 未実証(honest な線引き)

1. **どのエンジン内部パスが子オブジェクトを破棄するか**(シーンロード時の掃除か、
   プレイモード終了時のネイティブ後始末か)は managed ソースに無い。ただし修正の
   妥当性はこれに依存しない — Unity 自身が同じ状況で DontSave を付けている(§1.2)。
2. **プレイモード終了時にドメインリロードが起きないという前提**。既定構成で
   「壊れるのは終了時」と言い切るにはこの前提が要るが、引用できる文書が無い。
   本設計は取得時チェック(§2.4)と Entered* スイープ(§2.5)により、
   開始側・終了側のどちらで壊れても回復する形にしてあるので、前提の当否に依存しない。
3. **Reload Scene 軸**。Enter Play Mode Options は Reload Domain と Reload Scene を
   独立に切れる。シーンリロードを破棄の主因とみなす本ノートの因果連鎖からすると、
   Reload Scene 無効時は再現しない可能性がある。実機確認項目。
4. 例外の厳密な発生フレーム(§1.4)。
5. `PlayModeStateChange` の各値と破棄処理の前後関係。ドキュメントが述べるのは
   `Exiting*` が遷移**より前**に起きること、`Entered*` は「次のエディタ更新」で
   起きることまでで、同一コールスタックかどうかは書かれていない。
6. クラシック `Font`(OS モノスペース経路)の `Font.material` に同種の問題があるか。
   ネイティブ実装のため不明。**推測での先回り修正はしない**(§8)。

## 2. 修正案

### 2.1 全体像(3層)

| 層 | 内容 | 効果 |
|---|---|---|
| 予防 | 生成時に material と**使用中の全アトラスページ**へ `HideFlags.DontSave`。遷移直前に再スタンプ | 報告症状そのものを起こさせない(本命) |
| 検出 | 本体・material・全使用ページの生存判定。負のキャッシュと破棄を厳密に区別 | 壊れたアセットを外へ渡さない |
| 回復 | 遷移直後にスイープし、**同一インスタンスのまま**修復。不能時のみ破棄→再解決し、新イベントで通知 | 適用済みの VisualElement が消費者の協力なしに治る |

回復が「同一インスタンス」であることが要点である。作り直しても、既に
`style.unityFontDefinition` に古いインスタンスを持つ要素は治らない。

### 2.2 予防: hideFlags スタンプ(順序に制約あり)

- 値は `HideFlags.DontSave`(= `DontSaveInEditor|DontSaveInBuild|DontUnloadUnusedAsset`)。
  本体は現状どおり `HideAndDontSave` を維持。
- 走査範囲は `Math.Min(asset.atlasTextureCount, asset.atlasTextures.Length)` に限る。
  それを超えた末尾スロットは**本物の null**(§1.1)であり、触ってはいけない。
- **生存しているオブジェクトにしかスタンプしない**。`hideFlags` は extern であり
  (§1.4)、破棄済みへの代入は例外になる。したがって処理順は常に
  「生存判定 → 死んでいれば修復 → 生きているものにスタンプ」。
- 呼ぶ場所: 生成直後 / `playModeStateChanged` の `ExitingEditMode`・`ExitingPlayMode`
  (遅延追加ページを遷移前に保護する)/ 取得時(生存判定と同じ走査のついで)。

### 2.3 検出: 3状態を区別する

`_cjkStyleAssets` の **present-null は「キャッシュ済みのミス」**であり
(`FontFix.cs:53-60` の設計意図: 再 `CreateFontAsset` と TextCore の1行ログを繰り返さない)、
base も `_cjkUiProbed == true && _cjkUiAsset == null` でミスを表す。これを
「壊れている」と誤判定すると、getter を呼ぶたびに再探索と
`Debug.Log("Unable to find a font file ...")`(`FontAsset.cs:526`)が走る。

したがって判定は3状態:

1. `ReferenceEquals(asset, null)` … 未探索 or **キャッシュ済みミス** → 触らない
2. `asset == null`(fake-null だが参照は非 null) … 本体が破棄済み → 再構築
3. 本体は生存 … `material` と使用中の各ページを同じ2種類の null で判定。
   `!ReferenceEquals(page, null) && page == null` なら**破棄済みページ**。

`IsUsable` は **page0 だけでなく使用中の全ページ**を見る。`MaterialManager` は
任意インデックスのページを触る(`MaterialManager.cs:71`)ので、page0 だけの判定では
今回の症状を取りこぼす。

### 2.4 回復: その場修復(ドナー不要)

`ClearFontAssetData` を**いきなり呼んではならない**: `ClearAtlasTextures` は
`m_AtlasTextures[0]` に対して `texture.isReadable` を読む(`FontAsset.cs:3128-3131`)ので
page0 が破棄済みなら例外、配列長 0 なら `Array.Resize` 後の C# null で NRE
(いずれも一次ソースで確認済み)。手順:

1. **形状ガード**: `atlasWidth > 0 && atlasHeight > 0`(`ClearFontAssetTables` が
   `new GlyphRect(0, 0, m_AtlasWidth - packingModifier, ...)` を作る: `:3069`)。
   これらは public get / **internal set**(`:374-400`)で外から直せないため、
   不正なら修復を諦めて再構築へ。
2. **material が死んでいれば作り直す**。生成時に `asset.material.shader`(public)と
   TextCore が書き込む5つの float(`_GradientScale` `_TextureWidth` `_TextureHeight`
   `_WeightNormal` `_WeightBold`。ID は public な `TextShaderUtilities.ID_*`)を
   スナップショットしておき、`new Material(shader)` + 5つの `SetFloat` で復元する。
   Shader はプロジェクト/組み込みアセットでありプレイモードで破棄されない。
   値は `CreateFontAssetInstance` が書くもの(`FontAsset.cs:664-673`)と同一。
3. **page0 が死んでいれば** `new Texture2D(1, 1, TextureFormat.Alpha8, false)` を作る
   (`CreateFontAssetInstance` と同一形状。スクリプト生成テクスチャは既定で readable)。
   配列が null/長さ0 なら `asset.atlasTextures = new Texture2D[1]` してから `[0]` に代入
   (ゲッターはライブ配列を返すので要素代入が効く: `FontAsset.cs:314-333`)。
   **配列ごとの差し替えはしない**(ライブ配列であることと、末尾の本物 null を
   作り替えないため)。**インデックス1以降のページは手順5で全て破棄される**
   ため保全はしない — 修復後のアセットは1ページに戻る、が仕様。
4. `asset.material.SetTexture(TextShaderUtilities.ID_MainTex, asset.atlasTextures[0])`。
   片方だけ死んでいた場合も必要(生き残った material は死んだテクスチャを指している)。
5. **命名 → スタンプ → `asset.ClearFontAssetData(true)`** の順。命名を先にするのは
   `ReadFontAssetDefinition` が `name` からハッシュを再計算するため(`:810`, `:813`)。
   `ClearFontAssetData` はグリフ/キャラクタ表と `freeGlyphRects`/`usedGlyphRects`
   (いずれも internal)をリセットできる唯一の public 経路。`true` で 1x1 に戻し、
   以降のグリフ追加パスが `m_AtlasWidth/Height` へ Reinitialize する
   (`:1842, 2057, 2178, 2227, 2302, 2472, 2667` のいずれも先に整合させる)。
6. スタイル辞書の全アセット→base の順に実施。`fontWeightTable` は
   `ClearFontAssetData` / `ReadFontAssetDefinition` のどちらからも触られない
   (識別子の出現は `:461` `:462` `:465` のみ)ので再配線は不要。ただし
   **Bold は別アセットなので個別に修復が必要**。
7. 全体を try/catch で包み、失敗したら再構築へフォールバックする。

**コストの正直な評価**: `ClearFontAssetData` は `ReadFontAssetDefinition` →
`AddSynthesizedCharactersAndFaceMetrics` → `LoadFontFace(familyName, styleName, pointSize)`
(`FontAsset.cs:1015-1026`, `:1148`)を通り、**システムフォントのフェイスロードが1回**走る。
グリフ表も全消去され、次の描画で再ラスタライズされる。つまり修復は再構築に対して
**安くはない**。買っているのは**インスタンス同一性だけ**である。それでも採る理由は、
同一性こそが「消費者が何もしなくても治る」唯一の手段だからである。
なお `LoadFontFace` が失敗すると `Debug.LogWarning("Unable to load font face for [...]")`
が出る(`InternalDynamicOS` は 2022.3 の managed ソースで true にされることが無い)。

**再入ガード**: `FontEngine` のフェイス状態はプロセスグローバルであり、修復は
同期的にフェイスロードを走らせる。TextCore のグリフ追加中(描画中)に修復が再入すると、
進行中のラスタライズの足元でフェイスが差し替わる。try/catch では防げない
(むしろ静かに壊れる)ので、`_repairing` フラグで**入れ子の修復は何もせず現状を返す**。
併せて「`Apply*` / getter は `generateVisualContent` から呼ばない」を API 文書に明記する。

### 2.5 フック(push 型スイープが要)

報告元の消費者は `CreateGUI` で `ApplyCjkUi(root)` を1回呼ぶだけで、以後 FontFix の
API を呼ばない。したがって**取得時チェックだけでは永遠に発火しない**。

- `ExitingEditMode` / `ExitingPlayMode`: 生存しているオブジェクトへ再スタンプ(予防)。
- `EnteredEditMode` / `EnteredPlayMode`: **base と全スタイルアセットを無条件にスイープ**し、
  検出→修復する(push 型)。`Entered*` は「次のエディタ更新」で遅延発火するため、
  その1ティック分の露出は残る。これは取得時チェックでは埋まらない
  (誰も getter を呼ばないため)ので、遅延を受け入れたうえでスイープを主経路とする。
- 購読は既存の `_cleanupHooked` と同じ遅延1回購読パターン(`FontFix.cs:537-545`)に相乗り。
  `beforeAssemblyReload += DestroyOwned` は現状維持。

### 2.6 公開 API を1つだけ追加

```
public static event System.Action CachesInvalidated;
```

- 発火するのは「**手渡し済みのインスタンスが破棄/交替した**」ときだけ:
  `ResetCaches()` / `FontFixSettings` 変更 / 修復不能による再構築。
  **その場修復では発火しない**(同一インスタンスのまま治るため)。
  `beforeAssemblyReload` 経由の破棄でも発火しない(直後に購読ごと消える)。
- **発火位置は `InvalidateCaches` の最終文**。`DestroyOwned()` の直後ではない:
  `_cjkUiProbed` 等のフラグを戻す前に発火すると、購読者の `ApplyCjkUi` が
  「探索済み・結果 null」を見て**無言で no-op** し、フォントが失われる。
- 破棄と同一コールスタックで**同期**発火する(遅延させると、その隙間の再描画で
  まさに今回の例外が起きる)。
- 未定義を残さないための規定: 発火前に `GetInvocationList()` でスナップショットを取り
  (発火中の購読追加は次回から見える)、購読者ごとに try/catch して
  `Debug.LogException` で可視化する(黙って握り潰す既定からの意図的な例外)。
  再入(ハンドラが `ResetCaches` を呼ぶ)は入れ子発火を抑止し、**現在の発火が
  終わってから1回だけ**追加発火する。修復中(`_repairing`)からは発火しない。
  発火はメインスレッドのみ。**ドメインリロードで購読は消える**ので、消費者は
  `CreateGUI` / `[InitializeOnLoadMethod]` で購読し直す必要がある。
- ペイロードなし。新インスタンスが要る購読者は**ハンドラ内で getter を読み直す**。
- **バッチスコープ**を内部に設け、複数プロパティを続けて書く操作
  (`FontFixSettings.ResetToDefaults()`、`FontFixSettingsUi.applyFields`(「Apply」
  「Save to project」「Re-probe」の全ボタンが通る: `FontFixSettingsUi.cs:65-74`)、
  `FontFixProjectSettings.ApplyData`)で発火を1回にまとめる。
  スコープは入れ子可能。**何も変わらなかったスコープは発火しない**
  (各セッターが値比較でガードしているため: `FontFixSettings.cs:77-80, 103-106, 128-132`)。

### 2.7 自分自身への適用(ドッグフーディング)

`FontFixDiagnosticsWindow` は自分のルートに `ApplyCjkUi` した上で
(`FontFixDiagnosticsWindow.cs:43`)、Re-probe ボタンから `FontFix.ResetCaches()` を呼び
(`:108`)、**自分が描画に使っている FontAsset を破棄している**。Project Settings ペインも
同じ(`FontFixSettingsProvider.cs:30` と `FontFixSettingsUi.cs:90/101`)。

新イベントを購読して直すが、ハンドラは**再適用のみ**とする
(`ApplyCjkUi(rootVisualElement)` と `ApplyMono(_report)`)。`Rebuild()` を呼ぶと
`root.Clear()` がクリック中のボタンを破棄し、`_report` も差し替わる。
ウィンドウは `CreateGUI` で購読・`OnDisable` で解除、SettingsProvider は
`activateHandler` で購読・**`deactivateHandler` を追加して解除**する
(静的イベントが死んだ rootElement を掴み続けるため)。

## 3. 併せて直す既存欠陥(今回の調査で判明)

| # | 欠陥 | 影響 |
|---|---|---|
| A | hideFlags 欠落は base だけでなく**全フェイス**(既定で配線される Bold 含む)に及ぶ | 報告された暫定ガード(base のみ確認)を素通りする経路が存在する |
| B | `InvalidateCaches`→`DestroyOwned` が、生きている VisualElement が参照中の FontAsset を同期破棄する。`FontFixSettingsUi.cs:145`「open windows keep their current font」/ `FontFixSettingsProvider.cs:46-47`「windows already open pick them up when rebuilt or reopened」という記述と矛盾 | 設定変更のたびに開いている UI が壊れた参照を持つ。§2.6 のイベントで塞ぐ(文言も直す) |
| C | B の実例がパッケージ同梱 UI 自身 | §2.7 で修正。新イベントの回帰の証人になる |

## 4. 選択肢と裁定

| 論点 | 採用 | 棄却案と理由 |
|---|---|---|
| 予防の値 | 子は `HideFlags.DontSave`、本体は現状の `HideAndDontSave` | 子も `HideAndDontSave`: 前例はある(`MaterialManager.cs:45/94` は自前のフォールバック material に付けている)が、**ランタイム生成 FontAsset のキャッシュ**という同一状況の前例は `TextSettings.cs:308-310` の `DontSave` であり、そちらに合わせる |
| 走査範囲 | `atlasTextureCount` までに限定し、fake-null と本物 null を `ReferenceEquals` で区別 | 配列全長を走査: 末尾の本物 null で NRE。fake-null だけを見る: キャッシュ済みミス(present-null)を「壊れている」と誤判定し、getter 毎に再探索とログを撒く |
| material の作り直し | **生成時に Shader と5 float をスナップショット** → `new Material(shader)` + `SetFloat`×5 | (a) ドナー FontAsset を作ってテンプレートにする: 修復ごとにフェイスロード+一時アセットが増えるうえ、`TextResourceManager` への自己登録が残る。そもそも不要 — `asset.material.shader` は public で、Shader は破棄されない (b) `Shader.Find("TextMeshPro/Mobile/Distance Field SSD")` 等の名前直書き: `ShaderRef_MobileSDF` の実装(`TextShaderUtilities.cs:150-159`)そのものだが、文字列をこちらで保守することになる |
| テーブル初期化 | `ClearFontAssetData(true)` を **material/page0 差し替えの後に** 呼ぶ | 先に呼ぶ: `isReadable` 参照で確実に例外。呼ばない: 旧アトラス座標のグリフ表が残り、無言で空白/化けた描画になる |
| 検出の置き場所 | `Entered*` フックでの**無条件スイープ**(主)+ 取得時チェック(従) | 取得時のみ: 「一度適用して二度と呼ばない」消費者(報告元・同梱UI・一般的な EditorWindow)では永遠に発火しない。フックのみ: フックの前後関係が未実証(§1.6)なので単独では保証にならない |
| 回復方式 | 同一インスタンスのその場修復、不能時のみ再構築 | 常に再構築: 適用済み要素が治らず、下流は自前ガードを維持し続けることになる。**ただし段階投入案は有力**(下記「未決」) |
| 公開 API | `CachesInvalidated` イベント1つだけ | `VerifyAndRepair()`: 呼び忘れたら効かず、インスタンス交替も救えない。世代カウンタのポーリング: 消費者に更新ループを強制し、隙間が残る。API 無し: パッケージ都合の再構築を消費者が観測できない。引数付きイベント: 内部の修復/再構築の区別を公開契約に固定してしまう |
| 発火位置 | `InvalidateCaches` の**最終文** | 破棄直後: 購読者が「探索済み・null」を見て no-op し、無言でフォントが失われる(例外より発見が難しい) |
| 破棄の遅延 | しない(`ResetCaches` は同期破棄のまま) | 「破棄せず退役させる」: アトラスが積み上がる保証違反。既存回帰テスト `CjkFaceTests.cs:227-241` とも矛盾 |
| シーム | 新規の TextCore 呼び出しは**すべて `FontShims` 経由**にし、`FontAssetLifecycle` は方針だけを持つ | `ClearFontAssetData` だけシームに入れる: README:695-701 の「version-sensitive な TextCore 呼び出しは単一シームに集約」という公約を、シーム外に増える呼び出し(atlasTextures 操作、hideFlags、`SetTexture`)が破る |
| バージョン | **0.4.0**(public イベント追加=マイナー) | 0.3.1: 公開 API が増えるため semver 上不適切 |

### 未決(ユーザー判断が要る1点)

**その場修復を 0.4.0 で入れるか、段階投入にするか。**
予防が効けば修復はほぼ発火しない稀路であり、修復は本パッケージで最も危険な部類の
コード(material 再構築・配列操作・`ClearFontAssetData`・フェイス再ロード)である。
「0.4.0 = 予防+検出+再構築+イベント」に絞り、その場修復を次版へ回す案には理がある。
その場合に失うのは「イベントを購読していない消費者が、稀な破損時に一度だけ
古い参照を持ち続ける」ことのみ。**推奨は同版投入**(ドナーを捨てた結果、修復は
60行程度に収まり、後述のとおり Linux CI で実走テストできるため)。

## 5. 実装計画(ファイル単位)

- `Editor/FontAssetLifecycle.cs`(新規, internal static): 生存判定 / スタンプ /
  その場修復 / スイープの**方針**。TextCore へは直接触れず `FontShims` 経由。
  material テンプレート(Shader + 5 float)の保管もここ。
- `Editor/FontShims.cs`: 新規の TextCore 接点を集約
  (`TryClearFontAssetData`、ページ配列の取得/設定、material の取得/設定、
  `ID_MainTex`)。`ClearFontAssetData` は public だが「将来 internal 化しうる」と
  自身のドキュメントコメントが警告している(`FontAsset.cs:2985-2986`)。
  返り値は既存の `Try*` 規約(失敗は例外でなく false/null)に合わせる。
- `Editor/FontFix.cs`: 生成時スタンプ、取得時チェック、`playModeStateChanged` 購読、
  `CachesInvalidated` の発火・バッチ・再入ガード。
- `Editor/Diagnostics/FontFixDiagnostics.cs`: アトラス節に material/各ページの生存と
  hideFlags を出力。`FontFixDiagnosticsWindow.cs` / `FontFixSettingsProvider.cs`:
  §2.7 の購読と解除。`FontFixSettingsUi.cs:145` / `FontFixSettingsProvider.cs:46-47`
  の誤った文言を修正。
- `Tests/Editor/FontAssetLifecycleTests.cs`(新規)+ `.cs.meta`(guid は
  `uuid4().hex` の32桁小文字、既存テンプレートと同一)。
- `README.md` / `README.ja.md`: 罠一覧、API 表(`CjkUiFontAsset` 行と新イベント行、
  「getter を都度読む/自前フィールドにキャッシュしない」「`generateVisualContent`
  から呼ばない」「購読はドメインリロードで消える」)、「検証済みの挙動」項目12。
  `CHANGELOG.md` に `### Fixed` / `### Added`、`package.json` を 0.4.0 へ。

## 6. 回帰ガード(Linux CI で実走する形)

CI は CJK フォントの無い Ubuntu コンテナだが、`CjkFaceTests` は候補に
DejaVu Sans / Liberation Sans を含む(`Tests/Editor/CjkFaceTests.cs:26-33`)ため
**実物の DynamicOS FontAsset を headless で生成できている**。証拠は最新の main CI 実測
(run 33299071172, 2026-08-30):

```
total="115" passed="112" failed="0" inconclusive="0" skipped="3"
```

スキップは3件のみ(Windows 限定2件+CJK と mono の両解決が要る1件)。
CjkFaceTests の14件が `Assert.Ignore` していればスキップは17件以上になるはずなので、
これらは実走している。新テストも同じ SetUp パターンに乗せる:

1. 生成直後、`material.hideFlags` と使用中の全ページの `hideFlags` が
   `HideFlags.DontSave`(`DontSaveInEditor|DontSaveInBuild|DontUnloadUnusedAsset`)であること。
2. `atlasTextures` がライブ配列であること(`[0]` 代入が読み戻せる)。
3. **破壊シミュレーション**: `DestroyImmediate(asset.material)` と
   `DestroyImmediate(asset.atlasTextures[0])` の後、生存判定が false になり、
   修復が例外なく成功し、material/page0 が生き返り、`isReadable` が true、
   `hideFlags` が再付与され、`fontWeightTable[7]` の参照が保たれていること。
4. 取得時の自己修復: 破壊後に `FontFix.CjkUiFontAsset` を読むと
   **同一インスタンス**が使用可能な状態で返ること(例外なし)。
5. 負のキャッシュ保護: `GetCjkUiFontAsset("NoSuchFaceXyz")` を N 回読んでも
   `CreateFontAsset` は1回しか走らない(ログ数で判定)。
6. イベント: 5プロパティを既定から変えた状態で `ResetToDefaults()` を呼ぶと
   **ちょうど1回**発火する。何も変わらないときは**0回**。ハンドラ内から
   getter を読むと新しい使用可能インスタンスが返る。ハンドラが例外を投げても
   無効化処理が完走し、他の購読者にも届く。ハンドラから `ResetCaches()` を
   呼んでも停止する(再入ガード)。その場修復では発火しない。
7. 診断レポートが新セクションを含み、ASCII のままであること。

## 7. 検証計画

- 本コンテナに Unity ライセンスは無く、コンパイル/EditMode を実行できない。
  実装コミットの前に **CI を `workflow_dispatch` でこのブランチ上で実行**して
  compile + EditMode グリーンを確認するのが最も確実(release ジョブは
  `github.ref == 'refs/heads/main'` で守られており、ブランチ実行ではタグを打たない:
  `.github/workflows/ci.yml:79`)。実行はユーザーの承認後に行う。
- 実機(インタラクティブエディタ)でしか確認できない項目 →
  `docs/verify/` のチェックリストへ:
  1. Reload Domain / Reload Scene の4通りの組み合わせでの再現有無(§1.6-2, 3)
  2. その場修復後、再描画が新しい material/texture を拾うか
     (UIR が描画コマンドに旧テクスチャを抱えていないか)
  3. 修復時に `Unable to load font face` 警告が出ないか
  4. OS mono フォント(クラシック `Font`)適用要素でのプレイモード往復(§1.6-6)

## 8. 残課題

- クラシック `Font` の `Font.material` 問題(§1.6-6)。実機で1回確認してから判断する。
  現時点で推測に基づく先回り修正はしない。
- 消費者が FontAsset を自前フィールドにキャッシュしている場合の扱いは、
  ドキュメントでの明示(「都度 getter を読む」)に留める。
- `materialHashCode` は `name + " Atlas Material"` から再計算される(`:813`)一方、
  本パッケージは material を `<asset name> + " Material"` と命名している(`FontFix.cs:471`)。
  既存の不一致であり実害は確認されていないが、修復で焼き直されるので記録しておく。
