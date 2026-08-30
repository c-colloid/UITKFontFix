# UITK Font Fix

[English](README.md) | 日本語

Unity UI Toolkit 製の**エディタUI**向けフォントユーティリティです。
日本語などの CJK 文字をきれいに表示できる UI フォントと、コード表示用の
モノスペースフォントを「確実に表示できる形」で解決し、UI Toolkit の
スタイル継承と衝突しない方法で適用します。モデルやユーザー入力由来の
テキストが警告スパムや豆腐(□)を起こすのを防ぐヘルパも入っています。

依存パッケージなし・MIT ライセンス。エディタ用途が第一です
(Unity 2022.3 LTS 以降。ランタイム対応は将来版に向けて構造だけ準備済み)。

## UITK フォント処理の罠と、このパッケージの対処

Unity 2022.3 の UI Toolkit エディタUIには、踏みやすいのに原因究明が
難しいフォントの落とし穴がいくつもあります。このパッケージが**自動で
回避してくれるもの**と、**用意されたヘルパやテストをあなたが呼ぶことで
対処するもの**があるので、それぞれ明記します。

- **CJK 文字が「まだら太字」になる。** エディタ標準フォント(Inter)に
  CJK グリフがないため、1文字ごとに OS フォントへフォールバックし、
  文字列の途中でフォントや太さが混ざります。
  → **1回の呼び出しで対処**: ウィンドウのルートに `ApplyCjkUi` を
  呼ぶだけです。
- **OS フォントが無言で「何も表示しない」ことがある。**
  `Font.CreateDynamicFontFromOSFont` は UI Toolkit が読み込めない
  フェイスを返すことがあり("Unable to load font face")、しかも
  `FontEngine` では事前に検証できません。
  → **自動で回避**: このパッケージのリゾルバは、生の OS `Font` を
  UI Toolkit に渡す経路そのものを使いません。
- **USS では OS フォントを名前で指定できない。** これは Unity 側の
  制約で、どのパッケージにも解除できません。
  → **迂回路を用意**: C# から `style.unityFontDefinition` を書くのが
  唯一確実な経路であり、このパッケージの適用ヘルパがそれを包んでいます。
- **モデル/ユーザー由来のテキストが警告スパムと豆腐を生む。**
  異体字セレクタ(警告記号の後ろに付く U+FE0F など)や絵文字には
  エディタフォントのグリフがありません。
  → **あなたが呼ぶヘルパで対処**: 表示前に `SanitizeDisplayText` を
  通してください。ソースに埋め込んだ固定文字列は `GlyphAudit` の
  テストが検出します。
- **`Application.systemLanguage` はシリアライズ中に読むと例外を投げる。**
  素朴な言語判定が設定アセットの読み込みごと壊すことがあります。
  → **安全な形へ誘導(解決ではない)**: 判定ヘルパは言語を引数で
  受け取る純粋関数なので、読み取りを安全なタイミング(`OnEnable` 以降)
  に置く設計に自然と導かれます。実際にそこで呼ぶのはあなたの役目です
  (レシピ3参照)。

`FontFix` はこれらの実証済みの回避策を小さな窓口にまとめたものです。
リゾルバはキャッシュされ、例外を投げず、どの候補が採用されたかを
診断文字列で報告します。ほかに、継承関係を明記した適用ヘルパ、
テキストのサニタイザ、ソースレベルのグリフ監査、診断ウィンドウが
含まれます。

- インストール
- クイックスタート
- レシピ
- API リファレンス
- 検証済みの挙動(この設計の根拠)
- Unity バージョン互換性
- サンプル
- ライセンス

## インストール

git URL で追加します(Package Manager > `+` > *Add package from git URL...*):

```
https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-fix
```

または `jp.colloid.uitk-font-fix` フォルダをプロジェクトの `Packages/`
フォルダに置いても使えます(組み込みパッケージ)。

2つのパッケージアセンブリ(`Colloid.UitkFontFix` と
`Colloid.UitkFontFix.Editor`)はどちらも自動参照されるので、`Assets/`
直下のスクリプトからは何も設定せずにそのまま呼べます。自前の asmdef の
中から使う場合は、通常どおりアセンブリ参照を追加してください。

## クイックスタート

`Assets/Editor/FontFixQuickStart.cs` として保存し、
*Window > Font Fix Quick Start* を開いてください:

