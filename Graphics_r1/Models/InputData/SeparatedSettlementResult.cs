namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 地震時軸力の分離入力 (<see cref="InputModel.IsAxialForceSeparated"/>) のときの、単杭沈下の組合せごとの結果 1 件。
    ///
    /// <para>分離入力では地震時軸力が荷重組合せごとに N = VL + βL·ΔL(L) + βU·ΔL(U) と変わるので、
    /// 単杭沈下も (レベル, 荷重ケース, 組合せ) ごとに求めて残す。βU・βL の同じ組合せは軸力も同じなので 1 件にまとめ、
    /// 代表の組合せ番号 (<see cref="CombinationNo"/>) と係数を持つ。照合は係数で行う
    /// (<see cref="SeismicAxialForces.FindSeparatedSettlement"/>)。</para>
    ///
    /// <para>ケースごとの欄 (<see cref="PileLayoutDataItem.SinglePileSettlementLevel1s"/> など) には、
    /// 組合せのうち軸力が最大のもの (沈下が最も大きくなる側) を入れる。ケースだけで引く所はそちらを読む。</para>
    /// </summary>
    public class SeparatedSettlementResult
    {
        public int Level { get; set; }
        /// <summary>荷重ケースの番号 (1 始まり)。</summary>
        public int CaseNo { get; set; }
        /// <summary>代表の荷重組合せの番号 (βU・βL の同じ組合せの先頭)。</summary>
        public int CombinationNo { get; set; }
        /// <summary>上部構造慣性力の係数 βU。</summary>
        public double BetaU { get; set; }
        /// <summary>基礎部慣性力の係数 βL。</summary>
        public double BetaL { get; set; }
        /// <summary>沈下を求めた軸力 [kN] (圧縮が正)。</summary>
        public double AxialLoad { get; set; }
        /// <summary>単杭沈下量 [m]。</summary>
        public double Settlement { get; set; }
        /// <summary>軸力が荷重-沈下曲線の範囲外だったか (端の値を使った)。</summary>
        public bool BeyondCurve { get; set; }

        public SeparatedSettlementResult Clone() => new()
        {
            Level = Level, CaseNo = CaseNo, CombinationNo = CombinationNo, BetaU = BetaU, BetaL = BetaL,
            AxialLoad = AxialLoad, Settlement = Settlement, BeyondCurve = BeyondCurve,
        };
    }
}
