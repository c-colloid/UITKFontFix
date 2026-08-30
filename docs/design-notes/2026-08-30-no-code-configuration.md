# 設計ノート: コード不要のフォント設定経路(v0.3.0)

- 日付: 2026-08-30 / ステータス: 採用済み
- 発端: ユーザー要望「コード以外の場所からフォントを選択できる経路が欲しい」。
  A案(Project Settings)とB案(診断ウィンドウ内編集)の合わせ技を採用

## 前提(適用モデルの確認)

設定UIが変えるのは「**どのフォントが解決されるか**」(候補リスト等)であり、
UIへの適用経路は従来どおり各ウィンドウの `ApplyCjkUi` / `ApplyMono` 呼び出し。
- 効く範囲: 本パッケージ経由でフォント適用しているすべてのUI
- 効かない範囲: パッケージ未使用のエディタUI(エディタ全体のフォント差し替え
  APIはUnityに存在しない)、および**すでに開いているウィンドウ**(インライン
  スタイルは適用時にアセットを固定するため、再構築/再オープンで反映)

## 選択肢と裁定

| 論点 | 採用 | 棄却案と理由 |
|---|---|---|
| 保存先 | `ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json`(JsonUtility・schemaVersion付き) | EditorPrefs単独(B案のみ)はチーム共有不可。ScriptableObjectアセットは初期設計ノートで棄却済みの罠(SO初期化子でのsystemLanguage読み)をユーザーに近づけるうえ、Assets/配下を汚す。`ProjectSettings/Packages/<pkg>/` はUnity公式パッケージも使う慣行でVCS管理対象 |
| 保存の粒度 | **全5値のスナップショット**(候補3リスト+スタイル2名)。ファイルの存在=プロジェクト上書き。「Use package defaults」ボタンでファイル削除+既定復帰 | フィールド単位のoverrideフラグはUI・DTO・説明の全部を複雑にする割に、v0.3の利用者価値がない |
| 読み込みタイミング | パッケージの `[InitializeOnLoadMethod]` でファイルが在れば適用(例外は握りつぶし、壊れたファイルは無視) | |
| コード設定との優先順位 | **ファイル先行適用→後続のコード代入が勝つ**+診断レポートに「ファイルと現在値の乖離」を表示 | ファイル絶対優先(delayCallで再適用)はコードで明示した意図を黙って覆す。検出表示があれば、両方使う上級者にも状況が見える |
| 設定UI(A) | `SettingsProvider`(Project/UITK Font Fix、UI Toolkit描画)。候補リストは**1行1名のTextField(複数行)**。Apply / Save to project / Use package defaults / Open Diagnostics | ListView等のリッチな並べ替えUIは初版では過剰。行テキストは説明不要で堅牢 |
| 診断ウィンドウ(B) | 折りたたみの編集セクションとして**同じフォーム部品を共用**(`FontFixSettingsUi`)。Apply & Re-probe で試行錯誤→確定したら Save to project(=A形式へ書き出し) | 別実装の二重フォームは乖離バグの温床 |
| 反映範囲の明示 | 診断レポートに settings.json の有無・現在値との同期状態を表示 | |

## 実装構成(すべてEditorアセンブリ)

- `FontFixProjectSettings`(public static): `FilePath` / `Exists` / `Save()`
  (現在の実効値を書き出し)/ `TryLoadAndApply()` / `Delete()` /
  `CaptureData()` / `MatchesCurrentSettings()`。`[InitializeOnLoadMethod]`
  で `TryLoadAndApply()`
- `FontFixSettingsData`(internal, `[Serializable]`): schemaVersion+5値。
  JsonUtilityでnull配列/文字列は「その項目をスキップ」(壊れた/古いファイルへの防御。
  `CjkUiBoldStyleName` の空文字=配線無効は正しく往復する)
- `FontFixSettingsUi`(internal static): フォーム生成の共用部品
- `FontFixSettingsProvider`: SettingsProvider登録
- 診断ウィンドウ: 編集Foldout追加、レポートにファイル状態セクション追加

## テスト(回帰ガード)

保存→改変→読込→適用の往復、欠損/破損ファイルの無害性、Delete、
実効値キャプチャ、ファイルパス規約、同期判定、Provider生成スモーク。
テストはサンドボックスの ProjectSettings を汚すため teardown で必ず削除。

## 追記(同日): レビュー結果の反映

実装後レビュー(sonnet)で指摘された5点+文言2点を反映した。

実バグ:

1. **`MatchesCurrentSettings()` の欠損フィールド偽ドリフト**: ApplyData は
   null(=ファイルに無い)フィールドをスキップするのに、比較側は全フィールド
   を突き合わせていたため、手編集で一部フィールドだけ書いたファイルが読込直後
   から永久に DIFFERS 表示になる。→ 比較側も同じスキップ規則に(欠損は一致
   扱い)。部分ファイルの回帰テストを追加。
2. **Re-probe が未Applyの編集を無視**: 診断ウィンドウで候補を書き換えて
   Re-probe しても、Apply を押していなければ旧値で再解決される(編集→再解決の
   ループという設計意図に反する)。→ `CreateForm` がフォームハンドル
   (`FontFixSettingsForm.ApplyFields`)を返すようにし、Re-probe は
   「フィールド反映→キャッシュ破棄→レポート更新」の順で実行。

堅牢化:

3. `FilePath` を `Application.dataPath` 起点の絶対パスに(バッチ起動の
   カレントディレクトリ差異への防御)。相対の規約文字列は `RelativeFilePath`
   定数として公開し、テストは「絶対+規約サフィックス」を検証。
4. `Save()` を一時ファイル書き込み→スワップに(別プロセス/診断更新が
   途中状態を読まない)。
5. テストアセンブリへ `InternalsVisibleTo`(internal な UI 部品・DTO の
   直接テスト)。

文言の正直化:

- 「ファイルは利用側コードより**先に**適用される」という断定を、README(英/日)
  ・CHANGELOG・XMLドキュメントすべてで「参照アセンブリ先行初期化という**慣例**
  であり保証仕様ではない」に修正。乖離は診断レポートが常に表示するので、順序が
  崩れても状況は見える。
- DIFFERS 表示の原因列挙に「セッション開始後にディスク上で更新された
  (次のドメインリロードで再適用)」を追加。

未検証のまま残る対話的挙動(バッチ環境では検証不能):

- SettingsProvider ペインの実描画・検索キーワード・activateHandler の発火
- 診断ウィンドウ Foldout 内の編集値がレポート更新をまたいで保持されること

→ `docs/verify/2026-08-30-v0.3-interactive-checklist.md` に手動確認手順を記録。
