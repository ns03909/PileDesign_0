using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// VL 単独擬似ケース機能 (P-S 非線形ばね + 「VL 単独ケースも解析」オプション ON 時の追加解析) の
    /// 構成・有効化条件・カウント反映に関する単体テスト。
    ///
    /// 主に HCVM の派生プロパティ (TotalCalculationCount / TotalLoadCaseCount) が
    /// VL ケース分を加算するかを検証する (2026-05-17 のバグ修正に対するレグレッション)。
    /// </summary>
    [TestClass]
    public class VLPseudoCaseTests
    {
        private static (HorizontalCalculationViewModel hcvm, MainWindowViewModel main) CreateVm()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(inputModel, "例題ロード失敗");
            var main = new MainWindowViewModel();
            main.CurrentInputModel = inputModel;
            var hcvm = new HorizontalCalculationViewModel(main);
            return (hcvm, main);
        }

        [TestMethod]
        public void IsVLAnalysisEnabled_DefaultsToFalse()
        {
            var (hcvm, _) = CreateVm();
            Assert.IsFalse(hcvm.IsVLAnalysisEnabled, "新規 InputModel で VL 単独解析オプションは既定 OFF");
        }

        [TestMethod]
        public void UsePsSpringAtPileTip_DefaultsToFalse()
        {
            var (hcvm, _) = CreateVm();
            Assert.IsFalse(hcvm.UsePsSpringAtPileTip, "新規 InputModel で P-S 非線形ばねは既定 OFF (杭先端支持モード)");
        }

        [TestMethod]
        public void UsePsSpringAtPileTip_TogglingOn_ForcesUseAnalysisAxialForce()
        {
            // P-S 非線形ばね ON 時は M-φ N 評価のため UseAnalysisAxialForce 強制 ON
            var (hcvm, _) = CreateVm();
            hcvm.UseAnalysisAxialForce = false;
            hcvm.UsePsSpringAtPileTip = true;
            Assert.IsTrue(hcvm.UseAnalysisAxialForce,
                "P-S ON 時は UseAnalysisAxialForce が自動 ON されるべき");
        }

        [TestMethod]
        public void UsePsSpringAtPileTip_TogglingOn_DisablesInputOnlyAxialMode()
        {
            // P-S ON 時はラジオ 「入力値」 が選択不可になる
            var (hcvm, _) = CreateVm();
            hcvm.UsePsSpringAtPileTip = true;
            Assert.IsFalse(hcvm.CanSelectInputOnlyAxialMode,
                "P-S ON 時は「入力値」軸力モードを選択不可とすべき");
        }

        [TestMethod]
        public void TotalCalculationCount_NoVL_OnlySeismic()
        {
            // P-S OFF または VL OFF: 通常の地震時ケース合計のみ
            var (hcvm, _) = CreateVm();
            int baseCount = hcvm.TotalCalculationCount;
            Assert.IsTrue(baseCount > 0, $"地震時ケースは存在するはず: actual={baseCount}");

            // VL オプション ON だが P-S OFF → カウントは増えない
            hcvm.IsVLAnalysisEnabled = true;
            Assert.AreEqual(baseCount, hcvm.TotalCalculationCount,
                "P-S OFF の状態で VL オプションだけ ON にしてもカウント不変");
        }

        [TestMethod]
        public void TotalCalculationCount_BothPsAndVL_AddsLevel1Steps()
        {
            // P-S ON + VL ON: VL 擬似ケース 1 ケース × 1 組合せ × 1 液状化 × Level1 ステップ数 分を加算
            var (hcvm, _) = CreateVm();
            int baseCount = hcvm.TotalCalculationCount;

            hcvm.UsePsSpringAtPileTip = true;
            hcvm.IsVLAnalysisEnabled = true;
            int withVL = hcvm.TotalCalculationCount;

            int expectedDelta = hcvm.Level1CalculationStepsCount;
            Assert.AreEqual(baseCount + expectedDelta, withVL,
                $"VL 加算分 = Level1Steps ({expectedDelta}) ステップ");
        }

        [TestMethod]
        public void TotalLoadCaseCount_VL_AddsOne()
        {
            // VL 擬似ケース 1 件分を TotalLoadCaseCount に加算 (= 1 ケース × 1 組合せ × 1 液状化)
            var (hcvm, _) = CreateVm();
            int baseCount = hcvm.TotalLoadCaseCount;

            hcvm.UsePsSpringAtPileTip = true;
            hcvm.IsVLAnalysisEnabled = true;

            Assert.AreEqual(baseCount + 1, hcvm.TotalLoadCaseCount,
                "VL は 1 load case (1 組合せ × 液状化 false 固定) 分を加算");
        }

        [TestMethod]
        public void IsVLAnalysisEnabled_Toggling_NotifiesTotalCounts()
        {
            // setter で PropertyChanged が発火し、UI 表示が更新される
            var (hcvm, _) = CreateVm();
            hcvm.UsePsSpringAtPileTip = true;

            var changedProperties = new System.Collections.Generic.List<string>();
            hcvm.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProperties.Add(e.PropertyName); };

            hcvm.IsVLAnalysisEnabled = true;

            CollectionAssert.Contains(changedProperties, nameof(hcvm.TotalCalculationCount),
                "IsVLAnalysisEnabled 切替時に TotalCalculationCount の更新通知が発火すべき");
            CollectionAssert.Contains(changedProperties, nameof(hcvm.TotalLoadCaseCount),
                "IsVLAnalysisEnabled 切替時に TotalLoadCaseCount の更新通知が発火すべき");
        }
    }
}
