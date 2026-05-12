using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;

using static PileDesign.ViewModels.HorizontalCalculationViewModel;

namespace TestProject1
{
    /// <summary>
    /// HorizontalCalculationViewModel の収束判定/ステップ進捗ブックキーピング層のテスト。
    /// 反復ループ本体 (OnExecuteAnalysisCore) は AnaModel/UI 状態に深く依存し直接ユニット
    /// テスト不可だが、その周辺の純粋関数 (タグ生成 / 方向分類 / 液状化スーパーセット判定 /
    /// 視覚幅算出 / サマリレポート整形) は副作用なく検証できる。これらは v22-v29 の収束改善
    /// で繰り返し触られた領域なので、レグレッションガードとして価値が高い。
    /// </summary>
    [TestClass]
    public class HorizontalCalculationViewModelTests
    {
        // ========================================================================
        // BuildCaseTag: 並列ログ識別タグの形式
        //   形式: [L{level}-{iLC+1}.C{iLCOM+1}.{Liq|NoLq}] (末尾スペース付き)
        // ========================================================================

        [TestMethod]
        public void BuildCaseTag_Level1_NoLiquefaction_FormatsCorrectly()
        {
            string tag = BuildCaseTag(level: 1, iLC: 0, iLCOM: 0, isLiquefaction: false);
            Assert.AreEqual("[L1-1.C1.NoLq] ", tag);
        }

        [TestMethod]
        public void BuildCaseTag_Level2_WithLiquefaction_FormatsCorrectly()
        {
            string tag = BuildCaseTag(level: 2, iLC: 0, iLCOM: 3, isLiquefaction: true);
            Assert.AreEqual("[L2-1.C4.Liq] ", tag);
        }

        [TestMethod]
        public void BuildCaseTag_HigherIndices_OffsetsByOne()
        {
            // iLC=4, iLCOM=7 → "L*-5.C8" (+1 オフセットで人間可読化)
            string tag = BuildCaseTag(level: 2, iLC: 4, iLCOM: 7, isLiquefaction: false);
            Assert.AreEqual("[L2-5.C8.NoLq] ", tag);
        }

        [TestMethod]
        public void BuildCaseTag_EndsWithTrailingSpace()
        {
            // 後段のログ連結 (`{caseTag}message`) で空白が要るため、末尾は必ず ' '
            string tag = BuildCaseTag(level: 1, iLC: 2, iLCOM: 1, isLiquefaction: true);
            Assert.IsTrue(tag.EndsWith(" "), $"caseTag must end with space; got '{tag}'");
        }

        // ========================================================================
        // ClassifyLoadCombinationDirection: βU × βL の符号で順方向/逆方向を分類
        //   Forward       : βU * βL >= 0
        //   CounterLoading: βU * βL <  0  (S字曲げ — 収束しにくい組合せ)
        // ========================================================================

        [TestMethod]
        public void ClassifyLoadCombinationDirection_BothPositive_ReturnsForward()
        {
            var lc = new LoadCase();
            var combo = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: 1.0);
            Assert.AreEqual(LoadCombinationDirection.Forward,
                ClassifyLoadCombinationDirection(lc, combo, false));
        }

        [TestMethod]
        public void ClassifyLoadCombinationDirection_BothNegative_ReturnsForward()
        {
            // 符号同じ (両方マイナス) → 積は正 → Forward
            var lc = new LoadCase();
            var combo = new LoadCombination(no: 1, alpha1: 1.0, beta1: -1.0, beta2: -1.0);
            Assert.AreEqual(LoadCombinationDirection.Forward,
                ClassifyLoadCombinationDirection(lc, combo, false));
        }

