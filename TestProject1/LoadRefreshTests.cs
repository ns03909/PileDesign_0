using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 読み込みの仕上げで土層-杭セットを今の計算に合わせる (2026-10-24 の再レビュー 2 件)。
    /// </summary>
    [TestClass]
    public class LoadRefreshTests
    {
        private static InputModel Build()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) throw new AssertInconclusiveException("例題ファイルなし: " + err);
            return input;
        }

        /// <summary>
        /// 前の版の誤った支持力度を保存したファイルを開くと、支持力度を今の計算で求め直し、そのことを知らせる。
        /// 以前は平均 N 値の表示だけが直り、支持力度 (保存される値) は古いまま (平均 N=25 でも 4200 kN/m²、正しくは 3000) 残った。
        /// 今の版で保存した (今の計算と同じ) 値なら、何も言わない。
        /// </summary>
        [TestMethod]
        public void StaleBearingValuesFromAnEarlierVersion_AreRecomputedOnLoad()
        {
            var input = Build();
            Assert.AreEqual(0, MainWindowViewModel.RefreshSoilPileDerivedValuesOnLoad(input).Count,
                "今の計算と同じ値なのに、求め直したと言っています");

            var sp = input.ElementDivision.SoilPiles[0];
            double qpu = sp.Qpu, rfu = sp.Rfu, rpu = sp.Rpu;
            sp.Qpu = qpu * 1.4;    // 前の版の誤った値が保存されていた
            sp.Rfu = rfu * 1.2;
            var changes = MainWindowViewModel.RefreshSoilPileDerivedValuesOnLoad(input);
            Assert.AreEqual(1, changes.Count);
            StringAssert.Contains(changes[0], "先端支持力度");
            StringAssert.Contains(changes[0], "周面摩擦力");
            Assert.AreEqual(qpu, sp.Qpu, 1e-9, "支持力度を今の計算で求め直していません");
            Assert.AreEqual(rfu, sp.Rfu, 1e-9);
            Assert.AreEqual(rpu, sp.Rpu, 1e-9);

            string load = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            StringAssert.Contains(load, "RefreshSoilPileDerivedValuesOnLoad(CurrentInputModel)");
            StringAssert.Contains(load, "if (groundAltitudeFixes.Count > 0 || soilPileValueChanges.Count > 0)", "求め直したのに再解析を促していません");
        }

        /// <summary>
        /// 読み込み時の標高の補正のあとも、利用者が足した分割点は「削除できる」のまま。直した地層の境は固定点。
        /// 以前は Z だけを取り出して作り直したので、足した分割点がすべて削除できない固定点になった。
        /// </summary>
        [TestMethod]
        public void TheLoadTimeCorrection_KeepsUserPointsDeletable()
        {
            var input = Build();
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();   // 地盤ウィンドウの VM が見ていない実体
            var ground = input.GroundAt(1)!;
            var layer = ground.GroundLayers[1];
            double correctBottom = layer.BottomGLDepth + ground.GroundTopAltitude;
            layer.BottomAltitude = correctBottom - 0.4;
            input.GenerateSoilPiles();

            var sp = input.ElementDivision.SoilPiles[0];
            double userSplit = (sp.ZDataItems[^2].Z + sp.ZDataItems[^1].Z) / 2.0;
            var items = sp.ZDataItems.Select(z => z.DeepCopy()).ToList();
            items.Add(new PileZDataItem { Z = userSplit, GroundInput = ground, IsChangeable = true });
            sp.OnZDataItemsChanged(new System.Collections.ObjectModel.ObservableCollection<PileZDataItem>(items.OrderByDescending(z => z.Z)));

            MainWindowViewModel.SyncGroundAltitudesOnLoad(input);
            var nodes = input.ElementDivision.SoilPiles[0].ZDataItems;
            var user = nodes.Single(z => Math.Abs(z.Z - userSplit) < 1e-9);
            Assert.IsTrue(user.IsChangeable, "利用者が足した分割点が、削除できない固定点になっています");
            var boundary = nodes.Single(z => Math.Abs(z.Z - correctBottom) < 1e-9);
            Assert.IsFalse(boundary.IsChangeable, "直した地層の境を、削除できる点にしています");
        }
    }
}
