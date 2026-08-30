# UITK Font Fix

[English](README.md) | 日本語

Unity UI Toolkitの**エディタ**UI向けフォントユーティリティです。確実に読み込める
等幅フォントとLatin+CJK UIフォントを解決し、UI Toolkitのスタイル継承を乗り越える
唯一の合成パターンでそれらを適用し、自由入力テキストが既知のTextCoreレンダリング
の落とし穴に引っかからないようにします。

依存関係ゼロ。エディタファースト(Unity 2022.3 LTS以降。ランタイムアセンブリは
将来のランタイムサポートに向けて構造的に準備されています)。MITライセンス。

## このパッケージが解決する落とし穴

Unity 2022.3のUI ToolkitエディタUIには、遭遇しやすく診断しにくい一連のフォント
障害モードが存在します。

- **CJKテキストが「まだら太字」でレンダリングされる。** エディタのデフォルト
  フォント(Inter)はCJKをカバーしていないため、CJKグリフはグリフ単位でOSが
  供給できる任意のフォントにフォールバックし、文字列の途中でファミリーと
  ウェイトが混在します。
- **OSフォントが警告なしに空のテキストをレンダリングすることがある。**
  `Font.CreateDynamicFontFromOSFont`はUI Toolkitが読み込めないフェイス
  (「Unable to load font face」)を生成することがあり、`FontEngine`はそのような
  `Font`を事前に検証できません。
- **USSはOSフォントを名前で選択できない。** フォントはC#から
  `style.unityFontDefinition`経由で割り当てる必要があります。
- **モデル/ユーザーのテキストが警告を大量発生させ、プレースホルダーの四角を
  描画する。** 異体字セレクタ(例えば警告記号の後のU+FE0F)や絵文字面の文字は、
  エディタフォントにグリフを持ちません。
- **`Application.systemLanguage`はシリアライズ中に例外を投げる**ため、安易な
  言語分岐は設定アセット全体を道連れにして落とすことがあります。

`FontFix`は、検証済みの回避策を小さなファサードの背後にまとめています。
キャッシュし、決して例外を投げず、どの候補が勝ったかを報告するリゾルバー群。
継承の契約が明文化された適用ヘルパー群。表示テキストのサニタイザー。
ソースレベルのグリフ監査。そして診断ウィンドウです。

- インストール
- クイックスタート
- レシピ
- APIリファレンス
- 検証済みの挙動(この設計の根拠)
- Unityバージョン互換性
- サンプル
- ライセンス

## インストール

gitURL経由の場合(Package Manager > `+` > *Add package from git URL...*):

```
https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-fix
```

または、`jp.colloid.uitk-font-fix`フォルダをプロジェクトの`Packages/`
ディレクトリに配置します(embedded package)。

パッケージの両アセンブリ(`Colloid.UitkFontFix`、`Colloid.UitkFontFix.Editor`)は
自動参照されるため、`Assets/`直下の単体スクリプトはすぐにAPIを利用できます。
自分のasmdef内のコードは、通常どおり明示的なアセンブリ参照を追加してください。

## クイックスタート

`Assets/Editor/FontFixQuickStart.cs`として保存し、*Window > Font Fix Quick
Start*を開きます。

以下のウィンドウは、この合成パターン全体をエンドツーエンドで示しています。
まず、コンテナルートにLatin+CJKフォントを適用し、すべての子孫がそれを継承する
ようにします。`CreateGUI`はシリアライズ完了のずっと後に実行されるため、ここで
システム言語を読み取っても安全です。次に、通常のラベルは特別な処理を必要とせず、
単にルートフォントを継承します。3番目に、コードのリーフ要素には等幅フォントを
インラインで適用します。インラインスタイルは常に継承値に勝るため、CJKコンテナの
内側にあっても等幅のままです。4番目に、モデル出力、ユーザー入力、ファイル、
クリップボードといった自由入力テキストは、表示前にサニタイズすべきです。
除去されるのは不可視のコードポイントだけなので、可視の内容は変わりません。

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

