using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 水平解析の同時実行ケース数の<b>初期値</b>が、論理プロセッサ数で絞られていること。
    ///
    /// <para>既定値 16 は「論理プロセッサ数で自動制限されるので、8 コア機なら 8 に収まる」
    /// 設計だった。ところが絞り込みはセッターにしかなく、初期値はフィールドに 16 を
    /// 直に入れていたため通っていなかった。利用者が値を触るまでは、論理プロセッサ数が
    /// 16 未満の PC でも 16 並列で走り、設計が避けようとしていたメモリ圧迫の危険があった。</para>
    ///
    /// <para><b>論理プロセッサ数が 16 以上の PC では症状が出ない</b>（開発機がそうだった）。
    /// だから画面の値だけで見るのではなく、論理プロセッサ数を引数に取る関数で確かめ、
    /// フィールドがその関数を通っていることも見る。</para>
    /// </summary>
    [TestClass]
    public class CaseParallelismDefaultTests
    {
        [DataTestMethod]
        [DataRow(1, 1)]
        [DataRow(4, 4)]
        [DataRow(8, 8)]
        [DataRow(16, 16)]
        [DataRow(32, 16)]
        [DataRow(0, 1)]
        public void TheInitialValue_IsClampedToTheProcessorCount(int processors, int expected)
        {
            Assert.AreEqual(expected, HorizontalCalculationViewModel.InitialCaseParallelism(processors),
                $"論理プロセッサ {processors} の PC で、同時実行ケース数の初期値が {expected} になりません");
        }

        /// <summary>
        /// フィールドの初期値が絞り込みを通っていること。
        /// 直に数値を入れると、上の関数が正しくても画面の初期値には効かない。
        /// </summary>
        [TestMethod]
        public void TheFieldIsInitialisedThroughTheClamp()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            var m = Regex.Match(src, @"private int _maxCaseDegreeOfParallelism\s*=\s*([^;]+);");
            Assert.IsTrue(m.Success, "同時実行ケース数のフィールドが見つかりません (名前が変わった?)");

            StringAssert.Contains(m.Groups[1].Value, "InitialCaseParallelism(",
                "同時実行ケース数の初期値が、論理プロセッサ数での絞り込みを通っていません。"
                + "論理プロセッサ数の少ない PC で、利用者が値を触るまで過大な並列数で走ります");
        }

        [TestMethod]
        public void ANewWindow_StartsWithinTheProcessorCount()
        {
            var main = new MainWindowViewModel();
            var vm = new HorizontalCalculationViewModel(main);

            Assert.IsTrue(vm.MaxCaseDegreeOfParallelism >= 1
                && vm.MaxCaseDegreeOfParallelism <= Environment.ProcessorCount,
                $"開いた直後の同時実行ケース数 {vm.MaxCaseDegreeOfParallelism} が、"
                + $"論理プロセッサ数 {Environment.ProcessorCount} の範囲に収まっていません");
        }
    }
}
