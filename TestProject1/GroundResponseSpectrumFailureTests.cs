using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 応答スペクトル法で地盤変位を計算できなかったとき、理由を残し、利用者に知らせること。
    ///
    /// 以前は例外を含めて理由を持たずに T1 = NaN を返し、呼び出し側は<b>デバッグログだけで</b>略算法 (a2(b2)) に
    /// 切り替えていた。利用者は応答スペクトル法を選んだつもりで別の算定法の値を使い、入力の不備なのか計算の不具合
    /// なのかも分からなかった。
    /// </summary>
    [TestClass]
    public class GroundResponseSpectrumFailureTests
    {
        private static GroundMassDataInput Mass(double h = 5.0, double density = 18.0, double vs = 200.0, bool bedrock = false)
            => new() { H = h, Density = density, VS0 = vs, Mass = density / 9.80665 * h, IsEngineeringBedrock = bedrock };

        [TestMethod]
        public void InputProblemsAreNamed()
        {
            var noSoil = GroundResponseSpectrumCalc.Compute([Mass(bedrock: true)], 19.0, 400.0, "砂質土", 1.0);
            Assert.IsTrue(double.IsNaN(noSoil.T1));
            StringAssert.Contains(noSoil.Failure, "工学的基盤より上に質点がありません");
            Assert.IsTrue(noSoil.IsInputProblem);

            var zeroThickness = GroundResponseSpectrumCalc.Compute([Mass(), Mass(h: 0), Mass(bedrock: true)], 19.0, 400.0, "砂質土", 1.0);
            StringAssert.Contains(zeroThickness.Failure, "質点 2 の層厚が 0 以下です");
            Assert.IsTrue(zeroThickness.IsInputProblem);

            var zeroDensity = GroundResponseSpectrumCalc.Compute([Mass(), Mass(density: 0), Mass(bedrock: true)], 19.0, 400.0, "砂質土", 1.0);
            StringAssert.Contains(zeroDensity.Failure, "質点 2 の質量または VS が 0 以下です");
            Assert.IsTrue(zeroDensity.IsInputProblem);
        }

        /// <summary>計算中の例外は、入力の不備とは区別して理由を残すこと (例外の種類と内容を含む)。</summary>
        [TestMethod]
        public void ExceptionsAreKeptApartFromInputProblems()
        {
            var masses = new List<GroundMassDataInput> { Mass(), null!, Mass(bedrock: true) };   // 途中で例外になる
            var result = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1.0);

            Assert.IsTrue(double.IsNaN(result.T1));
            StringAssert.Contains(result.Failure, "計算中に例外が起きました");
            StringAssert.Contains(result.Failure, "NullReferenceException");
            Assert.IsFalse(result.IsInputProblem, "例外を入力の不備として扱っています");
        }

        [TestMethod]
        public void ASuccessfulCalculationHasNoFailure()
        {
            var result = GroundResponseSpectrumCalc.Compute([Mass(), Mass(), Mass(bedrock: true)], 19.0, 400.0, "砂質土", 1e-3);
            Assert.IsFalse(double.IsNaN(result.T1));
            Assert.IsNull(result.Failure);
        }

        /// <summary>
        /// 地盤の計算の経路で、応答スペクトル法を選んでいて計算できなかったら、略算法で代用したことと理由を
        /// 地盤の入力に残すこと (地盤ウィンドウの算定法の横に出す)。算定法を変えれば消えること。
        /// 最下層より深い土質点は密度 0 のまま残る (土層から写すため) ので、応答スペクトル法では質量 0 で計算できない。
        /// </summary>
        [TestMethod]
        public void TheGroundWindowIsToldWhenTheSimplifiedMethodIsUsedInstead()
        {
            var ground = new GroundInput
            {
                GroundTopAltitude = 0.0,
                BedrockDensity = 19.0,
                BedrockShearWaveVelocity = 400.0,
                ShallowSoilType = "砂質土",
                CalculationMethod = "応答スペクトル法",
            };
            ground.GroundLayers = [new GroundLayerInput
            {
                No = 1, BottomGLDepth = -10.0, LayerThickness = 10.0, BottomAltitude = -10.0,
                Name = "砂質土", GranularityClass = "砂質土", Density = 18.0, NValue = 10.0, Vs = 180.0, Es = 7000.0,
            }];
            ground.GroundMassesData = [];
            for (int i = 1; i <= 15; i++)
                ground.GroundMassesData.Add(new GroundMassDataInput { No = i, GLDepth = -i, H = 1.0, NValue = 10.0, VS0 = 180.0, Fc = 20.0 });

            var vm = new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground };
            vm.Update();

            Assert.IsTrue(ground.HasResponseSpectrumWarning, "応答スペクトル法で計算できなかったのに、画面に知らせていません");
            StringAssert.Contains(ground.ResponseSpectrumWarning, "略算法 (a2(b2)) で代用しています");
            StringAssert.Contains(ground.ResponseSpectrumWarning, "レベル1:");
            StringAssert.Contains(ground.ResponseSpectrumWarning, "質量または VS が 0 以下です");

            ground.CalculationMethod = "a1(b1)";
            vm.Update();
            Assert.IsFalse(ground.HasResponseSpectrumWarning, "算定法を変えたのに、注意書きが残っています");
        }
    }
}