合成のルール: `ApplyCjkUi`は**コンテナルート**に対して呼び出します(すべての
子孫がそれを継承します)。`ApplyMono`は**リーフ**要素に対して呼び出します
(インラインスタイルは常に継承値に勝るため、CJKコンテナの内側でもコードは
等幅のままです)。同一要素に対して両方を呼び出さないでください -- どちらも
インライン書き込みであり、後から呼んだ方が黙って勝ちます。

## レシピ

### 1. CJK UIテキストと等幅コードを混在させるエディタウィンドウ

この例では、CJK UIテキストと等幅コードを混在させるエディタウィンドウを構築
します。CJK対応フォントをルートに1つ適用すると、その下のすべての`Label`が
そのフォントを継承するため、日本語・中国語・韓国語の文字列は、まだらな
グリフ単位のOSフォールバックではなく単一のファミリーでレンダリングされます。
結果ラベルは単純にルートフォントを継承します。続く行では、メッセージラベルも
同様にルートフォントを継承してプロポーショナルなままですが、隣接するコード
ラベルはインラインで`ApplyMono`を呼び出し、継承フォントにローカルで勝ちます。
下部の複数行ログ本文も同じ仕組みです。そのリーフに対する1回の`ApplyMono`
呼び出しは、CJKルートの下にどれほど深く位置していても適用されます。

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

経験則: `ApplyCjkUi`はコンテナルートに、`ApplyMono`はリーフに対して呼び出し、
同一要素に両方を呼び出すことはしないでください -- どちらもインライン書き込み
であり、後から呼んだ方が黙って勝ちます。

### 2. 本物のBoldと他フェイスの選択(Semibold見出し)

0.2.0以降、このパッケージは解決済みファミリーの本物のBoldフェイスをベース
アセットのウェイトテーブルに組み込むため、`-unity-font-style: bold`
(およびリッチテキストの`<b>`)は、輪郭を太らせただけの合成ボールドではなく、
実際のBoldフェイスをレンダリングします。Boldフェイスを持たないファミリーは
従来どおり合成ボールドのレンダリングを維持し、
`FontFixSettings.CjkUiBoldStyleName = ""`で明示的にそれへ戻せます。本物の
Boldフェイスは合成ボールドとアドバンス幅が異なるため、アップグレード後に
既存のボールドラベルの折り返しが多少変わることがある点に注意してください。

USSが到達できないフェイス(Semibold、Light、Medium、...)は、代わりに要素単位で
利用できます。`GetCjkUiFontAsset`は解決済みファミリーの任意のフェイスに対して
キャッシュ済みアセットを返し、`ApplyCjkUiFace`はそれをLEAF要素に割り当てます
-- `ApplyMono`と同じリーフのセマンティクスであり、コンテナルート向けの
`ApplyCjkUi`とは意図的に区別されています。スタイル名は、OSが報告するフェイス名
と厳密に一致させる必要があります(一部のファミリーは通常のフェイスを「Book」と
名付けています)。フェイスが見つからない場合、`ApplyCjkUiFace`は何もせず
(no-op)、要素は継承済みのベースフェイスへ目に見える形で後退します。

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

このパッケージが作成するすべての`FontAsset`(ベース、Bold、フェイス別)には
`[UITK Font Fix]`という名前タグが付与されます -- これはUITK Debuggerの解決済み
スタイルパネルに表示される名前であり、Memory Profilerで検索する際の文字列でも
あります。診断ウィンドウには、どのフェイスが解決されたか、ウェイトテーブルに
何が組み込まれているか、そしてすべてのアトラスページ名が一覧表示されます。

### 3. 安全な言語分岐

`Application.systemLanguage`は、シリアライズ中(コンストラクタ、フィールド
初期化子)に読み取ると`UnityException`を投げます。例外を投げるフィールド
初期化子が1つあるだけで、アセットの読み込み全体が壊れることがあります。その
ためポリシーヘルパーは純粋関数になっています。言語をパラメータとして受け取り、
いつ読み取るかは*あなた*が制御します。`CjkLanguage.ShouldPreferCjkUi`自体は
決して言語を読み取りません。安全なパターンは、`OnEnable`(またはそれ以降の
任意のコールバック)で一度だけ言語を問い合わせ、結果をキャッシュすることです。
以下に示します。

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