下のウィンドウは、このパッケージの使い方をひととおり見せる例です。
まずコンテナのルートに Latin+CJK フォントを適用します。こうすると
配下の要素すべてがそのフォントを継承します(`CreateGUI` はシリアライズが
終わった後に走るので、ここでシステム言語を読むのは安全です)。ふつうの
ラベルには何もする必要がありません — ルートのフォントを継承するだけです。
コードを表示する要素にはモノスペースフォントをインラインで適用します。
インラインスタイルは継承値より常に強いので、CJK コンテナの中でも
モノスペースのまま表示されます。最後に、モデル出力・ユーザー入力・
ファイル・クリップボードなどの自由入力テキストは、表示前にサニタイズ
してください。除去されるのは不可視のコードポイントだけなので、見た目の
内容は変わりません。

```csharp
using Colloid.UitkFontFix;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class FontFixQuickStart : EditorWindow
{
    [MenuItem("Window/Font Fix Quick Start")]
    public static void Open()
    {
        GetWindow<FontFixQuickStart>("Font Fix Quick Start");
    }

    public void CreateGUI()
    {
        if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
        {
            FontFix.ApplyCjkUi(rootVisualElement);
        }

        rootVisualElement.Add(new Label("Ready")); // inherits the root font

        var code = new Label("if (x == 0) { return; }");
        FontFix.ApplyMono(code);
        rootVisualElement.Add(code);

        string raw = EditorGUIUtility.systemCopyBuffer;
        rootVisualElement.Add(new Label(FontFix.SanitizeDisplayText(raw)));
    }
}
```

構成のルールはこれだけです: `ApplyCjkUi` は**コンテナのルート**に
(配下がすべて継承)、`ApplyMono` は**末端(リーフ)の要素**に
(インラインは継承に勝つので、CJK コンテナ内でもコードはモノスペースの
まま)。同じ要素に両方を呼んではいけません — どちらもインライン書き込み
なので、後に呼んだほうが黙って勝ちます。

## レシピ

### 1. CJK テキストとモノスペースコードが混在するウィンドウ

CJK の UI テキストとモノスペースのコードを1つのウィンドウに混ぜる例です。
CJK 対応フォントをルートに1回適用すれば、配下の `Label` はすべてそれを
継承するので、日本語・中国語・韓国語の文字列が「まだらフォールバック」
ではなく単一フォントで表示されます。結果表示のラベルはルートのフォントを
継承するだけ。その下の行では、メッセージ側のラベルは継承のまま
プロポーショナルに、隣のコードラベルだけ `ApplyMono` をインラインで
呼んで、局所的にモノスペースにしています。一番下の複数行ログも同じで、
CJK ルートのどれだけ深くにいても、そのリーフに `ApplyMono` を1回呼べば
効きます。

```csharp
using Colloid.UitkFontFix;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class BuildLogWindow : EditorWindow
{
    [MenuItem("Window/Build Log")]
    public static void Open()
    {
        GetWindow<BuildLogWindow>("Build Log");
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
        {
            FontFix.ApplyCjkUi(root);
        }

        root.Add(new Label("Build result")); // inherits the root font

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        var message = new Label("Shader compilation finished ");
        row.Add(message);

        var code = new Label("ShaderLab.ParseError:0x2F");
        FontFix.ApplyMono(code);
        row.Add(code);
        root.Add(row);

        var log = new TextField { multiline = true, isReadOnly = true };
        log.value = "0x0042  OK\n0x0043  RETRY";
        FontFix.ApplyMono(log);
        root.Add(log);
    }
}
```

覚え方: `ApplyCjkUi` はコンテナルート、`ApplyMono` はリーフ。同じ要素に
両方は呼ばない(どちらもインライン書き込みで、後勝ちになるため)です。

### 2. 本物の Bold と、その他フェイスの選択(Semibold の見出しなど)

0.2.0 からは、解決したファミリーの本物の Bold フェイスがベースアセットの
ウェイトテーブルに自動で配線されます。これにより
`-unity-font-style: bold`(リッチテキストの `<b>` も)が、輪郭を
太らせた合成ボールドではなく実際の Bold フェイスで描画されます。
Bold フェイスを持たないファミリーでは従来どおり合成ボールドのままですし、
`FontFixSettings.CjkUiBoldStyleName = ""` とすれば明示的に元へ戻せます。
なお、本物の Bold は合成ボールドと字送りが違うため、アップグレード後は
太字ラベルの折り返し位置が少し変わることがあります。

USS からは選べないフェイス(Semibold・Light・Medium など)は、要素単位で
使えます。`GetCjkUiFontAsset` は解決済みファミリーの任意フェイスを
キャッシュ付きで返し、`ApplyCjkUiFace` はそれを**リーフ要素**に適用
します — `ApplyMono` と同じリーフ向けの操作なので、コンテナルート向けの
`ApplyCjkUi` とはあえて別名にしてあります。スタイル名は OS が報告する
フェイス名と完全一致させる必要があります(Regular 相当を "Book" と呼ぶ
ファミリーもあります)。フェイスが見つからない場合 `ApplyCjkUiFace` は
何もしないので、その要素はルートから継承したベースフェイスで表示され、
見た目で気づけます。

