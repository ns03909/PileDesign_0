using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 「押す前に分かる」— 実行できない理由は、押してから叱るのではなく
    /// ボタンの状態と説明で先に伝えること。
    ///
    /// 群杭沈下解析だけ CanExecute を持たず、実行してから最大 6 種類のダイアログで
    /// 叱っていた (F5/F6/F7 は CanExecute 済み)。判定を 1 か所に集めて、
    /// CanExecute と ToolTip の両方から使う形にした。
    /// </summary>
    [TestClass]
    public class GuardBeforePressTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(GuardBeforePressTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// 何も入力していない状態では実行できず、その理由が読めること。
        /// </summary>
        [TestMethod]
        public void EmptyModel_CannotRunAndSaysWhy()
        {
            var vm = new MainWindowViewModel();

            Assert.IsFalse(vm.PileGroupSettlementAnalysisCommand.CanExecute(null),
                "入力が無いのに群杭沈下解析を実行できる判定になっている");
            Assert.IsNotNull(vm.GroupSettlementAnalysisDisabledReason,
                "実行できないのに理由が無い");
            Assert.AreEqual(vm.GroupSettlementAnalysisDisabledReason, vm.GroupSettlementAnalysisToolTip,
                "実行できないときは ToolTip に理由を出すこと");
        }

        /// <summary>
        /// 理由は利用者に向けた文面であること (内部の型名・プロパティ名を出さない)。
        /// </summary>
        [TestMethod]
        public void Reason_IsWrittenForTheUser()
        {
            var vm = new MainWindowViewModel();
            string reason = vm.GroupSettlementAnalysisDisabledReason!;

            foreach (string internalWord in new[] { "PileGroupSettlement", "RectLoads", "CanExecute", "null" })
                StringAssert.DoesNotMatch(reason, new Regex(Regex.Escape(internalWord)),
                    $"利用者向けの文面に内部用語「{internalWord}」が出ている: {reason}");
        }

        /// <summary>
        /// キー操作がコマンドの CanExecute を迂回していないこと。
        ///
        /// code-behind の KeyDown から VM のメソッドや <c>Command.Execute</c> を直接呼ぶと、
        /// <c>Execute</c> は <c>CanExecute</c> を見ないため
        /// 「ボタンは灰色なのにキーでは実行できる」状態になる。
        /// 解析を起動するショートカットは XAML の InputBindings に置くこと
        /// (InputBindings は CanExecute が false なら発火しない)。
        /// </summary>
        [TestMethod]
        public void AnalysisShortcuts_GoThroughInputBindings()
        {
            string root = FindSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml.cs"));

            // 解析を起動する F キーは InputBindings にあること
            foreach (string key in new[] { "F5", "F6", "F7" })
                StringAssert.Contains(xaml, $"<KeyBinding Key=\"{key}\"",
                    $"{key} が InputBindings に無い (CanExecute を迂回する)");

            // code-behind の KeyDown ハンドラで解析コマンドを直接叩いていないこと
            var direct = new List<string>();
            var lines = code.Split('\n');
            foreach (var (line, i) in lines.Select((l, i) => (l, i)))
            {
                string stripped = StripComment(line);
                if (Regex.IsMatch(stripped, @"Open(LateralLoadAnalysis|Settlement|VerticalBeamCalculation)\w*(Command\.Execute|\s*\()"))
                    direct.Add($"MainWindow.xaml.cs:{i + 1}  {stripped.Trim()}");
            }

            Assert.AreEqual(0, direct.Count,
                "code-behind が解析コマンドを直接呼んでいる (CanExecute を迂回する):\n  "
                + string.Join("\n  ", direct));
        }

        /// <summary>
        /// 解析のキーが効かないときに<b>理由を出す</b>こと。
        ///
        /// <c>Window.InputBindings</c> は実行できない状態だと黙って何もしない。
        /// ボタンなら灰色と説明で分かるが、キーには押した感触が無く
        /// 「押しても何も起きない」としか見えない
        /// (群杭沈下 F7 を、土層も矩形荷重も無い状態で押したときに実際にそうなった)。
        ///
        /// InputBindings に登録した解析のキーは、すべて説明の対象に入れること。
        /// </summary>
        [TestMethod]
        public void BlockedAnalysisKeysExplainWhy()
        {
            string root = FindSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", "MainWindow.xaml.cs"));

            int helper = code.IndexOf("ExplainIfAnalysisKeyIsBlocked", StringComparison.Ordinal);
            Assert.IsTrue(helper >= 0, "解析キーの理由を出す処理がありません");

            // 実行できないときに黙らないよう、CanExecute を見てから説明していること
            StringAssert.Contains(code, "CanExecute(null)",
                "CanExecute を見ずに説明しています (実行できるのに割り込む恐れ)");

            var missing = new List<string>();
            foreach (Match m in Regex.Matches(xaml,
                         @"<KeyBinding\s+Key=""(?<key>F\d+)""(?:\s+Modifiers=""(?<mod>[^""]+)"")?\s+Command=""\{Binding\s+(?<cmd>\w+)\}"""))
            {
                string cmd = m.Groups["cmd"].Value;

                // 解析を起動しないキー (要素分割ウィンドウ等) は対象外
                if (!cmd.Contains("Analysis", StringComparison.Ordinal)
                    && !cmd.Contains("Settlement", StringComparison.Ordinal)
                    && !cmd.Contains("Calculation", StringComparison.Ordinal)) continue;

                string key = m.Groups["key"].Value;
                string mod = m.Groups["mod"].Success ? m.Groups["mod"].Value : "None";

                // 説明の対象表に、そのキーと修飾キーの組が出てくること
                if (!Regex.IsMatch(code, @"\(Key\." + key + @",\s*ModifierKeys\." + mod + @"\)"))
                    missing.Add($"{mod} + {key} ({cmd})");
            }

            Assert.AreEqual(0, missing.Count,
                "実行できないとき黙って何も起きない解析キーがあります。"
                + "ExplainIfAnalysisKeyIsBlocked に足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
