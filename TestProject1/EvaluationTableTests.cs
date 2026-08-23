using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 検定結果を解析結果テーブルに並べるための仕掛け。
    ///
    /// 結果を確認しに来た人が最初に開くのは解析結果テーブルなので、
    /// 検定比もそこから見られる必要がある
    /// (検定ウィンドウを開いてボタンを押さないと分からない状態だった)。
    ///
    /// 検定の表は荷重ケース・組合せ・液状化を<b>またぐ</b> 1 枚もの。
    /// 支配ケースを探すには全条件を検定比の降順で並べたものが要るため。
    /// </summary>
    [TestClass]
    public class EvaluationTableTests
    {
        private static ResultTable EvaluationTable() => new()
        {
            Name = "検定結果（低減前水平解析）",
            Category = "検定",
            SpansAllConditions = true,
        };

        private static ResultTable NormalTable() => new()
        {
            Name = "梁断面力",
            LoadCaseName = "L2-1",
            LoadCombinationName = "cmb1",
            IsLiquefaction = true,
        };

        /// <summary>
        /// 全条件をまたぐ表の名前に、液状化の有無を出さないこと。
        /// 「[無] 検定結果」は「液状化を考慮しないケースの検定」と読めてしまう。
        /// </summary>
        [TestMethod]
        public void SpanningTable_DoesNotClaimALiquefactionState()
        {
            Assert.AreEqual("検定結果（低減前水平解析）", EvaluationTable().DisplayName);

            // 通常の表は従来どおり条件を名前に出す
            StringAssert.Contains(NormalTable().DisplayName, "[有]");
            StringAssert.Contains(NormalTable().DisplayName, "L2-1");
        }

        /// <summary>検定の表は独自のカテゴリを持ち、種別フィルタで選べること。</summary>
        [TestMethod]
        public void EvaluationTable_HasItsOwnCategory()
        {
            Assert.AreEqual("検定結果", TableCategoryConverter.CategoryOf(EvaluationTable()));
            Assert.AreEqual("水平解析結果", TableCategoryConverter.CategoryOf(NormalTable()));
        }

        /// <summary>
        /// 荷重条件で絞り込んでも、全条件をまたぐ表は残ること。
        /// 落としてしまうと「液状化有で絞ったら検定が消えた」になる。
        /// </summary>
        [TestMethod]
        public void SpanningTable_SurvivesConditionFilters()
        {
            var vm = new TableWindowViewModel();
            vm.LoadTables([EvaluationTable(), NormalTable()]);

            Assert.AreEqual(2, vm.FilteredTables.Count, "絞り込み前は 2 枚");

            vm.SelectedLiquefactionFilter = "無";   // 通常表 (液状化有) は落ちる
            Assert.IsTrue(vm.FilteredTables.Any(t => t.SpansAllConditions),
                "液状化で絞ったら検定の表が消えた");
            Assert.IsFalse(vm.FilteredTables.Any(t => !t.SpansAllConditions),
                "条件の合わない通常表が残っている");

            vm.SelectedLiquefactionFilter = PileDesign.Common.UiText.All;
            vm.SelectedLoadCaseFilter = "存在しないケース";
            Assert.IsTrue(vm.FilteredTables.Any(t => t.SpansAllConditions),
                "荷重ケースで絞ったら検定の表が消えた");
        }

        /// <summary>
        /// 全条件をまたぐ表では、荷重条件のメタ列 (荷重条件 / 荷重組合せ / 液状化) を出さないこと。
        ///
        /// 表そのものは荷重条件を持たないため、出すと既定値の「液状化 = 無」が並び、
        /// <b>液状化を考慮しないケースの結果があるように読めてしまう</b>。
        /// 実際に「液状化なしの検討はしていないのに結果が出ている」と受け取られた。
        /// 画面 (TableWindow.RebuildColumns) と CSV 出力の両方で外す必要がある。
        /// </summary>
        [TestMethod]
        public void SpanningTable_DoesNotGetConditionMetaColumns()
        {
            var spanning = EvaluationTable();
            var normal = NormalTable();

            // 表そのものは荷重条件を持たない = 既定値のまま
            Assert.AreEqual("", spanning.LoadCaseName);
            Assert.AreEqual("", spanning.LoadCombinationName);
            Assert.IsFalse(spanning.IsLiquefaction);
            Assert.AreEqual("無", spanning.LiquefactionLabel,
                "既定値は「無」。これをそのまま列に出すと誤解される");

            // メタ列を出すかどうかはこのフラグだけで決まる
            Assert.IsTrue(spanning.SpansAllConditions, "検定の表はまたぐ表");
            Assert.IsFalse(normal.SpansAllConditions, "通常の表は 1 条件に対応する");

            // 条件は行ごとの列で分かる (メタ列が無くても情報は失われない)
            var headers = ResultColumnReflectionCache.GetColumns(typeof(EvaluationItem))
                .Select(c => c.Header).ToList();
            CollectionAssert.Contains(headers, "荷重ケース");
            CollectionAssert.Contains(headers, "荷重組合せ");
            CollectionAssert.Contains(headers, "液状化");
        }

        /// <summary>
        /// 検定の列がテーブル用に定義されていること。
        /// 検定比が先頭に来ないと、並べ替えずに支配ケースを見つけられない。
        /// </summary>
        [TestMethod]
        public void EvaluationColumns_StartWithTheRatio()
        {
            var columns = ResultColumnReflectionCache.GetColumns(typeof(EvaluationItem));

            Assert.IsTrue(columns.Length >= 8, $"検定の列が {columns.Length} 件しかない");
            Assert.AreEqual("検定比", columns[0].Header, "検定比が先頭でない");
            Assert.AreEqual("判定", columns[1].Header);

            var headers = columns.Select(c => c.Header).ToList();
            CollectionAssert.Contains(headers, "対象");
            CollectionAssert.Contains(headers, "応答値");
            CollectionAssert.Contains(headers, "限界値");

            // 説明はすべての列に要る (ResultColumnTooltipTests と同じ約束)
            Assert.IsFalse(columns.Any(c => string.IsNullOrWhiteSpace(c.Tooltip)),
                "説明の無い列がある");
        }
    }
}