`CjkLanguage`はランタイムアセンブリに存在するため、このパターンはどの
スクリプトでも機能します。エディタコードでは、`FontFix.ShouldPreferCjkUi`が
ファサードの背後にある同じポリシーです。

### 4. 表示前にモデル/ユーザーのテキストをサニタイズする

以下の例はエディタUIコードで、例えばEditorフォルダ配下に置くものです。
`ShowMessage`はロスレスであり、常に呼び出して安全です。除去するのは不可視の
コードポイントのみです -- 異体字セレクタ(表意文字用のものを含む)、ゼロ幅文字、
バイトオーダーマーク、絵文字タグ文字です。ユーザーに見える内容は決して変わり
ません。止まるのは、描画ごとの「not found in [Inter-Regular SDF]」警告の大量
発生とプレースホルダーの四角です。`ShowMessageBmpOnly`は、基本多言語面(BMP)
の範囲内に厳密に留まる必要がある表示面向けの、任意かつロッシーな第2ステップ
です。エディタフォントには絵文字面のグリフが一切ないため、すべての補助面文字
はプレースホルダーの四角ではなく目に見える代替文字になります。したがって
意図的に選択して使ってください。それが呼び出す2引数オーバーロードは、安全な
順序でストリップしてから置換を合成します -- 表意文字の異体字セレクタは不可視
であるため、非BMP置換より前にストリップされ、セレクタ付きの漢字に余計な
アスタリスクが付くことはありません。個別の操作が必要な場合、粒度の細かい操作
は`TextSanitizer`にあります。

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

すべてのサニタイザーは純粋関数であり、決して例外を投げず、変更の必要がない
場合は同じ文字列インスタンスを返し、nullは`string.Empty`にマップします。

### 5. 候補フォントの上書き(zh/ko優先プロダクト向け)

デフォルトは日本語優先のチェーンを解決します。主に中国語または韓国語の
ユーザー向けに出荷するプロダクトは、CJK候補リストを置き換えます -- コード
ファーストで、アセットは不要です。以下の例は、簡体字中国語優先のプロダクトを
設定するものです。`Microsoft YaHei UI`と`Microsoft YaHei`はWindows上の簡体字
中国語をカバーし、`Microsoft JhengHei UI`と`Microsoft JhengHei`はWindows上の
繁体字中国語をカバーし、`Noto Sans CJK SC`はLinuxをカバーし、`Yu Gothic UI`は
日本語のフォールバックとして残されています。

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

韓国語優先のプロダクトであれば、上記のブロックの代わりに以下のリストを割り
当てることになります。Windows向けの`Malgun Gothic`、Linux向けの`Noto Sans CJK
KR`、そして日本語のフォールバックとして残す`Yu Gothic UI`です。

```csharp
FontFixSettings.CjkUiFontNames = new[]
{
    "Malgun Gothic",
    "Noto Sans CJK KR",
    "Yu Gothic UI"
};
```

名前は最も優先されるものから順に1つずつプローブされ、英語のファミリー名で
なければなりません。`GetOSInstalledFontNames`はローカライズされたWindowsでも
英語名を報告するためです。実際に変化した値は`FontFix`のキャッシュを自動的に
無効化します。等しい値を再代入してもキャッシュは温存され、nullを代入すると
デフォルトに戻ります。

等幅側の`EditorMonoFontPaths`と`OsMonoFontNames`、および
`FontAsset.CreateFontAsset`に渡すスタイルのための`CjkUiStyleName`にも、同じ
パターンが当てはまります。

### 6. SafeGlyphs + GlyphAuditで固定UI文字列を保護する

