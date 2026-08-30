# 設計ノート: プレイモード往復で壊れる DynamicOS FontAsset のライフサイクル対策

- 日付: 2026-08-30 / ステータス: **設計提案(実装前)**
- 発端: 下流ツールからの不具合報告。プレイモードの出入り後、本パッケージが生成した
  CJK FontAsset で描画するたびに TextCore 由来の `MissingReferenceException` が発生し
  UI が崩れる。**FontAsset 本体は生きているのに、内部のアトラス `Texture2D` と
  `Material` だけが破棄されている**。下流側は「アトラスが生きている場合のみ適用する」
  ガードで暫定回避済み。

## 1. 事実確認(一次ソース)

検証環境に Unity ライセンスが無いため実機再現は行えない。代わりに
UnityCsReference の `2022.3` ブランチ(取得コミットは 2022.3.76f1 相当。CI 標的は
2022.3.22f1 のため行番号は前後しうる)と Unity 2022.3 スクリプトリファレンスを
一次ソースとして用いた。

### 1.1 生成側: TextCore は子オブジェクトに hideFlags を一切付けない

- `FontAsset.CreateFontAsset(familyName, styleName)`(`FontAsset.cs:522-529`)は
  `AtlasPopulationMode.DynamicOS` / SDFAA / 1024x1024 / padding 9 で
  `CreateFontAssetInstance` に委譲する。本パッケージが使う唯一の経路
  (`FontShims.TryCreateOsFontAsset`)。
- `CreateFontAssetInstance`(`FontAsset.cs:608-698`)は3つの UnityEngine.Object を作る:
  FontAsset 本体 / `new Texture2D(1, 1, TextureFormat.Alpha8, false)`(`:636`) /
  `new Material(TextShaderUtilities.ShaderRef_MobileSDF)`(`:648`, `:662`)。
- **`FontAsset.cs` 全体で `hideFlags` の代入は 0 件**(`grep -c hideFlags` = 0)。
  つまり子2つは `HideFlags.None` のまま生まれる。
- 遅延追加ページも同様: `SetupNewAtlasTexture`(`FontAsset.cs:2769-2790`)は
  `new Texture2D(m_AtlasWidth, m_AtlasHeight, Alpha8, false)` を作るだけで
  hideFlags を付けない。呼び出し元は `:2337` `:2516` `:2713`(グリフ追加時)。

### 1.2 Unity 自身の同等コードは3つ全部に DontSave を付けている

`TextSettings.GetCachedFontAssetInternal`(`TextSettings.cs:305-312`):

```
fontAsset.hideFlags = HideFlags.DontSave;
fontAsset.atlasTextures[0].hideFlags = HideFlags.DontSave;
fontAsset.material.hideFlags = HideFlags.DontSave;
fontAsset.isMultiAtlasTexturesEnabled = true;
```

これがランタイム生成 FontAsset をキャッシュ保持する際の Unity 公式の作法である。
なお UITK は、`style.unityFontDefinition` に **FontAsset を直接渡した場合はこの経路を
通らない**(`UITKTextHandle.GetFontAsset`: fontAsset が非 null ならそのまま返す)。
つまり hideFlags の責任は完全に呼び出し側=本パッケージにある。

### 1.3 本パッケージ側の欠落

`FontFix.CreateOwnedCjkAsset`(`Editor/FontFix.cs`)は
`asset.hideFlags = HideFlags.HideAndDontSave;` を **FontAsset 本体にだけ** 設定し、
`NameCreatedAsset` は material と atlasTextures[0] を **リネームするだけ**で
hideFlags には触れていない。`HideAndDontSave` は `DontUnloadUnusedAsset` を含むため
本体だけが生き残る。報告症状(本体は生存、子だけ破棄)と完全に一致する。

これは base の CJK アセットだけでなく、`GetCjkUiFontAsset(style)` で作る全フェイス
(既定で `fontWeightTable[7]` に配線される Bold を含む)に等しく当てはまる。
下流の暫定ガードは base しか見ていないため、Bold/Semibold 経路は未カバーだった。

### 1.4 破棄されると何が起きるか(例外の発生地点)

- `TextAsset.material` は `get => m_Material` のみ(`TextAsset.cs:56-60`)、
  `FontAsset.atlasTexture` も `m_AtlasTexture` が null のとき
  `atlasTextures[0]` を読み直すだけ(`FontAsset.cs:297-308`)。**自己修復しない**。
