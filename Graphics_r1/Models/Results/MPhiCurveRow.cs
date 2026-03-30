namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭要素の M-φ 曲線テーブル行（1要素につき複数行）
    /// PointIndex > 0: 曲線定義点、PointIndex = 0: 最終ステップ解析結果
    /// </summary>
    public sealed class MPhiCurveRow
    {
        [ResultColumn("ElemIdx", 0)] public int ElementIndex { get; init; }
        [ResultColumn("ElemName", 1)] public string ElementName { get; init; } = "";
        [ResultColumn("N(kN)", 2, "N1")] public double AxialForce { get; init; }
        [ResultColumn("PointIdx", 3)] public int PointIndex { get; init; }
        [ResultColumn("φ(rad/m)", 10, "N6")] public double Phi { get; init; }
        [ResultColumn("M(kNm)", 11, "N1")] public double Moment { get; init; }
        [ResultColumn("EI(kNm²)", 12, "N0")] public double EI { get; init; }
        [ResultColumn("状態", 20)] public string Status { get; init; } = "";
    }
}
