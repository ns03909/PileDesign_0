using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using System.Linq;
using System.Text;

namespace TestProject1
{
    /// <summary>
    /// 検定結果の型と、そこからのテキスト組立。
    ///
    /// golden テスト (EvaluationTextGoldenTests) は公表された計算例を使うため
    /// <b>NG が 1 件も出ない</b>。NG 側の書式と傾斜角の書式はここで固定する。
    /// 期待文字列は作り替え前の実装 (commit 99fb815 時点の
    /// EvaluationWindowViewModel) から書き写したもの。
    /// </summary>
    [TestClass]
    public class EvaluationResultTests
    {
        private static EvaluationItem Moment(double response, double limit, string end = "i端") => new()
        {
            Kind = EvaluationKind.PileSectionMoment,
            Level = 2,
            Category = "杭体曲げ (安全限界)",
            LimitName = "安全限界",
            TargetName = "beam",
            EndLabel = end,
            PileBodyNo = 7,
            PileNo = 12,
            SegmentIndex = 3,
            LoadCaseName = "L2-1",
            LoadCombinationName = "cmb1",
            IsLiquefaction = true,
            Response = response,
            Limit = limit,
            Unit = "kN·m",
            AxialForce = 2452.0,
            IsOk = !(response > limit),
        };

        // ── 比と判定 ───────────────────────────────────────

        [TestMethod]
        public void Ratio_IsResponseOverLimit()
        {
            Assert.AreEqual(1.15, Moment(1150, 1000).Ratio, 1e-12);
            Assert.AreEqual(0.5, Moment(500, 1000).Ratio, 1e-12);
        }

        /// <summary>
        /// 判定は比から導かず、算出元と同じ比較を保つ。
        /// 曲げ・回転角は「超えたら NG」なので、ちょうど等しいときは OK。
        /// </summary>
        [TestMethod]
        public void MomentAtExactLimit_IsOk()
        {
            var item = Moment(1000, 1000);
            Assert.AreEqual(1.0, item.Ratio, 1e-12);
            Assert.IsTrue(item.IsOk, "曲げは「超えたら NG」なので、ちょうど等しいときは OK");
        }

        /// <summary>
        /// 傾斜角だけは「限界未満なら OK」なので、ちょうど等しいと NG。
        /// 比から導いていたらここが変わってしまう。
        /// </summary>
        [TestMethod]
        public void InclinationAtExactLimit_IsNg()
        {
            const double limit = 1.0 / 300.0;
            double inclination = limit;   // 限界とちょうど同じ
            var item = new EvaluationItem
            {
                Kind = EvaluationKind.FoundationBeamInclination,
                Response = inclination,
                Limit = limit,
                IsOk = inclination < limit,   // 実装と同じ比較
            };

            Assert.AreEqual(1.0, item.Ratio, 1e-12);
            Assert.IsFalse(item.IsOk, "傾斜角は「限界未満なら OK」なので、ちょうど等しいときは NG");
        }

        // ── 集合 ───────────────────────────────────────────

        [TestMethod]
        public void Governing_IsTheItemWithTheLargestRatio()
        {
            var result = new EvaluationResult(
            [
                Moment(500, 1000),    // 0.50
                Moment(1150, 1000),   // 1.15  ← 支配
                Moment(980, 1000),    // 0.98
            ]);

            Assert.AreEqual(1.15, result.MaxRatio!.Value, 1e-12);
            Assert.AreEqual(1150, result.Governing!.Response, 1e-12);
            Assert.AreEqual(1, result.NgCount);
            Assert.AreEqual(2, result.OkCount);
        }

        [TestMethod]
        public void EmptyResult_HasNoGoverningAndDoesNotThrow()
        {
            var result = new EvaluationResult([]);

            Assert.IsTrue(result.IsEmpty);
            Assert.IsNull(result.Governing);
            Assert.IsNull(result.MaxRatio);
            Assert.AreEqual(0, result.NgCount);
            Assert.AreEqual(0, result.OkCount);
        }

        [TestMethod]
        public void ByRatioDescending_PutsTheSevererFirst()
        {
            var result = new EvaluationResult([Moment(500, 1000), Moment(1150, 1000), Moment(980, 1000)]);

            var ratios = result.ByRatioDescending
                .Select(i => System.Math.Round(i.Ratio, 10))
                .ToArray();

            CollectionAssert.AreEqual(new[] { 1.15, 0.98, 0.50 }, ratios);
        }

