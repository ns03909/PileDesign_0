using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 直近のセッションで追加した機能の堅牢性確認スモークテスト群。
    /// - SettlementSoilLayer.GranularityClass の永続化と土層コピー
    /// - PileSection.IsSelectedPrecastPileInLibrary の判定
    /// - DxfExporter / Rhino3dmExporter の杭頭 Z 計算 (v2 セマンティクス確認)
    /// </summary>
    [TestClass]
    public class RecentFeaturesSmokeTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RecentFeaturesSmoke_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
            }
        }

        // --- SettlementSoilLayer.GranularityClass --------------------------

        [TestMethod]
        public void SettlementSoilLayer_DefaultGranularityClassIsEmpty()
        {
            var layer = new SettlementSoilLayer();
            Assert.AreEqual("", layer.GranularityClass);
        }

        [TestMethod]
        public void SettlementSoilLayer_NullGranularityClassIsCoercedToEmpty()
        {
            var layer = new SettlementSoilLayer
            {
                GranularityClass = null!  // setter は null を "" に正規化する
            };
            Assert.AreEqual("", layer.GranularityClass);
        }

        [TestMethod]
        public void SettlementSoilLayer_GranularityClassRoundTripsThroughJson()
        {
            // InputModel のパラメータレスコンストラクタは Reset() を呼ばないため、
            // PileGroupSettlement を明示的に作成する。
            var input = new InputModel
            {
                PileGroupSettlement = new PileGroupSettlement()
            };
            input.PileGroupSettlement.SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>
            {
                new() { BottomAltitude = -5, Thickness = 5, Ek = 28000, PoissonsRatio = 0.3, GranularityClass = "砂質土" },
                new() { BottomAltitude = -7, Thickness = 2, Ek = 8400, PoissonsRatio = 0.45, GranularityClass = "粘性土" },
                new() { BottomAltitude = -10, Thickness = 3, Ek = 28000, PoissonsRatio = 0.3, GranularityClass = "礫質土" },
            };

            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "soil.json");
            svc.SaveProjectData(file, input, new AnaModel(), null);

            var loaded = svc.LoadProjectData(file);
            var layers = loaded.InputModel.PileGroupSettlement.SettlementSoilLayers;
            Assert.AreEqual(3, layers.Count);
            Assert.AreEqual("砂質土", layers[0].GranularityClass);
            Assert.AreEqual("粘性土", layers[1].GranularityClass);
            Assert.AreEqual("礫質土", layers[2].GranularityClass);
        }

        // --- PileSection.IsSelectedPrecastPileInLibrary -------------------

        [TestMethod]
        public void IsSelectedPrecastPileInLibrary_EmptyName_ReturnsTrue()
        {
            var sec = new PileSection { PileBodyType = "既製コンクリート杭", PileSectionType = "PHC杭" };
            // SelectedPrecastPile は新規 PrecastPile() なので Name="" が初期値
            Assert.IsTrue(sec.IsSelectedPrecastPileInLibrary(),
                "名前未指定 (空文字) の杭断面は検査対象外として true を返すべき");
        }

        [TestMethod]
        public void IsSelectedPrecastPileInLibrary_SteelPipe_ReturnsTrue()
        {
            // 既製コンクリート杭以外 (鋼管杭等) の検査は対象外
            var sec = new PileSection { PileSectionType = "鋼管杭" };
            Assert.IsTrue(sec.IsSelectedPrecastPileInLibrary());
        }

        [TestMethod]
        public void IsSelectedPrecastPileInLibrary_NameInPHCs_ReturnsTrue()
        {
            var sec = new PileSection { PileBodyType = "既製コンクリート杭", PileSectionType = "PHC杭" };
            // ライブラリに存在する代表的な名前
            sec.SelectedPrecastPile = new PrecastPile { Name = "PHC-600-標準-80-A" };
            Assert.IsTrue(sec.IsSelectedPrecastPileInLibrary(),
                "ライブラリに存在する名前は true を返すべき");
        }

        [TestMethod]
        public void IsSelectedPrecastPileInLibrary_NameNotInLibrary_ReturnsFalse()
        {
            var sec = new PileSection { PileBodyType = "既製コンクリート杭", PileSectionType = "PHC杭" };
            sec.SelectedPrecastPile = new PrecastPile { Name = "PHC-600-NONEXISTENT" };
            Assert.IsFalse(sec.IsSelectedPrecastPileInLibrary(),
                "ライブラリに存在しない名前は false を返すべき");
        }

        [TestMethod]
        public void IsSelectedPrecastPileInLibrary_OldFormatName_ReturnsFalse()
        {
            // PileExample5 の旧 typo 例。修正前は PHC-600-A-80-C になっていた
            var sec = new PileSection { PileBodyType = "既製コンクリート杭", PileSectionType = "PHC杭" };
            sec.SelectedPrecastPile = new PrecastPile { Name = "PHC-600-A-80-C" };
            Assert.IsFalse(sec.IsSelectedPrecastPileInLibrary(),
                "旧フォーマットの誤った名前は検出されるべき");
        }

        // --- 診断: PHCs ライブラリの中身を直接確認 ------------------------

        [TestMethod]
        public void Diag_PHCs_DoesNotContainNONEXISTENT()
        {
            // PileSection.PHCs に "PHC-600-NONEXISTENT" が含まれていない (CSV ロードの確認)
            bool exists = PileSection.PHCs.Any(p => p.Name == "PHC-600-NONEXISTENT");
            Assert.IsFalse(exists, $"PHCs.Count={PileSection.PHCs.Count}, contains PHC-600-NONEXISTENT? {exists}");
        }

        [TestMethod]
        public void Diag_PHCs_ContainsCorrectName()
        {
            bool exists = PileSection.PHCs.Any(p => p.Name == "PHC-600-標準-80-A");
            Assert.IsTrue(exists, $"PHCs.Count={PileSection.PHCs.Count}, contains PHC-600-標準-80-A? {exists}");
        }

        [TestMethod]
        public void Diag_IsSelectedPrecastPileInLibrary_DirectStateCheck()
        {
            var sec = new PileSection { PileBodyType = "既製コンクリート杭", PileSectionType = "PHC杭" };
            sec.SelectedPrecastPile = new PrecastPile { Name = "PHC-600-NONEXISTENT" };

            // 設定後の実際の値を確認
            Assert.AreEqual("PHC-600-NONEXISTENT", sec.SelectedPrecastPile?.Name,
                "SelectedPrecastPile.Name が期待値と異なる");
            Assert.AreEqual("PHC杭", sec.PileSectionType,
                "PileSectionType が期待値と異なる");

            bool inLib = sec.IsSelectedPrecastPileInLibrary();
            Assert.IsFalse(inLib,
                $"PHCs.Count={PileSection.PHCs.Count}, sec.SelectedPrecastPile.Name={sec.SelectedPrecastPile?.Name}, " +
                $"sec.PileSectionType={sec.PileSectionType}, IsSelectedPrecastPileInLibrary={inLib}");
        }

        // --- PileLayoutDataItem.PileHeadZ (v2 セマンティクス再確認) -----------

        [TestMethod]
        public void PileHeadZ_EqualsZMinusFoundationBeamDeltaZc()
        {
            var pile = new PileLayoutDataItem
            {
                Z = 5.0,
                FoundationBeamDeltaZc = 2.0
            };
            Assert.AreEqual(3.0, pile.PileHeadZ, 1e-12);
        }

        [TestMethod]
        public void PileHeadZ_NotifiesWhenZChanges()
        {
            var pile = new PileLayoutDataItem
            {
                Z = 5.0,
                FoundationBeamDeltaZc = 2.0
            };

            var raised = new System.Collections.Generic.List<string>();
            pile.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            pile.Z = 7.0;
            Assert.IsTrue(raised.Contains(nameof(PileLayoutDataItem.PileHeadZ)),
                "Z 変更時に PileHeadZ の PropertyChanged が発火すべき");
        }

        [TestMethod]
        public void PileHeadZ_NotifiesWhenDeltaZcChanges()
        {
            var pile = new PileLayoutDataItem
            {
                Z = 5.0,
                FoundationBeamDeltaZc = 2.0
            };

            var raised = new System.Collections.Generic.List<string>();
            pile.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            pile.FoundationBeamDeltaZc = 1.5;
            Assert.IsTrue(raised.Contains(nameof(PileLayoutDataItem.PileHeadZ)),
                "ΔZc 変更時に PileHeadZ の PropertyChanged が発火すべき");
        }

        // --- FoundationBeamInput の MaterialNoOptions / SectionNoOptions ---

        [TestMethod]
        public void FoundationBeamInput_OptionListsTrackCount()
        {
            var input = new FoundationBeamInput();

            Assert.AreEqual(0, input.MaterialNoOptions.Count);
            Assert.AreEqual(0, input.SectionNoOptions.Count);

            input.Materials.Add(new BeamMaterial { Name = "FC30" });
            input.Materials.Add(new BeamMaterial { Name = "FC36" });
            input.Sections.Add(new BeamSection { Name = "FG1" });

            Assert.AreEqual(2, input.MaterialNoOptions.Count);
            CollectionAssert.AreEqual(new List<int> { 1, 2 }, input.MaterialNoOptions.ToList());
            Assert.AreEqual(1, input.SectionNoOptions.Count);
            CollectionAssert.AreEqual(new List<int> { 1 }, input.SectionNoOptions.ToList());

            input.Materials.RemoveAt(0);
            Assert.AreEqual(1, input.MaterialNoOptions.Count);
        }

        // --- ProjectData FormatVersion (v2 確認) -------------------------

        [TestMethod]
        public void ProjectData_DefaultSaveWritesFormatVersion2()
        {
            var svc = new FileOperationService(MakeOptions());
            var file = Path.Combine(_tempDir, "fv.json");
            svc.SaveProjectData(file, new InputModel(), new AnaModel(), null);

            var loaded = svc.LoadProjectData(file);
            Assert.AreEqual(2, loaded.FormatVersion);
        }
    }
}
