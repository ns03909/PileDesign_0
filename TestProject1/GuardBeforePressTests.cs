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
        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

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

            int helper = code.IndexOf("private bool ExplainIfAnalysisKeyIsBlocked", StringComparison.Ordinal);
            Assert.IsTrue(helper >= 0, "解析キーの理由を出す処理がありません");

            // 説明の本体 (次のメソッドの手前まで)
            int helperEnd = code.IndexOf("private void MainWindow_KeyDown", helper, StringComparison.Ordinal);
            if (helperEnd < 0) helperEnd = Math.Min(code.Length, helper + 4000);
            string helperBody = code[helper..helperEnd];

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

                // 説明の本体にそのキーが出てくること (書き方は問わない)
                bool covered = helperBody.Contains("Key." + key, StringComparison.Ordinal)
                    && (mod == "None" || helperBody.Contains("ModifierKeys." + mod, StringComparison.Ordinal));

                // **黙らない道は二つ。**塞がれないコマンド (CanExecute を持たない) は、
                // 押せば動いて自分で理由を出す——むしろそのほうがよい。塞ぐと、ボタンは
                // 押しても何も返らず、キーだけがここで理由を出す状態になる。
                if (!covered && !CanBeBlocked(cmd)) continue;

                if (!covered) missing.Add($"{mod} + {key} ({cmd})");
            }

            Assert.AreEqual(0, missing.Count,
                "実行できないとき黙って何も起きない解析キーがあります。"
                + "ExplainIfAnalysisKeyIsBlocked に足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        /// <summary>
        /// そのコマンドは<b>塞がれうる</b>か——<c>[RelayCommand(CanExecute = ...)]</c> が
        /// 付いているか。
        ///
        /// <para>付いていなければ実行できない状態が無いので、キーを叩けば必ず動く。
        /// 動けば、前提が足りていないことはコマンド自身が言う。</para>
        /// </summary>
        private static bool CanBeBlocked(string commandName)
        {
            string stem = commandName.EndsWith("Command", StringComparison.Ordinal)
                ? commandName[..^"Command".Length]
                : commandName;

            string root = FindSolutionRoot();
            var files = Directory.GetFiles(
                Path.Combine(root, "Graphics_r1", "ViewModels"), "MainWindowViewModel*.cs");

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);

                // 生成元の宣言を探す。名前は Xxx か XxxAsync。
                foreach (string name in new[] { stem + "(", stem + "Async(" })
                {
                    int at = text.IndexOf(name, StringComparison.Ordinal);
                    while (at >= 0)
                    {
                        // 直前の [RelayCommand...] を見る。属性は宣言のすぐ上にある。
                        int attr = text.LastIndexOf("[RelayCommand", at, StringComparison.Ordinal);
                        if (attr >= 0 && at - attr < 400)
                        {
                            int close = text.IndexOf(']', attr);
                            string head = close > attr ? text[attr..close] : string.Empty;
                            return head.Contains("CanExecute", StringComparison.Ordinal);
                        }

                        at = text.IndexOf(name, at + 1, StringComparison.Ordinal);
                    }
                }
            }

            // 見つからなければ安全側。**塞がれうるものとして扱う**——見落として
            // 「黙るキー」を通すより、余計に説明を求めるほうがよい。
            return true;
        }

        /// <summary>
        /// 同じ状況の文は<b>一箇所から</b>。
        ///
        /// <para><c>GuardMessages</c> は「同じ状況には同じ文」のために作られているのに、
        /// 杭要素分割の断りだけ<b>三通りに分かれていた</b>（定数・短い直書き・同じ文の直書き）。
        /// 直したときに片方だけが直る。</para>
        /// </summary>
        [TestMethod]
        public void TheElementSplitGuardIsWordedInOnePlace()
        {
            string root = FindSolutionRoot();
            var offenders = new List<string>();

            var sources = Directory.GetFiles(
                Path.Combine(root, "Graphics_r1"), "*.cs", SearchOption.AllDirectories);

            // **見つからなくなったら落ちること。**走査する側が空を数えて 0 件と答えると、
            // 対象がどこかへ移っただけで合格し続ける。
            TestSource.AssertScanned(sources.Length, 200, "Graphics_r1 の C#");

            foreach (string file in sources)
            {
                if (file.EndsWith("GuardMessages.cs", StringComparison.OrdinalIgnoreCase)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string stripped = StripComment(lines[i]);
                    if (!stripped.Contains('"')) continue;

                    if (stripped.Contains("杭要素分割が済んでいません", StringComparison.Ordinal)
                        || stripped.Contains("杭要素分割を行ってください", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {stripped.Trim()}");
                    }
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "杭要素分割の断りが GuardMessages の外に書かれています。"
                + "GuardMessages.NotElementSplit を使ってください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>
        /// 済ませられる前提は、<b>案内ではなく訊く</b>。
        ///
        /// <para>足りないものが分かっていて、それを済ませる画面もこちらが持っているなら、
        /// 「実行してください」で終わらせない——閉じて探して実行してもう一度、の四手が
        /// 一手になる。</para>
        /// </summary>
        [TestMethod]
        public void TheSplitIsOfferedRatherThanOnlyExplained()
        {
            string root = FindSolutionRoot();
            string vm = File.ReadAllText(
                Path.Combine(root, "Graphics_r1", "ViewModels", "MainWindowViewModel.cs"));

            StringAssert.Contains(vm, "NotElementSplitAsk",
                "杭要素分割を訊く文を使っていません");
            StringAssert.Contains(vm, "OpenElementDivisionWindowCommand.Execute",
                "訊いたあとに分割の画面を開いていません (訊くだけでは四手のまま)");

            foreach (string what in new[] { "水平解析", "単杭沈下解析" })
                StringAssert.Contains(vm, $"EnsureElementSplit(\"{what}\")",
                    $"{what} が前提を確かめていません");
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}
