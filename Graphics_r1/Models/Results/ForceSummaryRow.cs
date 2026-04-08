namespace PileDesign.Models.Results
{
    /// <summary>
    /// 外力・反力サマリーテーブル行
    /// </summary>
    public sealed class ForceSummaryRow
    {
        [ResultColumn("項目", 0)] public string Item { get; init; } = "";
        [ResultColumn("Fx(kN)", 1, "N1")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 2, "N1")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 3, "N1")] public double Fz { get; init; }
        [ResultColumn("Fh(kN)", 4, "N1", "水平力合成 √(Fx²+Fy²)")] public double Fh { get; init; }
    }
}
