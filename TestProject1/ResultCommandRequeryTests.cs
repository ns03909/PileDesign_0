using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 解析が終わったら、結果ウィンドウのコマンド (グラフ出力・テーブル出力・ログ表示) が
    /// <b>押せるようになること</b>。
    ///
    /// <para><c>CanExecute</c> は <c>HasAnyAnalysisResult</c> を見ているが、
    /// CommunityToolkit の <c>RelayCommand</c> は <c>NotifyCanExecuteChanged()</c> を
    /// 呼ばないと聞き直さない。プロパティの通知だけではボタンが灰色のまま残る。</para>
    ///
    /// <para>2026-09-20 まで、4 つの解析済み旗のうち再評価を呼んでいたのは基礎梁考慮沈下だけ
    /// だった。単杭沈下だけを実行すると、沈下量は画面に出るのに「グラフ出力」「テーブル出力」が
    /// 押せない (実機で確認)。水平解析は別の経路がたまたま再評価していたので気づかなかった。</para>
    /// </summary>
    [TestClass]
    public class ResultCommandRequeryTests
    {
        private static readonly string[] DoneFlags =
        {
            "IsHorizontalAnalysisDone",
            "IsVerticalAnalysisDone",
            "IsGroupPileSettlementAnalysisDone",
            "IsVerticalBeamAnalysisDone",
        };

        /// <summary>
        /// 解析済み旗の setter は、結果ウィンドウのコマンドを再評価すること。
        /// </summary>
        [TestMethod]
        public void EveryAnalysisDoneFlagRequeriesTheResultCommands()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.DisplayOptions.cs");

            int scanned = 0;
            foreach (var flag in DoneFlags)
            {
                var m = Regex.Match(src, @"public bool " + flag + @"\s*$", RegexOptions.Multiline);
                Assert.IsTrue(m.Success, $"{flag} が見つかりません (書き方が変わった?)");

                string body = BodyAfter(src, m.Index);
                StringAssert.Contains(body, "RaiseResultCommandsCanExecute",
                    $"{flag} が結果ウィンドウのコマンドを再評価していません"
                    + " (解析が終わってもグラフ出力・テーブル出力が押せないままになります)");
                scanned++;
            }
            TestSource.AssertScanned(scanned, DoneFlags.Length, "解析済み旗");
        }

        /// <summary>
        /// 単杭沈下だけを終えた状態で「グラフ出力」が押せること (結果は荷重-沈下曲線)。
        /// 旗だけで曲線が無いなら、テーブル出力は押せない (出す表が無い)。
        /// </summary>
        [TestMethod]
        public void AfterASinglePileSettlementAnalysisTheGraphCommandIsEnabled()
        {
            var vm = new MainWindowViewModel();
            Assert.IsFalse(vm.OpenGraphWindowCommand.CanExecute(null), "前提: 解析前は押せない");

            vm.IsVerticalAnalysisDone = true;

            Assert.IsTrue(vm.HasAnyAnalysisResult, "前提: 解析結果があると答えること");
            Assert.IsTrue(vm.OpenGraphWindowCommand.CanExecute(null), "グラフ出力が押せない");
            Assert.IsFalse(vm.OpenTableWindowCommand.CanExecute(null),
                "曲線が無いのにテーブル出力が押せる (出す表が無い)");
        }

        /// <summary>
        /// 単杭沈下の荷重-沈下曲線があれば「テーブル出力」も押せること。
        ///
        /// 単杭沈下の結果は長くグラフだけで、テーブルには 1 枚も出ていなかった
        /// (2026-09-20 に荷重-沈下曲線と各杭の沈下量の表を追加)。
        /// </summary>
        [TestMethod]
        public void SinglePileSettlementCurvesEnableTheTableCommand()
        {
            var input = new PileDesign.Models.InputData.InputModel();
            input.ElementDivision ??= new PileDesign.Models.InputData.ElementDivision();
            var sp = new PileDesign.Models.InputData.SoilPile { GroundNo = 1, PileBodyNo = 1, Z = 0.0 };
            sp.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 0, DD0s = 0 });
            sp.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 1000, DD0s = 5 });
            input.ElementDivision.SoilPiles.Add(sp);

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            Assert.IsTrue(vm.HasSinglePileSettlementCurves, "前提: 曲線を持っていると答えること");

            vm.IsVerticalAnalysisDone = true;

            Assert.IsTrue(vm.OpenTableWindowCommand.CanExecute(null),
                "単杭沈下の曲線があるのにテーブル出力が押せない");
        }

        /// <summary>
        /// 群杭沈下 (一般) の結果があれば「テーブル出力」も押せること。
        ///
        /// テーブルウィンドウは群杭沈下 (一般) の表も組むのに、押せる条件は
        /// 「群杭沈下 (反復)」しか見ていなかった。表があるのにボタンが灰色だった。
        /// </summary>
        [TestMethod]
        public void GeneralGroupSettlementRecordsEnableTheTableCommand()
        {
            var input = new PileDesign.Models.InputData.InputModel();
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            input.PileGroupSettlement ??= new PileDesign.Models.InputData.PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords ??= [];
            input.PileGroupSettlement.CaseRecords.Add(new PileDesign.Models.Results.GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "任意矩形",
                IsBeamAware = false,        // 一般 (反復ではない)
                IsConverged = true,
            });

            vm.IsGroupPileSettlementAnalysisDone = true;

            Assert.IsTrue(vm.OpenGraphWindowCommand.CanExecute(null), "グラフ出力が押せない");
            Assert.IsTrue(vm.OpenTableWindowCommand.CanExecute(null),
                "群杭沈下 (一般) の表があるのにテーブル出力が押せない");
        }

        /// <summary>
        /// テーブルウィンドウが組む表の種類と、押せる条件が対応していること。
        /// 条件の側だけ増え忘れると、表があるのに開けない。
        /// </summary>
        [TestMethod]
        public void TheTableCommandConditionCoversWhatTheWindowBuilds()
        {
            string ctor = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Constructor.cs");
            var m = Regex.Match(ctor, @"OpenTableWindowCommand = new ToolkitRelayCommand\((.*?)\);",
                RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "テーブル出力コマンドの組み立てが見つかりません");

            string condition = m.Groups[1].Value;
            StringAssert.Contains(condition, "LatestResultTables", "水平解析の表を見ていない");
            StringAssert.Contains(condition, "VerticalBeamCaseResults", "基礎梁鉛直の表を見ていない");
            StringAssert.Contains(condition, "HasGroupSettlementCaseRecords",
                "群杭沈下の表を反復だけで見ている (一般の結果しか無いと開けない)");
            StringAssert.Contains(condition, "HasSinglePileSettlementCurves",
                "単杭沈下の表を見ていない (曲線があるのに開けない)");
        }

        /// <summary>波括弧の対応で property の本体を切り出す。</summary>
        private static string BodyAfter(string src, int from)
        {
            int open = src.IndexOf('{', from);
            if (open < 0) return string.Empty;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) return src[open..(i + 1)];
                }
            }
            return src[open..];
        }
    }
}
