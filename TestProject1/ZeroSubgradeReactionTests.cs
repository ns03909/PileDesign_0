using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Services;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 水平地盤反力係数 kh0 が 0 になる土層 (変形係数 Es = 0 など) は、水平解析の前に名指しで止める
    /// (<see cref="CheckInputData.CollectZeroSubgradeReactionProblems"/>)。
    ///
    /// 以前は解析に入ってから「杭地盤節点-1-1:Ux の対角値が 0」で止まり、Es が原因とは読み取れなかった。
    /// 同梱の例題 ExampleCPT2018 は全層 Es = 0 のまま、2026-05-29 の追加から一度も水平解析が通っていなかった。
    /// </summary>
    [TestClass]
    public class ZeroSubgradeReactionTests
    {
        private static PileDesign.Models.InputData.InputModel Build()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("ExampleCAP3_7", "PileExampleCAP3_7");
            if (model == null) Assert.Inconclusive("例題を組めません: " + error);
            return model!;
        }

        /// <summary>(前提) 例題のままでは何も出さない。</summary>
        [TestMethod]
        public void TheExampleAsShippedHasNoZeroSubgradeReaction()
        {
            var model = Build();
            TestSource.AssertScanned(model.PileLayoutItems.Sum(p => p.SoilPileAt(model)?.HorizontalSoilReactions.Count ?? 0), 1, "杭の要素の水平地盤反力");
            Assert.AreEqual(0, CheckInputData.CollectZeroSubgradeReactionProblems(model).Count);
        }

        /// <summary>Es = 0 の土層は、その層を名指しし、Es を入れる直し方を添えて止める。杭体は 1 行にまとめる。</summary>
        [TestMethod]
        public void AZeroEsLayerIsNamedBeforeTheAnalysis()
        {
            var model = Build();
            var layer = model.GroundsInput[0].GroundLayers[0];
            layer.Es = 0;
            model.GenerateSoilPiles();

            var problem = CheckInputData.CollectZeroSubgradeReactionProblems(model).Single();
            Assert.AreEqual(DiagnosticSeverity.Error, problem.Severity);
            Assert.AreEqual(DiagnosticTarget.GroundLayer(1, 1), problem.Target);
            StringAssert.Contains(problem.Message, $"土層「{layer.Name}」の変形係数 Es が 0 kN/m²");
            StringAssert.Contains(problem.Message, "水平解析が解けません");
            StringAssert.Contains(problem.Remedy, "変形係数 Es を入力する");
        }

        /// <summary>
        /// Es = 0 でも、その層の kh0 を手入力していれば正しい入力なので止めない。
        /// 手入力が 0 なら、手入力のほうを直すように言う。
        /// </summary>
        [TestMethod]
        public void AManualKh0MakesAZeroEsLayerSolvable_ButAManualZeroDoesNot()
        {
            var model = Build();
            var layer = model.GroundsInput[0].GroundLayers[0];
            layer.Es = 0;
            model.GenerateSoilPiles();
            var soilPiles = model.ElementDivision.SoilPiles.ToList();
            TestSource.AssertScanned(soilPiles.Count, 1, "土層-杭セット");

            foreach (var sp in soilPiles) sp.SetKh0Override(layer.Name, 5000);
            Assert.AreEqual(0, CheckInputData.CollectZeroSubgradeReactionProblems(model).Count, "kh0 を手入力した層は止めない");

            foreach (var sp in soilPiles) sp.SetKh0Override(layer.Name, 0);
            var problem = CheckInputData.CollectZeroSubgradeReactionProblems(model).Single();
            StringAssert.Contains(problem.Message, "kh0 が手入力で 0");
            StringAssert.Contains(problem.Remedy, "kh0 の手入力を正の値にする");
        }

        /// <summary>
        /// 水平解析の入口で止まり、その知らせに土層と Es が書かれる (以前は解析に入ってから内部の節点名で止まった)。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void TheHorizontalAnalysisStopsWithTheLayerNamed()
        {
            var shown = new List<string>();
            bool unattended = MessageService.IsUnattended;
            var answer = MessageService.UnattendedAnswer;
            MessageService.IsUnattended = true;
            MessageService.UnattendedAnswer = (text, _) => { lock (shown) shown.Add(text); return null; };
            try
            {
                var hcvm = HeadlessHorizontalRunner.RunExampleForHcvm("ExampleCAP3_7", "PileExampleCAP3_7",
                    new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 2,
                        Level2Steps = 2,
                        Customize = im =>
                        {
                            im.GroundsInput[0].GroundLayers[0].Es = 0;
                            im.GenerateSoilPiles();
                        },
                    });
                Assert.AreEqual(0, hcvm.CurrentModel?.AnalysisStepResults.Count ?? 0, "解析に入っています");
            }
            finally
            {
                MessageService.IsUnattended = unattended;
                MessageService.UnattendedAnswer = answer;
            }
            Assert.IsTrue(shown.Any(t => t.Contains("変形係数 Es が 0 kN/m²")),
                "Es の知らせが出ていません:\n" + string.Join("\n---\n", shown));
            Assert.IsFalse(shown.Any(t => t.Contains("杭地盤節点")), "内部の節点名で止まっています");
        }
    }
}
