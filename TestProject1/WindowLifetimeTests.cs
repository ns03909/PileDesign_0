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

        // 杭頭主筋の全断面積 (MainBarAg1/2) の検査は置かない。
        // 計算して入れているだけで誰も読まない値だった (杭頭の耐力は工法ごとのデータ、
        // 例えば CapringPile.GetTensionBarArea から求める)。値ごと 2026-09-19 に撤去した。
        // テストが「使われていない値」を守っていた形。

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
