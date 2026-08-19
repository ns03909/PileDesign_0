namespace PileDesign.Constants;

/// <summary>
/// 杭種（PileBodyType）・断面タイプ（PileSectionType）の名称定数。
///
/// これらの文字列は入力データ（JSON）・UI 表示・分岐判定のすべてで共有される識別子であり、
/// 従来はリテラル直書きが 100 箇所以上に散在していた（typo すると分岐が静かに死ぬ）。
/// C# コードでは必ず本定数を参照すること。値の変更は保存ファイル互換を壊すため不可。
///
/// 注: XAML 内（VisibilityConverter の ConverterParameter 等）は文字列のままなので、
/// 値を変える場合は XAML 側の一致も必要（変えない前提）。
/// </summary>
public static class PileTypeNames
{
    // ─── PileBodyType（杭体タイプ） ───

    /// <summary>場所打ち鉄筋コンクリート杭</summary>
    public const string InsituRc = "場所打ち鉄筋コンクリート杭";

    /// <summary>場所打ち鋼管コンクリート杭</summary>
    public const string InsituSteelPipeConcrete = "場所打ち鋼管コンクリート杭";

    /// <summary>既製コンクリート杭（PHC / PRC / SC）</summary>
    public const string PrecastConcrete = "既製コンクリート杭";

    /// <summary>鋼管杭</summary>
    public const string SteelPipe = "鋼管杭";

    // ─── PileSectionType（断面タイプ） ───

    /// <summary>鉄筋コンクリート部（場所打ち系）</summary>
    public const string RcSection = "鉄筋コンクリート部";

    /// <summary>鋼管コンクリート部（場所打ち鋼管コンクリート杭）</summary>
    public const string SteelPipeConcreteSection = "鋼管コンクリート部";

    /// <summary>PHC杭</summary>
    public const string Phc = "PHC杭";

    /// <summary>
    /// PHC節杭（軸部に一定ピッチで節を設けた既製コンクリート杭）。
    ///
    /// 断面性能はカタログ上すべて<b>軸部</b>の中空円形断面基準であり、断面耐力の計算は
    /// <see cref="Phc"/> と完全に同一（<c>PHCSection</c> をそのまま使う）。
    /// ストレート PHC 杭と異なるのは支持力の周面抵抗・自重・工法での使い分けであり、
    /// これらを取り違えないよう別の断面タイプとして区別している。
    /// </summary>
    public const string PhcNodular = "PHC節杭";

    /// <summary>PRC杭</summary>
    public const string Prc = "PRC杭";

    /// <summary>
    /// PRC節杭の PRC部（節杭のうち、PC鋼棒に加えて異形棒鋼を配した杭頭側の区間）。
    ///
    /// 断面性能は <see cref="PhcNodular"/> と同じく<b>軸部</b>の中空円形断面基準なので、
    /// 断面耐力の計算は <see cref="Prc"/> と完全に同一（<c>PRCSection</c> をそのまま使う）。
    /// </summary>
    public const string PrcNodular = "PRC節杭";

    /// <summary>
    /// PRC節杭の PHC部（同じ製品のうち、異形棒鋼を持たない下側の区間）。
    ///
    /// 1 本の PRC節杭 は杭頭側が PRC部、それより下が PHC部という 2 断面構成なので、
    /// 場所打ち鋼管コンクリート杭の「鋼管コンクリート部 / 鉄筋コンクリート部」と同じ思想で
    /// 別々の断面タイプとして杭区間に割り当てる。断面耐力の計算は <see cref="Phc"/> と同一。
    /// </summary>
    public const string PrcNodularPhcPart = "PRC節杭(PHC部)";

    /// <summary>
    /// 頭部厚型節付き杭の頭部軸部 (三谷セキサン BF.S パイル)。
    ///
    /// この製品は 1 本の杭が外径の違う 2 つの軸部から成るため、
    /// 場所打ち鋼管コンクリート杭の「鋼管コンクリート部 / 鉄筋コンクリート部」と同じ思想で
    /// 頭部軸部・先端軸部を別々の断面タイプとして杭区間に割り当てる。
    /// 断面耐力の計算は <see cref="Phc"/> と同一 (PC 鋼棒のみの中空断面)。
    /// </summary>
    public const string BfsHead = "BF.S頭部軸部";

    /// <summary>
    /// 頭部厚型節付き杭の先端軸部 (三谷セキサン BF.S パイル)。
    /// 頭部軸部より外径・肉厚が小さく、節はこちらに付く。
    /// カタログに断面性能・耐力の記載が無いため、断面諸元は取り込み時の算定値、
    /// 耐力はアプリ側の断面計算 (PHCSection) による。
    /// </summary>
    public const string BfsTip = "BF.S先端軸部";

    /// <summary>SC杭</summary>
    public const string Sc = "SC杭";

    /// <summary>鋼管部（鋼管杭の純鋼管区間）</summary>
    public const string SteelPipeSection = "鋼管部";

    /// <summary>コンクリート充填鋼管部（鋼管杭の杭頭部、鉄筋定着工法用）</summary>
    public const string CftSection = "コンクリート充填鋼管部";

    // ─── 断面タイプのグループ判定 ───
    // 節杭は「断面耐力はストレート杭と同一・形状と自重だけが違う」ため、
    // 分岐のたびに `== Phc || == PhcNodular || …` と並べると追随漏れが起きる。
    // 判定は必ず以下のヘルパーを通すこと。

    /// <summary>断面耐力の計算が PHC杭 と同一の断面タイプか。</summary>
    public static bool IsPhcLikeSection(string? sectionType) =>
        sectionType is Phc or PhcNodular or PrcNodularPhcPart or BfsHead or BfsTip;

    /// <summary>断面耐力の計算が PRC杭 と同一の断面タイプか。</summary>
    public static bool IsPrcLikeSection(string? sectionType) =>
        sectionType is Prc or PrcNodular;

    /// <summary>節（軸部に一定ピッチで設けた拡径部）を持つ断面タイプか。</summary>
    public static bool IsNodularSection(string? sectionType) =>
        sectionType is PhcNodular or PrcNodular or PrcNodularPhcPart or BfsHead or BfsTip;
}
