using System.Collections.Generic;

namespace PileDesign.Constants;

/// <summary>
/// 異形棒鋼の公称断面積 (mm²)。JIS G 3112 の値を正とする (2026-11-23 利用者の判断)。
///
/// <para>以前は同じ表の写しが 4 か所 (主筋 PileSection・杭頭 PileTop・材料モデル MainBars・
/// キャプリングパイル CapringPile) にあり、前の 3 つは RC 規準の表の丸めた値 (D25 = 507 など)、
/// キャプリングパイルだけ JIS の公称値 (D25 = 506.7) で、同じ呼び名の断面積が割れていた。
/// 鉄筋の断面積はここだけで持つ。</para>
///
/// <para>工法のせん断補強筋 (MD・U の呼び名) は工法の指針の表で、ここには入れない
/// (ShearReinforcementMethods)。</para>
/// </summary>
public static class RebarAreas
{
    private static readonly Dictionary<string, double> Areas = new()
    {
        ["D10"] = 71.33,
        ["D13"] = 126.7,
        ["D16"] = 198.6,
        ["D19"] = 286.5,
        ["D22"] = 387.1,
        ["D25"] = 506.7,
        ["D29"] = 642.4,
        ["D32"] = 794.2,
        ["D35"] = 956.6,
        ["D38"] = 1140.0,
        ["D41"] = 1340.0,
        ["D51"] = 2027.0,
    };

    /// <summary>呼び名 (「D25」など) の公称断面積 (mm²)。知らない呼び名・null は 0。</summary>
    public static double Of(string? barSize)
        => barSize != null && Areas.TryGetValue(barSize, out var area) ? area : 0.0;

    /// <summary>表にある呼び名か。</summary>
    public static bool Contains(string? barSize) => barSize != null && Areas.ContainsKey(barSize);

    /// <summary>表の呼び名 (細い順)。</summary>
    public static IEnumerable<string> Sizes => Areas.Keys;
}
