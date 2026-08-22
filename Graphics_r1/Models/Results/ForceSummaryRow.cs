namespace PileDesign.Models.Results
{
    /// <summary>
    /// 外力・反力サマリーテーブル行
    /// </summary>
    public sealed class ForceSummaryRow
    {
        [ResultColumn("項目", 0, tooltip: "集計の対象（外力の合計・反力の合計など）")] public string Item { get; init; } = "";
        [ResultColumn("Fx(kN)", 1, "N1", "全体座標系 X 方向（水平）の合計")] public double Fx { get; init; }
        [ResultColumn("Fy(kN)", 2, "N1", "全体座標系 Y 方向（水平）の合計")] public double Fy { get; init; }
        [ResultColumn("Fz(kN)", 3, "N1", "全体座標系 Z 方向（鉛直、上向きが正）の合計")] public double Fz { get; init; }
        [ResultColumn("Fh(kN)", 4, "N1", "水平力合成 √(Fx²+Fy²)")] public double Fh { get; init; }
    }
}