- 描画経路での参照:
  `UITKTextHandle.cs:322` の `tgs.material = tgs.fontAsset.material;`、および
  `MaterialManager.cs:68-72` の `Texture tex = fontAsset.atlasTextures[i]; tex.GetInstanceID();`
  — 破棄済みオブジェクトへのメンバアクセスで `MissingReferenceException`。

### 1.5 いつ破棄されるのか

- `HideFlags.DontSave` の定義(2022.3 スクリプトリファレンス):
  「The object will not be saved to the Scene. **It will not be destroyed when a new
  Scene is loaded.**」= `HideFlags.None` のオブジェクトはシーンロードで破棄されうる。
- プレイモード開始はシーンをリロードし、終了時はバックアップからシーンを復元する。
- ドメインリロード**有効**(既定)の場合: 開始時に `beforeAssemblyReload` が発火し、
  本パッケージの `DestroyOwned` が自分のアセットを破棄する
  (`FontAsset.OnDestroy` が `DestroyAtlasTextures()` と `DestroyImmediate(m_Material)`
  を行う: `FontAsset.cs:721-727`)。よって**壊れるのは終了時**である:
  プレイ中に再解決されて生成されたアセットが、**ドメインリロードを伴わない**終了処理
  (シーン復元)で子だけを失い、static キャッシュはそれを保持し続ける。
- Enter Play Mode Options で **Reload Domain を無効**にしている場合:
  static も購読も維持される(`Manual/DomainReloading.html`)ため、
  `beforeAssemblyReload` は一切発火せず、**開始時にも**同じ壊れ方をする。

### 1.6 未実証(正直な線引き)

- 「どのエンジン内部パスが子オブジェクトを破棄するか」(シーンロード時の掃除か、
  プレイモード終了時のネイティブ後始末か)は managed ソースに無く**未実証**。
  ただし修正の妥当性はこれに依存しない: Unity 自身が同じ状況で DontSave を
  付けている(1.2)ことが根拠として十分である。
- `PlayModeStateChange` の各値と破棄処理の厳密な前後関係も、ネイティブ側のため未実証。
  `Exiting*` は遷移前に同期発火、`Entered*` は「次のエディタ更新」で遅延発火、という
  ドキュメント記述のみが確定事実。→ フックだけに頼らず**取得時チェック**を併用する
  設計にする(下記 2.3)。
- クラシック `Font`(OS モノスペース経路)の `Font.material` に同種の問題があるかは
  ネイティブ実装のため**未実証**。推測で先回り修正はしない(残課題 §7)。

## 2. 修正案

### 2.1 全体像(3層)

| 層 | 内容 | 効果 |
|---|---|---|
| 予防 | 生成時に material と**全アトラスページ**へ `HideFlags.DontSave` を付与。プレイモード遷移の直前に再スタンプ | 報告症状そのものを起こさせない(本命) |
| 検出 | キャッシュから渡す前に「本体・material・page0」の生存を確認 | フックの前後関係に依存しない安全網 |
| 回復 | **同一インスタンスのまま**その場修復。不可能なら破棄→再解決し、消費者へ通知 | 既に適用済みの VisualElement も自動的に治る |

「その場修復」が要点である。新しいインスタンスを作り直しても、既に
`style.unityFontDefinition` に古いインスタンスを持っている要素は治らない。

### 2.2 予防: hideFlags スタンプ

- 生成時(`CreateOwnedCjkAsset`)に `asset.material` と `asset.atlasTextures[i]`(全要素)へ
  `HideFlags.DontSave` を付与。Unity 自身の実装(1.2)と同一の値を選ぶ。
  本体は現状どおり `HideAndDontSave`(DontSave の上位集合)を維持。
- 遅延追加ページ対策として、`EditorApplication.playModeStateChanged` の
  `ExitingEditMode` / `ExitingPlayMode`(いずれも遷移前に同期発火)で全ページを再スタンプ。
- 取得時(getter)にも同じ再スタンプを通す(配列長は通常 1〜数個で、コストは無視できる)。

### 2.3 検出

`IsUsable(asset)` = `asset != null && asset.material != null &&
asset.atlasTextures != null && asset.atlasTextures.Length > 0 && asset.atlasTextures[0] != null`。
すべて Unity の fake-null 比較で、破棄済みでも例外にならない。
**`asset.atlasTexture` プロパティで調べてはいけない**(配列が null/空だと NRE:
`FontAsset.cs:297-306`)。

