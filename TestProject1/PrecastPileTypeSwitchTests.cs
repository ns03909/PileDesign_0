using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 既製コンクリート杭の断面タイプ切替（PHC⇔PRC⇔SC）時の既定選択の検証。
    /// 旧選択名は新ライブラリに存在しないため、従来は断面欄が空欄のまま
    /// 旧タイプの諸元で計算が続いていた。同径（無ければ最近径）の杭を
    /// 既定選択するフォールバックを保証する。
    /// </summary>
    [TestClass]
    public class PrecastPileTypeSwitchTests
    {
        private static PileSection CreatePhcSection(double diameter)
        {
            var phc = PileSection.PHCs.FirstOrDefault(p => p.PileDiameter == diameter);
            Assert.IsNotNull(phc, $"PHC ライブラリに径 {diameter} がありません");

            var section = new PileSection
            {
                PileBodyType = "既製コンクリート杭",
                PileSectionType = "PHC杭",
            };
            section.SelectedPrecastPile = new PrecastPile { Name = phc.Name };
            section.RecalculateSelectedPrecastPile();
            return section;
        }

        [TestMethod]
        public void SwitchPhcToPrc_SelectsSameDiameterPile()
        {
            if (PileSection.PHCs.Count == 0 || PileSection.PRCs.Count == 0)
            {
                Assert.Inconclusive("杭ライブラリ CSV がテスト出力にありません");
                return;
            }

            var section = CreatePhcSection(600.0);
            Assert.AreEqual(600.0, section.SelectedPrecastPile.PileDiameter, 1e-9, "前提: PHC600 が選択済み");

            section.PileSectionType = "PRC杭";
            section.RecalculateSelectedPrecastPile();

            double expectedDia = PileSection.PRCs
                .OrderBy(p => Math.Abs(p.PileDiameter - 600.0))
                .First().PileDiameter;

            Assert.IsFalse(string.IsNullOrEmpty(section.SelectedPrecastPile.Name), "断面が空欄のまま（フォールバック未動作）");
            Assert.IsTrue(PileSection.PRCs.Any(p => p.Name == section.SelectedPrecastPile.Name),
                $"選択名 '{section.SelectedPrecastPile.Name}' が PRC ライブラリに存在しない");
            Assert.AreEqual(expectedDia, section.SelectedPrecastPile.PileDiameter, 1e-9,
                "同径（最近径）の杭が選ばれていない");
            Assert.AreEqual(expectedDia, section.PileDiameter, 1e-9, "PileSection の諸元に反映されていない");
        }

        [TestMethod]
        public void SwitchPhcToSc_SelectsSameDiameterPile()
        {
            if (PileSection.PHCs.Count == 0 || PileSection.SCs.Count == 0)
            {
                Assert.Inconclusive("杭ライブラリ CSV がテスト出力にありません");
                return;
            }

            var section = CreatePhcSection(600.0);

            section.PileSectionType = "SC杭";
            section.RecalculateSelectedPrecastPile();

            double expectedDia = PileSection.SCs
                .OrderBy(p => Math.Abs(p.PileDiameter - 600.0))
                .First().PileDiameter;

            Assert.IsTrue(PileSection.SCs.Any(p => p.Name == section.SelectedPrecastPile.Name),
                $"選択名 '{section.SelectedPrecastPile.Name}' が SC ライブラリに存在しない");
            Assert.AreEqual(expectedDia, section.SelectedPrecastPile.PileDiameter, 1e-9,
                "同径（最近径）の杭が選ばれていない");
        }

        /// <summary>名前がライブラリに存在する場合は従来どおりその杭を維持する（フォールバックが誤発動しない）。</summary>
        [TestMethod]
        public void ExistingName_IsKeptUnchanged()
        {
            if (PileSection.PHCs.Count == 0)
            {
                Assert.Inconclusive("杭ライブラリ CSV がテスト出力にありません");
                return;
            }

            var section = CreatePhcSection(600.0);
            string nameBefore = section.SelectedPrecastPile.Name;

            section.RecalculateSelectedPrecastPile();

            Assert.AreEqual(nameBefore, section.SelectedPrecastPile.Name, "既存の有効な選択が変更された");
        }
    }
}
