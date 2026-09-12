using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 慣性力が 0 になる「荷重ケース × 組合せ」を解析の前に止めること。
    ///
    /// <para>非線形反復の収束判定は残差比 ‖R‖²/‖F‖² で、分母 F は慣性力 (外力)。
    /// βU·上部構造慣性力 と βL·基礎部慣性力 がともに 0 になる組合せでは F = 0 になり、
    /// 比が固定値のまま反復上限まで回る。地盤変位を強制変位で与えるケースは応答そのものは
    /// 出るので、<b>収束しないまま結果が残る</b>。</para>
    ///
    /// <para>組合せ係数のスライダーが 0.5 未満だと
    /// <see cref="LoadCaseViewModel.GetCombinations"/> は (0, 0.0, 0.0, 0.0) を返すため、
    /// これは通常操作で到達する。解法 (残差比の分母) は変えず、入力検査で止める
    /// (2026-09-12)。</para>
    /// </summary>
    [TestClass]
    public class LoadCombinationValidationTests
    {
        /// <summary>組合せ係数だけを変えた最小の入力。</summary>
        private static InputModel ModelWithFactor(double factor)
        {
            var model = new InputModel
            {
                LoadCasesInput = new LoadCasesInput()
            };
            model.LoadCasesInput.LoadCasesLevel1 = new ObservableCollection<LoadCase>
            {
                new LoadCase { Level = 1, No = 1, IsApplicable = true, IsAnalysisTarget = true,
                    UpperMassForce = 1000, FoundationMassForce = 800 }
            };
            model.LoadCasesInput.LoadCasesLevel2 = new ObservableCollection<LoadCase>();
            model.LoadCasesInput.LoadCombinationFactor = factor;
            model.LoadCasesInput.LoadCombinations = LoadCaseViewModel.GetCombinations(factor);

            // この検査は「収束判定の基準値が外力のみ」のときだけ働く。
            // 既定 (外力と反力の大きい方) では慣性力 0 でも判定が成り立つので止めない
            // (その区別は ResidualReferenceModeTests が見る)。
            model.FundamentalInput = new FundamentalInput
            {
                ResidualReference = ResidualReferenceMode.ExternalForce,
            };
            return model;
        }

        /// <summary>
        /// スライダーが 0.5 未満のときの組合せ (0, 0.0, 0.0, 0.0) は止めること。
        /// </summary>
        [DataTestMethod]
        [DataRow(0.0)]
        [DataRow(0.2)]
        [DataRow(0.49)]
        public void ACombinationWithNoInertiaIsRejected(double factor)
        {
            var model = ModelWithFactor(factor);
            string message = CheckInputData.CheckLoadCombinations(model, "");

            Assert.AreNotEqual("", message,
                $"組合せ係数 {factor} は βU = βL = 0 の組合せを作りますが、解析が止まりません。"
                + "慣性力 0 では収束判定 (残差/外力) が成り立ちません");
            Assert.IsTrue(message.Contains("慣性力"),
                $"止めた理由が読み取れません: {message}");
        }

        /// <summary>
        /// 通常の組合せ (係数 0.5〜1.0) は通すこと。
        /// </summary>
        [DataTestMethod]
        [DataRow(0.5)]
        [DataRow(0.75)]
        [DataRow(1.0)]
        public void TheStandardCombinationsPass(double factor)
        {
            var model = ModelWithFactor(factor);
            Assert.AreEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                $"組合せ係数 {factor} は通常の入力ですが、解析が止まります");
        }

        /// <summary>
        /// 慣性力がどちらも 0 の荷重ケースは対象外 (水平解析側でスキップされるので、
        /// 組合せの責任ではない)。
        /// </summary>
        [TestMethod]
        public void ALoadCaseWithNoForceIsNotTheCombinationsFault()
        {
            var model = ModelWithFactor(0.0);
            model.LoadCasesInput.LoadCasesLevel1[0].UpperMassForce = 0;
            model.LoadCasesInput.LoadCasesLevel1[0].FoundationMassForce = 0;

            Assert.AreEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                "荷重がゼロの荷重ケースは解析側でスキップされるので、ここで止める必要はありません");
        }

        /// <summary>
        /// 上部構造と基礎部のどちらか一方でも慣性力が残れば通すこと。
        /// 2 つは別の節点に載るので、符号が逆でも外力ベクトルは 0 にならない。
        /// </summary>
        [TestMethod]
        public void OneRemainingInertiaTermIsEnough()
        {
            var model = ModelWithFactor(0.0); // 組合せは (0, 0, 0)
            model.LoadCasesInput.LoadCombinations = new ObservableCollection<LoadCombination>
            {
                new LoadCombination(1, 0.0, 0.0, 1.0) // βU = 0、βL = 1 → 基礎部の慣性力が残る
            };

            Assert.AreEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                "基礎部の慣性力が残っているのに解析が止まります");
        }

        /// <summary>
        /// 既に別の問題が書かれている message を消さないこと (検査は連ねて呼ばれる)。
        /// </summary>
        [TestMethod]
        public void ItAppendsToTheExistingMessage()
        {
            var model = ModelWithFactor(0.0);
            string message = CheckInputData.CheckLoadCombinations(model, "先の問題\n");

            Assert.IsTrue(message.StartsWith("先の問題\n"),
                "先に見つかっていた問題が消えています");
        }
    }
}
