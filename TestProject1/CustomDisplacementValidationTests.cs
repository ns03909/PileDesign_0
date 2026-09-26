using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 任意入力の地盤変位を、解析前にケースごとに検査すること (2026-09-27 のレビュー)。
    /// 2. 解くケースの曲線が空なら止める (以前は補間が 0 mm を返し、地盤変位を無視して解けた)
    /// 1. 点の並び (上から下へ標高が下がる)・同じ標高・数値でない値を検査する (画面は最高・最低標高しか見ていなかった)
    /// </summary>
    [TestClass]
    public class CustomDisplacementValidationTests
    {
        private static ObservableCollection<DisplacementPoint> Profile(params (double Z, double D)[] points)
            => new(points.Select(p => new DisplacementPoint(p.Z, p.D)));

        [TestMethod]
        public void AReversedPointMakesTheInterpolationIgnoreIt()
        {
            // 並びが崩れていると、その点を飛ばした区間で補間する (検査が要る理由)。
            // 上から下に並べると -7 m は -5 m (80) と -10 m (50) の間で 68 mm。逆順のままだと 0〜-10 m の区間で 65 mm
            var calc = new CustomDisplacementProfile();
            double reversed = calc.Interpolate(Profile((0, 100), (-10, 50), (-5, 80), (-20, 0)), -7.0);
            double ordered = calc.Interpolate(Profile((0, 100), (-5, 80), (-10, 50), (-20, 0)), -7.0);
            Assert.AreEqual(68.0, ordered, 1e-9);
            Assert.AreNotEqual(ordered, reversed, "(前提) 逆順でも同じ値になるなら、この検査の前提を見直すこと");
        }

        [TestMethod]
        public void OrderDuplicatesAndNonFiniteValuesAreNamed()
        {
            Assert.AreEqual(0, CustomDisplacementProfile.DescribeProblems(Profile((0, 100), (-5, 80), (-10, 50))).Count);

            var reversed = CustomDisplacementProfile.DescribeProblems(Profile((0, 100), (-10, 50), (-5, 80)));
            StringAssert.Contains(reversed.Single(), "3 点目");
            StringAssert.Contains(reversed.Single(), "上から下の順に並んでいません");

            var duplicate = CustomDisplacementProfile.DescribeProblems(Profile((0, 100), (-5, 80), (-5, 70)));
            StringAssert.Contains(duplicate.Single(), "同じ標高");

            var nan = CustomDisplacementProfile.DescribeProblems(Profile((0, 100), (double.NaN, 80), (-10, double.PositiveInfinity)));
            Assert.AreEqual(2, nan.Count(p => p.Contains("数値ではありません")), string.Join(" / ", nan));
        }

        private static InputModel ModelWith(CustomDisplacementProfile custom, int pileGround = 1)
        {
            var ground = new GroundInput { CustomDisplacementProfile = custom };
            return new InputModel
            {
                GroundsInput = [ground, new GroundInput()],
                PileLayoutItems = [new PileLayoutDataItem { No = 1, GroundNo = pileGround }],
            };
        }

        [TestMethod]
        public void AnEmptyProfileOfACaseToBeSolvedStopsTheAnalysis()
        {
            var custom = new CustomDisplacementProfile
            {
                IsEnabled = true,
                Level1NonLiq = Profile((0, 10), (-10, 0)),
                Level2NonLiq = [],   // レベル2 の非液状化だけ空
            };

            var problems = CheckInputData.DescribeCustomDisplacementProblems(ModelWith(custom), [(1, false), (2, false)]);
            Assert.AreEqual(1, problems.Count, string.Join(" / ", problems));
            StringAssert.Contains(problems[0], "地盤 1");
            StringAssert.Contains(problems[0], "L2 非液状化");
            StringAssert.Contains(problems[0], "点が入力されていません");

            Assert.AreEqual(0, CheckInputData.DescribeCustomDisplacementProblems(ModelWith(custom), [(1, false)]).Count,
                "解かないケース (レベル2) の空を理由に止めています");
        }

        [TestMethod]
        public void OnlyEnabledGroundsThatAreUsedAreChecked()
        {
            var disabled = new CustomDisplacementProfile { IsEnabled = false };
            Assert.AreEqual(0, CheckInputData.DescribeCustomDisplacementProblems(ModelWith(disabled), [(1, false)]).Count,
                "任意入力が無効な地盤を検査しています");

            var enabled = new CustomDisplacementProfile { IsEnabled = true };
            Assert.AreEqual(0, CheckInputData.DescribeCustomDisplacementProblems(ModelWith(enabled, pileGround: 2), [(1, false)]).Count,
                "どの杭も使っていない地盤を検査しています");
        }

        [TestMethod]
        public void ABrokenOrderIsReportedPerCase()
        {
            var custom = new CustomDisplacementProfile
            {
                IsEnabled = true,
                Level1Liq = Profile((0, 10), (-10, 0), (-5, 5)),
            };
            var problems = CheckInputData.DescribeCustomDisplacementProblems(ModelWith(custom), [(1, true)]);
            StringAssert.Contains(problems.Single(), "[L1 液状化]");
            StringAssert.Contains(problems.Single(), "上から下の順に並んでいません");
        }

        [TestMethod]
        public void TheHorizontalAnalysisChecksBeforeRunning()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "private async Task OnExecuteAnalysisCore(bool additive)");
            int check = body.IndexOf("DescribeCustomDisplacementProblems(", System.StringComparison.Ordinal);
            int running = body.IndexOf("IsAnalysisRunning = true", System.StringComparison.Ordinal);
            Assert.IsTrue(check >= 0, "水平解析の前に任意入力の地盤変位を検査していません");
            Assert.IsTrue(check < running, "解析を始めてから検査しています");
        }
    }
}
