namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 鉛直に解く計算の結果 (単杭沈下 (基礎梁考慮)・群杭沈下の記録) が、どの荷重ケース・荷重組合せのものか。
    ///
    /// <para>以前は結果の見出し (「L1-1: U1 / 組合せNo.2 (βU=1, βL=-1)」など) だけを持ち、照合のたびに文字から
    /// 読み取っていた。ケース名の文字 (「地震 VL」・「 / 組合せ」・括弧) や係数の丸めで読み違え、解析のあとに
    /// 荷重ケースを並べ替えると、番号の同じ別のケースと照合しえた (2026-11-04 外部レビュー)。
    /// 結果を作るときにこの値を持たせ、照合はこれで行う (<see cref="SeismicCaseLabels.KeyOf"/>)。
    /// これより前に保存した結果は持たないので、見出しから読む。</para>
    /// </summary>
    public sealed class ResultCaseIdentity
    {
        /// <summary>常時は 0、地震時は 1・2。見出しから読めなかった (以前の結果の見慣れない見出し) ときは -1。</summary>
        public int Level { get; set; }

        /// <summary>荷重ケースの番号 (そのレベルの並びの 1 始まり)。常時は 0。</summary>
        public int CaseNo { get; set; }

        /// <summary>荷重ケース名 (解いたときの名前)。常時は "VL"。</summary>
        public string CaseName { get; set; } = "";

        /// <summary>荷重組合せの番号 (地震時軸力の分離入力で組合せごとに解いたとき。βU・βL の同じ組合せは代表の番号)。それ以外は null。</summary>
        public int? CombinationNo { get; set; }

        /// <summary>組合せの βU (丸めない値)。組合せが無ければ null。</summary>
        public double? BetaU { get; set; }

        /// <summary>組合せの βL (丸めない値)。組合せが無ければ null。</summary>
        public double? BetaL { get; set; }

        /// <summary>常時 (VL) の結果か。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsLongTerm => Level == 0;

        /// <summary>荷重組合せごとに解いた結果か。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool HasCombination => CombinationNo != null;

        /// <summary>常時 (VL) の結果。</summary>
        public static ResultCaseIdentity LongTerm() => new() { Level = 0, CaseNo = 0, CaseName = "VL" };

        /// <summary>地震時の荷重ケース (と、分離入力のときは組合せ) の結果。</summary>
        public static ResultCaseIdentity Seismic(int level, int caseNo, string? caseName, LoadCombination? combination) => new()
        {
            Level = level,
            CaseNo = caseNo,
            CaseName = caseName ?? "",
            CombinationNo = combination?.No,
            BetaU = combination?.Beta1,
            BetaL = combination?.Beta2,
        };

        /// <summary>写し (結果を写すときに、元の結果と同じ実体を共有しない)。</summary>
        public ResultCaseIdentity Clone() => new()
        {
            Level = Level, CaseNo = CaseNo, CaseName = CaseName,
            CombinationNo = CombinationNo, BetaU = BetaU, BetaL = BetaL,
        };
    }
}
