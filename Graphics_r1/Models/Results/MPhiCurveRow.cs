namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭要素の M-φ 曲線テーブル行（1要素につき複数行）
    /// PointIndex > 0: 曲線定義点、PointIndex = 0: 最終ステップ解析結果
    /// </summary>
    public sealed class MPhiCurveRow
    {
        [ResultColumn("ElemIdx", 0)] public int ElementIndex { get; init; }
        [ResultColumn("杭No", 1)] public int PileNo { get; init; }
        [ResultColumn("要素順", 2)] public int SegmentOrder { get; init; }
        [ResultColumn("ElemName", 3)] public string ElementName { get; init; } = "";
        [ResultColumn("N入力(kN)", 4, "N1", "入力値軸力（圧縮が正）")] public double InputAxialForce { get; init; }
        [ResultColumn("N解析(kN)", 5, "N1", "応力解析で得られた軸力 -Fx（圧縮が正）")] public double AnalysisAxialForce { get; init; }
        [ResultColumn("PointIdx", 6)] public int PointIndex { get; init; }
        [ResultColumn("φ(rad/m)", 10, "N6")] public double Phi { get; init; }
        [ResultColumn("M(kNm)", 11, "N1")] public double Moment { get; init; }
        [ResultColumn("EI(kNm²)", 12, "N0")] public double EI { get; init; }
        [ResultColumn("状態", 20)] public string Status { get; init; } = "";
    }
}
