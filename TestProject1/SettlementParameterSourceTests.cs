using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 沈下解析が参照するパラメータの「出所」を固定する回帰テスト。
    ///
    /// 過去に以下 2 件の参照元取り違えがあった:
    ///   1. 杭先端沈下曲線の α・n を <c>InputModel.PileBodies[^1]</c> と最終要素固定で読んでいた
    ///      → 杭体が複数あると別の杭体の値で解析していた
    ///   2. 周面抵抗の考慮有無を <c>PileCircumVertical.GroundLayer</c> (土層側) から読んでいた
    ///      → 沈下ウィンドウのチェックボックス (PileCircumVertical 側を編集) が
    ///        支持力表にだけ効いて沈下解析に効かなかった
    /// </summary>
    [TestClass]
    public class SettlementParameterSourceTests
    {
        private static PileBodyInput MakePileBody(double settleAlpha, double settleN, double settleToeDia)
            => new()
            {
                SettleAlpha = settleAlpha,
                SettleN = settleN,
                SettlePileToeDia = settleToeDia,
            };

        private static SoilPile MakeSoilPile(int pileBodyNo, PileBodyInput body)
        {
            var soilPile = new SoilPile();
            soilPile.Initialize(no: pileBodyNo, groundNo: 1, groundInput: new GroundInput(),
                                pileBodyNo: pileBodyNo, pileBodyInput: body,
                                z: 0.0, zDataItems: []);
            return soilPile;
        }

        // ── 1. 杭先端沈下曲線パラメータの出所 ────────────────────

        [TestMethod]
        public void SoilPile_CarriesItsOwnPileBody_NotTheLastOne()
        {
            // 杭体が 2 つあり、それぞれ別の α・n を持つ状況
            var body1 = MakePileBody(settleAlpha: 0.30, settleN: 2.0, settleToeDia: 1200.0);
            var body2 = MakePileBody(settleAlpha: 0.55, settleN: 3.0, settleToeDia: 1500.0);

            var soilPile1 = MakeSoilPile(1, body1);
            var soilPile2 = MakeSoilPile(2, body2);

            // 各 SoilPile は自分の杭体を保持する。
            // 沈下解析の α・n はここから読む (旧実装の PileBodies[^1] だと両方 body2 になる)
            Assert.AreSame(body1, soilPile1.PileBodyInput);
            Assert.AreSame(body2, soilPile2.PileBodyInput);

            Assert.AreEqual(0.30, soilPile1.PileBodyInput.SettleAlpha, 1e-12);
            Assert.AreEqual(2.0, soilPile1.PileBodyInput.SettleN, 1e-12);
            Assert.AreEqual(0.55, soilPile2.PileBodyInput.SettleAlpha, 1e-12);
            Assert.AreEqual(3.0, soilPile2.PileBodyInput.SettleN, 1e-12);
        }

        [TestMethod]
        public void SoilPile_ToeDiameterAndCurveParameters_ComeFromTheSameBody()
        {
            // 先端径 Dp と α・n は同じ Rp-Sp 曲線に渡るので、出所が一致していないと整合しない
            var body1 = MakePileBody(settleAlpha: 0.30, settleN: 2.0, settleToeDia: 1200.0);
            var body2 = MakePileBody(settleAlpha: 0.55, settleN: 3.0, settleToeDia: 1500.0);

            var soilPile1 = MakeSoilPile(1, body1);
            _ = MakeSoilPile(2, body2);

            Assert.AreEqual(body1.SettlePileToeDia, soilPile1.Dp, 1e-12,
                "先端径は自分の杭体から取っている");
            Assert.AreEqual(body1.SettleAlpha, soilPile1.PileBodyInput.SettleAlpha, 1e-12,
                "α も同じ杭体から取らなければ Rp-Sp 曲線が整合しない");
        }

        // ── 2. 周面抵抗フラグの出所 ─────────────────────────────

        [TestMethod]
        public void CircumResistanceFlags_OnPileCircumVertical_AreIndependentOfGroundLayer()
        {
            // PileCircumVertical のフラグは生成時に土層の値をコピーするが、
            // その後は沈下ウィンドウで杭区間ごとに上書きできる「オーバーライド」である。
            var layer = new GroundLayerInput
            {
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true,
            };
            var pcv = new PileCircumVertical
            {
                GroundLayer = layer,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true,
            };

            // 沈下ウィンドウでチェックを外す操作に相当
            pcv.IsPositiveCircumResistance = false;

            Assert.IsFalse(pcv.IsPositiveCircumResistance, "杭区間側は変更される");
            Assert.IsTrue(layer.IsPositiveCircumResistance,
                "土層側は変わらない。ここを見ていると沈下解析がチェック操作に反応しない");
        }

        [TestMethod]
        public void BearingCapacity_RespondsToPerSegmentOverride()
        {
            // 支持力表 (SoilPile.CalculateResistances) は杭区間側のフラグを見る。
            // 沈下解析も同じ出所を見るよう修正済み（同じチェックボックスで両方が反応する）。
            var body = MakePileBody(0.3, 2.0, 1200.0);
            var soilPile = MakeSoilPile(1, body);

            var layer = new GroundLayerInput
            {
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true,
            };
            soilPile.PileCircumVerticals = [MakePcv(layer, positive: true, negative: true)];
            soilPile.CalculateResistances();
            double rfuBefore = soilPile.Rfu;
            Assert.IsTrue(rfuBefore > 0, "押込み周面抵抗が計上されていない");

            // 杭区間側だけ OFF にする（土層側は触らない）
            soilPile.PileCircumVerticals = [MakePcv(layer, positive: false, negative: true)];
            soilPile.CalculateResistances();

            Assert.AreEqual(0.0, soilPile.Rfu, 1e-12,
                "杭区間ごとのオーバーライドが支持力に反映されていない");
        }

        private static PileCircumVertical MakePcv(GroundLayerInput layer, bool positive, bool negative)
        {
            var section = new PileSection { PileDiameter = 1000.0 };
            return new PileCircumVertical
            {
                Top = 0.0,
                Bottom = -5.0,
                GroundLayer = layer,
                PileBodySegment = new PileBodySegment { PileSection = section },
                Tau2 = 100.0,
                TauT = -60.0,
                IsPositiveCircumResistance = positive,
                IsNegativeCircumResistance = negative,
            };
        }
    }
}