固定UI文字列(ソースに焼き込まれたアイコン、箇条書き記号、矢印)は、印字可能な
ASCIIと、安全性が実証された`SafeGlyphs`ホワイトリストのみに留めるべきです。
`GlyphAudit`は、そのポリシーをテストへと変換し、エディタフォントが描画できない
グリフを誰かが焼き込んだ際に、正確なファイルとコードポイントを示して失敗させ
ます。この例はEditModeテストアセンブリを前提としています。あなたのテスト
asmdefでは、`Colloid.UitkFontFix`と`Colloid.UitkFontFix.Editor`の両方を参照
してください。

`ExtraCodepoints`は、ホワイトリストへのプロジェクト固有の追加を保持します --
コードポイントをそこに追加するのは、対象のエディタフォントでそれがレンダリング
されることを確認した後だけにしてください。例えばラベルに入れてコンソールを
確認する、といった方法です。サンプルではU+2192(RIGHTWARDS ARROW)を追加して
います。`EditorSources_PassGlyphAudit`は、すべての`*.cs`ファイルを再帰的に
スキャンし、ホワイトリスト外で構築されたコードポイント(`ConvertFromUtf32`、
`(char)`キャスト、`\uXXXX`および`\UXXXXXXXX`エスケープ)、異体字セレクタの
リテラルとエスケープ、そして非ASCIIバイトを検出します。空のリストはソースが
クリーンであることを意味し、違反があれば「file: reason」形式の文字列として
返されるため、失敗メッセージは何がどこに紛れ込んだかを正確に示します。
`GlyphStrings_UseOnlySafeCodepoints`は個々のコードポイントを直接チェックし
ます。固定UI文字列は、ソースファイルをASCIIに保つために`char.ConvertFromUtf32`
でランタイムにコードポイントから組み立てるのが最善であり、以下のアサーション
は、U+2713(CHECK MARK)と拡張されたU+2192は通過する一方、絵文字面にある
U+1F4CEは通過しないことを確認しています。

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

このパッケージは、自身が出荷するソースに対しても、テストスイートの一部として
同じ監査を実行しています。

### 7. 診断: 何が解決され、なぜか

**Window > UITK Font Fix > Diagnostics**を開くと、*Re-probe*(キャッシュを
破棄して再解決)と*Copy report*ボタンを備えた読み取り専用レポートが表示され
ます。同じレポートはコードからも利用できます -- 例えばバグレポートのバンドル
の一部として。以下の例はエディタコードであり、`FontFixDiagnostics`はエディタ
専用アセンブリに存在するため、Editorフォルダ配下に置くべきです。`BuildReport`
は、どの候補がどの階層から解決されたか、CJKアトラスの状態、このマシンでの
候補の利用可能性、そして既知の落とし穴のチェックリストを網羅したプレーン
テキストのASCIIレポートを返します。これは決して例外を投げず、バッチモードで
呼び出しても安全です。

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

ソースのプレフィックス: `editor:` = エディタに同梱されたTTF、`os:` = フェイス
プローブに合格したOSフォント、`label` = デフォルトのエディタラベルフォント
(最後の手段)、`osasset:` = インストール済みファミリーから作成されたDynamicOS
の`FontAsset`、`(none)` = 何も解決されなかった(適用ヘルパーはno-opになる)。

## APIリファレンス

すべては`Colloid.UitkFontFix`名前空間に存在します。すべてのリゾルバーは結果を
キャッシュし、**決して例外を投げず**、バッチモードでも安全です。

