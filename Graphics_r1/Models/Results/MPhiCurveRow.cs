namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭要素の M-φ 曲線テーブル行（1要素につき複数行）
    /// PointIndex > 0: 曲線定義点、PointIndex = 0: 最終ステップ解析結果
    /// </summary>
    public sealed class MPhiCurveRow
    {
        [ResultColumn("ElemIdx", 0, tooltip: "梁要素の通し番号（1 始まり）")] public int ElementIndex { get; init; }
        [ResultColumn("杭No", 1, tooltip: "杭配置の番号")] public int PileNo { get; init; }
        [ResultColumn("要素順", 2, tooltip: "その杭の中で杭頭から数えた要素の順番")] public int SegmentOrder { get; init; }
        [ResultColumn("ElemName", 3, tooltip: "梁要素の名称")] public string ElementName { get; init; } = "";
        [ResultColumn("N入力(kN)", 4, "N1", "入力値軸力（圧縮が正）")] public double InputAxialForce { get; init; }
        [ResultColumn("N解析(kN)", 5, "N1", "応力解析で得られた軸力 -Fx（圧縮が正）")] public double AnalysisAxialForce { get; init; }
        [ResultColumn("PointIdx", 6, tooltip: "1 以上は M-φ 曲線の定義点の番号。0 は最終ステップの解析結果")] public int PointIndex { get; init; }
        [ResultColumn("φ(rad/m)", 10, "N6", "曲率。単位長さあたりの回転角")] public double Phi { get; init; }
        [ResultColumn("M(kNm)", 11, "N1", "その曲率に対応する曲げモーメント")] public double Moment { get; init; }
        [ResultColumn("EI(kNm²)", 12, "N0", "曲げ剛性 M/φ。ひび割れ・降伏が進むほど小さくなる")] public double EI { get; init; }
        [ResultColumn("状態", 20, tooltip: "その点が曲線上のどの領域か（弾性・ひび割れ後・降伏後など）")] public string Status { get; init; } = "";
    }
}
