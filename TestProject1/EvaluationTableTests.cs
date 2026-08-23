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

        private static EvaluationItem Item(string loadCase, bool? liquefaction) => new()
        {
            Kind = EvaluationKind.PileHeadRotation,
            LoadCaseName = loadCase,
            LoadCombinationName = "cmb1",
            IsLiquefaction = liquefaction,
            Response = 0.017,
            Limit = 0.010,
            IsOk = false,
        };

        private static ResultTable EvaluationTableWith(params EvaluationItem[] items) => new()
        {
            Name = "検定結果（低減前水平解析）",
            Category = "検定",
            SpansAllConditions = true,
            Columns = ResultColumnReflectionCache.GetColumns(typeof(EvaluationItem)),
            Rows = items,
        };

        /// <summary>
        /// またぐ表は、条件のフィルタで<b>行が絞られる</b>こと。
        ///
        /// 表ごと素通しにすると、解析していない条件で絞ったときにも表が残り、
        /// 「その条件の結果がある」と読めてしまう。
        /// 実際に「液状化なしの検討はしていないのに結果が出ている」と受け取られた。
        /// </summary>
        [TestMethod]
        public void SpanningTable_FiltersItsRowsByCondition()
        {
            var vm = new TableWindowViewModel();
            vm.LoadTables([EvaluationTableWith(Item("U1", true), Item("U1", true))]);

            Assert.AreEqual(1, vm.FilteredTables.Count, "絞り込み前は表が出る");
            Assert.AreEqual(2, vm.FilteredTables[0].Rows.Count);

            // 液状化「無」で絞る = 解析していない条件 → 表ごと消える
            vm.SelectedLiquefactionFilter = "無";
            Assert.AreEqual(0, vm.FilteredTables.Count,
                "解析していない条件で絞ったのに検定の表が残っている");

            // 液状化「有」なら出る
            vm.SelectedLiquefactionFilter = "有";
            Assert.AreEqual(1, vm.FilteredTables.Count);
            Assert.AreEqual(2, vm.FilteredTables[0].Rows.Count);
        }

        /// <summary>条件が混在する表では、合う行だけが残ること。</summary>
        [TestMethod]
        public void SpanningTable_KeepsOnlyTheMatchingRows()
        {
            var vm = new TableWindowViewModel();
            vm.LoadTables([EvaluationTableWith(
                Item("U1", true), Item("U2", true), Item("U1", false))]);

            vm.SelectedLoadCaseFilter = "U1";
            Assert.AreEqual(1, vm.FilteredTables.Count);
            Assert.AreEqual(2, vm.FilteredTables[0].Rows.Count, "U1 の 2 行だけが残る");

            vm.SelectedLiquefactionFilter = "有";
            Assert.AreEqual(1, vm.FilteredTables[0].Rows.Count, "U1 かつ 液状化有 の 1 行");
        }

        /// <summary>
        /// 液状化の概念が無い行 (基礎梁の傾斜角) は、液状化で絞ると有/無どちらでも出ないこと。
        /// 「無」に出すと液状化を考慮しない検討をしたように読める。
        /// </summary>
        [TestMethod]
        public void RowsWithoutLiquefactionConcept_AppearInNeither()
        {
            var vm = new TableWindowViewModel();
            vm.LoadTables([EvaluationTableWith(Item("反復ケース", null))]);

            Assert.AreEqual(1, vm.FilteredTables.Count, "絞り込み前は出る");

            vm.SelectedLiquefactionFilter = "無";
            Assert.AreEqual(0, vm.FilteredTables.Count, "液状化無に混ざっている");

            vm.SelectedLiquefactionFilter = "有";
            Assert.AreEqual(0, vm.FilteredTables.Count, "液状化有に混ざっている");
        }

        /// <summary>液状化の概念が無い検定では、ラベルを空にすること。</summary>
        [TestMethod]
        public void LiquefactionLabel_IsEmptyWhenNotApplicable()
        {
            Assert.AreEqual("液状化有", Item("U1", true).LiquefactionLabel);
            Assert.AreEqual("液状化無", Item("U1", false).LiquefactionLabel);
            Assert.AreEqual("", Item("反復ケース", null).LiquefactionLabel,
                "液状化の概念が無い検定に「液状化無」と出してはいけない");
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
        /// 応答値・限界値の桁数が量ごとに変わること。
        /// 列ごとに固定すると、モーメント (千の位) と回転角 (小数 3 桁) が
        /// 同じ表に並ぶため、どちらかが読めなくなる。
        /// </summary>
        [TestMethod]
        public void ValueDigits_DependOnTheQuantity()
        {
            var moment = new EvaluationItem
            {
                Kind = EvaluationKind.PileSectionMoment,
                Response = 1741.346, Limit = 1969.026,
            };
            Assert.AreEqual("1,741.3", moment.ResponseText, "モーメントは小数 1 桁");
            Assert.AreEqual("1,969.0", moment.LimitText);

            var rotation = new EvaluationItem
            {
                Kind = EvaluationKind.PileHeadRotation,
                Response = 0.016974, Limit = 0.01,
            };
            Assert.AreEqual("0.017", rotation.ResponseText, "回転角は小数 3 桁");
            Assert.AreEqual("0.010", rotation.LimitText);

            // 傾斜角は限界 1/300 = 0.00333。3 桁だと応答も限界も 0.003 に潰れる
            var inclination = new EvaluationItem
            {
                Kind = EvaluationKind.FoundationBeamInclination,
                Response = 0.0025, Limit = 1.0 / 300.0,
            };
            Assert.AreEqual("0.00250", inclination.ResponseText);
            Assert.AreEqual("0.00333", inclination.LimitText);
            Assert.AreNotEqual(inclination.ResponseText, inclination.LimitText,
                "傾斜角で応答と限界が同じ表示に潰れている");
        }

        /// <summary>
        /// 桁数を行ごとに変えるため応答値・限界値は文字列の列になる。
        /// 数値として読めるよう右寄せの指定が要る。
        /// </summary>
        [TestMethod]
        public void FormattedValueColumns_AreRightAligned()
        {
            var columns = ResultColumnReflectionCache.GetColumns(typeof(EvaluationItem));

            foreach (string header in new[] { "応答値", "限界値" })
            {
                var col = columns.First(c => c.Header == header);
                Assert.AreEqual(typeof(string), col.Property.PropertyType,
                    $"{header} は桁数を行ごとに変えるため文字列");
                Assert.IsTrue(col.RightAlign, $"{header} が右寄せになっていない");
            }
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