### `FontFix`(静的ファサード、エディタアセンブリ)

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFont` | 解決済みの等幅`Font`。同梱のRobotoMonoが最初、フェイスプローブされた単一名のOSフォントが次点、デフォルトのラベルフォントが最後です。キャッシュされ、正常に機能しているエディタでは事実上nullになりません。 |
| `EditorMonoFontSource` | どの等幅候補が勝ったか: `"editor:<path>"`、`"os:<name>"`、`"label"`、または空文字列。 |
| `CjkUiFontAsset` | 最初にインストールされていた候補から作られたLatin+CJK `FontAsset`(DynamicOSモード)。何も解決しなかった場合はnull。グリフはレンダリング時に遅延して読み込まれます。この一時アセットは、ドメインリロードのたびに破棄されます。 |
| `CjkUiFontSource` | どのCJK候補が勝ったか: `"osasset:<name>"`、または空文字列。 |
| `ApplyMono(VisualElement)` | 1つの**リーフ**要素に対する`unityFontDefinition`のインライン割り当て。祖先の`ApplyCjkUi`があっても生き残ります。nullの場合、または何も解決しなかった場合はno-opです(継承フォントのまま)。 |
| `ApplyCjkUi(VisualElement)` | 子孫が継承する**コンテナルート**へのフォント割り当て。nullの場合、または何も解決しなかった場合はno-opです -- 呼び出し側はフォントが変わったと想定してはいけません。 |
| `GetCjkUiFontAsset(string)` | 指定したフェイススタイル(厳密なフェイス名、例: `"Semibold"`)での解決済みファミリー。null/空文字列/ベーススタイルは`CjkUiFontAsset`のエイリアスになります(同一インスタンス)。それ以外のフェイスは一度だけ作成され、キャッシュされ(ミスもキャッシュされます)、キットが所有し、名前タグが付与されます。ベースが解決しなかった場合、またはそのフェイスが存在しない場合はnullです。 |
| `ApplyCjkUiFace(VisualElement, string)` | **リーフ**要素への特定フェイスのインライン割り当て(Semibold見出しなど)。要素がnullの場合、またはフェイスが見つからない場合はno-opとなり、継承済みのベースフェイスへ後退します。 |
| `CreatedObjectNameTag` | `"[UITK Font Fix]"` -- キットが作成するすべてのオブジェクト名(アセット、マテリアル、最初のアトラスページ、所有するOSフォント)に付与されるサフィックス。Memory Profilerでの検索文字列です。 |
| `ShouldPreferCjkUi(SystemLanguage)` | 純粋なポリシー: 日本語、中国語(すべてのバリアント)、韓国語に対してtrue。 |
| `SanitizeDisplayText(string)` | ロスレスな表示衛生処理: すべての異体字セレクタ(表意文字用のものを含む)、ゼロ幅文字、BOM、絵文字タグ文字を除去します。クリーンな場合は同じインスタンスを返し、nullには`string.Empty`を返します。それ自体のグリフを描画する文字を除去することは決してありません。 |
| `SanitizeDisplayText(string, string)` | **ロッシー**なオーバーロード: 上記のストリップを行った後、すべての非BMPコードポイントと対をなさないサロゲートが、指定された置換文字列になります(このパラメータがオプトインです)。ストリップしてから置換するという順序は、文書化された保証です。 |
| `ResetCaches()` | キャッシュされたすべての解決結果を破棄し(パッケージが所有する一時オブジェクトを破棄します)、次のアクセスで再プローブされるようにします。 |

### `FontFixSettings`(静的設定、エディタアセンブリ)

コードファーストの候補設定です。実際に変化した値は`FontFix`のキャッシュを
無効化します。等しい値を代入してもキャッシュは温存されます。どのプロパティに
nullを代入しても、そのプロパティのデフォルトに戻ります。

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFontPaths` | 同梱の等幅TTFに対して試行される`EditorGUIUtility.Load`のパス群。空配列はこの階層を無効化します。 |
| `OsMonoFontNames` | 1つずつプローブされるOS等幅ファミリー名。空配列はこの階層を無効化します。 |
| `CjkUiFontNames` | 1つずつプローブされる、最も優先されるものから順のLatin+CJKファミリー名。空配列はCJK解決を完全に無効化します(その場合`ApplyCjkUi`はno-opになります)。 |
| `CjkUiStyleName` | `FontAsset.CreateFontAsset`に渡すスタイル名(デフォルトは`"Regular"`)。null/空文字列はデフォルトに戻します。 |
| `CjkUiBoldStyleName` | ボールドテキストがそれをレンダリングするようベースアセットのウェイトテーブルに組み込まれるフェイス(デフォルトは`"Bold"`)。nullはデフォルトに戻します。`""`は組み込みを無効化し、0.2.0以前の合成ボールドに戻します。 |
| `ResetToDefaults()` | すべてのプロパティを復元します。実際に何かが変化した場合にのみキャッシュを無効化します(テストのteardownで安全に使えます)。 |

