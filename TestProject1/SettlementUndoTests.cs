using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下ウィンドウの Undo が、<b>この画面が書き換えるものを残らず</b>戻すこと。
    ///
    /// この画面は地盤杭セット (<c>SoilPiles</c>) は控えを編集するが、
    /// 杭体入力 (<c>PileBody</c>) は実体を編集する。しかも沈下検討用杭先端径は
    /// <c>PileBody.SettlePileToeDia</c> と <c>SoilPile.Dp</c> の<b>両方</b>へ書いており、
    /// <b>解析が読むのは <c>SoilPile.Dp</c></b> の側である。
    ///
    /// Undo の控えが地盤杭セットしか写していなかったため、Undo すると
    /// <c>Dp</c> だけが戻り、<b>画面には新しい値が出たまま解析は古い値で回る</b>
    /// 状態を 2 手で作れた。α・N・杭体記号・先端非排水率は履歴にすら入っていなかった。
    ///
    /// 控えの形が初期化・Undo の直前・コードビハインドの 3 か所に散っていたのが根で、
    /// <c>CaptureUndoState</c> に一本化した。
    /// </summary>
    [TestClass]
    public class SettlementUndoTests
    {
        [TestMethod]
        public void Undo_RestoresBothSidesOfTheMirroredDiameter()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var soilPiles = model.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0)
            {
                Assert.Inconclusive("要素分割された地盤杭セットがありません");
                return;
            }

            // 先端平均N値が 0 だと ViewModel の生成が警告ダイアログを出す (テストでは開けない)
            if (soilPiles[0].PileToeNValue == 0)
            {
                Assert.Inconclusive("杭先端平均N値が 0 のため ViewModel を生成できません");
                return;
            }

            // ViewModel の生成は WPF のコントロールを作るので STA が要る
            var captured = XamlSmokeTestSupport.RunOnStaThread(
                () => Check(model), out bool timedOut);

            if (timedOut) { Assert.Inconclusive("STA スレッドで 60 秒以内に終わりませんでした"); return; }
            if (captured is AssertFailedException || captured is AssertInconclusiveException)
                throw captured;
            if (captured != null)
                Assert.Fail($"{captured.GetType().Name}: {captured.Message}"
                    + Environment.NewLine + captured.StackTrace);
        }

        private static void Check(InputModel model)
        {
            var mainVm = new MainWindowViewModel { CurrentInputModel = model };
            var vm = new SettlementViewModel(mainVm);

            var body = vm.PileBody;
            Assert.IsNotNull(body, "杭体入力を掴めていません");

            double diaBefore = vm.SettlePileToeDiaM;
            double dpBefore = vm.SoilPile.Dp;
            double alphaBefore = body!.SettleAlpha;

            Assert.IsTrue(diaBefore > 0, $"沈下用先端径が 0 です ({diaBefore})");

            // 画面でいじる (セッターは PileBody と SoilPile の両方へ書く)
            vm.SettlePileToeDiaM = diaBefore + 0.4;
            body.SettleAlpha = alphaBefore + 0.5;

            Assert.AreNotEqual(dpBefore, vm.SoilPile.Dp, "揺らせていません (Dp が動いていない)");

            // Undo
            vm.UndoCommand.Execute(null);

            // 対で書いている値が、対で戻ること。
            // ここが食い違うと、画面の表示と解析が使う値が違う。
            Assert.AreEqual(vm.SoilPile.Dp, vm.PileBody!.SettlePileToeDia, 1e-9,
                "Undo で先端径が片方だけ戻りました。"
                + $"画面は {vm.PileBody.SettlePileToeDia / 1000.0:F3}m を出しますが、"
                + $"解析は {vm.SoilPile.Dp / 1000.0:F3}m で回ります");

            Assert.AreEqual(dpBefore, vm.SoilPile.Dp, 1e-9, "Undo で Dp が元に戻っていません");
            Assert.AreEqual(diaBefore, vm.SettlePileToeDiaM, 1e-9,
                "Undo で沈下用先端径が元に戻っていません");
            Assert.AreEqual(alphaBefore, vm.PileBody.SettleAlpha, 1e-9,
                "Undo で沈下パラメータ α が元に戻っていません");
        }

        /// <summary>
        /// 控えの形を組み立てるのが<b>1 か所だけ</b>であること。
        ///
        /// 以前は 3 か所にあり、どれも杭体入力を写していなかった。
        /// 散らばると、足すのを 1 か所忘れても気づけない。
        /// </summary>
        [TestMethod]
        public void TheUndoSnapshot_IsBuiltInOnePlace()
        {
            string vm = StripComments(
                TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs"));
            string behind = StripComments(
                TestSource.Read("Graphics_r1", "Views", "SettlementWindow.xaml.cs"));

            // 控えを積むのは SaveUndoSnapshot だけ (形を知っているのは CaptureUndoState だけ)
            StringAssert.Contains(vm, "SettlementUndoState CaptureUndoState()",
                "CaptureUndoState が見つかりません (名前が変わった?)");

            int saves = Regex.Matches(vm, @"_undoManager\.SaveState\s*\(|UndoManager\.SaveState\s*\(").Count;
            Assert.AreEqual(1, saves,
                $"Undo の控えを積んでいる箇所が {saves} 個あります。"
                + "SaveUndoSnapshot に一本化してください "
                + "(散らばると、足すのを 1 か所忘れても気づけません)");

            StringAssert.Contains(vm, "InputModel.PileBodies",
                "Undo の控えに杭体入力が入っていません。"
                + "α・N・先端径がキャンセルでもなく Undo でも戻らなくなります");

            // コードビハインドは形を知らない
            Assert.AreEqual(0, Regex.Matches(behind, @"SaveState\s*\(").Count,
                "コードビハインドが控えの形を組み立てています。"
                + "ViewModel の SaveUndoSnapshot を呼ぶだけにしてください");
            StringAssert.Contains(behind, "SaveUndoSnapshot(",
                "コードビハインドが Undo の控えを取らなくなっています "
                + "(テキストボックスのフォーカス喪失で 1 段積む作り)");
        }

        private static string StripComments(string source)
        {
            string s = Regex.Replace(source, @"/\*[\s\S]*?\*/", "");
            return Regex.Replace(s, @"//[^\n]*", "");
        }
    }
}
