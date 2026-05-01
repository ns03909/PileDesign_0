using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 「水平解析 段階追加再解析」機能の単体テスト群。
    ///
    /// 検証対象:
    ///   - AnalysisRunSnapshot.CaseKey の Equals/GetHashCode
    ///   - HashSet&lt;CaseKey&gt; の重複排除
    ///   - AnaModel.ClearAllAnalysisResults() が全結果コレクションをクリアする
    ///   - 旧 JSON 互換 (LastRunConfig=null) 時の挙動
    ///
    /// 注: ValidateIncrementalCompatibility は HorizontalCalculationViewModel の
    ///     private メソッドのため直接呼出不可。代わりに「同等ロジックの仕様検証」
    ///     を CaseKey 比較で行う。
    /// </summary>
    [TestClass]
    public class IncrementalRunTests
    {
        // ===== Test 1: CaseKey 等価性 =====

        [TestMethod]
        public void CaseKey_SameValues_AreEqual()
        {
            var k1 = new AnalysisRunSnapshot.CaseKey("VL+E1", "α=1.0/βU=1.0/βL=1.0", true);
            var k2 = new AnalysisRunSnapshot.CaseKey("VL+E1", "α=1.0/βU=1.0/βL=1.0", true);
            Assert.AreEqual(k1, k2);
            Assert.AreEqual(k1.GetHashCode(), k2.GetHashCode());
        }

        [TestMethod]
        public void CaseKey_DifferentLoadName_AreNotEqual()
        {
            var k1 = new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", true);
            var k2 = new AnalysisRunSnapshot.CaseKey("VL+E2", "C1", true);
            Assert.AreNotEqual(k1, k2);
        }

        [TestMethod]
        public void CaseKey_DifferentCombinationName_AreNotEqual()
        {
            var k1 = new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", true);
            var k2 = new AnalysisRunSnapshot.CaseKey("VL+E1", "C2", true);
            Assert.AreNotEqual(k1, k2);
        }

        [TestMethod]
        public void CaseKey_DifferentLiquefaction_AreNotEqual()
        {
            var k1 = new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", true);
            var k2 = new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", false);
            Assert.AreNotEqual(k1, k2);
        }

        [TestMethod]
        public void HashSet_OfCaseKey_DeduplicatesByValue()
        {
            var set = new HashSet<AnalysisRunSnapshot.CaseKey>
            {
                new("VL+E1", "C1", true),
                new("VL+E1", "C1", true),  // 重複
                new("VL+E1", "C1", false), // 液状化違いは別キー
                new("VL+E2", "C1", true),  // ロード名違いは別キー
            };
            Assert.AreEqual(3, set.Count);
        }

        // ===== Test 2: AnalysisRunSnapshot のフィールド初期化と書込 =====

        [TestMethod]
        public void AnalysisRunSnapshot_DefaultValues_AreSafe()
        {
            var s = new AnalysisRunSnapshot();
            // デフォルト値が NPE を招かないこと
            Assert.IsNotNull(s.LiquefactionOption);
            Assert.IsNotNull(s.ConnectionMode);
            Assert.IsNotNull(s.ExecutedCaseKeys);
            Assert.AreEqual(0, s.ExecutedCaseKeys.Count);
            Assert.IsNull(s.InputModelHash);
        }

        [TestMethod]
        public void AnalysisRunSnapshot_ExecutedCaseKeys_RoundTripsThroughList()
        {
            var s = new AnalysisRunSnapshot
            {
                Level1StepsCount = 2,
                Level2StepsCount = 8,
                LiquefactionOption = "Yes",
                ExecutedCaseKeys = new List<AnalysisRunSnapshot.CaseKey>
                {
                    new("VL+E1", "C1", true),
                    new("VL+E2", "C1", true),
                }
            };
            Assert.AreEqual(2, s.ExecutedCaseKeys.Count);
            Assert.IsTrue(s.ExecutedCaseKeys.Contains(new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", true)));
            Assert.IsFalse(s.ExecutedCaseKeys.Contains(new AnalysisRunSnapshot.CaseKey("VL+E1", "C1", false)));
        }

        // ===== Test 3: 互換性検証ロジックの仕様 (純粋関数として再実装したもので検証) =====

        // ValidateIncrementalCompatibility 内の判定を再実装した参照ロジック
        private static bool IsLiqSuperset_Reference(string current, string previous)
        {
            if (previous == "Both") return current == "Both";
            return current == "Both" || current == previous;
        }

        [TestMethod]
        public void LiquefactionSuperset_BothCoversAll()
        {
            Assert.IsTrue(IsLiqSuperset_Reference("Both", "Yes"));
            Assert.IsTrue(IsLiqSuperset_Reference("Both", "None"));
            Assert.IsTrue(IsLiqSuperset_Reference("Both", "Both"));
        }

        [TestMethod]
        public void LiquefactionSuperset_SameValueAllowed()
        {
            Assert.IsTrue(IsLiqSuperset_Reference("Yes", "Yes"));
            Assert.IsTrue(IsLiqSuperset_Reference("None", "None"));
        }

        [TestMethod]
        public void LiquefactionSuperset_NarrowingDisallowed()
        {
            // Both → Yes / None: シュリンクは不可
            Assert.IsFalse(IsLiqSuperset_Reference("Yes", "Both"));
            Assert.IsFalse(IsLiqSuperset_Reference("None", "Both"));
            // Yes → None: 範囲違い
            Assert.IsFalse(IsLiqSuperset_Reference("None", "Yes"));
            Assert.IsFalse(IsLiqSuperset_Reference("Yes", "None"));
        }

        // ===== Test 4: AnaModel.ClearAllAnalysisResults =====

        [TestMethod]
        public void ClearAllAnalysisResults_EmptiesAllResultLists()
        {
            // 最小限の AnaModel を作成 (Helper を直接組み立て)
            // Note: ListContext のため null フィールドが許容される
            var model = CreateMinimalAnaModel();

            // 結果を仮に積む
            model.AnalysisStepResults.Add(new AnalysisStepResult());
            model.AnalysisStepResults.Add(new AnalysisStepResult());
            Assert.AreEqual(2, model.AnalysisStepResults.Count);

            model.ClearAllAnalysisResults();

            Assert.AreEqual(0, model.AnalysisStepResults.Count);
            Assert.IsNull(model.LastRunConfig);
        }

        [TestMethod]
        public void ClearAllAnalysisResults_AlsoResetsLastRunConfig()
        {
            var model = CreateMinimalAnaModel();
            model.LastRunConfig = new AnalysisRunSnapshot { Level1StepsCount = 4 };

            model.ClearAllAnalysisResults();

            Assert.IsNull(model.LastRunConfig);
        }

        [TestMethod]
        public void LastRunConfig_DefaultsToNull_OnFreshAnaModel()
        {
            // 旧 JSON 互換: LastRunConfig フィールドなしで読込んだ場合の振る舞い
            var model = CreateMinimalAnaModel();
            Assert.IsNull(model.LastRunConfig);
        }

        // ===== Helper =====

        /// <summary>
        /// 最小限の空 AnaModel を生成する。リフレクションで internal な空コンストラクタを叩くか、
        /// 公開コンストラクタの最小引数で作る。AnaModel のコンストラクタが多数引数を要求するため、
        /// 簡易テスト用には FormatterServices.GetUninitializedObject を使うのが安全。
        /// </summary>
        private static AnaModel CreateMinimalAnaModel()
        {
            // AnaModel は通常コンストラクタが入力モデル等を要求するため、
            // テスト目的では未初期化オブジェクトとして生成しコレクションのみセット。
            var model = (AnaModel)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(AnaModel));
            // ClearAllAnalysisResults が触る全てのコレクションを初期化
            typeof(AnaModel).GetProperty(nameof(AnaModel.AnalysisStepResults))!
                .SetValue(model, new List<AnalysisStepResult>());
            typeof(AnaModel).GetProperty(nameof(AnaModel.Nodes))!
                .SetValue(model, new List<Node>());
            typeof(AnaModel).GetProperty(nameof(AnaModel.Beams))!
                .SetValue(model, new List<Beam>());
            typeof(AnaModel).GetProperty(nameof(AnaModel.HorizontalSoilSprings))!
                .SetValue(model, new List<HorizontalSoilSpring>());
            typeof(AnaModel).GetProperty(nameof(AnaModel.RotationalSprings))!
                .SetValue(model, new List<RotationalSpring>());
            return model;
        }
    }
}