### ランタイムユーティリティ(ランタイムアセンブリ)

**`TextSanitizer`** -- 純粋な表示テキストの衛生処理です。決して例外を投げず、
クリーンな高速パスでは決してアロケーションを行わず、nullは`string.Empty`に
マップします。

| メンバー | 動作 |
| --- | --- |
| `StripVariationSelectors(string)` | すべてのUnicode異体字セレクタを除去します: U+FE00..U+FE0F、モンゴル文字の自由変形セレクタ(FVS)U+180B..U+180Dおよび U+180F、そして表意文字選択子U+E0100..U+E01EF(有効なサロゲートペアとしてのみマッチします)。シェーピングに対して安全な粒度の細かい操作であり、ZWJ/ZWNJには一切触れません。 |
| `StripInvisibleCharacters(string)` | 上記のスーパーセットです。ゼロ幅スペース/非結合子/結合子(U+200B..U+200D)、単語結合子と不可視の数学演算子(U+2060..U+2064)、BOM(U+FEFF)、絵文字タグ文字(U+E0000..U+E007F)も除去します。`FontFix.SanitizeDisplayText`が転送する先はこれです。 |
| `ReplaceNonBmpCharacters(string, string)` | **ロッシー**: 各補助面コードポイント(サロゲートペアごとに1回の置換)と、対をなさない各サロゲートを置換文字列で置き換えます。置換文字列自体にサロゲートが含まれない場合、結果にサロゲートコード単位は含まれません。厳密にBMPに留まる必要がある表示面向けです。 |

**`CjkLanguage`**

| メンバー | 動作 |
| --- | --- |
| `ShouldPreferCjkUi(SystemLanguage)` | 純粋なja/zh/koポリシーです。意図的に言語をパラメータとして受け取ります。シリアライズ中に`Application.systemLanguage`を読み取ると例外が発生するためです(レシピ3を参照)。 |

**`SafeGlyphs`**

| メンバー | 動作 |
| --- | --- |
| `DefaultCodepoints` | 2022.3のエディタフォントでレンダリングされることが検証済みの、非ASCIIなBMPコードポイントの読み取り専用ホワイトリスト(箇条書き記号、矢印、チェックマーク、テキスト提示のギア/警告記号など)。 |
| `IsSafeCodepoint(int)` | 印字可能なASCII、またはホワイトリストのメンバーであればtrue。 |
| `IsSafeCodepoint(int, int[])` | 上記に加えて、プロジェクト固有の拡張リストを考慮します(エディタフォントでのカバレッジを検証した後にのみ拡張してください)。 |

**`FontFixDefaults`**

| メンバー | 動作 |
| --- | --- |
| `EditorMonoFontPaths` | デフォルトの同梱等幅プローブパス(RobotoMono)。 |
| `OsMonoFontNames` | デフォルトのOS等幅候補(Consolas、Menlo、DejaVu Sans Mono、Courier New)。 |
| `CjkUiFontNames` | デフォルトのLatin+CJKチェーン(Yu Gothic UI > Yu Gothic > Meiryo UI > Meiryo > Noto Sans CJK JP > MS UI Gothic)。 |
| `CjkUiStyleName` | `"Regular"`。 |

これらの配列は読み取り専用として扱ってください。カスタマイズは
`FontFixSettings`経由で行います。

### エディタヘルパー

**`GlyphAudit`** -- ソースレベルのグリフ安全性監査で、利用側のテストアセンブリ
から使用できます(レシピ6を参照)。