このチェックを `CjkUiFontAsset` / `GetCjkUiFontAsset` / `Apply*` の経路に入れる。
フック(2.2)の発火順がネイティブ依存で未実証(1.6)である以上、
**タイミング非依存の防御はこの取得時チェックだけ**である。

### 2.4 回復: その場修復の手順

前提として `ClearFontAssetData` を**いきなり呼んではならない**:
`ClearAtlasTextures` は `m_AtlasTextures[0]` に対して `texture.isReadable` を読む
(`FontAsset.cs:3128-3131`)ため、page0 が破棄済みだと**その場で例外**になる。
また配列長 0 のときは `Array.Resize(ref m_AtlasTextures, 1)` の後 `[0]` が C# null で NRE。

手順(全体を try/catch で包み、never-throws 契約を維持):

1. 形状ガード: `atlasWidth > 0 && atlasHeight > 0 && atlasPadding >= 0` でなければ
   その場修復を諦めて再構築へ(`ClearFontAssetTables` が `GlyphRect` をこの値で作るため)。
2. **テンプレート用ドナー**を作る: `FontShims.TryCreateOsFontAsset(family, style)`。
   null なら(ファミリが消えた等)修復を諦めて再構築へ。
3. material が死んでいれば `asset.material = new Material(donor.material)`
   (`Material` のコピーコンストラクタ。`TextShaderUtilities.ShaderRef_MobileSDF` は
   internal で選べないため、正しいシェーダと float 群
   `_GradientScale` `_TextureWidth` `_TextureHeight` `_WeightNormal` `_WeightBold` を
   まとめて引き継ぐ唯一の合法手段)。
4. page0 が死んでいれば `new Texture2D(1, 1, TextureFormat.Alpha8, false)` を作り
   (`CreateFontAssetInstance` と同一形状。スクリプト生成テクスチャは既定で readable)、
   配列が null/空なら `asset.atlasTextures = new Texture2D[1]` してから `[0]` に代入。
   **配列ごと差し替えない**(生きている追加ページを漏らさないため)。
5. `asset.material.SetTexture(TextShaderUtilities.ID_MainTex, asset.atlasTextures[0])`
   で `_MainTex` を貼り直す(片方だけ死んでいた場合も必要)。
6. 命名と hideFlags を先に済ませる(`ReadFontAssetDefinition` が name からハッシュを
   再計算するため: `FontAsset.cs:811-813`)。
7. `asset.ClearFontAssetData(true)` を呼ぶ。グリフ/キャラクタテーブルと
   `freeGlyphRects`/`usedGlyphRects`(いずれも internal で外から触れない)を
   リセットできる唯一の public 経路。`true` で 1x1 の初期状態に戻し、以降の
   `TryAddCharacter` が必要に応じて拡張する。
8. ドナーは `finally` で `Object.DestroyImmediate(donor)`。**ドナーからは何も移さない**
   ので、ドナーの `OnDestroy` は自分が作った material とページだけを破棄する
   = `DestroyImmediate(null)` の未定義挙動に依存しない。
9. スタイル辞書の全アセット→base の順に修復する。`fontWeightTable` は
   `ClearFontAssetData`/`ReadFontAssetDefinition` のいずれからも触られないため
   再配線は不要だが、**Bold フェイスは別 FontAsset なので個別に修復が必要**。

修復不能時のみ、破棄→再解決(=インスタンス交替)にフォールバックする。

### 2.5 消費者への通知: 公開 API を1つだけ追加

```
public static event System.Action CachesInvalidated;
```

- 発火するのは「**手渡し済みのインスタンスが別物に入れ替わった/破棄された**」ときだけ:
  `ResetCaches()` / `FontFixSettings` 変更 / 修復不能による再構築。
- **その場修復では発火しない**(同一インスタンスのまま治るので消費者に用は無い)。
- `beforeAssemblyReload` 経由の破棄でも発火しない(直後に購読ごと消えるため無意味)。
- 破棄と同一コールスタック内で**同期発火**する(遅延させると、その隙間の再描画で
  まさに今回の例外が起きる)。
- 購読者ごとに try/catch し、例外は `Debug.LogException` で可視化する
  (パッケージの「黙って握り潰す」既定からの意図的な例外)。
