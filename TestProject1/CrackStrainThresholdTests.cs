using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;

namespace TestProject1
{
    /// <summary>
    /// ひび割れ開始の判定ひずみ度が、そのとき使っている構成則と一致していること。
    ///
    /// ひび割れ開始は「引張縁の応力度が曲げ引張強度 Ft に達した状態」なので、
    /// 判定に使うひずみ度は構成則ごとに違う。ところが
    /// <c>GetCrackMoment</c> は<b>常に e 関数の圧縮側</b>から逆算した値を使っており、
    ///  ・バイリニアでは Ft の 81%（まだひび割れていない）で「ひび割れ」と判定 → Mcr が 6〜19% 過小
    ///  ・e 関数でも、原点対称でないため引張側では |σ| が Ft を 10% 超過
    /// となっていた。解析用 M-φ の第 1 折れ点も同じ値を使うため、解析結果にも効いていた。
    ///
    /// この不整合は「M-φ の折れ点が少しずれる」だけで例外にも NaN にもならず、
    /// 既存のファイバー突合せテストは同じ φcr で比較するため差が相殺されて検出できない。
    /// そこで<b>応力度が Ft に達しているか</b>を直接確かめる。
    /// </summary>
    [TestClass]
    public class CrackStrainThresholdTests
    {
        private static InsituConcrete Concrete() => new(1000.0, 0.75, 27.0);

        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.UseUnitGsiForConcreteE = false;
        }

        [TestInitialize]
        public void Init() => ResetOptions();

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        /// <summary>
        /// 各構成則のひび割れひずみ度で、引張縁の応力度がちょうど −Ft になること。
        /// 許容は Ft の 0.5%（ソルバの収束幅）。
        /// </summary>
        [TestMethod]
        public void CrackStrainReachesExactlyTheTensileStrength()
        {
            var c = Concrete();
            double ft = c.SigmaCr;
            Assert.IsTrue(ft > 0, "Ft が求まっていません");

            foreach (MaterialLaw law in new[] { MaterialLaw.Bilinear, MaterialLaw.EFunction })
            {
                double eps = c.GetCrackTensileStrain(law);
                Assert.IsTrue(eps > 0, $"{law}: ひび割れひずみ度が正になりません ({eps:E4})");

                double sigma = law == MaterialLaw.EFunction
                    ? c.GetEFuncSigma(-eps)          // e 関数の生の値（打切り前）
                    : -c.Ec * eps;                   // バイリニアの弾性域

                Assert.AreEqual(-ft, sigma, 0.005 * ft,
                    $"{law}: ε=-{eps:E4} での引張応力度が -Ft と違います "
                    + $"(σ={sigma:F4}, -Ft={-ft:F4})。判定ひずみ度と構成則がちぐはぐです。");
            }
        }

        /// <summary>
        /// e 関数は原点対称ではないので、圧縮側の解を符号反転して流用してはいけない。
        /// 流用すると引張側で Ft を 1 割ほど超える（この差がまさに元の不具合）。
        /// </summary>
        [TestMethod]
        public void EFunctionIsNotSymmetricAboutTheOrigin()
        {
            var c = Concrete();
            double ft = c.SigmaCr;

            double compressionSide = c.GetEFuncEpsilon(ft);        // 圧縮側で σ=+Ft となる ε
            double tensionSide = c.GetEFuncEpsilonTension(ft);     // 引張側で σ=−Ft となる ε

            Assert.IsTrue(tensionSide < compressionSide,
                "引張側のほうが立ち上がりが急なので、ひび割れひずみ度は圧縮側より小さいはずです "
                + $"(引張 {tensionSide:E4} / 圧縮 {compressionSide:E4})");

            // 圧縮側の値を引張に流用すると Ft を超過する（超過しないなら前提が変わっている）
            double misused = -c.GetEFuncSigma(-compressionSide);
            Assert.IsTrue(misused > ft * 1.05,
                $"圧縮側の解の流用で Ft 超過が起きません (|σ|={misused:F4}, Ft={ft:F4})。"
                + "この前提が崩れたなら本テストの意義を見直してください。");

            // 使っている値は超過しない
            double used = -c.GetEFuncSigma(-tensionSide);
            Assert.AreEqual(ft, used, 0.005 * ft, "引張側の解が Ft に一致しません");
        }

        /// <summary>
        /// バイリニアのひび割れひずみ度は Ft/Ec そのもの。
        /// e 関数の値（別物）が混ざっていないことを、閉形式で押さえる。
        /// </summary>
        [TestMethod]
        public void BilinearCrackStrainIsFtOverEc()
        {
            var c = Concrete();
            Assert.AreEqual(c.SigmaCr / c.Ec, c.GetCrackTensileStrain(MaterialLaw.Bilinear), 1e-12);
            Assert.AreNotEqual(c.GetCrackTensileStrain(MaterialLaw.Bilinear),
                               c.GetCrackTensileStrain(MaterialLaw.EFunction),
                               "2 つの構成則で同じ値になっています（取り違えの疑い）");
        }

        /// <summary>
        /// 場所打ち RC 杭のひび割れ開始モーメントが、弾性の閉形式 Mcr = Ze·(Ft + σ0e) と整合すること。
        ///
        /// バイリニアではひび割れ時点まで断面は全域弾性なので、Newton で解いた Mcr は
        /// 閉形式と一致するはず。元の実装では 0.81〜0.94 倍にしかならなかった。
        /// 許容 2%（主筋の存在と数値解の幅）。
        /// </summary>
        [TestMethod]
        public void BilinearCrackMomentMatchesTheElasticClosedForm()
        {
            var concrete = Concrete();
            var bars = new MainBars(600.0, 20, "SD390", "D25");
            var section = new InsituReinforcedConcreteSection(concrete, bars);

            foreach (double n in new[] { 0.0, 1000e3, 3000e3, 5000e3 })
            {
                (double mcr, _) = section.GetCrackMoment(n, false);      // Newton（構成則で積分）
                (double mcrElastic, _) = section.GetCrackMoment(n, true); // 閉形式 Ze·(Ft + σ0e)

                Assert.AreEqual(mcrElastic, mcr, 0.02 * mcrElastic,
                    $"N={n / 1e3:F0} kN: Mcr が弾性解と一致しません "
                    + $"(Newton={mcr / 1e6:F1} kNm, 閉形式={mcrElastic / 1e6:F1} kNm, "
                    + $"比={mcr / mcrElastic:F3})");
            }
        }
    }
}
