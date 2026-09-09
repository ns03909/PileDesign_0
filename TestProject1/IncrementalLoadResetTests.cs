using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 増分荷重が再試行で累積しないこと。
    ///
    /// 水平解析は収束しないと <b>nStep を倍にして同じケースをやり直す</b>。
    /// やり直しのたびに <c>SetVectorDF</c> が増分荷重を組み立て直すが、
    /// 杭の接合節点と杭節点については「既存の値に足す」形だった
    /// (杭軸力と杭体自重を同じ節点の Z に順に足すため)。
    ///
    /// <see cref="AnaModel.InitializeStates"/> は変位・内力は戻すが<b>節点荷重は戻さない</b>ので、
    /// nStep 16 → 32 のやり直しで最終的な鉛直荷重が 1.5 倍、もう一度やり直せば 3 倍に
    /// なっていた。足し込む側が起点をゼロに戻す必要がある。
    /// </summary>
    [TestClass]
    public class IncrementalLoadResetTests
    {
        private static Node MakeNode(string name)
        {
            var node = new Node();
            node.SetNodeInfo(name, 0.0, 0.0, 0.0);
            return node;
        }

        private static AnaModel MakeModel(params Node[] nodes)
            => new(new PileDesign.Models.InputData.InputModel { PileLayoutItems = [] },
                   [.. nodes], [], [], [], [], []);

        /// <summary>
        /// <see cref="AnaModel.InitializeStates"/> は節点荷重を戻さない。
        ///
        /// これが「足し込む側が自分で戻さなければならない」理由。
        /// ここが変わったら <c>SetVectorDF</c> の戻し処理も見直すこと。
        /// </summary>
        [TestMethod]
        public void InitializeStates_LeavesNodeLoadsAlone()
        {
            var node = MakeNode("CapNode-1");
            var model = MakeModel(node);
            node.SetIncrementalLoad(new NodeLoad(0, 0, -100.0, 0, 0, 0));

            model.InitializeStates();

            Assert.AreEqual(-100.0, node.IncrementalLoad.Fz, 1e-12,
                "InitializeStates が節点荷重を戻すようになった。"
                + "SetVectorDF の戻し処理と二重になっていないか確認すること");
        }

        /// <summary>
        /// 足し込みは前回の値を引きずる。だから起点を戻す必要がある、という事実の確認。
        /// </summary>
        [TestMethod]
        public void AddingToTheExistingLoad_AccumulatesAcrossAttempts()
        {
            var node = MakeNode("CapNode-1");

            // 1 回目の試行: N=1600kN を 16 ステップに分ける
            var first = node.IncrementalLoad;
            node.SetIncrementalLoad(new NodeLoad(first.Fx, first.Fy, first.Fz - 1600.0 / 16, first.Mx, first.My, first.Mz));
            Assert.AreEqual(-100.0, node.IncrementalLoad.Fz, 1e-9);

            // 2 回目の試行: 32 ステップでやり直す。戻さずに足すと 1 回目が残る
            var second = node.IncrementalLoad;
            node.SetIncrementalLoad(new NodeLoad(second.Fx, second.Fy, second.Fz - 1600.0 / 32, second.Mx, second.My, second.Mz));

            Assert.AreEqual(-150.0, node.IncrementalLoad.Fz, 1e-9,
                "足し込みが累積しない前提になっている。テストの想定を見直すこと");
        }

        /// <summary>
        /// <c>SetVectorDF</c> が、足し込む前に増分荷重を戻していること。
        ///
        /// 実際の呼び出しには杭・地盤・FEM モデル一式が要り単体で動かせないので、
        /// 「戻す処理が、足し込みより前にある」ことをソースで押さえる。
        /// </summary>
        [TestMethod]
        public void SetVectorDF_ResetsBeforeAccumulating()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs"),
                "private void SetVectorDF(");

            int reset = body.IndexOf("ResetIncrementalLoad(", StringComparison.Ordinal);
            int accumulate = body.IndexOf("prev.Fz - deltaN_per_step", StringComparison.Ordinal);

            Assert.IsTrue(reset >= 0,
                "SetVectorDF が増分荷重を戻していない。再試行のたびに鉛直荷重が累積する");
            Assert.IsTrue(accumulate >= 0,
                "杭軸力の足し込みが見つからない。テストの当て先を見直すこと");
            Assert.IsTrue(reset < accumulate,
                "戻す処理が足し込みより後にある。足した分がその場で消える");
        }

        // ── ソース走査の道具 (UnsavedWorkAndRestoreTests と同じ作り) ──

        private static string FindSolutionRoot([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(typeof(IncrementalLoadResetTests).Assembly.Location), Path.GetDirectoryName(thisFile) })
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                        return dir.FullName;
                }
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

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