- 再入ガードと、`ResetToDefaults()`/プロジェクト設定適用のような複数プロパティ操作を
  1回にまとめる内部バッチスコープを設ける(5プロパティ×フェイスロードの空回りを防ぐ)。

これは**今回の play mode 問題だけの API ではない**。既存の
`FontFixSettings` 変更経路にも同じ穴があり(§3)、同じ1メンバで塞がる。

### 2.6 自分自身での適用(ドッグフーディング)

`FontFixDiagnosticsWindow`(Re-probe と設定フォーム)と Project Settings ペインは、
自分のルートに `ApplyCjkUi` した上で、自分のボタンから `InvalidateCaches` を起こし、
**自分が使っている FontAsset を破棄している**。新イベントを購読して再適用させる。
これが新 API の回帰の証人になる。

## 3. 併せて直す既存欠陥(今回の調査で判明)

| # | 欠陥 | 影響 |
|---|---|---|
| A | `CreateOwnedCjkAsset` の hideFlags 欠落は base だけでなく**全フェイス**(Bold 含む)に及ぶ | 下流の暫定ガード(base のみ確認)を素通りする経路が存在した |
| B | `InvalidateCaches`→`DestroyOwned` が、生きている VisualElement が参照中の FontAsset を同期破棄する。`FontFixSettingsUi` の「開いているウィンドウは現在のフォントを保持」という記述と矛盾 | 設定変更のたびに、開いている自前 UI が壊れた参照を持つ。2.5 のイベントで塞ぐ |
| C | B の実例がパッケージ同梱 UI 自身(診断ウィンドウ / Project Settings ペイン)に存在 | 2.6 で修正 |

## 4. 選択肢と裁定

| 論点 | 採用 | 棄却案と理由 |
|---|---|---|
| 予防の値 | 子(material/atlas)は `HideFlags.DontSave`、本体は現状の `HideAndDontSave` | 子も `HideAndDontSave` にする案: `HideInHierarchy`/`NotEditable` は転送先で無意味。Unity 自身の作法(`TextSettings.cs:308-310`)に合わせる方が将来差分を読みやすい |
| ページ追加への追随 | 遷移フック(`Exiting*`)+取得時の再スタンプ | 生成時のみ: `SetupNewAtlasTexture` が後から無防備なページを足すため不十分。ポーリング: コスト過大 |
| 回復方式 | **同一インスタンスをその場修復**、不能時のみ再構築 | 常に再構築: 適用済み要素が治らず、下流は結局自前ガードを維持することになる |
| material の作り直し | ドナー FontAsset を**テンプレート**として `new Material(donor.material)` | (a) ドナーからの移植: ドナーの `OnDestroy` が移植先の material/texture を破棄する(同じバグを自分で再生産する)。無害化には `DestroyImmediate(null)` の未実証挙動か犠牲 material が必要 (b) 生成時にシェーダ+float 群をスナップショット: 安いがプロパティ列挙の保守が必要で、フェイス再ロード可否の早期検出も失う |
| テーブル初期化 | `ClearFontAssetData(true)` を **material/page0 差し替えの後に** 呼ぶ | 先に呼ぶ: `isReadable` 参照で確実に例外。呼ばない: 旧アトラス座標のグリフ表が残り、無言で空白/化けた描画になる |
| 検出の置き場所 | 取得時チェック + 遷移フック(両方) | フックのみ: `Entered*` は「次の更新」で遅延発火し、その前の再描画を守れない。取得時のみ: 誰も getter を呼ばないまま再描画される経路を守れない |
| 公開 API | `CachesInvalidated` イベント1つだけ | `VerifyAndRepair()` メソッド: 呼び忘れたら効かず、インスタンス交替も救えない。世代カウンタのポーリング: 消費者に更新ループを強制し、ポーリング間隔の隙間が残る。API 無し(文書のみ): パッケージ都合の再構築を消費者が観測できない。イベント引数付き: 内部の修復/再構築の区別を公開契約に固定してしまう |
| 破棄の遅延 | しない(`ResetCaches` は同期破棄のまま) | 「破棄せず退役させる」案: アトラスが積み上がる保証違反、既存の回帰テスト(`ResetCaches_DestroysStyleAssets_AndReprobeRewiresFresh`)とも矛盾 |
| バージョン | **0.4.0**(public イベント追加=マイナー) | 0.3.1: 公開 API が増えるため semver 上不適切 |

