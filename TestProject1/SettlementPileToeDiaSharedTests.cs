using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下ウィンドウで沈下検討用杭先端径 (dp) を変えると、<b>同じ杭体の土層-杭セットすべて</b>の
    /// Dp が変わること。
    ///
    /// <para>dp は杭体の値 (<c>PileBodyInput.SettlePileToeDia</c>) で、セットの <c>Dp</c> はその写し
    /// (解析が読むのは <c>Dp</c>)。以前は表示中のセットの <c>Dp</c> だけを書き換えていたので、
    /// 同じ杭体でも地盤や杭頭の高さが違う別のセットは古い径のまま、曲線も古いまま OK で確定していた。</para>
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class SettlementPileToeDiaSharedTests
    {
        [TestMethod]
        public void ChangingDp_UpdatesEverySetOfTheSamePileBody()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            // 同じ杭体のセットを 2 つ以上にする。杭頭の高さが違えば別のセットになる
            // (セットの鍵は 地盤番号・杭体番号・杭頭 Z)。
            var piles = model.PileLayoutItems;
            Assert.IsTrue(piles.Count >= 2, "杭が 2 本以上ある計算例が要ります");
            var target = piles.Last();
            int bodyNo = target.PileBodyNo;
            Assert.IsTrue(piles.Count(p => p.PileBodyNo == bodyNo) >= 2, "同じ杭体の杭が 2 本以上要ります");
            target.Z -= 0.5;
            model.GenerateSoilPiles();

            var sameBody = model.ElementDivision.SoilPiles.Where(sp => sp.PileBodyNo == bodyNo).ToList();
            Assert.IsTrue(sameBody.Count >= 2, $"同じ杭体のセットが {sameBody.Count} 個しかできていません");
            if (sameBody[0].PileToeNValue == 0) { Assert.Inconclusive("杭先端平均N値が 0 (警告ダイアログが出る)"); return; }

            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var captured = XamlSmokeTestSupport.RunOnStaThread(() => Check(model, bodyNo), out bool timedOut);
                if (timedOut) { Assert.Inconclusive("STA スレッドで時間内に終わりませんでした"); return; }
                if (captured is AssertFailedException || captured is AssertInconclusiveException) throw captured;
                if (captured != null) Assert.Fail(captured.ToString());
            }
            finally { MessageService.IsUnattended = unattended; }
        }

        private static void Check(InputModel model, int bodyNo)
        {
            var mainVm = new MainWindowViewModel { CurrentInputModel = model };
            var vm = new SettlementViewModel(mainVm);

            int index = vm.SoilPiles.ToList().FindIndex(sp => sp.PileBodyNo == bodyNo);
            vm.SelectSoilPileIndex(index);
            Assert.AreEqual(bodyNo, vm.SoilPile.PileBodyNo);

            var others = vm.SoilPiles.Where(sp => sp.PileBodyNo == bodyNo && !ReferenceEquals(sp, vm.SoilPile)).ToList();
            Assert.IsTrue(others.Count >= 1, "同じ杭体の別のセットがありません");
            var otherBodies = vm.SoilPiles.Where(sp => sp.PileBodyNo != bodyNo).ToDictionary(sp => sp, sp => sp.Dp);
            var curvesBefore = others.ToDictionary(sp => sp, sp => sp.LoadDisplacements);

            double before = vm.SettlePileToeDiaM;
            double dpBefore = vm.SoilPile.Dp;
            foreach (var sp in others) Assert.AreEqual(dpBefore, sp.Dp, 1e-9, "前提: 同じ杭体のセットの Dp がそろっていません");

            vm.SettlePileToeDiaM = before + 0.2;
            double expected = (before + 0.2) * 1000.0;

            Assert.AreEqual(expected, vm.SoilPile.Dp, 1e-9);
            Assert.AreEqual(expected, vm.PileBody!.SettlePileToeDia, 1e-9);
            foreach (var sp in others)
            {
                Assert.AreEqual(expected, sp.Dp, 1e-9,
                    $"同じ杭体のセット {sp.No} の Dp が古いまま ({sp.Dp} mm) です。解析は古い径で回ります");
                Assert.AreNotSame(curvesBefore[sp], sp.LoadDisplacements,
                    $"セット {sp.No} の荷重-沈下曲線が解き直されていません");
                int i = vm.SoilPiles.IndexOf(sp);
                Assert.IsFalse(vm.SoilPileLampStates[i].IsOn, $"曲線が変わったセット {sp.No} が確認済みのままです");
            }
            foreach (var (sp, dp) in otherBodies)
                Assert.AreEqual(dp, sp.Dp, 1e-9, $"別の杭体のセット {sp.No} の Dp まで変わりました");

            // Undo で全セットがそろって戻る
            vm.UndoCommand.Execute(null);
            Assert.AreEqual(dpBefore, vm.PileBody!.SettlePileToeDia, 1e-9, "Undo で杭体の先端径が戻っていません");
            foreach (var sp in vm.SoilPiles.Where(sp => sp.PileBodyNo == bodyNo))
                Assert.AreEqual(dpBefore, sp.Dp, 1e-9, $"Undo でセット {sp.No} の Dp が戻っていません");
        }
    }
}
