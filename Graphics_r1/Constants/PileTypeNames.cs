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

    /// <summary>PRC杭</summary>
    public const string Prc = "PRC杭";

    /// <summary>SC杭</summary>
    public const string Sc = "SC杭";

    /// <summary>鋼管部（鋼管杭の純鋼管区間）</summary>
    public const string SteelPipeSection = "鋼管部";

    /// <summary>コンクリート充填鋼管部（鋼管杭の杭頭部、鉄筋定着工法用）</summary>
    public const string CftSection = "コンクリート充填鋼管部";
}