## 5. 実装計画(ファイル単位)

- `Editor/FontAssetLifecycle.cs`(新規, internal static): `Stamp` / `Adopt`(命名+スタンプ) /
  `IsUsable` / `TryRepair(asset, donorFactory)`。ドナー生成をデリゲートで受けることで、
  OS フォント非依存のテストが書ける。
- `Editor/FontShims.cs`: `ClearFontAssetData` をシームへ集約
  (public だが「将来 internal 化しうる」と自身のドキュメントコメントが警告している:
  `FontAsset.cs:2985-2986`)。
- `Editor/FontFix.cs`: 生成時スタンプ、取得時の生存確認と修復、
  `playModeStateChanged` 購読(既存の `_cleanupHooked` と同じ遅延1回購読パターン)、
  `CachesInvalidated` の発火とバッチ/再入ガード。
- `Editor/Diagnostics/FontFixDiagnostics.cs`: アトラス節に material/ページの生存と
  hideFlags を出力。`FontFixDiagnosticsWindow.cs` / `FontFixSettingsProvider.cs`:
  イベント購読で自己再適用。
- `Tests/Editor/FontAssetLifecycleTests.cs`(新規)+ `.cs.meta`(guid は
  `uuid4().hex`、既存 `.meta` と同一テンプレート)。
- `README.md` / `README.ja.md`: 罠一覧、API 表(`CjkUiFontAsset` 行 + 新イベント行)、
  「検証済みの挙動」に項目12を追加。`CHANGELOG.md` に `### Fixed` / `### Added`、
  `package.json` を 0.4.0 へ。

## 6. 回帰ガード(EditMode、Linux CI で実走する形)

CI コンテナ(unityci/editor:ubuntu-2022.3.22f1-base-3)には CJK フォントが無いが、
`CjkFaceTests` と同じ「DejaVu Sans / Liberation Sans を候補に据える」パターンで
**実物の DynamicOS FontAsset を headless 生成できる**ことが実測済み
(`docs/verify/2026-07-30-linux-batch-results.md`)。これに乗せる:

1. 生成直後、`material.hideFlags` と `atlasTextures[0].hideFlags` に
   `DontSaveInEditor|DontUnloadUnusedAsset` が立っていること。
2. `atlasTextures` がライブ配列であること(`[0]` 代入が読み戻せる)。
   将来の Unity がコピーを返すようになった場合の検出。
3. **破壊シミュレーション**: `DestroyImmediate(asset.material)` と
   `DestroyImmediate(asset.atlasTextures[0])` の後、`IsUsable` が false になり、
   `TryRepair` が例外なく成功し、material/page0 が生き返り、`isReadable` が true、
   ルックアップが再構築され、Bold スロットの参照が保たれていること。
4. 取得時の自己修復: 上記の破壊後に `FontFix.CjkUiFontAsset` を読むと
   **同一インスタンス**が使用可能な状態で返ること。
5. `CachesInvalidated` が `ResetCaches`/設定変更で発火し、その場修復では発火しないこと。
   `ResetToDefaults()` で発火が1回にまとまること。購読者が例外を投げても
   無効化処理が完走すること。
6. 診断レポートが新セクションを含み、ASCII のままであること。

## 7. 検証計画と残課題

- 本コンテナに Unity ライセンスが無く、コンパイル/EditMode を実行できない。
  実装コミットは (a) CI(`workflow_dispatch`、ブランチ上で実行、release ジョブは
  main 限定なので発火しない)で compile+EditMode グリーンを確認するか、
  (b) それが不可なら CLAUDE.md §2 に従い**未検証である旨をコミットメッセージに明記**する。
- 実機(インタラクティブエディタ)でしか確認できない項目:
  プレイモード往復での実挙動、その場修復後に再描画が新 material/texture を拾うか
  (UIR が描画コマンドに旧テクスチャを抱えていないか)。
  → `docs/verify/` のチェックリストに追加する。
- 未実証の残課題: クラシック `Font`(OS モノスペース経路)の `Font.material` が
  同じ破棄を受けるか。ネイティブ実装のため推測での先回り修正はしない。
  実機で「OS mono フォントを適用した要素でプレイモード往復」を1回確認して判断する。
