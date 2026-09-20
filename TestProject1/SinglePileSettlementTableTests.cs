using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下解析の結果をテーブルで確認できること (2026-09-20 追加)。
    ///
    /// <para>それまで単杭沈下の結果はグラフ (荷重沈下曲線) だけで、テーブルには 1 枚も
    /// 出していなかった。数値で確かめる手段が無く、単杭沈下しか実行していないと
    /// 「テーブル出力」自体が押せなかった。</para>
    ///
    /// <para>出すのは 2 種類。土層-杭セットごとの<b>荷重-沈下曲線</b>と、
    /// 荷重ケースごとの<b>各杭の沈下量</b>。単位はどちらも画面と同じ (沈下量は mm)。</para>
    /// </summary>
    [TestClass]
    public class SinglePileSettlementTableTests
    {
        private const double ToMm = 1000.0;

        /// <summary>
        /// 単杭沈下を終えた状態の入力を作る (土層-杭セット 1 つ、杭 2 本)。
        /// 杭配置の追加はハンドラが親 ViewModel を要るので、先に結び付けておく。
        /// </summary>
        private static (MainWindowViewModel vm, InputModel input) Build()
        {
            var input = new InputModel();
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            input.PileLayoutItems ??= [];
            for (int no = 1; no <= 2; no++)
            {
                input.PileLayoutItems.Add(new PileLayoutDataItem
                {
                    PileNo = no,
                    No = no,
                    X = no * 1.5,
                    Y = 0.0,
                    AxialForceVL0 = 800.0 * no,
                    AxialForceVLAdditional = 0.0,
                    SinglePileSettlementVL = 0.0036 * no,   // m
                });
            }

            // 曲線は最後に入れる (杭配置の追加が土層-杭セットの作り直しを予約するため)
            input.ElementDivision ??= new ElementDivision();
            var sp = new SoilPile { GroundNo = 2, PileBodyNo = 3, Z = 0.0 };
            // 荷重の小さい順に入れない (表は荷重で並べ替えることの確認も兼ねる)
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 1000, DD0s = 5.0, DDns = 3.0, RzToe = 400, RzCircum = 600, Note = "R_SLS" });
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 0, DD0s = 0.0, DDns = 0.0, RzToe = 0, RzCircum = 0 });
            input.ElementDivision.SoilPiles.Add(sp);

            vm.IsVerticalAnalysisDone = true;
            return (vm, input);
        }

        /// <summary>荷重-沈下曲線の表が、土層-杭セットごとに 1 枚出ること。</summary>
        [TestMethod]
        public void TheCurveTableIsBuiltPerSoilPileSet()
        {
            var (vm, _) = Build();

            var tables = vm.BuildSinglePileSettlementTables();

            var curve = tables.FirstOrDefault(t => t.Name.Contains("荷重-沈下曲線"));
            Assert.IsNotNull(curve, "荷重-沈下曲線の表が出ていない");
            StringAssert.Contains(curve.Name, "地盤2", "どの土層-杭セットか分からない");
            StringAssert.Contains(curve.Name, "杭体3", "どの杭体か分からない");
            Assert.AreEqual("単杭沈下解析", curve.Category);
            Assert.AreEqual(2, curve.Rows.Count, "荷重段階の数だけ行が出ること");
        }

        /// <summary>
        /// 曲線の表はグラフと同じ値を出し、荷重の小さい順に並ぶこと
        /// (グラフは <c>OrderBy(PileTopLoad)</c> で描く)。
        /// </summary>
        [TestMethod]
        public void TheCurveTableMatchesTheGraphValuesInLoadOrder()
        {
            var (vm, _) = Build();

            var curve = vm.BuildSinglePileSettlementTables()
                .First(t => t.Name.Contains("荷重-沈下曲線"));

            var rows = curve.Rows
                .Cast<MainWindowViewModel.SinglePileSettlementCurveRow>()
                .ToList();

            Assert.AreEqual(1, rows[0].Step);
            Assert.AreEqual(0.0, rows[0].PileTopLoad_kN, 1e-9, "荷重の小さい順に並んでいない");
            Assert.AreEqual(2, rows[1].Step);
            Assert.AreEqual(1000.0, rows[1].PileTopLoad_kN, 1e-9);
            Assert.AreEqual(5.0, rows[1].HeadSettlement_mm, 1e-9, "杭頭沈下量 (mm) が違う");
            Assert.AreEqual(3.0, rows[1].ToeSettlement_mm, 1e-9, "杭先端沈下量 (mm) が違う");
            Assert.AreEqual(400.0, rows[1].ToeReaction_kN, 1e-9);
            Assert.AreEqual(600.0, rows[1].CircumResistance_kN, 1e-9);
            Assert.AreEqual("R_SLS", rows[1].Note, "備考 (極限に達した段階の印) が落ちている");
        }

        /// <summary>
        /// 各杭の沈下量の表が出て、<b>m を mm に直して</b>いること。
        /// 3D 表示は mm で出しているので、表が m のままだと 1000 倍食い違う。
        /// </summary>
        [TestMethod]
        public void ThePileTableIsBuiltInMillimetres()
        {
            var (vm, _) = Build();

            var pileTable = vm.BuildSinglePileSettlementTables()
                .FirstOrDefault(t => t.Name.Contains("各杭の沈下量"));
            Assert.IsNotNull(pileTable, "各杭の沈下量の表が出ていない");

            var rows = pileTable.Rows
                .Cast<MainWindowViewModel.SinglePileSettlementPileRow>()
                .Where(r => r.LoadCaseName == "VL")
                .OrderBy(r => r.PileNo)
                .ToList();

            Assert.AreEqual(2, rows.Count, "杭の数だけ VL の行が出ること");
            Assert.AreEqual(0.0036 * ToMm, rows[0].Settlement_mm, 1e-9, "mm に直していない");
            Assert.AreEqual(0.0072 * ToMm, rows[1].Settlement_mm, 1e-9);
            Assert.AreEqual(800.0, rows[0].AxialForce_kN, 1e-9, "VL の軸力が違う");
            Assert.AreEqual(1600.0, rows[1].AxialForce_kN, 1e-9);
        }

        /// <summary>
        /// 曲線を持っていなければ表を出さないこと (空の表を並べない)。
        /// </summary>
        [TestMethod]
        public void NothingIsBuiltWithoutCurves()
        {
            var input = new InputModel();
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles.Add(new SoilPile { GroundNo = 1, PileBodyNo = 1, Z = 0.0 });
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);

            Assert.AreEqual(0, vm.BuildSinglePileSettlementTables().Count);
        }
    }
}