        [TestMethod]
        public void PassesFilter_MatchesTheDisplayFilterMeaning()
        {
            var ng = Moment(1150, 1000);
            var ok = Moment(500, 1000);

            Assert.IsTrue(EvaluationResult.PassesFilter(ng, 0), "0 = NG のみ");
            Assert.IsFalse(EvaluationResult.PassesFilter(ok, 0));

            Assert.IsFalse(EvaluationResult.PassesFilter(ng, 1), "1 = OK のみ");
            Assert.IsTrue(EvaluationResult.PassesFilter(ok, 1));

            Assert.IsTrue(EvaluationResult.PassesFilter(ng, 2), "2 = 両方");
            Assert.IsTrue(EvaluationResult.PassesFilter(ok, 2));
        }

        // ── テキスト書式 (NG 側は golden が届かないのでここで固定) ──

        [TestMethod]
        public void MomentNg_TextMatchesTheOriginalFormat()
        {
            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, Moment(3000.05, 2685.14));

            Assert.AreEqual(
                "  [NG] 安全限界超過（i端）: beam  杭No.12 / 杭体No.7 / 要素3\r\n" +
                "       荷重ケース: L2-1 / 組合せ: cmb1 / 液状化有\r\n" +
                "       M=3000.1 kNm > 安全限界M=2685.1 kNm (N=2452.0 kN)\r\n" +
                "\r\n",
                sb.ToString());
        }

        [TestMethod]
        public void MomentOk_TextMatchesTheOriginalFormat()
        {
            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, Moment(376.94, 2685.14, "j端"));

            Assert.AreEqual(
                "  [OK] 安全限界（j端）: beam  杭No.12 / 杭体No.7 / 要素3\r\n" +
                "       荷重ケース: L2-1 / 組合せ: cmb1 / 液状化有\r\n" +
                "       M=376.9 kNm ≤ 安全限界M=2685.1 kNm (N=2452.0 kN)\r\n" +
                "\r\n",
                sb.ToString());
        }

        [TestMethod]
        public void RotationNg_TextMatchesTheOriginalFormat()
        {
            var item = new EvaluationItem
            {
                Kind = EvaluationKind.PileHeadRotation,
                TargetName = "RS-1",
                PileBodyNo = 4,
                LoadCaseName = "L2-1",
                LoadCombinationName = "cmb1",
                IsLiquefaction = false,
                Response = 0.012345,
                Limit = 0.01,
                Unit = "rad",
                IsOk = false,
            };

            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, item);

            Assert.AreEqual(
                "  [NG] θ超過（場所打ちRC杭）: RS-1  杭体No.4\r\n" +
                "       荷重ケース: L2-1 / 組合せ: cmb1 / 液状化無\r\n" +
                "       θ=0.01235 rad > 0.01 rad\r\n" +
                "\r\n",
                sb.ToString());
        }

        [TestMethod]
        public void Inclination_TextMatchesTheOriginalFormat()
        {
            var item = new EvaluationItem
            {
                Kind = EvaluationKind.FoundationBeamInclination,
                FoundationBeamNo = 12,
                Response = 0.0025,
                Limit = 1.0 / 300.0,
                BeamLength = 7.2,
                IsOk = 0.0025 < 1.0 / 300.0,
            };

            var sb = new StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, item);

            Assert.AreEqual("  OK 梁 #12: 傾斜角 = 2.500E-003 rad (1/400), L=7.20m\r\n", sb.ToString());
        }

        /// <summary>
        /// 画面の一覧に出す説明文。対象と荷重条件が読み取れること。
        /// </summary>
        [TestMethod]
        public void Descriptions_IdentifyTheTargetAndCondition()
        {
            var item = Moment(1150, 1000);
            Assert.AreEqual("杭No.12 / 杭体No.7 / 要素3 / i端", item.TargetDescription);
            Assert.AreEqual("L2-1 / cmb1 / 液状化有", item.ConditionDescription);

            var beam = new EvaluationItem
            {
                Kind = EvaluationKind.FoundationBeamInclination,
                FoundationBeamNo = 12,
                TargetName = "FoundationBeam-12",
            };
            Assert.AreEqual("基礎梁 #12", beam.TargetDescription);
        }

        /// <summary>
        /// 杭体は複数の杭で共有されるので、杭体番号だけでは行を特定できない。
        /// 杭が分かるときは杭No. を併記し、分からないときは杭体No. だけを名乗る
        /// (「杭配置No.」と名乗って杭体番号を出すと、別の杭の行が同じ表記になる)。
        /// </summary>
        [TestMethod]
        public void TargetDescription_DistinguishesPilesSharingAPileBody()
        {
            var withPile = Moment(1150, 1000);
            Assert.AreEqual("杭No.12 / 杭体No.7 / 要素3 / i端", withPile.TargetDescription);

            var withoutPile = withPile with { PileNo = null };
            Assert.AreEqual("杭体No.7 / 要素3 / i端", withoutPile.TargetDescription,
                "杭が特定できないときに杭No. を名乗ってはいけない");
        }
    }
}