```csharp
using Colloid.UitkFontFix;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class ReleaseNotesWindow : EditorWindow
{
    [MenuItem("Window/Release Notes")]
    public static void Open()
    {
        GetWindow<ReleaseNotesWindow>("Release Notes");
    }

    public void CreateGUI()
    {
        if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
        {
            FontFix.ApplyCjkUi(rootVisualElement);
        }

        var heading = new Label("Release highlights");
        FontFix.ApplyCjkUiFace(heading, "Semibold");
        rootVisualElement.Add(heading);

        var body = new Label("Bold runs in this text use the real Bold face.");
        body.style.unityFontStyleAndWeight = FontStyle.Bold;
        rootVisualElement.Add(body);
    }
}
```

このパッケージが生成する `FontAsset`(ベース・Bold・各フェイス)には
すべて `[UITK Font Fix]` という名前タグが付きます。UITK Debugger の
解決スタイル欄に表示されるのはこの名前ですし、Memory Profiler でも
この文字列で検索できます。どのフェイスが解決され、ウェイトテーブルに
何が配線されているかは診断ウィンドウで確認できます。

### 3. 言語判定を安全にやる

`Application.systemLanguage` はシリアライズ中(コンストラクタや
フィールド初期化子)に読むと `UnityException` を投げます。フィールド
初期化子1つの例外がアセットの読み込みまるごと壊すこともあります。
そのため、このパッケージの判定ヘルパは言語を引数で受け取る純粋関数に
なっています — いつ読むかを決めるのはあなたです。
`CjkLanguage.ShouldPreferCjkUi` 自身は言語を読みません。安全な形は、
`OnEnable`(またはそれ以降のコールバック)で1回だけ読んで結果を
キャッシュしておくこと。下の例がそのパターンです。

```csharp
using Colloid.UitkFontFix;
using UnityEngine;

public class MyToolState : ScriptableObject
{
    // WRONG: throws during serialization
    // private bool _preferCjk =
    //     CjkLanguage.ShouldPreferCjkUi(Application.systemLanguage);

    private bool _preferCjk;

    private void OnEnable()
    {
        // RIGHT: query from OnEnable or later
        _preferCjk = CjkLanguage.ShouldPreferCjkUi(
            Application.systemLanguage);
    }

    public bool PreferCjk
    {
        get { return _preferCjk; }
    }
}
```

`CjkLanguage` はランタイムアセンブリにあるので、この書き方はどの
スクリプトでも使えます。エディタコードなら `FontFix.ShouldPreferCjkUi`
が同じ判定の窓口です。

### 4. モデル/ユーザー由来テキストのサニタイズ

下の例はエディタUIのコードです(Editor フォルダ配下などに置きます)。
`ShowMessage` は無損失で、いつ呼んでも安全です。取り除くのは不可視の
コードポイントだけ — 異体字セレクタ(漢字用の IVS を含む)、ゼロ幅文字、
BOM、絵文字タグ文字 — なので、ユーザーに見える内容は変わりません。
変わるのは、描画のたびに出ていた "not found in [Inter-Regular SDF]"
警告と豆腐が出なくなることです。`ShowMessageBmpOnly` はオプションの
**損失あり**の追加処理で、BMP(基本多言語面)外を許さない表示面向け
です。エディタフォントには絵文字のグリフが一切ないので、補助面の文字を
豆腐ではなく目に見える代替文字に置き換えます — 意図して使ってください。
内部で呼んでいる2引数オーバーロードは「除去してから置換」という安全な
順序で合成されています。不可視の IVS を先に除去するので、IVS 付きの
漢字の後ろに余計な `*` が付くことはありません。個別の操作が必要なら
`TextSanitizer` に粒度の細かい API があります。

```csharp
using Colloid.UitkFontFix;
using UnityEngine.UIElements;

public static class ChatView
{
    public static void ShowMessage(Label target, string rawModelText)
    {
        target.text = FontFix.SanitizeDisplayText(rawModelText);
    }

    public static void ShowMessageBmpOnly(Label target, string rawModelText)
    {
        target.text = FontFix.SanitizeDisplayText(rawModelText, "*");
    }
}
```

サニタイザはすべて純粋関数で、例外を投げず、変更が不要なら同じ
インスタンスをそのまま返し、null は `string.Empty` になります。

### 5. 候補フォントの差し替え(中国語/韓国語優先の製品向け)

