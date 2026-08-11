using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// docx「計算条件・仮定」章 (WordDocument.Assumptions.cs) の検証。
    /// ConcreteModelOptions は static のため、各テストで退避・復元して汚染を防ぐ。
    /// </summary>
    [TestClass]
    public class AssumptionsSectionTests
    {
        /// <summary>ConcreteModelOptions の全 static 状態を退避し、Dispose で復元する。</summary>
        private sealed class OptionsScope : IDisposable
        {
            private readonly bool _tension = ConcreteModelOptions.IgnoreTensileStrength;
            private readonly bool _reduced = ConcreteModelOptions.UseReducedCompression;
            private readonly bool _rebar = ConcreteModelOptions.RebarYieldAt11F;
            private readonly bool _pipe = ConcreteModelOptions.SteelPipeYieldAt11F;
            private readonly bool _unitGsi = ConcreteModelOptions.UseUnitGsiForConcreteE;
            private readonly bool _n1113c = ConcreteModelOptions.UseNotification1113Compression;
            private readonly bool _n1113s = ConcreteModelOptions.UseNotification1113Shear;
            private readonly bool _eFunc = ConcreteModelOptions.UseInsituUltimateEFunction;
            private readonly bool _fiber = ConcreteModelOptions.UseFiberMPhi;
            private readonly int _case = ConcreteModelOptions.Notification1113CompressionCase;

            public static OptionsScope AllDefaults()
            {
                var scope = new OptionsScope();
                ConcreteModelOptions.IgnoreTensileStrength = false;
                ConcreteModelOptions.UseReducedCompression = false;
                ConcreteModelOptions.RebarYieldAt11F = false;
                ConcreteModelOptions.SteelPipeYieldAt11F = false;
                ConcreteModelOptions.UseUnitGsiForConcreteE = false;
                ConcreteModelOptions.UseNotification1113Compression = false;
                ConcreteModelOptions.UseNotification1113Shear = false;
                ConcreteModelOptions.UseInsituUltimateEFunction = false;
                ConcreteModelOptions.UseFiberMPhi = false;
                ConcreteModelOptions.Notification1113CompressionCase = 1;
                return scope;
            }

            public void Dispose()
            {
                ConcreteModelOptions.IgnoreTensileStrength = _tension;
                ConcreteModelOptions.UseReducedCompression = _reduced;
                ConcreteModelOptions.RebarYieldAt11F = _rebar;
                ConcreteModelOptions.SteelPipeYieldAt11F = _pipe;
                ConcreteModelOptions.UseUnitGsiForConcreteE = _unitGsi;
                ConcreteModelOptions.UseNotification1113Compression = _n1113c;
                ConcreteModelOptions.UseNotification1113Shear = _n1113s;
                ConcreteModelOptions.UseInsituUltimateEFunction = _eFunc;
                ConcreteModelOptions.UseFiberMPhi = _fiber;
                ConcreteModelOptions.Notification1113CompressionCase = _case;
            }
        }

        [TestMethod]
        public void BuildMaterialOptionRows_Defaults_Returns12RowsWithDefaultChoices()
        {
            using var _ = OptionsScope.AllDefaults();

            var rows = WordDocument.BuildMaterialOptionRows();

            Assert.AreEqual(12, rows.Count, "材料モデル化オプションの行数");
            Assert.IsTrue(rows[0].Choice.Contains("個別選択"), "2025解説書はチェックなし表示のはず");
            Assert.IsTrue(rows.Count(r => r.Choice.Contains("既定")) >= 8,
                "全既定なら大半の行に（既定）表記が付くはず");
            // 圧縮・せん断の 2 行が既定呼称「使用限界」を項目名に含む
            // (区分行の「告示1113(第8) 長期許容応力度」は告示側の固有名詞なので対象外)
            Assert.AreEqual(2, rows.Count(r => r.Item.Contains("使用限界")), "既定では「使用限界」呼称のはず");
            // 告示オプション OFF なら区分は対象外
            var caseRow = rows.Single(r => r.Item.Contains("区分"));
            Assert.AreEqual("—", caseRow.Choice);
        }

        [TestMethod]
        public void BuildMaterialOptionRows_Guideline2025_MapsLabelsAndCase()
        {
            using var _ = OptionsScope.AllDefaults();
            ConcreteModelOptions.UseNotification1113Compression = true;
            ConcreteModelOptions.UseNotification1113Shear = true;
            ConcreteModelOptions.Notification1113CompressionCase = 2;

            var rows = WordDocument.BuildMaterialOptionRows();

            Assert.AreEqual("準拠", rows[0].Choice);
            // MapLimitStateText により項目名の「使用限界・損傷限界」は「長期許容・短期許容」へ置換される
            Assert.IsTrue(rows.Any(r => r.Item.Contains("長期許容")), "呼称が長期許容へ置換されるはず");
            Assert.IsFalse(rows.Any(r => r.Item.Contains("使用限界")), "「使用限界」が残ってはいけない");
            var caseRow = rows.Single(r => r.Item.Contains("区分"));
            Assert.AreEqual("区分 2", caseRow.Choice);
            Assert.IsTrue(caseRow.Note.Contains("Fc/4.5"), "区分2 の式が説明に含まれるはず");
        }

        [TestMethod]
        public void BuildMaterialOptionRows_FiberMphi_ShowsFiberChoice()
        {
            using var _ = OptionsScope.AllDefaults();
            ConcreteModelOptions.UseFiberMPhi = true;

            var rows = WordDocument.BuildMaterialOptionRows();

            var mphiRow = rows.Single(r => r.Item.Contains("M-φ"));
            Assert.AreEqual("ファイバーモデル", mphiRow.Choice);
            Assert.IsTrue(mphiRow.Note.Contains("β1・β2"), "β 低減を乗じない旨の説明があるはず");
        }

        [TestMethod]
        public void BuildDesignConditionRows_MinimalModel_ReturnsGradeConnectionCorrosionKh0()
        {
            using var _ = OptionsScope.AllDefaults();
            var inputModel = new InputModel();

            var rows = WordDocument.BuildDesignConditionRows(inputModel);

            Assert.AreEqual(4, rows.Count, "設計条件の行数（グレード/接続/腐食代/kh0）");
            Assert.IsTrue(rows[0].Item.Contains("グレード"));
            Assert.IsTrue(rows[1].Value is "剛体連結" or "剛床連結");
            var kh0Row = rows.Single(r => r.Item.Contains("kh0"));
            Assert.AreEqual("自動算定", kh0Row.Value, "既定モデルでは kh0 手入力なしのはず");
        }

        // ===== Phase 2 回帰: 「基礎部材の強度と変形性能」節のオプション分岐 =====

        private static string RenderMemberCapacitiesText()
        {
            var body = new Body();
            WordDocument.AddSectionMemberCapacities(body);
            return body.InnerText;
        }

        [TestMethod]
        public void MemberCapacities_UnitGsiForEc_ShowsNoteAndDropsXiFromEcFormula()
        {
            using var _ = OptionsScope.AllDefaults();
            ConcreteModelOptions.UseUnitGsiForConcreteE = true;

            string text = RenderMemberCapacitiesText();

            Assert.IsTrue(text.Contains("Ec の算定では ξ = 1.0"), "ξ=1.0 の注記が出力されるはず");
            Assert.IsFalse(text.Contains("ζ"), "旧ハードコードの ζ が残ってはいけない");
        }

        [TestMethod]
        public void MemberCapacities_Notification1113ShearCase2_ShowsOnlySelectedCase()
        {
            using var _ = OptionsScope.AllDefaults();
            ConcreteModelOptions.UseNotification1113Shear = true;
            ConcreteModelOptions.Notification1113CompressionCase = 2;

            string text = RenderMemberCapacitiesText();

            Assert.IsTrue(text.Contains("区分2を適用"), "適用区分の明記が必要");
            Assert.IsFalse(text.Contains("区分2は"), "旧・両区分併記の文字列が残ってはいけない");
        }

        [TestMethod]
        public void MemberCapacities_EFunction_ShowsUnreducedMuWithoutBetaFormula()
        {
            using var _ = OptionsScope.AllDefaults();
            ConcreteModelOptions.UseInsituUltimateEFunction = true;

            string text = RenderMemberCapacitiesText();

            Assert.IsTrue(text.Contains("軸力適用範囲の制限は課さない"), "低減なしの注記が必要");
            Assert.IsTrue(text.Contains("e関数法"), "e関数法の明記が必要");
        }

        [TestMethod]
        public void MemberCapacities_Default_ExplainsPolylineMphi()
        {
            using var _ = OptionsScope.AllDefaults();

            string text = RenderMemberCapacitiesText();

            Assert.IsTrue(text.Contains("指針ポリリニア"), "既定でも M-φ の作り方の説明が必要");
            Assert.IsTrue(text.Contains("単調非減少化"), "単調非減少化の後処理の明記が必要");
        }

        [TestMethod]
        public void AddAssumptionTable_ProducesValidOpenXml()
        {
            using var _ = OptionsScope.AllDefaults();
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                var body = new Body();
                WordDocument.AddAssumptionTable(body, "項目", "選択", "内容",
                    WordDocument.BuildMaterialOptionRows()
                        .Select(r => (r.Item, r.Choice, r.Note)));
                mainPart.Document = new Document(body);
                mainPart.Document.Save();

                var validator = new OpenXmlValidator();
                var errors = validator.Validate(doc).ToList();
                Assert.AreEqual(0, errors.Count,
                    "OpenXml 検証エラー: " + string.Join(" / ", errors.Select(e => e.Description)));
            }
        }
    }
}
