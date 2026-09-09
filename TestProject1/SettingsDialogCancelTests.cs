using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 基本設定の「キャンセル」で、沈下解析の結果が残ること。
    ///
    /// 以前は材料のオプションを変えた<b>その瞬間</b>に確認して捨てていた。
    /// そのあと「キャンセル」を押すと、入力は元に戻るのに結果は消えたままになる。
    /// 同じボタンの意味が 2 つに割れていた。入力の取り消しであって、破棄の取り消しでは
    /// なかった、という状態。
    ///
    /// 捨てるのは OK を押したときだけにし、確認もそこで 1 回だけ出す。
    /// 断ったら設定ごと元に戻し、画面は開いたままにする。
    ///
    /// なお捨てられるのは<b>沈下解析の結果だけ</b>で、水平解析の結果は
    /// 「再解析が必要」の印が付くだけで残る。破棄の確認はダイアログを伴い単体では
    /// 動かせないので、そこはソース走査で押さえる。
    /// </summary>
    [TestClass]
    public class SettingsDialogCancelTests
    {
        /// <summary>
        /// キャンセルで、入力も静的なオプションも元に戻ること。
        /// </summary>
        [TestMethod]
        public void Cancelling_RestoresTheSettings()
        {
            var main = new MainWindowViewModel();
            bool before = main.CurrentInputModel!.FundamentalInput.IgnoreConcreteTensileStrength;

            var vm = new FundamentalViewModel(main);
            vm.IgnoreConcreteTensileStrength = !before;
            Assert.AreNotEqual(before, main.CurrentInputModel.FundamentalInput.IgnoreConcreteTensileStrength,
                "変更が入力に反映されていない");

            vm.CancelCommand.Execute(null);

            Assert.AreEqual(before, main.CurrentInputModel.FundamentalInput.IgnoreConcreteTensileStrength,
                "キャンセルで入力が戻らない");
            Assert.AreEqual(before, ConcreteModelOptions.IgnoreTensileStrength,
                "キャンセルで静的なオプションが戻らない");
        }

        /// <summary>
        /// 破棄の確認は OK のときに 1 回だけ出す作りであること。
        ///
        /// 破棄の確認はダイアログを伴うので単体では動かせない。流れをソースで押さえる。
        /// オプションは 9 つあり、変えるたびに確認していると押すだけのものになる。
        /// </summary>
        [TestMethod]
        public void TheDiscardIsConfirmedOnceAtOk()
        {
            var source = ReadSource("Graphics_r1", "ViewModels", "FundamentalViewModel.cs");

            var onChanged = ExtractMethodBody(source, "private void HandleConcreteOptionChanged(");
            Assert.IsFalse(onChanged.Contains("CheckAndResetAnalysisResultsKeepingSplit"),
                "オプションを変えた時点で捨てている。キャンセルで戻せない");
            StringAssert.Contains(onChanged, "NoteAnalysisResultsWillBeDiscarded",
                "捨てることになる印を立てていない");

            var onOk = ExtractMethodBody(source, "private void OnOk()");
            StringAssert.Contains(onOk, "CheckAndResetAnalysisResultsKeepingSplit",
                "OK で確認していない");
            StringAssert.Contains(onOk, "RestorePreviousSettings",
                "確認を断ったときに設定を戻していない");

            var onCancel = ExtractMethodBody(source, "private void OnCancel()");
            StringAssert.Contains(onCancel, "RestorePreviousSettings",
                "キャンセルで設定を戻していない");
        }

        /// <summary>
        /// 捨てられるのは沈下解析の結果で、水平解析の結果は残ること。
        ///
        /// この経路は杭要素分割を保持する呼び方をしており、水平解析の結果も
        /// 「再解析が必要」の印が付くだけで消えない。文面もそう書いてある。
        /// ここを取り違えると、直す対象を見誤る。
        /// </summary>
        [TestMethod]
        public void OnlyTheSettlementResultsAreDiscarded()
        {
            var source = ReadSource("Graphics_r1", "ViewModels",
                "MainWindowViewModel.ConfirmDeleteAnalysisModel.cs");

            var body = ExtractMethodBody(source, "public bool CheckAndResetAnalysisResultsKeepingSplit(");
            StringAssert.Contains(body, "includeElementSplit: false",
                "杭要素分割まで捨てる呼び方になっている");

            var confirm = ExtractMethodBody(source, "private bool ConfirmDiscardInvalidatedByInputChange(");
            StringAssert.Contains(confirm, "水平解析の結果は保持されます",
                "水平解析の結果を保持する説明が消えている");
        }

        // ── ソース走査の道具 ──

        private static string FindSolutionRoot([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(typeof(SettingsDialogCancelTests).Assembly.Location), Path.GetDirectoryName(thisFile) })
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
