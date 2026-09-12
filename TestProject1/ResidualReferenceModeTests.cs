using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 収束判定で残差を割る<b>基準値の取り方</b> (<see cref="ResidualReferenceMode"/>)。
    ///
    /// <para>判定は残差比 ‖R‖²/‖基準‖² で行う。従来は基準が外力 (慣性力) のみだったので、
    /// 地盤変位を強制変位で与えるケースでは基準が実際の駆動力より小さく、慣性力 0 の組合せでは
    /// 基準が 0 になって判定そのものが成り立たなかった。基準値の取り方を選べるようにした
    /// (2026-09-12)。<b>既定は従来どおり外力のみ</b>なので、既存の結果は動かない。</para>
    ///
    /// <para>比の計算は <see cref="AnaModel.ResidualRatio"/> に純関数として切り出してあるので、
    /// ここではそれを直接見る。FEM モデルを組まずに、基準値の取り方だけを確かめられる。</para>
    /// </summary>
    [TestClass]
    public class ResidualReferenceModeTests
    {
        // ‖R‖² = 1、外力 ‖F‖² = 100、反力 ‖T_強制変位‖² = 10,000、内力の平均 ‖T‖ = 1,000
        private const double NormsqR = 1.0;
        private const double NormsqF = 100.0;
        private const double NormsqReaction = 10000.0;
        private const double FluxNorm = 1000.0;

        private static double Ratio(ResidualReferenceMode mode, double normsqR = NormsqR,
            double normsqF = NormsqF, double normsqReaction = NormsqReaction, double flux = FluxNorm)
            => AnaModel.ResidualRatio(mode, normsqR, normsqF, normsqReaction, flux);

        /// <summary>
        /// 旧版の判定 (外力のみ) は ‖R‖²/‖F‖² のまま。過去の結果と比べるための選択肢なので、
        /// 式が変わってはいけない。
        /// </summary>
        [TestMethod]
        public void TheOldModeKeepsTheOldRatio()
        {
            Assert.AreEqual(NormsqR / NormsqF, Ratio(ResidualReferenceMode.ExternalForce), 1e-15);
        }

        /// <summary>
        /// <b>既定値の定義は 1 か所</b>であること。入力・解析モデル・受け渡し・入力検査が
        /// それぞれ既定値を書くと、片方だけ直して静かに食い違う (このリポジトリで繰り返している形)。
        /// </summary>
        [TestMethod]
        public void EveryDefaultAgrees()
        {
            Assert.AreEqual(ResidualReferenceMode.ExternalForceOrReaction, ResidualReferenceModes.Default,
                "既定は「外力と強制変位の反力の大きい方」のはずです。"
                + "外力のみだと慣性力 0 の組合せで判定が成り立ちません");
            Assert.AreEqual(ResidualReferenceModes.Default, new FundamentalInput().ResidualReference,
                "入力の既定が ResidualReferenceModes.Default と違います");
            Assert.AreEqual(ResidualReferenceModes.Default, new AnaModel().ResidualReference,
                "解析モデルの既定が ResidualReferenceModes.Default と違います");
            Assert.IsTrue(ResidualReferenceModes.Default.WorksWithoutInertia(),
                "既定は慣性力 0 でも判定が成り立つものにすること");
        }

        /// <summary>反力基準は外力と反力の大きい方で割る。</summary>
        [TestMethod]
        public void TheReactionModeTakesTheLargerOfForceAndReaction()
        {
            Assert.AreEqual(NormsqR / NormsqReaction,
                Ratio(ResidualReferenceMode.ExternalForceOrReaction), 1e-15,
                "反力のほうが大きいのに外力で割っています");

            // 外力のほうが大きければ外力で割る (従来と同じ値になる)
            Assert.AreEqual(NormsqR / NormsqF,
                Ratio(ResidualReferenceMode.ExternalForceOrReaction, normsqReaction: 1.0), 1e-15,
                "外力のほうが大きいのに反力で割っています");
        }

        /// <summary>内力基準は内力の平均で割る。外力・反力の大きい方が下限。</summary>
        [TestMethod]
        public void TheInternalForceModeUsesTheAveragedFluxNorm()
        {
            Assert.AreEqual(NormsqR / (FluxNorm * FluxNorm),
                Ratio(ResidualReferenceMode.InternalForce), 1e-15,
                "内力の平均で割っていません (渡すのは 2 乗ではない ‖T‖)");

            // 内力の平均がまだ小さい (最初のステップ) ときは、外力・反力の大きい方を使う
            Assert.AreEqual(NormsqR / NormsqReaction,
                Ratio(ResidualReferenceMode.InternalForce, flux: 1.0), 1e-15,
                "内力の平均が小さいときに外力・反力を下限にしていません");
        }

        /// <summary>
        /// 慣性力 0 のケース。従来の基準では判定できず、新しい基準では判定できること。
        /// これがオプションを入れた理由そのもの。
        /// </summary>
        [TestMethod]
        public void WithoutInertiaOnlyTheNewModesCanJudge()
        {
            Assert.AreEqual(1e30, Ratio(ResidualReferenceMode.ExternalForce, normsqF: 0.0), 0.0,
                "外力のみの基準は慣性力 0 で判定できないこと (従来の扱いを残す)");

            Assert.AreEqual(NormsqR / NormsqReaction,
                Ratio(ResidualReferenceMode.ExternalForceOrReaction, normsqF: 0.0), 1e-15,
                "慣性力 0 でも強制変位の反力で判定できること");
            Assert.AreEqual(NormsqR / (FluxNorm * FluxNorm),
                Ratio(ResidualReferenceMode.InternalForce, normsqF: 0.0), 1e-15,
                "慣性力 0 でも内力で判定できること");
        }

        /// <summary>
        /// 基準も残差も 0 なら釣り合っている (0 を返す)。基準だけ 0 なら判定できない (1e30)。
        /// </summary>
        [TestMethod]
        public void AZeroReferenceMeansConvergedOnlyIfTheResidualIsAlsoZero()
        {
            foreach (var mode in ResidualReferenceModes.All)
            {
                Assert.AreEqual(0.0, Ratio(mode, normsqR: 0.0, normsqF: 0.0, normsqReaction: 0.0, flux: 0.0), 0.0,
                    $"{mode}: 残差も基準も 0 なら釣り合っているとみなすこと");
                Assert.AreEqual(1e30, Ratio(mode, normsqF: 0.0, normsqReaction: 0.0, flux: 0.0), 0.0,
                    $"{mode}: 基準が 0 で残差があるなら判定できないこと");
            }
        }

        /// <summary>
        /// どの基準でも、残差が小さくなれば比も小さくなること (単調)。
        /// 基準の取り方は「ものさし」なので、残差との大小関係を反転させてはいけない。
        /// </summary>
        [TestMethod]
        public void EveryModeIsMonotoneInTheResidual()
        {
            foreach (var mode in ResidualReferenceModes.All)
            {
                double big = Ratio(mode, normsqR: 4.0);
                double small = Ratio(mode, normsqR: 1.0);
                Assert.IsTrue(small < big, $"{mode}: 残差を 1/4 にしたのに比が小さくなりません");
            }
        }

        /// <summary>強制変位だけで駆動するケースを解けるのは新しい基準だけ、という区別。</summary>
        [TestMethod]
        public void OnlyTheNewModesClaimToWorkWithoutInertia()
        {
            Assert.IsFalse(ResidualReferenceMode.ExternalForce.WorksWithoutInertia());
            Assert.IsTrue(ResidualReferenceMode.ExternalForceOrReaction.WorksWithoutInertia());
            Assert.IsTrue(ResidualReferenceMode.InternalForce.WorksWithoutInertia());
        }

        /// <summary>内力基準の時間平均は、積んだ回数で割った平均であること。</summary>
        [TestMethod]
        public void TheFluxNormIsAveragedOverCommittedSteps()
        {
            var model = new AnaModel();
            Assert.AreEqual(0.0, model.AveragedInternalFluxNorm, 0.0,
                "まだ 1 つも積んでいないのに平均が 0 ではありません");

            // 内力ベクトルが無い状態で呼んでも落ちないこと (ステップ 1 の前に呼ばれうる)
            model.CommitInternalFluxNorm();
            Assert.AreEqual(0, model.InternalFluxNormCount);
        }

        /// <summary>
        /// 基準値の取り方が入力の控え (ShallowCopy) を渡ること。
        /// 渡らないと、控えからの復元で既定に戻り、選んだ設定が静かに効かなくなる。
        /// </summary>
        [TestMethod]
        public void TheModeSurvivesCopies()
        {
            var fundamental = new FundamentalInput { ResidualReference = ResidualReferenceMode.InternalForce };
            Assert.AreEqual(ResidualReferenceMode.InternalForce, fundamental.ShallowCopy().ResidualReference,
                "入力の控えに基準値の取り方が写っていません");
        }

        /// <summary>
        /// 慣性力 0 の組合せを止める入力検査は、<b>従来の基準のときだけ</b>働くこと。
        /// 新しい基準では判定が成り立つので、止める理由が無い。
        /// </summary>
        [TestMethod]
        public void TheInputCheckOnlyBlocksWhenTheReferenceIsTheExternalForce()
        {
            // new InputModel() は FundamentalInput を作らない (既定の入力は MainWindowViewModel が組む)
            var model = new InputModel
            {
                LoadCasesInput = new LoadCasesInput(),
                FundamentalInput = new FundamentalInput(),
            };
            model.LoadCasesInput.LoadCasesLevel1 = new ObservableCollection<LoadCase>
            {
                new LoadCase { Level = 1, No = 1, IsApplicable = true, IsAnalysisTarget = true,
                    UpperMassForce = 1000, FoundationMassForce = 800 }
            };
            model.LoadCasesInput.LoadCasesLevel2 = new ObservableCollection<LoadCase>();
            model.LoadCasesInput.LoadCombinations = LoadCaseViewModel.GetCombinations(0.0);

            model.FundamentalInput.ResidualReference = ResidualReferenceMode.ExternalForce;
            Assert.AreNotEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                "旧版の判定 (外力のみ) では慣性力 0 の組合せを止めること");

            // 既定のままなら止めない (既定が外力のみに戻ったらここで落ちる)
            model.FundamentalInput.ResidualReference = ResidualReferenceModes.Default;
            Assert.AreEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                "既定では慣性力 0 でも判定が成り立つので、止めないこと");

            foreach (var mode in new[] { ResidualReferenceMode.ExternalForceOrReaction,
                                         ResidualReferenceMode.InternalForce })
            {
                model.FundamentalInput.ResidualReference = mode;
                Assert.AreEqual("", CheckInputData.CheckLoadCombinations(model, ""),
                    $"{mode} なら慣性力 0 でも判定が成り立つので、止めないこと");
            }
        }

        /// <summary>表示名が 3 つとも違い、空でないこと (ComboBox で区別できること)。</summary>
        [TestMethod]
        public void EveryModeHasItsOwnLabel()
        {
            var texts = new HashSet<string>();
            var shorts = new HashSet<string>();
            foreach (var mode in ResidualReferenceModes.All)
            {
                string text = ResidualReferenceModes.ToText(mode);
                string small = ResidualReferenceModes.ToShortText(mode);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{mode}: 表示名が空です");
                Assert.IsFalse(string.IsNullOrWhiteSpace(small), $"{mode}: 短縮表記が空です");
                Assert.IsTrue(texts.Add(text), $"{mode}: 表示名が他と同じです");
                Assert.IsTrue(shorts.Add(small), $"{mode}: 短縮表記が他と同じです");
            }
            TestSource.AssertScanned(texts.Count, 3, "収束判定の基準値の選択肢");
        }
    }
}
