using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下の画面の解析と結果の管理 (<see cref="SingleSettlementSession"/>) を、画面なしで確かめる。
    /// 画面越しの確認は <see cref="ReviewFindings20261010Tests"/> にある。ここは STA スレッドもダイアログも要らない。
    /// </summary>
    [TestClass]
    public class SingleSettlementSessionTests
    {
        private static (InputModel Model, List<SoilPile> Copies) Setup()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (model == null || model.ElementDivision.SoilPiles.Count < 2)
            {
                Assert.Inconclusive("土層-杭セットが 2 つ以上ある例題を組めません");
                return default;
            }
            // 画面の写しと同じく、土層-杭セットは写しを編集する
            return (model, model.ElementDivision.SoilPiles.Select(s => s.DeepCopy()).ToList());
        }

        private static string Settlements(InputModel model) => string.Join("|", model.PileLayoutItems.Select(p =>
            $"{p.SinglePileSettlementVL:R};{p.SinglePileSettlementVLBeyondCurve};"
            + string.Join(",", p.SinglePileSettlementLevel1s ?? []) + ";" + string.Join(",", p.SinglePileSettlementLevel2s ?? [])));

        /// <summary>結果は写しのセットにだけ書く。元の入力の土層-杭セットは OK まで変わらない。</summary>
        [TestMethod]
        public void Results_GoToTheCopies_NotToTheOriginalSets()
        {
            var (model, copies) = Setup();
            var originals = model.ElementDivision.SoilPiles.ToList();
            var origCurves = originals.Select(s => s.LoadDisplacements).ToList();
            var session = new SingleSettlementSession(model, () => copies);

            var warnings = new List<string>();
            for (int i = 0; i < copies.Count; i++) session.AnalyzeAt(i, warnings);

            for (int i = 0; i < originals.Count; i++)
            {
                Assert.AreSame(origCurves[i], originals[i].LoadDisplacements, $"元のセット {i + 1} へ書いています");
                Assert.IsNotNull(copies[i].LoadDisplacements, $"写しのセット {i + 1} に結果がありません");
            }

            var committed = session.CommitCopies();
            Assert.IsTrue(session.IsClosed);
            Assert.AreEqual(copies.Count, committed.Count);
            for (int i = 0; i < copies.Count; i++)
                Assert.AreNotSame(copies[i], committed[i], "反映する一覧が写しそのもの (画面と共有) になっています");
        }

        /// <summary>同じセットで後から始めた解析があれば、先に始めた方の結果は完了の順にかかわらず捨てる。</summary>
        [TestMethod]
        public async Task StaleResults_AreDropped()
        {
            var (model, copies) = Setup();
            var session = new SingleSettlementSession(model, () => copies);
            var set = copies[0];
            var r1 = session.Solve(set);
            var r2 = session.Solve(set);

            var first = new TaskCompletionSource<VerticalLoadTransferMethod>();
            var second = new TaskCompletionSource<VerticalLoadTransferMethod>();
            var t1 = session.RunAsync(set, (_, _) => first.Task);
            var t2 = session.RunAsync(set, (_, _) => second.Task);
            second.SetResult(r2);
            first.SetResult(r1);

            Assert.IsNull(await t1, "古い解析の結果を反映しました");
            Assert.IsNotNull(await t2);
            Assert.AreSame(r2.LoadDisplacements, set.LoadDisplacements);

            // 別のセットの解析は、互いの世代に影響しない
            var other = copies[1];
            var r3 = session.Solve(other);
            Assert.IsNotNull(await session.RunAsync(other, (_, _) => Task.FromResult(r3)));
            Assert.AreSame(r3.LoadDisplacements, other.LoadDisplacements);
        }

        /// <summary>閉じたあとに終わった解析は反映しない。</summary>
        [TestMethod]
        public async Task ResultsAfterClosing_AreNotApplied()
        {
            var (model, copies) = Setup();
            var session = new SingleSettlementSession(model, () => copies);
            var set = copies[0];
            var before = set.LoadDisplacements;
            string settlementsBefore = Settlements(model);

            var late = new TaskCompletionSource<VerticalLoadTransferMethod>();
            var run = session.RunAsync(set, (_, _) => late.Task);
            session.Cancel();
            late.SetResult(session.Solve(set));

            Assert.IsNull(await run);
            Assert.AreSame(before, set.LoadDisplacements, "閉じたあとに写しへ書きました");
            Assert.AreEqual(settlementsBefore, Settlements(model), "閉じたあとに杭の単杭沈下量へ書きました");
        }

        /// <summary>キャンセルは、各杭の単杭沈下量と杭体の入力を開いた時点へ戻す。杭体の実体は差し替えない。</summary>
        [TestMethod]
        public void Cancel_RestoresSettlementsAndBodies()
        {
            var (model, copies) = Setup();
            string settlementsBefore = Settlements(model);
            var bodies = model.PileBodies.ToList();
            var alphas = bodies.Select(b => b.SettleAlpha).ToList();
            var session = new SingleSettlementSession(model, () => copies);

            foreach (var b in bodies) b.SettleAlpha += 0.3;
            var warnings = new List<string>();
            for (int i = 0; i < copies.Count; i++) session.AnalyzeAt(i, warnings);
            Assert.AreNotEqual(settlementsBefore, Settlements(model), "前提: 解析で単杭沈下量が変わった");

            session.Cancel();

            Assert.IsTrue(session.IsClosed);
            Assert.AreEqual(settlementsBefore, Settlements(model), "キャンセルしても単杭沈下量が残っています");
            CollectionAssert.AreEqual(bodies, model.PileBodies.ToList(), "杭体の実体が差し替わっています");
            for (int i = 0; i < bodies.Count; i++)
                Assert.AreEqual(alphas[i], bodies[i].SettleAlpha, 1e-12, $"杭体 {i + 1} の α が戻っていません");
        }
    }
}
