using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// 水平解析の前回実行設定スナップショット。
    /// 「追加実行」(段階追加再解析) で前回と互換性がある実行かを検証するために
    /// AnaModel.LastRunConfig として保持され、ProjectData 経由で JSON 永続化される。
    /// 旧 JSON 互換のため、AnaModel.LastRunConfig は null 許容 (= 過去の追加実行情報なし)。
    /// </summary>
    public sealed class AnalysisRunSnapshot
    {
        // 解析パラメータ (互換性比較用)
        public string LiquefactionOption { get; set; } = "None";
        public int Level1StepsCount { get; set; }
        public int Level2StepsCount { get; set; }
        public bool UseModifiedNewtonRaphson { get; set; }
        public int FullNRIterations { get; set; }
        public bool SkipIteration { get; set; }
        public bool UseLineSearch { get; set; }
        public double RelaxationFactor { get; set; }
        public bool UseAnalysisAxialForce { get; set; }
        public string ConnectionMode { get; set; } = "RigidBody";

        /// <summary>基礎のねじれ (代表節点の Rz) を拘束したか。境界条件が変わるので追加実行はできない。</summary>
        public bool RestrainFoundationTorsion { get; set; }

        /// <summary>
        /// 最後まで解けた (全ステップを受理した) ケース。追加実行はここにあるケースだけを飛ばす。
        /// 途中までの結果・未収束のケースは入れない (追加実行でやり直す)。
        /// </summary>
        public List<CaseKey> ExecutedCaseKeys { get; set; } = new();

        /// <summary>
        /// 解析したときの入力の署名 (HorizontalCalculationViewModel.HorizontalInputSignature)。
        /// 追加実行は、今の入力の署名がこれと一致するときだけできる。無い (以前の版の結果) ときもできない。
        /// </summary>
        public string? InputModelHash { get; set; }

        /// <summary>
        /// 1 ケース 1 件を表す識別子。荷重レベル・荷重ケース番号・荷重組合せ番号・液状化の別。
        ///
        /// 以前は荷重ケース名と、係数を丸めた組合せの表示名で作っていた。表示名の重なる別の組合せを
        /// 既存のケースと取り違えて飛ばし得た。番号は読込で並び順に揃えるので一意。
        /// </summary>
        public sealed record CaseKey(int Level, int CaseNo, int CombinationNo, bool IsLiquefaction)
        {
            public static CaseKey Of(Models.InputData.LoadCase loadCase, Models.InputData.LoadCombination combination, bool isLiquefaction)
                => new(loadCase.Level, loadCase.No, combination.No, isLiquefaction);

            /// <summary>画面の「済」の印で使う文字列 (荷重ケースごとの接頭辞 <see cref="PrefixOf"/> で始まる)。</summary>
            public string ToDisplayKey() => $"{PrefixOf(Level, CaseNo)}{CombinationNo}|{IsLiquefaction}";

            public static string PrefixOf(int level, int caseNo) => $"L{level}-{caseNo}|";
        }
    }
}
