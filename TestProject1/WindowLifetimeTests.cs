using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// ウィンドウを閉じたあとに、そのウィンドウの持ち物が動き続けないこと。
    ///
    /// 水平解析ウィンドウの ViewModel は<b>開くたびに作られる</b>のに、
    /// 荷重ケース・組合せ (プロジェクトと同じ寿命) への購読を外していなかった。
    /// また閉じるときの後始末は解析の終了を最大 3 秒しか待たず、諦めたあとに
    /// 完走したワーカーが閉じた窓の ViewModel からメイン画面を更新していた。
    /// </summary>
    [TestClass]
    public class WindowLifetimeTests
    {
        /// <summary>
        /// 後始末で購読を外すこと。
        ///
        /// 外さないと、ウィンドウを開いて閉じた回数ぶんの死んだ ViewModel が
        /// 購読者として残り、チェックを 1 つ変えるだけで全部が動く。
        /// 解析モデルと結果を抱えたままなので、メモリも解放されない。
        /// </summary>
        [TestMethod]
        public void ClosingTheWindow_UnsubscribesFromTheLoadCases()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "public async Task CleanupAsync()");

            StringAssert.Contains(body, "UnsubscribeApplicabilityChanged",
                "後始末で購読を外していない。ウィンドウを開閉するたびに死んだ VM が積み上がる");
        }

        /// <summary>
        /// 購読は名前付きハンドラで張ること。ラムダだと外せない。
        /// </summary>
        [TestMethod]
        public void TheSubscription_UsesAHandlerThatCanBeRemoved()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");

            StringAssert.Contains(source, "item.PropertyChanged -= _applicabilityChangedHandler",
                "購読の解除ができない張り方に戻っている");
            StringAssert.Contains(source, "item.PropertyChanged += _applicabilityChangedHandler",
                "名前付きハンドラで張っていない");
        }

        /// <summary>
        /// 閉じたあとはメイン画面を触らないこと。
        ///
        /// 触ると、リボンタブが勝手に切り替わり結果テーブルだけ作り直される。
        /// OK を通らないので解析済みフラグは立たず、中途半端な状態になる。
        /// </summary>
        [TestMethod]
        public void AfterClosing_TheMainWindowIsLeftAlone()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");

            int at = source.IndexOf("mainWin.AnalysisResultRibbonTab.IsSelected = true;", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "リボンタブの切替が見つからない");

            string guard = source[Math.Max(0, at - 600)..at];
            StringAssert.Contains(guard, "IsWindowClosed",
                "閉じたあとかどうかを見ずにメイン画面を更新している");
        }

        /// <summary>閉じたあとに完了ダイアログを出さないこと。</summary>
        [TestMethod]
        public void AfterClosing_NoCompletionDialogIsShown()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");

            StringAssert.Contains(source, "!BypassUiPromptsForTesting && !IsWindowClosed",
                "閉じた窓の完了通知が出る");
        }

        // ───────── 杭頭の定着筋 ─────────

        /// <summary>
        /// 定着筋の断面積は、その段の本数で決まること。
        ///
        /// 径の setter だけが 1 段目の本数を掛けていた (書き写しの誤り)。
        /// 本数の setter は 2 段目で計算しており、どちらの順で入力したかで値が変わる。
        /// </summary>
        [TestMethod]
        public void TheSecondRebarArea_UsesItsOwnCount()
        {
            // 1 段目と 2 段目で本数を変えておく (既定の 1 段目は 16 本)
            var top = new PileTop { MainBarNum1 = 16, MainBarNum2 = 10 };

            // 径を「既定 (D29) と違う値へ」変える。同じ値を入れ直すと setter が走らない
            top.MainBarSize2 = "D25";

            double expected = 10 * PileTop.GetBarArea("D25");
            Assert.AreEqual(expected, top.MainBarAg2, 1e-9,
                "径を入力したときの定着筋の断面積が、その段の本数で計算されていない");
        }

        /// <summary>入力の順で結果が変わらないこと。</summary>
        [TestMethod]
        public void TheSecondRebarArea_IsTheSameWhicheverOrderYouType()
        {
            var countFirst = new PileTop { MainBarNum2 = 10 };
            countFirst.MainBarSize2 = "D25";

            var sizeFirst = new PileTop();
            sizeFirst.MainBarSize2 = "D25";
            sizeFirst.MainBarNum2 = 10;

            Assert.AreEqual(sizeFirst.MainBarAg2, countFirst.MainBarAg2, 1e-9,
                "本数と径のどちらを先に入力したかで定着筋の断面積が変わる");
        }

        // ── ソース走査の道具 ──

        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

        private static string ReadSource(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = FindSolutionRoot();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return File.ReadAllText(Path.Combine(parts));
        }

        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");

            int open = source.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source[open..(i + 1)];
                }
            }
            Assert.Fail($"本体の閉じ括弧が見つかりません: {signatureFragment}");
            return "";
        }
    }
}
