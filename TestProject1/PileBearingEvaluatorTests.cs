using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭の鉛直支持力の検定（押込み・引抜き）。
    ///
    /// 支持力は「限界値をいくつ取るか」がそのまま結論を変えるので、
    /// レベルと限界状態の対応をここで固定する。
    ///
    /// <code>
    ///   長期 (VL)   使用限界   R_SLS = Ru/3      Rt_SLS
    ///   レベル1     損傷限界   R_DLS = Ru/1.5    Rt_DLS
    ///   レベル2     終局限界   R_ULS = Ru        Rt_ULS   ※グレード S は損傷限界
    /// </code>
    /// </summary>
    [TestClass]
    public class PileBearingEvaluatorTests
    {
        private const double Ru = 3000.0;

        /// <summary>
        /// 押込み Ru = 3000 (→ 1000 / 2000 / 3000)、
        /// 引抜きは実装と同じく<b>負値</b>で持たせる。
        /// </summary>
        private static SoilPile Ground() => new()
        {
            Rfu = Ru,            // Rpu = 0 なので Ru = Rfu
            Rt_SLS = -600.0,
            Rt_DLS = -1200.0,
            Rt_ULS = -1800.0,
        };

        private static PileLayoutDataItem Pile(double axialForceVL) => new()
        {
            PileNo = 7,
            AxialForceVL0 = axialForceVL,
        };

        /// <summary>地震時軸力を入力していない杭。<c>GetDesignAxialForce</c> は長期軸力へ落ちる。</summary>
        private static readonly IReadOnlyList<(string Name, int No)> Level1 = [("L1-1", 1)];
        private static readonly IReadOnlyList<(string Name, int No)> Level2 = [("L2-1", 1)];

        private static List<EvaluationItem> Evaluate(double axialForceVL, bool gradeS = false,
            SoilPile? ground = null)
        {
            var items = new List<EvaluationItem>();
            PileBearingEvaluator.AddPileItems(items, Pile(axialForceVL), ground ?? Ground(),
                Level1, Level2, level2UsesDamageLimit: gradeS);
            return items;
        }

        // ── 限界状態の割り当て ─────────────────────────────

        [TestMethod]
        public void CompressionLimits_FollowTheLimitState()
        {
            var items = Evaluate(axialForceVL: 900.0);

            Assert.AreEqual(3, items.Count, "長期・レベル1・レベル2 の 3 件");
            CollectionAssert.AreEqual(
                new[] { Ru / 3.0, Ru / 1.5, Ru },
                items.Select(i => i.Limit).ToArray());
            CollectionAssert.AreEqual(
                new[] { "使用限界", "損傷限界", "終局限界" },
                items.Select(i => i.LimitName).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, items.Select(i => i.Level).ToArray());
        }

        /// <summary>耐震グレード S ではレベル2 も損傷限界で押さえる。</summary>
        [TestMethod]
        public void GradeS_UsesTheDamageLimitAtLevel2()
        {
            var level2 = Evaluate(axialForceVL: 900.0, gradeS: true).Single(i => i.Level == 2);

            Assert.AreEqual("損傷限界", level2.LimitName);
            Assert.AreEqual(Ru / 1.5, level2.Limit, 1e-9, "終局限界 (Ru) を使ってしまっている");
        }

        [TestMethod]
        public void UpliftLimits_FollowTheLimitState()
        {
            var items = Evaluate(axialForceVL: -300.0);

            CollectionAssert.AreEqual(
                new[] { 600.0, 1200.0, 1800.0 },
                items.Select(i => i.Limit).ToArray(),
                "引抜き抵抗は負値で保持されているが、限界値は大きさで出す");
            Assert.AreEqual(1200.0, Evaluate(-300.0, gradeS: true).Single(i => i.Level == 2).Limit, 1e-9);
        }

        // ── 押込みと引抜きの選び分け ───────────────────────

        /// <summary>
        /// 軸力の向きで<b>どちらか一方</b>だけを出す。
        /// 両方出すと、圧縮の杭に「引抜きは OK」という無意味な行が並ぶ。
        /// </summary>
        [TestMethod]
        public void OnlyOneDirection_IsEmittedPerCase()
        {
            Assert.IsTrue(Evaluate(900.0).All(i => i.Kind == EvaluationKind.PileBearingCompression));
            Assert.IsTrue(Evaluate(-300.0).All(i => i.Kind == EvaluationKind.PileUpliftResistance));
        }

        /// <summary>引抜きは応答も限界も大きさで比べ、軸力そのものは符号付きで残す。</summary>
        [TestMethod]
        public void Uplift_ComparesMagnitudesButKeepsTheSignedAxialForce()
        {
            var longTerm = Evaluate(axialForceVL: -300.0).Single(i => i.Level == 0);

            Assert.AreEqual(300.0, longTerm.Response, 1e-9);
            Assert.AreEqual(600.0, longTerm.Limit, 1e-9);
            Assert.AreEqual(0.5, longTerm.Ratio, 1e-9);
            Assert.IsTrue(longTerm.IsOk);
            Assert.AreEqual(-300.0, longTerm.AxialForce!.Value, 1e-9, "軸力は符号付きで残す (引抜きが負)");
        }

        /// <summary>
        /// 引抜き抵抗を求めていない杭 (限界値 0) は検定できない。
        /// 0 で割って比が ∞ になったり、常に NG と出したりしてはいけない。
        /// </summary>
        [TestMethod]
        public void ZeroLimit_IsSkippedInsteadOfReportedAsNg()
        {
            var noUplift = new SoilPile { Rfu = Ru };   // Rt_* は 0 のまま

            var items = Evaluate(axialForceVL: -300.0, ground: noUplift);

            Assert.AreEqual(0, items.Count, "引抜き抵抗が 0 の杭を検定してしまっている");
        }

        // ── 判定 ───────────────────────────────────────────

        [TestMethod]
        public void Verdict_IsNgOnlyWhenTheResponseExceedsTheLimit()
        {
            Assert.IsTrue(Evaluate(Ru / 3.0).Single(i => i.Level == 0).IsOk,
                "ちょうど限界と等しいときは OK");
            Assert.IsFalse(Evaluate(Ru / 3.0 + 1.0).Single(i => i.Level == 0).IsOk);
        }

        [TestMethod]
        public void Ratio_IsResponseOverLimit()
        {
            var item = Evaluate(axialForceVL: 1200.0).Single(i => i.Level == 0);

            Assert.AreEqual(1200.0 / (Ru / 3.0), item.Ratio, 1e-12);
            Assert.IsFalse(item.IsOk);
        }

        // ── 表示 ───────────────────────────────────────────

        /// <summary>
        /// 支持力は kN で千の位まで出るので、モーメントと同じ小数 1 桁。
        /// 回転角と同じ 3 桁にすると桁が読めない。
        /// </summary>
        [TestMethod]
        public void Values_AreShownToOneDecimal()
        {
            var item = Evaluate(axialForceVL: 912.345).Single(i => i.Level == 0);

            Assert.AreEqual("912.3", item.ResponseText);
            Assert.AreEqual("1,000.0", item.LimitText);
            Assert.AreEqual("kN", item.Unit);
        }

        /// <summary>
        /// 支持力に液状化の別は無い。<c>false</c> にすると
        /// 「液状化なしの検討をした」と読めるので、概念が無いことを null で表す。
        /// </summary>
        [TestMethod]
        public void Liquefaction_IsNotApplicable()
        {
            var item = Evaluate(axialForceVL: 900.0).Single(i => i.Level == 0);

            Assert.IsNull(item.IsLiquefaction);
            Assert.AreEqual("", item.LiquefactionLabel);
            Assert.AreEqual("長期 (VL)", item.ConditionDescription, "組合せ・液状化の空欄が出ている");
        }

        [TestMethod]
        public void TargetDescription_IdentifiesThePile()
        {
            Assert.AreEqual("杭No.7", Evaluate(axialForceVL: 900.0).First().TargetDescription);
        }

        // ── 入力が無いとき ─────────────────────────────────

        [TestMethod]
        public void NoInputModel_ReturnsNothingInsteadOfThrowing()
        {
            Assert.AreEqual(0, PileBearingEvaluator.Evaluate(null, "A").Count);
            Assert.AreEqual(0, PileBearingEvaluator.Evaluate(new InputModel(), "A").Count);
        }

        // ── 実際の例題 ───────────────────────────

        /// <summary>
        /// 例題を読んだだけで検定が出ること (水平解析は要らない)。
        ///
        /// 地盤は<b>杭体 No</b> で引くこと。<c>PileLayoutDataItem.SoilPile</c> は
        /// (地盤No, 杭体No, 杭頭Z) を鍵にするキャッシュなので Z が合わず null になり、
        /// それを使うと<b>黙って検定が 0 件になる</b>。
        /// </summary>
        [TestMethod]
        public void RealExample_ProducesBearingItemsWithoutAnyAnalysis()
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(inputModel, error);

            Assert.IsNull(inputModel.PileLayoutItems[0].SoilPile,
                "前提が崩れている: この例題では pile.SoilPile は引けないはず");

            var items = PileBearingEvaluator.Evaluate(inputModel, "A");

            int piles = inputModel.PileLayoutItems.Count;
            Assert.AreEqual(piles * 3, items.Count,
                $"杭 {piles} 本 × (長期 + レベル1 + レベル2) になっていない");
            Assert.IsTrue(items.All(i => i.Limit > 0 && double.IsFinite(i.Ratio)),
                "限界値が 0 の項目が混ざっている");
            Assert.IsTrue(items.All(i => !string.IsNullOrWhiteSpace(i.LoadCaseName)),
                "荷重ケース名が空の行がある (表で行を区別できない)");

            // この例題は全杭が圧縮。長期が最も厳しい (限界値が Ru/3 で最小)
            Assert.IsTrue(items.All(i => i.Kind == EvaluationKind.PileBearingCompression));
            Assert.AreEqual(0, items.OrderByDescending(i => i.Ratio).First().Level,
                "長期 (使用限界) が支配になっていない");
        }
    }
}
