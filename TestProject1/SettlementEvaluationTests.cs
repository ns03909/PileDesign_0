using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭の沈下量の検定 (<see cref="PileSettlementEvaluator"/>)。
    ///
    /// <para><b>既定では検定しない。</b> 許容沈下量は上部構造が許せる変形から設計者が定める量で、
    /// 建築基礎構造設計指針も構造種別に応じて定めるとしており一意の値を与えない。
    /// プログラムが既定値のまま合否を出すと、根拠のない判定が計算書に残る。
    /// 基本設定で明示的に有効にしたときだけ項目を作る (2026-09-21 追加)。</para>
    /// </summary>
    [TestClass]
    public class SettlementEvaluationTests
    {
        /// <summary>
        /// 杭 2 本と沈下量を持つ入力を作る。沈下量は m で持たれている。
        /// 杭配置の追加はハンドラが親 ViewModel を要るので、先に結び付けておく。
        /// </summary>
        private static InputModel Build(double single1_m, double single2_m, double group_mm = 0.0, bool withSingleResults = true)
        {
            var input = new InputModel();
            input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
            // 基本設定は既定で null (検定側は null を「検定しない」として扱う)
            input.FundamentalInput ??= new FundamentalInput();
            input.PileLayoutItems ??= [];
            input.PileLayoutItems.Add(new PileLayoutDataItem
            { PileNo = 1, No = 1, PileBodyNo = 1, SinglePileSettlementVL = single1_m });
            input.PileLayoutItems.Add(new PileLayoutDataItem
            { PileNo = 2, No = 2, PileBodyNo = 1, SinglePileSettlementVL = single2_m });

            // 単杭沈下の結果 = 土層-杭セットの荷重-沈下曲線 (沈下量の 0 と「未計算」を区別する手掛かり)
            if (withSingleResults)
            {
                var soilPile = new SoilPile();
                soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
                soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, D0s = 5 });
                input.ElementDivision ??= new ElementDivision();
                input.ElementDivision.SoilPiles = [soilPile];
                foreach (var p in input.PileLayoutItems) p.SoilPileAltNo = 1;
            }

            if (group_mm != 0.0)
            {
                input.PileGroupSettlement ??= new PileGroupSettlement();
                input.PileGroupSettlement.CaseRecords ??= [];
                input.PileGroupSettlement.CaseRecords.Add(new GroupSettlementCaseRecord
                {
                    LoadCaseName = "VL",
                    LoadingType = "任意矩形",
                    IsConverged = true,
                    PileSettlements_mm = new Dictionary<int, double> { [1] = group_mm, [2] = group_mm },
                });
                input.PileGroupSettlement.ActiveCaseIndex = 0;
            }
            return input;
        }

        private static void Enable(InputModel input, double allowable_mm)
        {
            input.FundamentalInput.EvaluateSettlement = true;
            input.FundamentalInput.AllowableSettlement_mm = allowable_mm;
        }

        /// <summary><b>本題。</b> 既定では 1 件も検定しないこと。</summary>
        [TestMethod]
        public void ByDefaultNothingIsEvaluated()
        {
            var input = Build(0.030, 0.010);   // 30mm / 10mm

            Assert.IsFalse(input.FundamentalInput.EvaluateSettlement, "既定は検定なしであること");
            Assert.AreEqual(0, PileSettlementEvaluator.Evaluate(input).Count,
                "既定で検定している (根拠のない判定が計算書に残る)");
        }

        /// <summary>基本設定そのものが無い入力でも落ちず、検定しないこと。</summary>
        [TestMethod]
        public void WithoutFundamentalInputNothingIsEvaluated()
        {
            var input = Build(0.030, 0.010);
            input.FundamentalInput = null;

            Assert.AreEqual(0, PileSettlementEvaluator.Evaluate(input).Count);
            Assert.AreEqual(0, PileSettlementEvaluator.Evaluate(null).Count);
        }

        /// <summary>有効にすると、杭ごとに 1 件ずつ長期の項目が作られること。</summary>
        [TestMethod]
        public void EnablingItEvaluatesEachPileForTheLongTermCase()
        {
            var input = Build(0.030, 0.010);
            Enable(input, allowable_mm: 20.0);

            var items = PileSettlementEvaluator.Evaluate(input);

            Assert.AreEqual(2, items.Count, "杭の数だけ項目が出ること");
            Assert.IsTrue(items.All(i => i.Kind == EvaluationKind.PileSettlement));
            Assert.IsTrue(items.All(i => i.LoadCaseName == "VL"), "長期だけを対象にすること");
            Assert.IsTrue(items.All(i => i.Level == 0), "常時なので地震動レベルは 0");
            Assert.IsTrue(items.All(i => i.Unit == "mm"));
            Assert.IsTrue(items.All(i => i.Limit == 20.0));
        }

        /// <summary>
        /// 応答値は m を mm に直した沈下量。30mm &gt; 20mm は NG、10mm は OK。
        /// </summary>
        [TestMethod]
        public void TheResponseIsInMillimetresAndComparedToTheAllowable()
        {
            var input = Build(0.030, 0.010);
            Enable(input, allowable_mm: 20.0);

            var byPile = PileSettlementEvaluator.Evaluate(input).ToDictionary(i => i.PileNo ?? 0);

            Assert.AreEqual(30.0, byPile[1].Response, 1e-9, "m を mm に直していない");
            Assert.IsFalse(byPile[1].IsOk, "許容値を超えているのに OK");
            Assert.AreEqual(10.0, byPile[2].Response, 1e-9);
            Assert.IsTrue(byPile[2].IsOk);
        }

        /// <summary>許容値ちょうどは OK (超えたときだけ NG)。</summary>
        [TestMethod]
        public void ExactlyAtTheAllowableIsOk()
        {
            var input = Build(0.020, 0.020);
            Enable(input, allowable_mm: 20.0);

            Assert.IsTrue(PileSettlementEvaluator.Evaluate(input).All(i => i.IsOk));
        }

        /// <summary>
        /// 応答値は単杭沈下 + 群杭沈下。群杭沈下を実行していなければ単杭だけになる。
        /// </summary>
        [TestMethod]
        public void TheResponseAddsTheGroupSettlement()
        {
            var withoutGroup = Build(0.010, 0.010);
            Enable(withoutGroup, allowable_mm: 20.0);
            Assert.AreEqual(10.0, PileSettlementEvaluator.Evaluate(withoutGroup)[0].Response, 1e-9,
                "群杭沈下が無いのに足されている");

            var withGroup = Build(0.010, 0.010, group_mm: 5.0);
            Enable(withGroup, allowable_mm: 20.0);
            Assert.AreEqual(15.0, PileSettlementEvaluator.Evaluate(withGroup)[0].Response, 1e-9,
                "群杭沈下を足していない");
        }

        /// <summary>
        /// 許容値が正の有限の数でなければ判定しない。検定を有効にしているので黙って省かず、入力箇所を示す項目を 1 件出す。
        /// 以前は「0 より大きい」だけを見ていたので、無限大が通って有限の沈下量がすべて OK になった。
        /// </summary>
        [TestMethod]
        public void AnUnusableAllowableIsReportedInsteadOfJudged()
        {
            foreach (double bad in new[] { 0.0, -5.0, double.NaN, double.PositiveInfinity })
            {
                var input = Build(0.030, 0.010);
                Enable(input, allowable_mm: bad);
                var items = PileSettlementEvaluator.Evaluate(input);
                Assert.AreEqual(1, items.Count, $"許容値 {bad}: 杭ごとに判定しています");
                Assert.IsTrue(items[0].IsUnavailable && !items[0].IsOk, $"許容値 {bad} で判定しています");
                StringAssert.Contains(items[0].UnavailableReason, "許容沈下量");
                StringAssert.Contains(items[0].UnavailableReason, "基本設定");
            }
        }

        /// <summary>
        /// 単杭沈下の結果が無い杭は 0 mm・OK にせず、検定不能にする (沈下量は未計算でも 0 を持つ)。
        /// 本当に 0 mm の結果がある杭は判定する。
        /// </summary>
        [TestMethod]
        public void APileWithoutSingleSettlementResultsIsNotJudged()
        {
            var input = Build(0.0, 0.0, withSingleResults: false);
            Enable(input, allowable_mm: 20.0);
            var items = PileSettlementEvaluator.Evaluate(input);
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items.All(i => i.IsUnavailable && !i.IsJudged), "未計算の杭を 0 mm・OK と判定しています");
            StringAssert.Contains(items[0].UnavailableReason, "単杭沈下解析の結果がありません");

            var computedZero = Build(0.0, 0.0, withSingleResults: true);
            Enable(computedZero, allowable_mm: 20.0);
            Assert.IsTrue(PileSettlementEvaluator.Evaluate(computedZero).All(i => i.IsJudged && i.IsOk),
                "結果のある 0 mm まで判定をやめています");
        }

        /// <summary>
        /// 検定の判定は <see cref="EvaluationItem"/> の歯止めを通ること。
        /// 沈下量が NaN なら OK と読めてはいけない。
        /// </summary>
        [TestMethod]
        public void ANotANumberSettlementIsNotOk()
        {
            var input = Build(double.NaN, 0.010);
            Enable(input, allowable_mm: 20.0);

            var item = PileSettlementEvaluator.Evaluate(input).First(i => i.PileNo == 1);

            Assert.IsFalse(item.IsOk, "沈下量が NaN の項目が OK と読めている");
            Assert.AreEqual("NG", item.StatusLabel);
        }

        /// <summary>
        /// まとめ (ダッシュボード・キャンバスの色分け) にも入ること。
        /// 別扱いにすると、沈下量が NG でも杭の色に出ない。
        /// </summary>
        [TestMethod]
        public void TheSummaryFoldsTheSettlementItems()
        {
            var input = Build(0.030, 0.010);
            Enable(input, allowable_mm: 20.0);

            var vertical = new EvaluationResult(PileSettlementEvaluator.Evaluate(input));
            var summary = PileEvaluationSummary.FromResults(null, null, vertical, "A");

            Assert.AreEqual(PileRatioBand.Ng, summary.ByPile[1].Band,
                "沈下量が NG なのに杭の色に出ない");
            Assert.AreNotEqual(PileRatioBand.Ng, summary.ByPile[2].Band,
                "OK の杭まで NG になっている");
        }
    }
}
