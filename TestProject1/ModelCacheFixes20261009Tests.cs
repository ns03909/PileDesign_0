using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の見直しで見つかった、モデル側のキャッシュと保存の形の誤り。
    /// どれも「編集中は正しく見えて、差し替え・保存往復のあとでだけ古い値になる」形だった。
    /// (耐力曲線のキャッシュは CurveCacheInvalidationTests の側で見る)
    /// </summary>
    [TestClass]
    public class ModelCacheFixes20261009Tests
    {
        private static FileOperationService Service() => new(new JsonSerializerOptions
        {
            WriteIndented = false,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        });

        private static (InputModel Model, string Json) SaveAndLoad(InputModel input)
        {
            string path = Path.Combine(Path.GetTempPath(), $"pd_cachefix_{Guid.NewGuid():N}.pdjson");
            try
            {
                Service().SaveProjectData(path, input, null);
                string json = File.ReadAllText(path);
                return (Service().LoadProjectData(path).InputModel, json);
            }
            finally { File.Delete(path); }
        }

        private static SoilPile NewSoilPile(int groundNo, int pileBodyNo, double z)
            => new() { GroundNo = groundNo, PileBodyNo = pileBodyNo, Z = z };

        /// <summary>
        /// 要素分割・沈下のウィンドウは OK で ElementDivision.SoilPiles を新しいコレクションに差し替える。
        /// 以前は検索キャッシュが GenerateSoilPiles の経路でしか捨てられず、差し替え前の
        /// SoilPile を返し続けた (PileLayoutDataItem.SoilPile は LookupSoilPile を引く)。
        /// </summary>
        [TestMethod]
        public void LookupSoilPile_FollowsAReplacedCollection()
        {
            var model = new MainWindowViewModel().CurrentInputModel!;
            Assert.IsNotNull(model.ElementDivision, "(前提) 要素分割がありません");

            var old = NewSoilPile(1, 1, -2.0);
            model.ElementDivision.SoilPiles = [old];
            Assert.AreSame(old, model.LookupSoilPile(1, 1, -2.0), "(前提) 引けていません");

            // ウィンドウの OK と同じ書き方 (DeepCopy した組で差し替える)
            var replaced = NewSoilPile(1, 1, -2.0);
            model.ElementDivision.SoilPiles = new ObservableCollection<SoilPile>([replaced]);

            Assert.AreSame(replaced, model.LookupSoilPile(1, 1, -2.0),
                "SoilPiles を差し替えても、差し替え前の SoilPile が返っています");
        }

        /// <summary>同じ入れ物への追加 (件数が変わる) にも追従すること。</summary>
        [TestMethod]
        public void LookupSoilPile_FollowsAnAddedItem()
        {
            var model = new MainWindowViewModel().CurrentInputModel!;
            model.ElementDivision.SoilPiles = [NewSoilPile(1, 1, -2.0)];
            Assert.IsNull(model.LookupSoilPile(2, 1, -2.0), "(前提) まだ無い組が引けています");

            var added = NewSoilPile(2, 1, -2.0);
            model.ElementDivision.SoilPiles.Add(added);

            Assert.AreSame(added, model.LookupSoilPile(2, 1, -2.0), "追加した組が引けません");
        }

        /// <summary>
        /// 杭配置の行が引く土層-杭セットも、差し替えたあとの組であること
        /// (鉛直解析のモデル化・検定の総括・杭配置表の支持力欄がこれを読む)。
        /// </summary>
        [TestMethod]
        public void PileLayoutSoilPile_FollowsAReplacedCollection()
        {
            var model = new MainWindowViewModel().CurrentInputModel!;
            var pile = new PileLayoutDataItem { No = 1, GroundNo = 1, PileBodyNo = 1 };
            pile.SetOwner(model);

            var old = NewSoilPile(pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ);
            model.ElementDivision.SoilPiles = [old];
            Assert.AreSame(old, pile.SoilPile, "(前提) 杭配置の行から引けていません");

            var replaced = NewSoilPile(pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ);
            model.ElementDivision.SoilPiles = new ObservableCollection<SoilPile>([replaced]);

            Assert.AreSame(replaced, pile.SoilPile, "杭配置の行が差し替え前の土層-杭セットを返しています");
        }

        /// <summary>
        /// 土圧抵抗ばねの ΔP・ysp が保存ファイルを往復すること。
        /// 以前は公開フィールドで書き出されず、開き直すと理論曲線が消えていた。
        /// </summary>
        [TestMethod]
        public void DoatsuGoryokuBane_DeltaPAndYsp_SurviveASaveAndLoad()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            Assert.IsNotNull(input.ElementDivision, "(前提) 要素分割がありません");
            input.ElementDivision.DoatsuGoryokuBane ??= new DoatsuGoryokuBane();
            input.ElementDivision.DoatsuGoryokuBane.DeltaP = 12.5;
            input.ElementDivision.DoatsuGoryokuBane.Ysp = 1.25;

            var (restored, _) = SaveAndLoad(input);

            var bane = restored.ElementDivision?.DoatsuGoryokuBane;
            Assert.IsNotNull(bane, "土圧抵抗ばねが読み戻せません");
            Assert.AreEqual(12.5, bane!.DeltaP, 1e-12, "ΔP が保存往復で消えています");
            Assert.AreEqual(1.25, bane.Ysp, 1e-12, "ysp が保存往復で消えています");
        }

        /// <summary>
        /// 通り心の杭 (GridDataItem.Piles) はグラフを開くたびに作り直す一時データなので書き出さない。
        /// 以前は書き出しており、グラフを開いたあとで削除した杭まで保存されていた。
        /// </summary>
        [TestMethod]
        public void GridPiles_AreNotSaved_SoDeletedPilesDoNotComeBack()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            if (input.GridXItems.Count == 0)
                input.GridXItems.Add(new GridDataItem { No = 1, Name = "X1", Coord = 0.0 });
            var grid = input.GridXItems[0];

            var deleted = new PileLayoutDataItem { No = 9999, X = grid.Coord, Y = 0.0 };
            grid.Piles = [deleted];   // グラフを開いたときの状態 (SetPiles の結果)
            // 杭配置からは消えている (グラフを開いたあとで削除した)
            Assert.IsFalse(input.PileLayoutItems.Contains(deleted), "(前提) 削除した杭が杭配置に残っています");

            var (restored, json) = SaveAndLoad(input);

            Assert.IsFalse(json.Contains("\"Piles\"", StringComparison.Ordinal),
                "通り心の杭 (描画用の一時データ) が保存ファイルに書き出されています");
            Assert.AreEqual(0, restored.GridXItems[0].Piles.Count,
                "削除した杭が通り心の杭として読み戻されています");
        }
    }
}