既定では日本語優先の候補リストで解決します。中国語や韓国語のユーザーが
主対象の製品では、CJK 候補リストを差し替えてください — コードだけで
完結し、アセットは不要です。下の例は簡体字中国語優先の設定です:
`Microsoft YaHei UI` / `Microsoft YaHei` が Windows の簡体字、
`Microsoft JhengHei UI` / `Microsoft JhengHei` が Windows の繁体字、
`Noto Sans CJK SC` が Linux をカバーし、`Yu Gothic UI` を日本語
フォールバックとして残しています。

```csharp
using Colloid.UitkFontFix;
using UnityEditor;

public static class MyProjectFontConfig
{
    [InitializeOnLoadMethod]
    private static void Configure()
    {
        FontFixSettings.CjkUiFontNames = new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei",
            "Microsoft JhengHei UI", "Microsoft JhengHei",
            "Noto Sans CJK SC",
            "Yu Gothic UI"
        };
    }
}
```

韓国語優先ならこちらを代わりに使います: Windows は `Malgun Gothic`、
Linux は `Noto Sans CJK KR`、日本語フォールバックに `Yu Gothic UI`。

```csharp
FontFixSettings.CjkUiFontNames = new[]
{
    "Malgun Gothic",
    "Noto Sans CJK KR",
    "Yu Gothic UI"
};
```

候補名は優先順に1つずつ試されます。名前は**英語のファミリー名**で
指定してください — `GetOSInstalledFontNames` は日本語版 Windows でも
英語名を返します。値が実際に変わったときだけ `FontFix` のキャッシュが
自動で無効化され、同じ値の再代入ではキャッシュは温存されます。null を
代入すると既定値に戻ります。

モノスペース側の `EditorMonoFontPaths` / `OsMonoFontNames`、
`FontAsset.CreateFontAsset` に渡すスタイル名 `CjkUiStyleName` も
同じ要領で差し替えられます。

### 6. 固定UI文字列を SafeGlyphs + GlyphAudit でガードする

ソースに直接埋め込む固定のUI文字列(アイコン・箇条書き記号・矢印など)
は、印字可能な ASCII と、実証済みの `SafeGlyphs` ホワイトリストの範囲に
収めるのが安全です。`GlyphAudit` を使うと、この方針をテストにできます。
エディタフォントで描けないグリフを誰かがソースに埋め込むと、ファイル名と
コードポイントつきでテストが落ちます。例は EditMode テストアセンブリを
前提としています(テスト用 asmdef で `Colloid.UitkFontFix` と
`Colloid.UitkFontFix.Editor` を参照してください)。

`ExtraCodepoints` はプロジェクト独自のホワイトリスト拡張です。追加して
よいのは、対象エディタフォントで実際に描画されることを確認した
コードポイントだけにしてください(ラベルに置いてコンソールを見るのが
手早い確認方法です)。例では U+2192(右向き矢印)を追加しています。
`EditorSources_PassGlyphAudit` は `*.cs` を再帰的にスキャンし、
ホワイトリスト外の構築コードポイント(`ConvertFromUtf32`・`(char)`
キャスト・`\uXXXX` / `\UXXXXXXXX` エスケープ)、異体字セレクタの
リテラルとエスケープ、非 ASCII バイトを探します。空リストならクリーン、
違反は「ファイル名: 理由」の形で返るので、何がどこに紛れ込んだか
そのまま分かります。`GlyphStrings_UseOnlySafeCodepoints` は個別の
コードポイント判定の例です。固定UI文字列は `char.ConvertFromUtf32` で
実行時に組み立てると、ソースファイル自体を ASCII のまま保てます。

```csharp
using System.Collections.Generic;
using Colloid.UitkFontFix;
using NUnit.Framework;

public class UiGlyphSafetyTests
{
    private static readonly int[] ExtraCodepoints =
    {
        0x2192
    };

    [Test]
    public void EditorSources_PassGlyphAudit()
    {
        List<string> offenders = GlyphAudit.AuditSourceDirectory(
            "Assets/Editor", ExtraCodepoints);
        Assert.IsEmpty(offenders, string.Join("\n", offenders));
    }

    [Test]
    public void GlyphStrings_UseOnlySafeCodepoints()
    {
        Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(0x2713));
        Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(0x2192, ExtraCodepoints));
        Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x1F4CE));
    }
}
```

このパッケージ自身も、出荷ソースに対して同じ監査をテストスイートの
一部として実行しています。

### 7. 診断: 何がどう解決されたかを見る

**Window > UITK Font Fix > Diagnostics** を開くと、読み取り専用の
レポートが表示されます。*Re-probe*(キャッシュを捨てて再解決)と
*Copy report* のボタン付きです。同じレポートはコードからも取れるので、
バグレポートに添付する、といった使い方もできます。下の例はエディタ
コードです(`FontFixDiagnostics` はエディタ専用アセンブリにあるため、
Editor フォルダ配下に置いてください)。`BuildReport` はプレーンテキスト
(ASCII)のレポートを返します。内容は、どの候補がどの段で解決されたか、
CJK アトラスの状態、このマシンでの候補フォントの有無、既知の罠の
チェックリストです。例外は投げず、バッチモードでも安全です。

