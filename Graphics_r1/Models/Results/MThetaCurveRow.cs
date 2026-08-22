namespace PileDesign.Models.Results
{
    /// <summary>
    /// 杭頭 M-θ 曲線テーブル行（1ばねにつき複数行）
    /// PointIndex > 0: 曲線定義点、PointIndex = 0: 最終ステップ解析結果
    /// </summary>
    public sealed class MThetaCurveRow
    {
        [ResultColumn("SpringIdx", 0, tooltip: "杭頭回転ばねの通し番号（1 始まり）")] public int SpringIndex { get; init; }
        [ResultColumn("SpringName", 1, tooltip: "杭頭回転ばねの名称")] public string SpringName { get; init; } = "";
        [ResultColumn("PointIdx", 2, tooltip: "1 以上は M-θ 曲線の定義点の番号。0 は最終ステップの解析結果")] public int PointIndex { get; init; }
        [ResultColumn("θ(rad)", 10, "N6", "杭頭と基礎（フーチング）の相対回転角")] public double Theta { get; init; }
        [ResultColumn("M(kNm)", 11, "N1", "その回転角に対応する杭頭曲げモーメント")] public double Moment { get; init; }
        [ResultColumn("Kθ(kNm/rad)", 12, "N0", "杭頭回転ばね剛性 M/θ")] public double Ktheta { get; init; }
        [ResultColumn("状態", 20, tooltip: "その点が曲線上のどの領域か（弾性・降伏後など）")] public string Status { get; init; } = "";
    }
}