        [TestMethod]
        public void ClassifyLoadCombinationDirection_OppositeSigns_ReturnsCounterLoading()
        {
            // βU × βL < 0 → CounterLoading (S字曲げで収束しにくい)
            var lc = new LoadCase();
            var combo = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: -1.0);
            Assert.AreEqual(LoadCombinationDirection.CounterLoading,
                ClassifyLoadCombinationDirection(lc, combo, false));
        }

        [TestMethod]
        public void ClassifyLoadCombinationDirection_BetaZero_ReturnsForward()
        {
            // 積 = 0 → Forward (>= 0 として扱う)
            var lc = new LoadCase();
            var combo = new LoadCombination(no: 1, alpha1: 1.0, beta1: 0.0, beta2: 1.0);
            Assert.AreEqual(LoadCombinationDirection.Forward,
                ClassifyLoadCombinationDirection(lc, combo, false));
        }

        [TestMethod]
        public void ClassifyLoadCombinationDirection_LiqFlagDoesNotAffect()
        {
            // 現状 isLiq は分類に未使用 (v28 で液状化静的分類は廃止済) — 不変条件として固定化
            var lc = new LoadCase();
            var combo = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: -0.5);
            Assert.AreEqual(
                ClassifyLoadCombinationDirection(lc, combo, false),
                ClassifyLoadCombinationDirection(lc, combo, true));
        }

        // ========================================================================
        // IsLiqSuperset: 追加実行時、現在の液状化選択が前回をカバーするか
        //   Both は Yes/None を内包、それ以外は完全一致のみ可
        // ========================================================================

        [TestMethod]
        public void IsLiqSuperset_BothCoversBoth_True()
        {
            Assert.IsTrue(IsLiqSuperset(LiquefactionOptionType.Both, "Both"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevBoth_CurrentYes_False()
        {
            // 前回 Both (Yes と None 両方実行済) → 現在 Yes だけでは None 分が不足
            Assert.IsFalse(IsLiqSuperset(LiquefactionOptionType.Yes, "Both"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevBoth_CurrentNone_False()
        {
            Assert.IsFalse(IsLiqSuperset(LiquefactionOptionType.None, "Both"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevYes_CurrentYes_True()
        {
            Assert.IsTrue(IsLiqSuperset(LiquefactionOptionType.Yes, "Yes"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevYes_CurrentBoth_True()
        {
            // 前回 Yes → 現在 Both は前回を完全カバー (Yes ⊂ Both)
            Assert.IsTrue(IsLiqSuperset(LiquefactionOptionType.Both, "Yes"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevYes_CurrentNone_False()
        {
            // Yes 結果はあるが None は未実施 → スーパーセットではない
            Assert.IsFalse(IsLiqSuperset(LiquefactionOptionType.None, "Yes"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevNone_CurrentNone_True()
        {
            Assert.IsTrue(IsLiqSuperset(LiquefactionOptionType.None, "None"));
        }

        [TestMethod]
        public void IsLiqSuperset_PrevNone_CurrentBoth_True()
        {
            Assert.IsTrue(IsLiqSuperset(LiquefactionOptionType.Both, "None"));
        }

        // ========================================================================
        // VisualWidth: MS Gothic 等幅フォント上での視覚 col 数
        //   ASCII = 1, CJK / 全角 / Greek / Box-drawing / ✓✗ = 2
        // ========================================================================

        [TestMethod]
        public void VisualWidth_Empty_ReturnsZero()
        {
            Assert.AreEqual(0, VisualWidth(""));
        }

        [TestMethod]
        public void VisualWidth_AsciiOnly_OneColPerChar()
        {
            Assert.AreEqual(5, VisualWidth("Hello"));
            Assert.AreEqual(12, VisualWidth("Step 1/16 OK"));  // 12 chars
        }

        [TestMethod]
        public void VisualWidth_AsciiSpecials_OneCol()
        {
            // '|', '-', '+', '=' は ASCII (< 0x80) → 1 col
            Assert.AreEqual(1, VisualWidth("|"));
            Assert.AreEqual(1, VisualWidth("-"));
            Assert.AreEqual(1, VisualWidth("+"));
        }

        [TestMethod]
        public void VisualWidth_GreekLetters_TwoColsPerChar()
        {
            // α (U+03B1) β (U+03B2) → MS Gothic で全角 2 col
            Assert.AreEqual(2, VisualWidth("α"));
            Assert.AreEqual(4, VisualWidth("αβ"));
        }

        [TestMethod]
        public void VisualWidth_CheckMarkArrows_TwoCols()
        {
            // ✓ ✗ ▶ などサマリで使う記号は 2 col
            Assert.AreEqual(2, VisualWidth("✓"));
            Assert.AreEqual(2, VisualWidth("✗"));
            Assert.AreEqual(2, VisualWidth("▶"));
        }

        [TestMethod]
        public void VisualWidth_BoxDrawing_TwoCols()
        {
            // U+2500-257F 範囲 (─ ━ │ ┃ ┣ ┳ etc) → 2 col
            Assert.AreEqual(2, VisualWidth("━"));
            Assert.AreEqual(2, VisualWidth("│"));
            Assert.AreEqual(2, VisualWidth("┃"));
        }

        [TestMethod]
        public void VisualWidth_CJK_TwoColsPerChar()
        {
            Assert.AreEqual(2, VisualWidth("収"));         // 漢字
            Assert.AreEqual(4, VisualWidth("収束"));        // 2 chars
            Assert.AreEqual(2, VisualWidth("あ"));          // ひらがな
            Assert.AreEqual(2, VisualWidth("ア"));          // カタカナ
            Assert.AreEqual(2, VisualWidth("Ａ"));          // 全角英字 U+FF21
        }

        [TestMethod]
        public void VisualWidth_Mixed_SumsCorrectly()
        {
            // "OK 収束 ✓" = 2 + 1 + 4 + 1 + 2 = 10
            Assert.AreEqual(10, VisualWidth("OK 収束 ✓"));
        }

        // ========================================================================
        // VisualPad: 視覚幅ベースで列幅をパディング
        // ========================================================================

        [TestMethod]
        public void VisualPad_AsciiShorterThanTarget_PadsRight()
        {
            // "abc" (3 col) を 6 col 幅に左寄せパディング → "abc   "
            Assert.AreEqual("abc   ", VisualPad("abc", 6));
        }

        [TestMethod]
        public void VisualPad_AsciiRightAlign_PadsLeft()
        {
            // "abc" を 6 col 幅で右寄せ → "   abc"
            Assert.AreEqual("   abc", VisualPad("abc", 6, rightAlign: true));
        }

        [TestMethod]
        public void VisualPad_CJKContent_PadsByVisualWidth()
        {
            // "収束" は 4 col → 6 col 幅で左寄せは 2 個分のスペース
            Assert.AreEqual("収束  ", VisualPad("収束", 6));
        }

        [TestMethod]
        public void VisualPad_LongerThanTarget_NoTruncate()
        {
            // 入力が target を超える場合は切り詰めず原文をそのまま返す (パディング 0)
            Assert.AreEqual("abcdef", VisualPad("abcdef", 3));
        }

        [TestMethod]
        public void VisualPad_ExactWidth_NoPadding()
        {
            Assert.AreEqual("abc", VisualPad("abc", 3));
        }

        [TestMethod]
        public void VisualPad_EmptyString_FillsWithSpaces()
        {
            Assert.AreEqual("    ", VisualPad("", 4));
        }

        // ========================================================================
        // BuildStepSummaryReportText: 解析サマリレポート整形 (Add_LegacyOutputStepSummary 経由)
        //   - 集計行 (収束/未収束/物理的未収束/再試行カウント)
        //   - 表形式の各ステップ詳細
        //   - 末尾に未収束ステップ再掲セクション
        // ========================================================================

        private static StepSummary MakeStep(
            string caseTag = "[L1-1.C1.NoLq]",
            int level = 1, int loadCaseNo = 1, int comboNo = 1, bool isLiq = false,
            int step = 1, int nStep = 4, int bisectionAttempt = 0,
            int iterations = 5, double residual = 1e-6, double alpha = 1e-3,
            double maxDisp = 1e-4,
            StepStatus status = StepStatus.Converged, double elapsed = 0.5,
            int kRebuild = 5, int kReuse = 0)
            => new StepSummary(caseTag, level, loadCaseNo, comboNo, isLiq,
                               step, nStep, bisectionAttempt, iterations,
                               residual, alpha, maxDisp, status, elapsed,
                               kRebuild, kReuse);

        [TestMethod]
        public void BuildStepSummaryReportText_EmptySnapshot_StillProducesSkeleton()
        {
            // 空でも header/footer の罫線は出る
            string text = BuildStepSummaryReportText(Array.Empty<StepSummary>());
            StringAssert.Contains(text, "解析サマリーレポート");
            StringAssert.Contains(text, "ステップ総数 0");
            StringAssert.Contains(text, "✓ 収束 0 件");
        }

        [TestMethod]
        public void BuildStepSummaryReportText_AllConverged_ShowsConvergedCountOnly()
        {
            var snap = new[] { MakeStep(step: 1), MakeStep(step: 2), MakeStep(step: 3) };
            string text = BuildStepSummaryReportText(snap);

            StringAssert.Contains(text, "ステップ総数 3");
            StringAssert.Contains(text, "✓ 収束 3 件");
            // 失敗 0 件なので未収束/物理的未収束/再試行のラベルは出ない
            Assert.IsFalse(text.Contains("✗ 未収束"));
            Assert.IsFalse(text.Contains("⛔ 物理的未収束"));
            Assert.IsFalse(text.Contains("♻ 再試行発生"));
        }

        [TestMethod]
        public void BuildStepSummaryReportText_WithUnconverged_ListsInFailureSection()
        {
            var snap = new[]
            {
                MakeStep(step: 1, status: StepStatus.Converged),
                MakeStep(step: 2, status: StepStatus.Unconverged, residual: 0.05),
                MakeStep(step: 3, status: StepStatus.PhysicallyUnconverged, residual: 0.5),
            };
            string text = BuildStepSummaryReportText(snap);

            StringAssert.Contains(text, "✓ 収束 1 件");
            StringAssert.Contains(text, "✗ 未収束 (反復上限到達) 1 件");
            StringAssert.Contains(text, "⛔ 物理的未収束 (耐力超過の可能性) 1 件");

            // 未収束再掲セクション
            StringAssert.Contains(text, "未収束 / 物理的未収束のステップ");
            StringAssert.Contains(text, "✗ 未収束");
            StringAssert.Contains(text, "⛔ 物理的未収束");
        }

        [TestMethod]
        public void BuildStepSummaryReportText_WithRetries_ShowsRetryCount()
        {
            var snap = new[]
            {
                MakeStep(step: 1, bisectionAttempt: 0),
                MakeStep(step: 2, bisectionAttempt: 1),  // retry
                MakeStep(step: 3, bisectionAttempt: 2),  // retry
            };
            string text = BuildStepSummaryReportText(snap);

            StringAssert.Contains(text, "♻ 再試行発生 2 件");
            // retry tag #1, #2 が表内に出る
            StringAssert.Contains(text, "#1");
            StringAssert.Contains(text, "#2");
        }

        [TestMethod]
        public void BuildStepSummaryReportText_SortByLevelCaseComboLiqStep()
        {
            // ソート順: Level → LoadCaseNo → ComboNo → IsLiq(false→true) → Step → BisectionAttempt
            // 入力をシャッフルしても出力順は決定的
            var snap = new[]
            {
                MakeStep(caseTag: "L2-2.C1.NoLq", level: 2, loadCaseNo: 2, comboNo: 1, step: 1),
                MakeStep(caseTag: "L1-1.C2.NoLq", level: 1, loadCaseNo: 1, comboNo: 2, step: 1),
                MakeStep(caseTag: "L1-1.C1.NoLq", level: 1, loadCaseNo: 1, comboNo: 1, step: 1),
            };
            string text = BuildStepSummaryReportText(snap);

            int p1 = text.IndexOf("L1-1.C1.NoLq");
            int p2 = text.IndexOf("L1-1.C2.NoLq");
            int p3 = text.IndexOf("L2-2.C1.NoLq");
            Assert.IsTrue(p1 > 0 && p2 > p1 && p3 > p2,
                $"Expected sorted order; got positions {p1}, {p2}, {p3}");
        }

        [TestMethod]
        public void BuildStepSummaryReportText_TotalElapsedSummed()
        {
            var snap = new[]
            {
                MakeStep(step: 1, elapsed: 1.5),
                MakeStep(step: 2, elapsed: 2.3),
                MakeStep(step: 3, elapsed: 0.7),
            };
            string text = BuildStepSummaryReportText(snap);
            StringAssert.Contains(text, "合計時間 4.5s");
        }

        [TestMethod]
        public void BuildStepSummaryReportText_NRModeColumn_ShowsRebuildAndReuseCounts()
        {
            // KRebuildCount/KReuseCount は "rebuild/reuse" 形式で表示 (Modified NR 適用度の指標)
            var snap = new[]
            {
                MakeStep(step: 1, iterations: 7, kRebuild: 3, kReuse: 4),
            };
            string text = BuildStepSummaryReportText(snap);
            StringAssert.Contains(text, "3/4");
        }

        [TestMethod]
        public void Add_LegacyOutputStepSummary_EmitsLineByLine()
        {
            // Action<string> 経由で各行を受け取れる (CSV/log 等のシンクへの繋ぎ込み用)
            var snap = new[] { MakeStep(step: 1) };
            var captured = new List<string>();
            Add_LegacyOutputStepSummary(snap, captured.Add);

            Assert.IsTrue(captured.Count > 0, "Expected at least one emitted line");
            Assert.IsTrue(captured.Exists(l => l.Contains("解析サマリーレポート")),
                "Expected header line containing '解析サマリーレポート' to be emitted");
        }
    }
}
