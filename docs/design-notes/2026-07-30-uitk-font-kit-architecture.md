# 設計ノート: UITK Font Kit v1 アーキテクチャ

- 日付: 2026-07-30 / ステータス: 採用済み
- 関連: UnityAgentPanel の FontLoader/IconLoader の汎用パッケージ化(キックオフ文書のグラウンドトゥルース GT#1〜GT#12 を設計根拠とする)

## 決定事項サマリ

1. アセンブリは Runtime + Editor の2分割
2. `FromSDFFont` 改名は**未確認** — 改名前提の `#if` 分岐は張らず、シムファイル+予約 define を単一変更点として用意
3. `FontKitSettings` は static 設定クラス(ScriptableObject にしない)
4. モノフォントの OS 候補は TextCore フェイスプローブでゲート(UAP 実装を踏襲)

## 1. アセンブリ分割

| 案 | 内容 | 判定 | 根拠 |
|---|---|---|---|
| A | Editor 単一アセンブリ | 棄却 | v2 の Runtime 対応時に公開 API の移動(破壊的変更)が必要になる |
| B | Runtime(純粋関数)+ Editor(解決・適用) | **採用** | `StripVariationSelectors` / `ShouldPreferCjkUi` / 既定候補リスト / 安全グリフ表はエディタ API に依存しない純粋コード。Runtime 側に置けば v2 でそのまま Runtime 対応の土台になり、Editor 側は今後もエディタ専用のまま保てる |

- `Colloid.UitkFontKit`(Runtime): `TextSanitizer` / `CjkLanguage` / `SafeGlyphs` / `FontKitDefaults`
- `Colloid.UitkFontKit.Editor`(Editor専用): `FontKit`(ファサード=キックオフ指定の API 面)/ `FontKitSettings` / `FontShims`
- 空の Runtime asmdef は「スクリプトなし」警告を出すため、構造準備は実コード(純粋関数)を置く形で行う

## 2. FontDefinition API シム(GT#3 の裏取り結果)

キックオフの GT#3 は「後のメジャーで `FromSDFFontAsset` に改名(実バージョンは要確認)」としていたが、
2026-07-30 の調査では**改名の証拠は見つからなかった**:

- docs.unity3d.com の 2022.1 / 最新(6000.x)の Scripting API いずれにも `FontDefinition.FromSDFFont(TextCore.Text.FontAsset)` が掲載されている
- `FromSDFFontAsset` という API 名は公式ドキュメント・フォーラムのどちらからも確認できなかった
- 制約: この調査環境からは docs.unity3d.com への直接アクセスが遮断されており(プロキシ 403)、検索結果スニペット経由の確認である。2023.x エディタ実機での最終確認は M3 で行う

| 案 | 内容 | 判定 | 根拠 |
|---|---|---|---|
| A | `#if UNITY_2023_2_OR_NEWER` で `FromSDFFontAsset` を呼ぶ | 棄却 | 改名が確認できていない以上、未検証バージョン分岐は 2023.x でのコンパイル切れを自ら作るリスク |
| B | シムファイル `FontShims` に集約+予約 define `UITK_FONT_KIT_FROMSDFFONTASSET` | **採用** | 既定は全バージョンで `FromSDFFont` を直呼び(ドキュメント上 2021.2〜6000.x で存在)。万一改名版にしか無い環境が現れたら、define を立てるか `FontShims` 1ファイルの修正で済む |

## 3. FontKitSettings の形態

| 案 | 内容 | 判定 | 根拠 |
|---|---|---|---|
| A | ScriptableObject(Project Settings 資産) | 棄却 | GT#10 の実事故(SO コンストラクタ/フィールド初期化子での `Application.systemLanguage` 例外によるシングルトン破壊)と同型のライフサイクル罠を消費者に露出する。資産管理・.meta・保存先の説明コストも増える |
| B | static プロパティによるコード設定 | **採用** | 依存ゼロ・テスト容易・「数行で導入」に合致。値が実際に変わった時だけキャッシュ無効化(`ResetToDefaults` を teardown に置いてもキャッシュが無駄に飛ばない)。SO ベースの設定 UI は将来ニーズが出た時に上に被せられる |

## 4. モノフォント解決の OS 候補ゲート

UAP 実装を踏襲: 編集器同梱 RobotoMono(GT#5)→ OS 単一名フォントを `FontEngine.LoadFontFace` プローブでゲート → 既定ラベルフォント。

GT#1 により OS 動的フォントはこのプローブを事実上通過しない(常に `Invalid_File`)ため、OS 候補段は
「RobotoMono が無い将来バージョンで、かつプローブが直る環境」でのみ効く防御層である。これは意図的:
プローブを外すと GT#4 の「ロード不能フェイス → テキスト空」事故を再導入する。CJK 側は GT#2 の通り
`FontAsset.CreateFontAsset(family, style)`(DynamicOS)経路のみを使い、`LoadFontFace(Font)` は使わない。

## 5. 実行環境の制約(このセッション)

- 本セッションはクラウドの Linux コンテナで、Unity 実行・uloop・Windows サンドボックス(AITemp)には接触できない
- よって「サンドボックス全グリーンでのみコミット」は本環境では実行不能。M1 は静的レビュー(ASCII 監査・API 照合・参照実装との差分レビュー)まで行いコミットし、バッチ検証は `docs/verify/M1-verification.md` の手順でユーザー環境(または AITemp に接続できるセッション)で実施する
- 回帰ガードテスト自体は本コミットに含まれており、検証実行のみが後段に残る