| メンバー | 動作 |
| --- | --- |
| `PackageRootPath()` | このパッケージ自身のソースのルートディレクトリ(embedded、junction、package-cacheのいずれのインストール形態でも)。該当なしの場合はnull。 |
| `AuditSourceDirectory(string, int[])` | ディレクトリ配下のすべての`*.cs`に対して(再帰的に)すべての監査を実行します。違反は`"file: reason"`形式で返されます。ディレクトリが存在しない場合、例外を投げる代わりに違反を1件返すため、パスが移動していてもテストが目に見える形で失敗します。 |
| `AuditSourceFile(string, int[])` | 1つのファイルに対するすべての監査。 |
| `AuditConstructedCodepoints(string, string, int[])` | ソース内でホワイトリスト外に構築されたコードポイントを検出します。U+FE0F/U+FE0E/U+FEFFの`(char)`キャストは対象外です。サニタイザーがそれらの値と正当に比較を行うことがあり、比較は決してレンダリングを引き起こさないためです。 |
| `AuditVariationSelectors(string, string)` | リテラルなU+FE0F/U+FE0Eおよびそのエスケープ形式を検出します。意図的に順序(オーディナル)スキャンを行います -- カルチャに依存する検索では、セレクタが照合上無視可能な文字として扱われるためです。 |
| `AuditAsciiOnly(string, byte[])` | ファイルごとに最初の非ASCIIバイトを検出します(厳格ASCIIのソースポリシーにより、グリフ内容がレビュー可能かつdiffセーフに保たれます)。 |

**`FontFixDiagnostics`**

| メンバー | 動作 |
| --- | --- |
| `BuildReport()` | プレーンテキストのASCIIレポート: 解決結果、CJKアトラスページの状態、候補の利用可能性、既知の落とし穴のチェックリスト。決して例外を投げず、バッチモードでも安全です。 |

**`FontFixDiagnosticsWindow`**

| メンバー | 動作 |
| --- | --- |
| `Open()` / **Window > UITK Font Fix > Diagnostics** | *Re-probe*と*Copy report*を備えた読み取り専用の診断ウィンドウ。パッケージ自身の合成パターン(CJKルート、等幅のレポート本文)をドッグフーディングしています。 |

## 検証済みの挙動(この設計の根拠)

Unity 2022.3のTextCore / UI Toolkitに関する以下の事実は、インタラクティブ
エディタとバッチエディタの両方で(主にWindows上で。ヘッドレスのサブセットは
Linuxで再検証済み)、経験的に確立されたものです。上記の自明でない設計上の
判断はすべて、これらのいずれかに由来しています。

1. `FontEngine.LoadFontFace(Font)`は、**あらゆる**OS動的フォント
   (`Font.CreateDynamicFontFromOSFont`)に対して`Invalid_File`を返します。
   OSフォントは`Font`ルートでは検証できません -- これが、このパッケージが
   決してOSの`Font`をUI Toolkitに渡さない理由です。
2. 機能するOSフォントのルートは、`FontDefinition`経由で割り当てられる
   `TextCore.Text.FontAsset.CreateFontAsset(family, style)`(DynamicOSモード)
   です。グリフはレンダリング時に遅延して取得されます(作成直後に
   `HasCharacter`がfalseを返すのは正常です)。作成処理はヘッドレスでも機能
   します。
