using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// 杭節点 1 点の鉛直方向地盤ばね物理モデル。
    ///
    /// 設計意図:
    ///   沈下解析 (<see cref="VerticalLoadTransferMethod"/>) と「完全に同じ」物理関数
    ///   (杭周面: τ-s 双線形、杭先端: Rp-Sp = 0.1Dp(αRp/Rpu + (1-α)(Rp/Rpu)^n)) を
    ///   水平解析でもそのまま呼び出し、相対変位 s から接線/割線剛性と内力を返す。
    ///
    /// 従来の <see cref="VerticalPileSpringCurve"/> は (δ, P) の離散点列を線形補間する近似モデルで、
    ///   ・刻みが粗いと曲線形状が階段状に劣化
    ///   ・K_min floor (1% × K_init) で plateau 領域が過大評価
    ///   ・履歴外挿で物理的妥当性なし
    /// などの誤差源があった。本クラスは沈下解析と同じ純関数を直接呼ぶことでこれらを完全に解消する。
    ///
    /// 符号規約: s > 0 = 圧縮 (沈下方向、杭が下、地盤が上 = 杭周負摩擦)、s &lt; 0 = 引抜き
    ///   - FEM 相対変位: s = -(pn.Uz - sn.Uz) = sn.Uz - pn.Uz (杭が沈下 = pn.Uz &lt; 0 = s &gt; 0)
    ///   - state: s ≥ 0 → "positive", s &lt; 0 → "negative" で自動切替
    ///     (沈下解析 GetTangentStiffnessPilePerimeter の "positive"/"negative" は押込み/引抜き方向の意味)
    /// </summary>
    public class PileVerticalSoilSpringModel
    {
        // この節点に寄与する杭周面要素 (1 ノードに対し最大 2 要素: i-1 と i)
        private readonly List<PileCircumVertical> _perimeterContribs;

        // 杭先端パラメータ (先端節点のみ非 null)
        private readonly bool _isToe;
        private readonly double _dp;        // m
        private readonly double _rpu;       // kN
        private readonly double _alpha;
        private readonly double _n;

        // 自重 (この節点に集約される杭体重量 kN)
        public double Weight { get; }

        /// <summary>
        /// 杭節点 k (0-based、杭頭=0) に対応する物理モデルを構築。
        /// </summary>
        /// <param name="circumVerticals">沈下解析側の PileCircumVerticals 配列</param>
        /// <param name="nodeIndex">節点インデックス k (杭頭=0)</param>
        /// <param name="isToe">杭先端ノードか</param>
        /// <param name="dp">杭先端径 [m]</param>
        /// <param name="rpu">極限先端支持力 [kN]</param>
        /// <param name="alpha">先端 R-S 曲線パラメータ α</param>
        /// <param name="n">先端 R-S 曲線パラメータ n</param>
        /// <param name="weight">この節点に集約される自重 [kN]</param>
        public PileVerticalSoilSpringModel(
            IList<PileCircumVertical> circumVerticals,
            int nodeIndex,
            bool isToe,
            double dp,
            double rpu,
            double alpha,
            double n,
            double weight)
        {
            _perimeterContribs = [];
            int count = circumVerticals?.Count ?? 0;
            // 沈下解析 GetTangentSoilStiffness と同じインデックス規約 (line 253-256):
            //   for j in [i-1, i]: skip if j == -1 || j == pileNodesCount - 1
            //   (= PileCircumVerticals.Count - 1 ではないことに注意。pileNodesCount = count + 1)
            // i.e., 杭頭 (i=0) は j=-1 で skip 残り j=0 のみ
            //       杭先端 (i=last) は j=last-1, last だが j=last=count で skip
            for (int j = nodeIndex - 1; j <= nodeIndex; j++)
            {
                if (j < 0 || j >= count) continue;
                _perimeterContribs.Add(circumVerticals[j]);
            }

            _isToe = isToe;
            _dp = dp;
            _rpu = rpu;
            _alpha = alpha;
            _n = n;
            Weight = weight;
        }

        // 接線剛性 [kN/m]
        public double GetTangentStiffness(double s)
        {
            string state = ResolveState(s);
            double k = 0.0;
            foreach (var pcv in _perimeterContribs)
            {
                double psiLHalf = pcv.PsiL * 0.5; // sett 解析と同じ (line 264)
                k += VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                    state, s, pcv.IsPositiveCircumResistance, pcv.IsNegativeCircumResistance,
                    pcv.Tau1, pcv.Tau2,
                    pcv.S1 / 1000.0, pcv.S2 / 1000.0,
                    psiLHalf);
            }
            if (_isToe && state == "positive")
            {
                k += VerticalLoadTransferMethod.GetTangentStiffnessPileToeFromSettlement(
                    s, _dp, _rpu, _alpha, _n);
            }
            return k;
        }

        // 割線剛性 [kN/m]
        public double GetSecantStiffness(double s)
        {
            string state = ResolveState(s);
            double k = 0.0;
            foreach (var pcv in _perimeterContribs)
            {
                double psiLHalf = pcv.PsiL * 0.5;
                k += VerticalLoadTransferMethod.GetSecantStiffnessPilePerimeter(
                    state, s, pcv.IsPositiveCircumResistance, pcv.IsNegativeCircumResistance,
                    pcv.Tau1, pcv.Tau2,
                    pcv.S1 / 1000.0, pcv.S2 / 1000.0,
                    psiLHalf);
            }
            if (_isToe && state == "positive")
            {
                k += VerticalLoadTransferMethod.GetSecantStiffnessPileToeFromSettlement(
                    s, _dp, _rpu, _alpha, _n);
            }
            return k;
        }

        // sett 解析の "positive" / "negative" / "initial" マッピング
        // s = 0 のときは "initial" 相当だが、剛性 = 0 だと K 行列特異化のため
        // 初期接線 (= "positive" with s=0+) を返すことにする。
        // (沈下解析側も GetTangentSoilStiffness は state="positive"/"negative" で呼ばれ、
        //  "initial" は SetState 等で別途切替えられる)
        private static string ResolveState(double s)
        {
            if (s >= 0) return "positive";
            return "negative";
        }

        /// <summary>
        /// 初期接線剛性 (s=+0 の "positive" 状態) — 行列特異化回避用の最低剛性として使用。
        /// </summary>
        public double InitialTangentStiffness => GetTangentStiffness(1e-9);
    }
}
