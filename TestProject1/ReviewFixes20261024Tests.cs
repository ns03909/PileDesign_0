using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-24 のレビュー報告 (杭・地盤のレベル) の 4 件。どれも「物理的に同じ条件なのに、座標の置き方や読み込みの経路で
    /// 結果が変わる」誤り。基準の高さを変えても物理量が保たれること・範囲端の前後・遅れて走る処理のあとまでを確かめる。
    /// </summary>
    [TestClass]
    public class ReviewFixes20261024Tests
    {
        /// <summary>
        /// 1. 平均 N 値の範囲の端にちょうどある土質点は、杭と地盤をそろって動かしても範囲に入ったまま。
        /// 以前は許容差なしで比べたので、-6.01 m 動かすと端の点が浮動小数点の差で外れ、先端支持力度が 40% 変わった。
        /// </summary>
        [TestMethod]
        public void TheEdgesOfTheNValueRange_DoNotDependOnWhereTheModelSits()
        {
            foreach (double shift in new[] { 0.0, -6.01, -6.1, 1.7, 100.0, -0.3 })
            {
                var ground = new GroundInput { GroundTopAltitude = 0.0 + shift };
                ground.GroundMassesData =
                [
                    new GroundMassDataInput { GLDepth = -8.8, AltitudeDepth = -8.8 + shift, NValue = 60 },
                    new GroundMassDataInput { GLDepth = -10.0, AltitudeDepth = -10.0 + shift, NValue = 10 },
                    new GroundMassDataInput { GLDepth = -11.2, AltitudeDepth = -11.2 + shift, NValue = 5 },
                ];
                // 杭先端 -10・径 1.2 m の上下 1D (範囲の端は杭の高さから求めるので、土質点とは別の計算で同じ値になる)
                double toe = -10.0 + shift, d = 1.2;
                var points = SoilPile.MassesInAltitudeRange(ground, toe - d, toe + d);
                Assert.AreEqual(3, points.Count, $"{shift:+0.00;-0.00} m 動かすと、範囲の端の土質点が外れます");
                Assert.AreEqual(25.0, points.Average(p => p.NValue), 1e-12);
            }

            string soilPile = TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs");
            StringAssert.Contains(soilPile, "=> MassesInAltitudeRange(GroundInput, PileToeNValueAverageRangeLowerAltitude, PileToeNValueAverageRangeUpperAltitude)", "一般式の平均 N 値が、範囲端の規則を通っていません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.SmartMagnum.cs"), "MassesInAltitudeRange(",
                "Smart-MAGNUM の平均 N 値が、範囲端の規則を共有していません");
            Assert.AreEqual(1, soilPile.Split('\n').Count(l => !l.TrimStart().StartsWith("//") && l.Contains("AltitudeDepth <=")),
                "平均 N 値の範囲の比較の写しが残っています");
        }

        /// <summary>
        /// 2. 基準標高 (Z=0 の標高) を変えると、任意入力の地盤変位の点も同じだけ動き、同じ深さの変位は変わらない。
        /// 以前は点を動かさず、地表 Z=0→2 で GL-5 m の変位が 50→70 mm になった。
        /// </summary>
        [TestMethod]
        public void ChangingTheReferenceAltitude_KeepsTheCustomGroundDisplacementAtTheSameDepth()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            var ground = input.GroundAt(1)!;
            var profile = ground.CustomDisplacementProfile;
            double top = ground.GroundTopAltitude;
            foreach (var points in new[] { profile.Level1NonLiq, profile.Level1Liq, profile.Level2NonLiq, profile.Level2Liq })
            {
                points.Clear();
                points.Add(new DisplacementPoint { Z = top, Displacement = 100 });
                points.Add(new DisplacementPoint { Z = top - 10, Displacement = 0 });
            }
            double Before(System.Collections.ObjectModel.ObservableCollection<DisplacementPoint> p) => profile.Interpolate(p, top - 5);
            double l1 = Before(profile.Level1NonLiq);
            Assert.AreEqual(50.0, l1, 1e-9, "(前提)");

            input.ShiftGroundZByDelta(2.0);
            double newTop = ground.GroundTopAltitude;
            Assert.AreEqual(top + 2.0, newTop, 1e-12);
            foreach (var points in new[] { profile.Level1NonLiq, profile.Level1Liq, profile.Level2NonLiq, profile.Level2Liq })
                Assert.AreEqual(50.0, profile.Interpolate(points, newTop - 5), 1e-9, "基準標高を変えると、同じ深さの地盤変位が変わります");
        }

        /// <summary>
        /// 3. ファイルを開いたときの標高の補正は、保存していた要素分割を捨てない (遅れて走る作り直しを予約しない)。
        /// 足した分割点は残り、直した地層の境は節点になる (要素が地層の境をまたがない)。
        /// </summary>
        [TestMethod]
        public void TheLoadTimeCorrection_KeepsTheSavedDivision_AndAddsTheCorrectedBoundary()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();   // 地盤ウィンドウの VM が見ていない実体
            var ground = input.GroundAt(1)!;
            var layer = ground.GroundLayers[1];
            double correctBottom = layer.BottomGLDepth + ground.GroundTopAltitude;
            layer.BottomAltitude = correctBottom - 0.4;   // 保存された標高が GL 深さと食い違っている
            input.GenerateSoilPiles();

            // 利用者が足した分割点
            var sp = input.ElementDivision.SoilPiles[0];
            double userSplit = (sp.ZDataItems[^2].Z + sp.ZDataItems[^1].Z) / 2.0;
            var withSplit = sp.ZDataItems.Select(z => z.Z).Append(userSplit).OrderByDescending(z => z)
                .Select(z => new PileZDataItem { Z = z, GroundInput = ground });
            sp.OnZDataItemsChanged(new System.Collections.ObjectModel.ObservableCollection<PileZDataItem>(withSplit));

            var fixes = MainWindowViewModel.SyncGroundAltitudesOnLoad(input);
            Assert.AreEqual(1, fixes.Count);
            var after = input.ElementDivision.SoilPiles[0];
            Assert.AreSame(sp, after, "土層-杭セットを作り直しています (保存していた要素分割が消える)");
            var zs = after.ZDataItems.Select(z => z.Z).ToList();
            Assert.IsTrue(zs.Any(z => Math.Abs(z - userSplit) < 1e-9), "利用者が足した分割点が消えています");
            Assert.IsTrue(zs.Any(z => Math.Abs(z - correctBottom) < 1e-9), "直した地層の境が節点になっていません");
            bool pending = (bool)typeof(InputModel).GetField("_regeneratePending",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(input)!;
            Assert.IsFalse(pending, "補正のあとに土層-杭セットの作り直しを予約しています (遅れて要素分割を捨てる)");
        }

        /// <summary>
        /// 4. 層厚と地層の境の食い違い。読み込みの補正で層厚も GL 深さから決め直し、解析前の検査は隙間・重なりを止める。
        /// 以前は第 2 層の層厚だけ 18 m (正しくは 16 m) にすると、検査は空のまま、Z=-5 の有効応力が 117 → 153 kN/m² になった。
        /// </summary>
        [TestMethod]
        public void LayerThicknessesThatDisagreeWithTheBoundaries_AreFixedOnLoadAndCaughtBeforeAnalysis()
        {
            var ground = new GroundInput { GroundTopAltitude = 1.5, GroundWaterGLDepth = -50, StressGLDepth = 0 };
            ground.GroundWaterTableAltitude = -48.5;
            ground.StressAltitude = 1.5;
            ground.GroundLayers =
            [
                new GroundLayerInput { BottomGLDepth = -4.0, BottomAltitude = -2.5, LayerThickness = 4.0, Density = 18 },
                new GroundLayerInput { BottomGLDepth = -20.0, BottomAltitude = -18.5, LayerThickness = 18.0, Density = 18 },
            ];
            ground.GroundMassesData = [];
            double sigmaWrong = SoilPile.GetEffectiveStress(ground, -5.0, ground.StressAltitude);

            using var vm = new MainWindowViewModel();
            var model = vm.CurrentInputModel!;
            model.GroundsInput.Clear();
            model.GroundsInput.Add(ground);
            var problems = CheckInputData.CollectGroundLayerGeometryProblems(model);
            Assert.IsTrue(problems.Any(p => p.Message.Contains("重なり")), "層厚と地層の境の食い違いを検査で止めていません");

            var fixes = ground.SyncAltitudesFromGLDepths();
            Assert.IsTrue(fixes.Any(f => f.StartsWith("土層の層厚")), "読み込みの補正で層厚を直していません");
            Assert.AreEqual(16.0, ground.GroundLayers[1].LayerThickness, 1e-12);
            double sigma = SoilPile.GetEffectiveStress(ground, -5.0, ground.StressAltitude);
            Assert.AreEqual(18.0 * 6.5, sigma, 1e-9, "有効応力 (地表 1.5 から -5 まで 6.5 m の土被り)");
            Assert.IsTrue(sigmaWrong > sigma, "(前提) 食い違ったままだと土被りを二重に数える");
            Assert.AreEqual(0, CheckInputData.CollectGroundLayerGeometryProblems(model).Count(p => p.Message.Contains("合っていません")));
        }
    }
}