```csharp
using Colloid.UitkFontFix;
using UnityEngine;

public static class SupportBundle
{
    public static void LogFontReport()
    {
        Debug.Log(FontFixDiagnostics.BuildReport());
    }
}
```

レポートの読み方:

```
-- Resolution --
mono   : editor:Fonts/RobotoMono/RobotoMono-Regular.ttf (RobotoMono-Regular)
cjk-ui : osasset:Yu Gothic UI (Yu Gothic UI)

-- Environment --
language   : Japanese (prefer CJK UI: yes)
cjk-ui candidates:
  [x] Yu Gothic UI
  [ ] Noto Sans CJK JP
```

ソース欄の接頭辞: `editor:` = エディタ同梱の TTF、`os:` = フェイス検証を
通過した OS フォント、`label` = エディタ既定のラベルフォント(最終手段)、
`osasset:` = インストール済みファミリーから作った DynamicOS の
`FontAsset`、`(none)` = 何も解決できなかった(適用ヘルパは何もしません)。

### 8. コードを書かずにフォントを設定する(Project Settings)

`FontFixSettings` で設定できることは、すべてエディタUIからも設定
できます。**Edit > Project Settings > UITK Font Fix** を開いてください。
候補リストは1行に1ファミリー名で編集し、ボタンは3つです —
*Apply (this session)* は今のエディタセッションに反映、
*Save to project* はさらに
`ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json`
に書き出します(プロジェクトと一緒にバージョン管理されるので、チーム
全員に共有されます)。*Use package defaults* はすべてを既定に戻して
このファイルを削除します。

診断ウィンドウにも同じフォームが *Edit configuration* の折りたたみに
入っています。解決レポートのすぐ隣なので、編集 → *Apply* →
*Re-probe* → 結果を見る、を1画面で回せます。気に入る設定になったら、
そこから *Save to project* すれば同じファイルに保存されます。

反映のしくみ: 保存されたファイルはドメインロードごとに1回、
**利用側のコードより先に**適用されます。そのため、後から
`FontFixSettings` に代入するコードがあればそちらが勝ちます(明示的な
コードのほうが強い意思表示だ、という意図的な設計です)。実効値が
ファイルとズレていないかは、診断レポートの「Project settings file」
セクションで確認できます。適用範囲はこれまでどおり: 適用ヘルパを
呼んでいるウィンドウすべてに効き、開きっぱなしのウィンドウは再構築か
再オープンで反映され、このパッケージを使っていないUIには影響しません。

## API リファレンス

すべて `Colloid.UitkFontFix` 名前空間にあります。リゾルバはどれも結果を
キャッシュし、**例外を投げず**、バッチモードでも安全です。

