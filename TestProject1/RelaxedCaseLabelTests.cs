using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.Results;
using System.Text;

namespace TestProject1
{
    /// <summary>
    /// 緩めた基準で受理したケースが、<b>どの出口でも</b>そのことを名乗ること。
    ///
    /// <para>残差が許容値 (1e-6) まで下がらず、停滞・振動のため緩めた基準 (最大 1e-2) で受理した
    /// ステップは <see cref="StepStatus.ConvergedRelaxed"/> で記録する。検定は未収束としては
    /// 扱わないが、ヘルプは「判定に <c>OK(緩和受理)</c> のように明記します」と書いている。</para>
    ///
    /// <para>2026-09-19 まで明記していたのはテキスト出力だけで、
    /// <see cref="EvaluationItem.StatusLabel"/> を読む画面の一覧・計算書の検定表・
    /// 結果ダッシュボードには素の「OK」「NG」が出ていた。同じ項目が出口によって違う顔をする。</para>
    /// </summary>
    [TestClass]
    public class RelaxedCaseLabelTests
    {
        private static EvaluationItem Item(StepStatus status, double response, double limit) => new()
        {
            Kind = EvaluationKind.PileSectionMoment,
            Level = 2,
            Category = "杭体曲げ (安全限界)",
            LimitName = "安全限界",
            TargetName = "beam",
            EndLabel = "i端",
            PileBodyNo = 1,
            PileNo = 1,
            SegmentIndex = 1,
            LoadCaseName = "L2-1",
            LoadCombinationName = "cmb1",
            Response = response,
            Limit = limit,
            Unit = "kN·m",
            AxialForce = 1000.0,
            IsOk = !(response > limit),
            CaseConvergence = status,
        };

        /// <summary>緩和受理で限界値の内側なら「OK(緩和受理)」。</summary>
        [TestMethod]
        public void RelaxedOkSaysSoInTheLabel()
        {
            Assert.AreEqual("OK(緩和受理)", Item(StepStatus.ConvergedRelaxed, 100, 200).StatusLabel);
        }

        /// <summary>緩和受理で限界値を超えていれば「NG(緩和受理)」。</summary>
        [TestMethod]
        public void RelaxedNgSaysSoInTheLabel()
        {
            Assert.AreEqual("NG(緩和受理)", Item(StepStatus.ConvergedRelaxed, 300, 200).StatusLabel);
        }

        /// <summary>普通に収束したケースは素の「OK」「NG」のまま (余計な但し書きを付けない)。</summary>
        [TestMethod]
        public void ConvergedKeepsThePlainLabel()
        {
            Assert.AreEqual("OK", Item(StepStatus.Converged, 100, 200).StatusLabel);
            Assert.AreEqual("NG", Item(StepStatus.Converged, 300, 200).StatusLabel);
        }

        /// <summary>未収束は従来どおり「未収束」。緩和受理の但し書きに乗っ取られない。</summary>
        [TestMethod]
        public void UnconvergedStillWins()
        {
            Assert.AreEqual("未収束", Item(StepStatus.Unconverged, 100, 200).StatusLabel);
            Assert.AreEqual("未収束", Item(StepStatus.PhysicallyUnconverged, 300, 200).StatusLabel);
        }

        /// <summary>
        /// 画面・計算書が読む <see cref="EvaluationItem.StatusLabel"/> と、テキスト出力が
        /// 同じことを言うこと。出口によって違う顔をしていたのが元の不具合。
        /// </summary>
        [TestMethod]
        public void TheLabelAndTheTextAgree()
        {
            foreach (var (status, response) in new[]
            {
                (StepStatus.ConvergedRelaxed, 100.0),
                (StepStatus.ConvergedRelaxed, 300.0),
                (StepStatus.Converged, 100.0),
                (StepStatus.Unconverged, 100.0),
            })
            {
                var item = Item(status, response, 200);
                var sb = new StringBuilder();
                EvaluationTextFormatter.AppendItem(sb, item);
                string text = sb.ToString();
                StringAssert.Contains(text, item.StatusLabel,
                    $"テキスト出力に判定「{item.StatusLabel}」が出ていません: {text}");
            }
        }
    }
}
