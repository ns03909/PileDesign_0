using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 座屈長が、地盤の液状化データから杭断面まで<b>実際に届いている</b>こと。
    ///
    /// 算定そのものは <see cref="SteelPipeBucklingTests"/> で固定しているが、
    /// それだけでは「地盤と杭を結ぶ経路が繋がっているか」が分からない。
    /// 繋がっていなければ座屈長は常に 0 になり、<b>低減は一度も効かないまま
    /// 単体テストは全部通る</b>。実際の例題の地盤に液状化を入れて確かめる。
    ///
    /// 例題の地盤ファイルは β を持たない（液状化の判定は地盤ウィンドウで計算され、
    /// 保存ファイルに入る）。ここではその計算結果に相当する値を直接入れている。
    /// </summary>
    [TestClass]
    public class SteelPipeBucklingWiringTests
    {
        /// <summary>設計例集 3.5 = 鋼管杭基礎。杭体は鋼管杭。</summary>
        private static InputModel? BuildSteelPipeModel(out string? skipReason)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (model == null) { skipReason = $"例題ロード失敗: {error}"; return null; }

            bool hasSteelPipe = model.PileBodies
                .Any(pb => (pb?.PileBodyType ?? "").Contains(PileTypeNames.SteelPipe));
            if (!hasSteelPipe) { skipReason = "例題 3.5 が鋼管杭ではありません"; return null; }

            skipReason = null;
            return model;
        }

        /// <summary>上から <paramref name="count"/> 番目までの土質点を液状化させる。</summary>
        private static void MakeLiquefied(GroundInput ground, int count, double beta = 0.5)
        {
            int applied = 0;
            foreach (var mass in ground.GroundMassesData)
            {
                if (applied >= count) break;
                mass.IsLiquefactionLayer = true;   // 液状化の判定を行った状態を模す
                mass.BetaL = new ObservableCollection<double?> { beta, beta };
                applied++;
            }
        }

        private static double SectionBucklingLength(InputModel model) =>
            model.PileBodies
                .Where(pb => (pb?.PileBodyType ?? "").Contains(PileTypeNames.SteelPipe))
                .SelectMany(pb => pb.PileBodySegments)
                .Select(seg => seg?.PileSection?.BucklingLength ?? 0.0)
                .DefaultIfEmpty(0.0)
                .Max();

        [TestMethod]
        public void WithoutLiquefaction_TheSectionHasNoBucklingLength()
        {
            var model = BuildSteelPipeModel(out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            model.GenerateSoilPiles();

            Assert.AreEqual(0.0, SectionBucklingLength(model), 1e-9,
                "液状化を入れていないのに座屈長が付いています");
        }

        [TestMethod]
        public void LiquefiedLayers_ReachTheSectionAsBucklingLength()
        {
            var model = BuildSteelPipeModel(out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            var ground = model.GroundsInput[0];
            double fourLayers = ground.GroundMassesData.Take(4).Sum(m => m.H ?? 0.0);
            Assert.IsTrue(fourLayers > 0,
                "土質点の層厚 H が入っていません。例題の読込で H が落ちていないか確認してください");

            MakeLiquefied(ground, count: 4);
            model.GenerateSoilPiles();

            double lk = SectionBucklingLength(model);
            Assert.IsTrue(lk > 0,
                "地盤に液状化区間を入れたのに、杭断面へ座屈長が届いていません。"
                + "地盤と杭を結ぶ経路 (GenerateSoilPiles) が繋がっているか確認してください。");

            // 杭頭は地表より下なので、液状化区間のうち杭が通る分だけが座屈長になる。
            // 算定そのものは SteelPipeBucklingTests が固定するので、ここでは
            // 「区間の合計を超えない」ことだけを確かめる。
            Assert.IsTrue(lk <= fourLayers + 1e-9,
                $"座屈長 {lk:F3} m が液状化区間の合計 {fourLayers:F3} m を超えています");

            // 液状化区間を伸ばせば座屈長も伸びる (= 中身が本当に地盤から来ている)
            MakeLiquefied(ground, count: 8);
            model.GenerateSoilPiles();
            Assert.IsTrue(SectionBucklingLength(model) > lk,
                "液状化区間を伸ばしても座屈長が変わりません");
        }

        [TestMethod]
        public void BucklingLength_LowersTheCompressionCapacity()
        {
            var model = BuildSteelPipeModel(out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            model.GenerateSoilPiles();
            var section = model.PileBodies
                .Where(pb => (pb?.PileBodyType ?? "").Contains(PileTypeNames.SteelPipe))
                .SelectMany(pb => pb.PileBodySegments)
                .Select(seg => seg?.PileSection)
                .FirstOrDefault(sec => sec != null && sec.PileDiameter > 0);
            Assert.IsNotNull(section, "鋼管杭の断面が見つかりません");

            double before = section!.BucklingLength;
            Assert.AreEqual(0.0, before, 1e-9);

            // 液状化を入れて再生成すると、許容圧縮応力度が下がる
            MakeLiquefied(model.GroundsInput[0], count: 6);
            model.GenerateSoilPiles();

            Assert.IsTrue(section.BucklingLength > 0, "座屈長が付いていません");
        }

        [TestMethod]
        public void OptionOff_KeepsTheBucklingLengthAtZero()
        {
            var model = BuildSteelPipeModel(out string? skip);
            if (model == null) { Assert.Inconclusive(skip); return; }

            MakeLiquefied(model.GroundsInput[0], count: 4);

            bool originalOption = ConcreteModelOptions.ConsiderSteelPipeColumnBuckling;
            try
            {
                ConcreteModelOptions.ConsiderSteelPipeColumnBuckling = false;
                model.GenerateSoilPiles();

                Assert.AreEqual(0.0, SectionBucklingLength(model), 1e-9,
                    "考慮しない設定なのに座屈長が付いています");
            }
            finally
            {
                // 静的オプションはプロセス全体で共有されるので必ず戻す
                ConcreteModelOptions.ConsiderSteelPipeColumnBuckling = originalOption;
            }
        }

        /// <summary>
        /// 設定が保存ファイルに残り、読み戻せること。
        ///
        /// 「書き出されるが復元されない」プロパティは何度も出ているので
        /// (README「暗黙の前提」1)、新しい設定は往復を確かめる。
        /// </summary>
        [TestMethod]
        public void TheSettingSurvivesSaveAndLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PileDesignTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                };
                var svc = new PileDesign.Services.FileOperationService(options);

                // Reset() は MainWindowViewModel を要るので、必要なものだけ用意する
                var input = new InputModel { FundamentalInput = new FundamentalInput() };
                Assert.IsTrue(input.FundamentalInput.ConsiderSteelPipeColumnBuckling, "既定は考慮する");

                input.FundamentalInput.ConsiderSteelPipeColumnBuckling = false;
                string file = Path.Combine(dir, "buckling.pdj");
                svc.SaveProjectData(file, input, new PileDesign.FEM.AnaModel(), null);

                var loaded = svc.LoadProjectData(file);
                Assert.IsFalse(loaded.InputModel.FundamentalInput.ConsiderSteelPipeColumnBuckling,
                    "設定が保存ファイルから復元されていません");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 後片付けの失敗は無視 */ }
            }
        }
    }
}