### `FontFix`(static ファサード、エディタアセンブリ)

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFont` | 解決済みのモノスペース `Font`。同梱 RobotoMono → フェイス検証済み OS フォント → 既定ラベルフォントの順。キャッシュされ、正常なエディタでは実質 null になりません。 |
| `EditorMonoFontSource` | どのモノスペース候補が勝ったか: `"editor:<パス>"` / `"os:<名前>"` / `"label"` / 空文字。 |
| `CjkUiFontAsset` | 最初に見つかった候補から作る Latin+CJK の `FontAsset`(DynamicOS モード)。候補が1つも解決できなければ null。グリフは描画時に遅延取得され(生成直後の `HasCharacter` が false でも正常)、一時アセットはドメインリロード前に必ず破棄されます。 |
| `CjkUiFontSource` | どの CJK 候補が勝ったか: `"osasset:<名前>"` / 空文字。 |
| `ApplyMono(VisualElement)` | **リーフ**1要素へのインライン `unityFontDefinition` 代入。先祖の `ApplyCjkUi` に常に勝ちます。null や未解決時は何もせず、継承フォントを保ちます。 |
| `ApplyCjkUi(VisualElement)` | **コンテナルート**へのフォント適用。配下が継承します。null や未解決時は何もしないので、フォントが変わった前提のコードは書かないでください。 |
| `GetCjkUiFontAsset(string)` | 解決済みファミリーの指定フェイス(フェイス名は完全一致、例 `"Semibold"`)。null / 空 / ベーススタイル名なら `CjkUiFontAsset` と同一インスタンス。他のフェイスは一度だけ生成してキャッシュ(ミスもキャッシュ)し、キット所有・名前タグ付きになります。ベース未解決またはフェイス不在なら null。 |
| `ApplyCjkUiFace(VisualElement, string)` | **リーフ**要素への特定フェイスのインライン適用(Semibold の見出しなど)。要素が null またはフェイスが無ければ何もせず、継承ベースフェイスで表示されます。 |
| `CreatedObjectNameTag` | `"[UITK Font Fix]"` — キットが生成する全オブジェクト名(アセット・マテリアル・最初のアトラスページ・所有 OS フォント)に付く接尾辞。Memory Profiler の検索文字列でもあります。 |
| `ShouldPreferCjkUi(SystemLanguage)` | ja/zh/ko の純粋判定。言語を引数で受け取るのは意図的です — シリアライズ中の `Application.systemLanguage` 読み取りは例外を投げます(レシピ3参照)。 |
| `SanitizeDisplayText(string)` | 無損失のテキスト衛生: 異体字セレクタ(IVS 含む)・ゼロ幅文字・BOM・絵文字タグ文字を除去します。クリーンなら同一インスタンスを返し、null は `string.Empty` に。自分のグリフを描く文字は決して消しません。 |
| `SanitizeDisplayText(string, string)` | **損失あり**のオーバーロード: 上の除去の後、BMP 外のコードポイントと孤立サロゲートを指定の置換文字列に置き換えます(引数を渡すこと自体がオプトイン)。「除去→置換」の順序は仕様として保証されます。 |
| `ResetCaches()` | キャッシュを全部捨てます(キット所有の一時オブジェクトは破棄)。次のアクセスで再解決されます。 |

### `FontFixSettings`(static 設定、エディタアセンブリ)

コードから候補を設定します。値が実際に変わったときだけ `FontFix` の
キャッシュが無効化され、同じ値の再代入ではキャッシュは温存されます。
どのプロパティも null 代入で既定値に戻ります。

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFontPaths` | 同梱モノスペース TTF を探す `EditorGUIUtility.Load` のパス。空配列でこの段を無効化。 |
| `OsMonoFontNames` | OS モノスペースのファミリー名(1つずつ試行)。空配列でこの段を無効化。 |
| `CjkUiFontNames` | Latin+CJK のファミリー名(優先順、1つずつ試行)。空配列で CJK 解決ごと無効化(`ApplyCjkUi` は何もしなくなります)。 |
| `CjkUiStyleName` | `FontAsset.CreateFontAsset` に渡すスタイル名(既定 `"Regular"`)。null/空で既定に戻ります。 |
| `CjkUiBoldStyleName` | ウェイトテーブルに配線する Bold フェイス名(既定 `"Bold"`)。null で既定に復帰、`""` で配線を無効化し 0.2.0 以前の合成ボールドに戻ります。 |
| `ResetToDefaults()` | 全プロパティを既定値へ。実際に変わったものがある場合だけキャッシュを無効化します(テストの teardown に置いても安全)。 |

### ランタイムユーティリティ(ランタイムアセンブリ)

**`TextSanitizer`** — 純粋なテキスト衛生。例外を投げず、クリーンな
入力ではアロケーションなし、null は `string.Empty` になります。

| メンバー | 動作 |
| --- | --- |
| `StripVariationSelectors(string)` | すべての異体字セレクタを除去: U+FE00〜U+FE0F、モンゴル文字 FVS(U+180B〜U+180D と U+180F)、漢字用 IVS(U+E0100〜U+E01EF、正しいサロゲートペアのみ照合)。ZWJ/ZWNJ には触れない、字形結合に安全な粒度の操作です。 |
| `StripInvisibleCharacters(string)` | 上の上位集合: ゼロ幅スペース/非結合子/結合子(U+200B〜U+200D)、ワードジョイナと不可視数学演算子(U+2060〜U+2064)、BOM(U+FEFF)、絵文字タグ文字(U+E0000〜U+E007F)も除去。`FontFix.SanitizeDisplayText` の実体です。 |
| `ReplaceNonBmpCharacters(string, string)` | **損失あり**: 補助面のコードポイント(サロゲートペア1組につき置換1回)と孤立サロゲートを置換文字列へ。置換文字列にサロゲートが無ければ、結果にサロゲート単位は残りません。BMP 限定の表示面向け。 |

**`CjkLanguage`**

| メンバー | 動作 |
| --- | --- |
| `ShouldPreferCjkUi(SystemLanguage)` | ja/zh/ko の純粋判定。言語を引数で受け取るのは、シリアライズ中の `Application.systemLanguage` 読み取りが例外を投げるからです(レシピ3参照)。 |

**`SafeGlyphs`**

