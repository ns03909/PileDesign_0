using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// LoadCase / LoadCombination / LoadCasesInput の派生コレクション (AllLoadCases /
    /// AnalysisTargetSeismicLoadCases / AllLoadCombinations の filter / count 挙動を検証。
    ///
    /// memo project_test_gaps.md item 5 のカバー追加。
    /// </summary>
    [TestClass]
    public class LoadCasesInputExtraTests
    {
        // ============ LoadCombination ============

        [TestMethod]
        public void LoadCombination_DefaultIsApplicable_True()
        {
            var lc = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: 1.0);
            Assert.IsTrue(lc.IsApplicable, "新規 LoadCombination は既定で IsApplicable=true");
        }

        [TestMethod]
        public void LoadCombination_PropertyAssignment_Notifies()
        {
            var lc = new LoadCombination(1, 1.0, 1.0, 1.0);
            var changed = new System.Collections.Generic.List<string>();
            lc.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            lc.IsApplicable = false;
            lc.Alpha1 = 0.5;
            lc.Beta1 = -1.0;
            lc.Beta2 = 0.0;

            CollectionAssert.Contains(changed, nameof(lc.IsApplicable));
            CollectionAssert.Contains(changed, nameof(lc.Alpha1));
            CollectionAssert.Contains(changed, nameof(lc.Beta1));
            CollectionAssert.Contains(changed, nameof(lc.Beta2));
        }

        // ============ LoadCase ============

        [TestMethod]
        public void LoadCase_DefaultIsAnalysisTarget_False()
        {
            // 新規 LoadCase は IsAnalysisTarget の bool 既定値 false で生成される
            var lc = new LoadCase();
            Assert.IsFalse(lc.IsAnalysisTarget, "新規 LoadCase の既定 IsAnalysisTarget は false");
        }

        [TestMethod]
        public void LoadCase_IsAnalysisTargetChange_FiresNotification()
        {
            // false → true へ変更 → 通知発火
            var lc = new LoadCase();
            var changed = new System.Collections.Generic.List<string>();
            lc.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

            lc.IsAnalysisTarget = true;
            CollectionAssert.Contains(changed, nameof(lc.IsAnalysisTarget));
        }

        // ============ LoadCasesInput - AllLoadCases ============

        [TestMethod]
        public void AllLoadCases_CombinesApplicableLevel1AndLevel2()
        {
            // AllLoadCases は IsApplicable=true の LoadCasesLevel1/2 のみ集約
            var input = new LoadCasesInput();
            var l1a = new LoadCase { No = 1, Level = 1, UpperMassForce = 100, IsApplicable = true };
            var l1b = new LoadCase { No = 2, Level = 1, UpperMassForce = 200, IsApplicable = true };
            var l2 = new LoadCase { No = 1, Level = 2, UpperMassForce = 1000, IsApplicable = true };
            input.LoadCasesLevel1 = [l1a, l1b];
            input.LoadCasesLevel2 = [l2];

            var all = input.AllLoadCases;
            Assert.AreEqual(3, all.Count, "AllLoadCases は IsApplicable=true の L1 + L2 合計");
        }

        [TestMethod]
        public void AllLoadCases_ExcludesNotApplicable()
        {
            var input = new LoadCasesInput();
            var l1a = new LoadCase { No = 1, Level = 1, UpperMassForce = 100, IsApplicable = true };
            var l1b = new LoadCase { No = 2, Level = 1, UpperMassForce = 200, IsApplicable = false }; // 除外
            input.LoadCasesLevel1 = [l1a, l1b];

            Assert.AreEqual(1, input.AllLoadCases.Count,
                "IsApplicable=false のケースは AllLoadCases に含めない");
        }

        [TestMethod]
        public void AnalysisTargetSeismicLoadCases_FiltersOutNonTargets()
        {
            var input = new LoadCasesInput();
            var lc1 = new LoadCase { No = 1, Level = 1, UpperMassForce = 100, IsAnalysisTarget = true };
            var lc2 = new LoadCase { No = 2, Level = 1, UpperMassForce = 200, IsAnalysisTarget = false };
            var lc3 = new LoadCase { No = 1, Level = 2, UpperMassForce = 1000, IsAnalysisTarget = true };
            input.LoadCasesLevel1 = [lc1, lc2];
            input.LoadCasesLevel2 = [lc3];

            var targets = input.AnalysisTargetSeismicLoadCases;
            Assert.AreEqual(2, targets.Count, "IsAnalysisTarget=false のケースは除外");
            CollectionAssert.Contains(targets, lc1);
            CollectionAssert.Contains(targets, lc3);
        }

        // ============ LoadCasesInput - LoadCombinations ============

        [TestMethod]
        public void AllLoadCombinations_DefaultEmpty()
        {
            // パラメータレス LoadCasesInput() は初期化を行わない (SetMainWindowViewModel が空 collection を埋める)
            var input = new LoadCasesInput();
            Assert.AreEqual(0, input.AllLoadCombinations.Count,
                "パラメータレス new LoadCasesInput() の AllLoadCombinations は空");
        }

        [TestMethod]
        public void AllLoadCombinations_OnlyReturnsApplicable()
        {
            // AllLoadCombinations getter は LoadCombinations を IsApplicable=true でフィルタ
            var input = new LoadCasesInput();
            input.LoadCombinations = new ObservableCollection<LoadCombination>
            {
                new(1, 1.0, 1.0, 1.0) { IsApplicable = true },
                new(2, 1.0, 0.5, 1.0) { IsApplicable = false }, // 除外
                new(3, 1.0, 1.0, 0.5) { IsApplicable = true },
            };
            Assert.AreEqual(2, input.AllLoadCombinations.Count);
            Assert.IsTrue(input.AllLoadCombinations.All(c => c.IsApplicable));
        }

        // ============ 統合: 解析対象カウント計算 (TotalCalculationCount 相当) ============

        [TestMethod]
        public void IntegrationTest_AnalysisCount_FiltersZeroForceCases()
        {
            // 「荷重ゼロ」のケース (UpperMassForce==0 && FoundationMassForce==0) はスキップされる
            var input = new LoadCasesInput();
            var lc1 = new LoadCase { No = 1, Level = 1, UpperMassForce = 100, FoundationMassForce = 50, IsAnalysisTarget = true };
            var lc2 = new LoadCase { No = 2, Level = 1, UpperMassForce = 0, FoundationMassForce = 0, IsAnalysisTarget = true };
            input.LoadCasesLevel1 = [lc1, lc2];

            int nonZeroAndTargeted = input.LoadCasesLevel1.Count(c =>
                c.IsAnalysisTarget && (c.UpperMassForce != 0 || c.FoundationMassForce != 0));
            Assert.AreEqual(1, nonZeroAndTargeted,
                "「IsAnalysisTarget かつ非ゼロ荷重」のフィルタで lc1 のみ残るはず");
        }
    }
}
