namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭頭 M-θ 曲線テーブル行（1ばねにつき複数行）
    /// PointIndex > 0: 曲線定義点、PointIndex = 0: 最終ステップ解析結果
    /// </summary>
    public sealed class MThetaCurveRow
    {
        [ResultColumn("SpringIdx", 0)] public int SpringIndex { get; init; }
        [ResultColumn("SpringName", 1)] public string SpringName { get; init; } = "";
        [ResultColumn("PointIdx", 2)] public int PointIndex { get; init; }
        [ResultColumn("θ(rad)", 10, "N6")] public double Theta { get; init; }
        [ResultColumn("M(kNm)", 11, "N1")] public double Moment { get; init; }
        [ResultColumn("Kθ(kNm/rad)", 12, "N0")] public double Ktheta { get; init; }
        [ResultColumn("状態", 20)] public string Status { get; init; } = "";
    }
}