| メンバー | 動作 |
| --- | --- |
| `DefaultCodepoints` | 2022.3 のエディタフォントで描画確認済みの非 ASCII BMP コードポイント一覧(チェックマーク・矢印・箇条書き記号・テキスト表現の歯車/警告記号など)。読み取り専用として扱ってください。 |
| `IsSafeCodepoint(int)` | 印字可能 ASCII またはホワイトリスト所属なら true。 |
| `IsSafeCodepoint(int, int[])` | 上に加えてプロジェクト独自の拡張リストも見る版(拡張はエディタフォントでの描画確認後に)。 |

**`FontFixDefaults`**

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFontPaths` | 同梱モノスペースの既定探索パス(RobotoMono)。 |
| `OsMonoFontNames` | OS モノスペースの既定候補(Consolas・Menlo・DejaVu Sans Mono・Courier New)。 |
| `CjkUiFontNames` | Latin+CJK の既定候補チェーン(Yu Gothic UI → Yu Gothic → Meiryo UI → Meiryo → Noto Sans CJK JP → MS UI Gothic)。 |
| `CjkUiStyleName` | `"Regular"`。 |
| `CjkUiBoldStyleName` | `"Bold"`。 |

配列は読み取り専用として扱い、カスタマイズは `FontFixSettings` 経由で
行ってください。

### エディタヘルパ

**`GlyphAudit`** — ソースレベルのグリフ安全性監査。利用側プロジェクトの
テストアセンブリから使えます(レシピ6参照)。

| メンバー | 動作 |
| --- | --- |
| `PackageRootPath()` | このパッケージ自身のソースのルートディレクトリ(組み込み・ジャンクション・パッケージキャッシュのいずれでも解決)。解決不能なら null。 |
| `AuditSourceDirectory(string, int[])` | ディレクトリ配下の全 `*.cs` に全監査を実行(再帰)。違反を「ファイル名: 理由」のリストで返します。ディレクトリが無い場合は例外ではなく違反1件として返すので、パスの移動にテストで気づけます。 |
| `AuditSourceFile(string, int[])` | 1ファイルに全監査を実行。 |
| `AuditConstructedCodepoints(string, string, int[])` | ソース中で構築されるコードポイントのうちホワイトリスト外のものを検出。U+FE0F/U+FE0E/U+FEFF の `(char)` キャストは除外されます(サニタイザが除去のために比較する正当な用途で、比較は描画されないため)。 |
| `AuditVariationSelectors(string, string)` | 異体字セレクタのリテラルとエスケープを検出。序数(ordinal)走査なのは意図的です — カルチャ依存の検索はセレクタを照合上無視してしまいます。 |
| `AuditAsciiOnly(string, byte[])` | ファイルごとに最初の非 ASCII バイトを検出(厳格 ASCII のソース方針は、グリフの中身をレビューと diff で追える状態に保ちます)。 |

**`FontFixProjectSettings`**

| メンバー | 動作 |
| --- | --- |
| `FilePath` / `Exists` | プロジェクト共有の設定ファイル(`ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json`)のパスと、その有無。 |
| `Save()` | 現在の実効値をファイルに書き出します(IO失敗時は例外ではなく false)。 |
| `TryLoadAndApply()` | ファイルがあれば適用します。ドメインロードごとに自動で1回走ります。壊れたファイルは無視。 |
| `Delete()` | ファイルを削除し、プロジェクトを既定値追従に戻します(メモリ上の設定は変えません)。 |
| `MatchesCurrentSettings()` | ファイルが存在し、実効値と一致していれば true。診断レポートが表示する「ズレ」検出の実体です。 |

**`FontFixDiagnostics`**

| メンバー | 動作 |
| --- | --- |
| `BuildReport()` | プレーンテキスト(ASCII)のレポート: 解決結果、CJK アトラスのページ状態、フェイスキャッシュと Bold 配線、候補フォントの有無、既知の罠チェックリスト。例外を投げず、バッチモードでも安全。 |

**`FontFixDiagnosticsWindow`**

| メンバー | 動作 |
| --- | --- |
| `Open()` / **Window > UITK Font Fix > Diagnostics** | 読み取り専用の診断ウィンドウ。*Re-probe* と *Copy report* 付き。パッケージ自身の構成ルール(CJK ルート+モノスペースの本文)でドッグフーディングしています。 |

## 検証済みの挙動(この設計の根拠)

以下は Unity 2022.3 の TextCore / UI Toolkit について実測で確認した
事実です(主に Windows、ヘッドレスで再現可能な部分は Linux でも
再検証済み)。このパッケージの「なぜそうなっているのか」は、どれも
この一覧のどれかに行き着きます。

1. `FontEngine.LoadFontFace(Font)` は、OS 動的フォント
   (`Font.CreateDynamicFontFromOSFont`)に対して**例外なく**
   `Invalid_File` を返します。`Font` 経路では OS フォントを検証
   できない — だからこのパッケージは OS の `Font` を UI Toolkit に
   渡しません。
2. OS フォントの正しい経路は
   `TextCore.Text.FontAsset.CreateFontAsset(family, style)`
   (DynamicOS モード)を `FontDefinition` で割り当てることです。
   グリフは描画時に遅延取得され(生成直後の `HasCharacter` が false
   でも正常)、ヘッドレス環境でも生成できます。
3. `Font.CreateDynamicFontFromOSFont` に**名前の配列**を渡すと、
   読み込み不能なフェイスができて("Unable to load font face" /
   "Can't Generate Mesh")テキストが空になります。単一名でも
   フェイス検証は通らないため、この経路自体を使いません。
4. `EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf")`
   は 2022.3 で解決します。実 TTF アセットなので TextCore が必ず
   受け入れる — モノスペースの第一候補にしている理由です。
