namespace PileDesign.Constants;

/// <summary>
/// 杭工法（PileConstructionType）の名称定数。
///
/// <see cref="PileTypeNames"/> と同じ思想で、入力データ（JSON）・UI 表示・分岐判定が
/// 共有する識別子を一箇所に集約する。値の変更は保存ファイル互換を壊すため不可。
///
/// プレボーリングは過去の入力ファイルに 3 通りの表記が混在しており
/// （「埋込み杭（プレボーリング）」「埋込み杭（プレボーリング杭）」「埋込み杭（プレポーリング）」）、
/// 判定は必ず <see cref="IsPreboring"/> を通すこと。個別に <c>==</c> で比べると
/// 表記揺れのファイルで分岐が静かに外れ、支持力が既定値のまま計算される。
///
/// 注: XAML 内（VisibilityConverter の ConverterParameter 等）は文字列のままなので、
/// 値を変える場合は XAML 側の一致も必要（変えない前提）。
/// </summary>
public static class PileConstructionTypeNames
{
    /// <summary>場所打ちコンクリート杭</summary>
    public const string Insitu = "場所打ちコンクリート杭";

    /// <summary>埋込み杭（プレボーリング）— 現行の正規表記</summary>
    public const string Preboring = "埋込み杭（プレボーリング）";

    /// <summary>埋込み杭（プレボーリング）の旧表記。新規入力では使わない。</summary>
    public const string PreboringLegacyPile = "埋込み杭（プレボーリング杭）";

    /// <summary>埋込み杭（プレボーリング）の旧表記（誤記）。新規入力では使わない。</summary>
    public const string PreboringLegacyTypo = "埋込み杭（プレポーリング）";

    /// <summary>埋込み杭（中掘り）</summary>
    public const string Chubori = "埋込み杭（中掘り）";

    /// <summary>打込み杭</summary>
    public const string Driven = "打込み杭";

    /// <summary>回転貫入杭</summary>
    public const string Rotary = "回転貫入杭";

    /// <summary>
    /// Smart-MAGNUM 工法（ジャパンパイル、プレボーリング拡大根固め工法）。
    ///
    /// 大臣認定 TACP-0625（砂質）/ 0626（礫質）/ 0627（粘土質）、
    /// 引抜きは GBRC 性能証明第 20-21 号。
    ///
    /// 支持力式が基礎指針'19 の <c>q_p = f(N)</c> 形とは構造が異なり、
    /// 先端は根固め部の拡大比 ωp と杭下拡大根固め部長さ LL から定まる α を用いて
    /// <c>Rp = α·N·Ap</c>（Ap は<b>節部径 Don</b> 基準）、
    /// 周面は 標準型/周面強化型 × ストレート杭/節杭 の 4 通りで係数が変わる。
    /// 算定は <c>SoilPile.SmartMagnum.cs</c> に集約している。
    /// </summary>
    public const string SmartMagnum = "埋込み杭（Smart-MAGNUM）";

    // ─── グループ判定 ───

    /// <summary>
    /// プレボーリング系（基礎指針'19 の埋込み杭・プレボーリング）か。
    /// 過去ファイルの 3 表記を吸収する。Smart-MAGNUM は施工分類こそプレボーリングだが
    /// 支持力式が別系統なので<b>含めない</b>。
    /// </summary>
    public static bool IsPreboring(string? constructionType) =>
        constructionType is Preboring or PreboringLegacyPile or PreboringLegacyTypo;

    /// <summary>Smart-MAGNUM 工法か。</summary>
    public static bool IsSmartMagnum(string? constructionType) =>
        constructionType == SmartMagnum;

    /// <summary>
    /// 杭先端に拡大根固め部（ソイルセメント球根）を持つ工法か。
    /// 姿図・3D の先端形状分岐で使う。
    /// </summary>
    public static bool HasEnlargedBulb(string? constructionType) =>
        IsPreboring(constructionType) || constructionType == Chubori || IsSmartMagnum(constructionType);
}