3. **名前配列**を指定した`Font.CreateDynamicFontFromOSFont`は読み込み不能な
   フェイス(「Unable to load font face」/「Can't Generate Mesh」)を生成し、
   テキストは空でレンダリングされます。単一名の場合もフェイス検証を通過
   しないため、このルートは完全に避けられています。
4. `EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf")`は
   2022.3で解決します -- 実体のあるTTFアセットはTextCoreが常に受け入れる
   ため、これが優先される等幅候補になっています。
5. USSはOSフォントを名前で選択できません。フォントの適用はC#から
   `style.unityFontDefinition`経由で行われます。インラインスタイルは継承値
   に勝つため、これがルート/リーフの合成パターンを信頼できるものにして
   います。
6. エディタのデフォルトフォント(Inter)にはCJKのカバレッジがありません。
   グリフ単位のOSフォールバックはフォントとウェイトを混在させ、CJKテキスト
   を「まだら太字」でレンダリングします。コンテナルートに明示的な
   Latin+CJKフォントを1つ置くことで、このフォールバック経路が取り除かれ
   ます。動作を検証済みのチェーン: Yu Gothic UI > Yu Gothic > Meiryo UI >
   Meiryo > Noto Sans CJK JP > MS UI Gothic。
7. `Font.GetOSInstalledFontNames()`は、ローカライズされた(例えば日本語の)
   Windowsでも**英語の**ファミリー名を報告します -- これが候補のデフォルト
   が英語名である理由です。
8. リッチテキストの`<mark>`タグはDynamicOSのFontAssetと互換性がありません。
   マーククアッドは、そのグリフが属するアトラスページによってグリフの上か
   下に描画され、まだらな太字/薄れた文字列を生じさせます(不透明なマーク
   の場合はテキストが完全に隠れることもあります)。アトラスの事前充填でも
   これは解決できません(ASCIIだけでも4ページにまたがり、CJKは決して単一
   ページには収まりません)。これらのフォントと`<mark>`を組み合わせないで
   ください。
9. 絵文字面の文字(例: U+1F4CE)や、モデルやユーザーが日常的に出力する
   異体字セレクタ(U+FE0F)は、エディタフォントにグリフを持ちません。
   プレースホルダーの四角に加え、描画ごとのコンソール警告が大量発生します。
   自由入力テキストは`SanitizeDisplayText`を通し、固定UI文字列は
   `SafeGlyphs`ホワイトリストの内側に留めます。
10. `Application.systemLanguage`は、ScriptableObjectのコンストラクタや
    フィールド初期化子から読み取ると例外を投げます。`OnEnable`以降で問い
    合わせ、その値を`ShouldPreferCjkUi`に渡してください。
11. バッチモードでは、OS動的フォントの**フェイス**操作はすべて失敗する
    一方で、DynamicOS FontAssetの**作成**は機能します。テストスイートは、
    まさにこの違いを軸に構築されています。

## Unityバージョン互換性

主対象は**Unity 2022.3 LTS**です -- 上記のすべての項目はここで検証されました。
このパッケージは、LinuxエディタでもヘッドレスでコンパイルされEditModeスイート
に合格します(CJK解決はインストール済みのOSフォントに依存するため、CJK候補の
ないマシンでは正しく「none resolved」を報告します)。

バージョンに依存するTextCore/UI Toolkitの呼び出しはすべて、単一の内部シーム
(`FontShims`)に集約されています。UnityCsReferenceのソースに対する検証:
`FontDefinition.FromSDFFont(FontAsset)`、`FontAsset.CreateFontAsset(family,
style)`、`atlasPopulationMode`、`atlasTextures`は2022.3、2023.2、6000.0の
各ブランチで同一であるため、このパッケージにはバージョン分岐が不要です。
将来のUnityバージョンがこれらのAPIのいずれかを変更した場合でも、修正は
そのファイル1つに封じ込められます。

## サンプル

インポート可能な2つのサンプルがパッケージに同梱されています(Package
Manager > UITK Font Fix > *Samples*):

- **Editor Font Setup** -- コンテナ/リーフの合成パターン、安全な言語分岐、
  表示テキストのサニタイズを実演する完全な`EditorWindow`です。インポートして
  *Window*メニューからそのウィンドウを開き、自分自身のツールウィンドウの
  出発点として使ってください。
- **Glyph Audit Tests** -- `GlyphAudit`と`SafeGlyphs`をあなた自身のプロジェクト
  ソースに組み込む、コピーしてすぐ使えるEditModeテストファイルです。スキャン
  対象のディレクトリと追加コードポイントのホワイトリストを、あなたの
  プロジェクトに合わせて調整してください。

## ライセンス

MIT -- 詳細は[LICENSE.md](LICENSE.md)を参照してください。