5. USS では OS フォントを名前で選べません。フォント適用は C# の
   `style.unityFontDefinition` で行います。インラインスタイルは
   継承値に勝つ — ルート/リーフの構成が成立する根拠です。
6. エディタ標準フォント(Inter)には CJK グリフがありません。
   1文字単位の OS フォールバックはフォントと太さを混ぜ、CJK テキストを
   「まだら太字」にします。コンテナルートに Latin+CJK フォントを
   1つ明示すればフォールバック経路ごと消えます。実証済みのチェーン:
   Yu Gothic UI → Yu Gothic → Meiryo UI → Meiryo → Noto Sans CJK JP →
   MS UI Gothic。
7. `Font.GetOSInstalledFontNames()` は、日本語などローカライズされた
   Windows でも**英語**のファミリー名を返します — 既定候補が英語名なのは
   このためです。
8. リッチテキストの `<mark>` タグは DynamicOS の FontAsset と
   相性が最悪です: mark の矩形がグリフのアトラスページ次第で上に
   乗ったり下に敷かれたりして、まだら太字やかすれ(不透明 mark なら
   文字の完全消失)を起こします。アトラスの事前充填では直せません
   (ASCII だけで4ページに及び、CJK は原理的に単一ページ化不能)。
   これらのフォントと `<mark>` を併用しないでください。
9. 絵文字(U+1F4CE など)や、モデル/ユーザーがよく混入させる異体字
   セレクタ(U+FE0F)には、エディタフォントのグリフがありません。
   豆腐と描画ごとの警告スパムになります。自由入力テキストは
   `SanitizeDisplayText` へ、固定UI文字列は `SafeGlyphs` の
   ホワイトリスト内へ。
10. `Application.systemLanguage` は ScriptableObject のコンストラクタや
    フィールド初期化子から読むと例外を投げます。`OnEnable` 以降で
    読んで、値を `ShouldPreferCjkUi` に渡してください。
11. バッチモードでは、OS 動的フォントの**フェイス**操作は全滅しますが、
    DynamicOS FontAsset の**生成**は動きます。テストスイートはこの
    非対称を前提に組んであります。

## Unity バージョン互換性

第一ターゲットは **Unity 2022.3 LTS** です(上の一覧はすべてそこで
実測)。Linux エディタでもコンパイルと EditMode スイートのヘッドレス
実行がグリーンになることを確認しています(CJK 解決はインストール済み
フォント依存なので、CJK 候補が無いマシンでは正しく「未解決」と
報告されます)。

バージョン依存になりうる TextCore / UI Toolkit の呼び出しは、内部の
継ぎ目1ファイル(`FontShims`)に集約してあります。UnityCsReference の
ソース照合では、`FontDefinition.FromSDFFont(FontAsset)`・
`FontAsset.CreateFontAsset(family, style)`・`atlasPopulationMode`・
`atlasTextures` は 2022.3 / 2023.2 / 6000.0 ブランチで同一でした。
将来 Unity がどれかを変えたとしても、修正はこの1ファイルに収まります。

## サンプル

Package Manager の *Samples* タブから2つインポートできます:

- **Editor Font Setup** — コンテナ/リーフの構成・安全な言語判定・
  テキストサニタイズをひととおり実演する `EditorWindow`。インポート後、
  *Window* メニューから開けます。自分のツールウィンドウの雛形に
  どうぞ。
- **Glyph Audit Tests** — `GlyphAudit` と `SafeGlyphs` を自分の
  プロジェクトのソースに配線する、コピーしてすぐ使える EditMode
  テスト。スキャン対象ディレクトリと追加ホワイトリストを自分の
  プロジェクトに合わせて調整してください。

## ライセンス

MIT — [LICENSE.md](LICENSE.md) を参照してください。
