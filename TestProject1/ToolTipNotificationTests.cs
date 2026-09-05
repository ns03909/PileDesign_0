using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 計算で作るツールチップは、元になる値が変わったら<b>作り直されること</b>。
    ///
    /// <c>ToolTip="{Binding Foo}"</c> の Foo が計算プロパティ (get のみ) だと、
    /// バインドされた時点の文字列がそのまま残る。ボタンの可否は
    /// <c>CommandManager.RequerySuggested</c> で問い直しているのに文言は据え置き、
    /// という取り合わせになり、<b>矩形荷重を足したのに「定義されていません」と出続けた</b>。
    ///
    /// 押せるかどうかと、押せない理由は<b>同じ信号で更新する</b>こと。
    /// </summary>
    [TestClass]
    public class ToolTipNotificationTests
    {
        /// <summary>
        /// 矩形荷重を足したら、実行ボタンの理由が消えること。
        /// 判定そのものではなく<b>通知</b>を見る (判定は昔から正しかった)。
        /// </summary>
        [TestMethod]
        public void AddingARectLoad_UpdatesTheAnalysisButtonToolTip()
        {
            var input = new InputModel
            {
                PileLayoutItems = [new PileLayoutDataItem { PileNo = 1 }],
            };
            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.LoadingType = "任意矩形";
            input.PileGroupSettlement.RectLoads = [];

            var vm = new MainWindowViewModel { CurrentInputModel = input };

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            vm.RefreshGroupSettlementGuard();
            StringAssert.Contains(vm.GroupSettlementAnalysisToolTip, "矩形荷重",
                "前提: 矩形荷重が無いので理由が出ているはず");

            // 荷重タブで矩形荷重を足した状況
            input.PileGroupSettlement.RectLoads.Add(new RectLoad
            {
                X1 = -0.9, Y1 = -1.9, X2 = 37.7, Y2 = 14.65, QA = 44136.0,
            });

            raised.Clear();
            vm.RefreshGroupSettlementGuard();

            CollectionAssert.Contains(raised, nameof(MainWindowViewModel.GroupSettlementAnalysisToolTip),
                "矩形荷重を足してもツールチップの更新が通知されていません "
                + "(押せるボタンに「定義されていません」と出たままになる)");
            Assert.IsFalse(vm.GroupSettlementAnalysisToolTip.Contains("矩形荷重）が定義されていません"),
                $"理由が古いままです: {vm.GroupSettlementAnalysisToolTip}");
        }

        /// <summary>
        /// 変わっていないときは通知しないこと。
        /// <c>RequerySuggested</c> はクリックやフォーカス移動のたびに飛んでくるので、
        /// 毎回通知すると無駄にバインドが走る。
        /// </summary>
        [TestMethod]
        public void RepeatedRefresh_DoesNotKeepRaising()
        {
            var input = new InputModel
            {
                PileLayoutItems = [new PileLayoutDataItem { PileNo = 1 }],
            };
            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.LoadingType = "任意矩形";

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            vm.RefreshGroupSettlementGuard();

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            vm.RefreshGroupSettlementGuard();
            vm.RefreshGroupSettlementGuard();

            Assert.IsFalse(raised.Contains(nameof(MainWindowViewModel.GroupSettlementAnalysisToolTip)),
                "変わっていないのに通知しています");
        }

        /// <summary>
        /// 杭体番号・地盤番号の ComboBox のホバー説明が、番号を変えたら作り直されること。
        /// </summary>
        [TestMethod]
        public void ChangingTheNumber_UpdatesTheRowSummaryToolTips()
        {
            var pile = new PileLayoutDataItem { PileNo = 1, PileBodyNo = 1, GroundNo = 1 };

            var raised = new List<string>();
            pile.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            pile.PileBodyNo = 2;
            CollectionAssert.Contains(raised, nameof(PileLayoutDataItem.PileBodySummary),
                "杭体番号を変えても説明の更新が通知されていません");

            raised.Clear();
            pile.GroundNo = 2;
            CollectionAssert.Contains(raised, nameof(PileLayoutDataItem.GroundSummary),
                "地盤番号を変えても説明の更新が通知されていません");
        }

        /// <summary>
        /// XAML の <c>ToolTip="{Binding X}"</c> の X が、通知される作りになっていること。
        ///
        /// 計算プロパティ (get のみ) を直接バインドすると、値が変わっても文字列が残る。
        /// <c>OnPropertyChanged(nameof(X))</c> がどこかにあるか、
        /// セッターを持つ (SetProperty / [ObservableProperty]) ならよい。
        /// </summary>
        [TestMethod]
        public void EveryBoundToolTipPropertyIsNotified()
        {
            string root = FindSolutionRoot();
            string appDir = Path.Combine(root, "Graphics_r1");

            var bound = new HashSet<string>(StringComparer.Ordinal);
            foreach (string xaml in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(xaml),
                             @"ToolTip=""\{Binding\s+(?:Path=)?([A-Za-z_][A-Za-z0-9_]*)\s*\}"""))
                {
                    bound.Add(m.Groups[1].Value);
                }
            }
            Assert.IsTrue(bound.Count > 3, $"ToolTip のバインドが見つかりません ({bound.Count} 件)");

            string all = string.Join("\n", Directory
                .EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Select(File.ReadAllText));

            var silent = new List<string>();
            foreach (string name in bound.OrderBy(x => x, StringComparer.Ordinal))
            {
                string e = Regex.Escape(name);

                // どこかで明示的に通知している
                if (Regex.IsMatch(all, @"OnPropertyChanged\(nameof\([A-Za-z0-9_.]*" + e + @"\)\)")) continue;
                // セッターを持つ (SetProperty / ObservableProperty / 通常の set)
                if (Regex.IsMatch(all, @"(?:public|internal)\s+[\w<>?\[\], ]+\s+" + e
                                     + @"\s*\{[^}]*\bset\b")) continue;
                if (Regex.IsMatch(all, @"\[ObservableProperty\][^;]*\b" + char.ToLowerInvariant(name[0])
                                     + Regex.Escape(name[1..]) + @"\b")) continue;

                if (ConstantToolTips.Contains(name)) continue;

                silent.Add(name);
            }

            Assert.AreEqual(0, silent.Count,
                "値が変わっても作り直されないツールチップがあります。"
                + "元の値が変わるところで OnPropertyChanged を上げてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", silent));
        }

        /// <summary>
        /// 中身が固定の説明文で、値が変わりようがないもの。通知は要らない。
        /// <b>「たぶん変わらない」ではなく、実装が定数文字列だけのものに限ること。</b>
        /// </summary>
        private static readonly HashSet<string> ConstantToolTips =
        [
            "ProgressTextTooltip",   // 「ステップ評価」の意味の説明。状態を読まない
        ];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(ToolTipNotificationTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }
    }
}
