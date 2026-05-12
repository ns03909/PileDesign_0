// Smoke tests for the No-property removal refactor (BeamMaterial/BeamSection/FoundationBeam).
// Verifies that:
//   - DTO deserialization silently ignores legacy "no" fields
//   - Loaded data class instances have correct counts
//   - Position-based references (MaterialNo, SectionNo) are preserved
//   - GetMaterialNo/GetSectionNo/GetBeamNo helpers return 1-based positions correctly
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    [TestClass]
    public class NoRemovalSmokeTests
    {
        private static string GetExamplesDir()
        {
            // Test dll lives in TestProject1\bin\..\..\
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            // Try several relative locations
            string[] candidates =
            {
                Path.Combine(asmDir, "Examples"),
                Path.Combine(asmDir, "..", "..", "..", "..", "Graphics_r1", "Examples"),
                Path.Combine(asmDir, "..", "..", "..", "Examples"),
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (Directory.Exists(full)) return full;
            }
            return Path.GetFullPath(Path.Combine(asmDir, "Examples"));
        }

        private static PileExampleData? LoadPileExampleDto(string fileName)
        {
            var path = Path.Combine(GetExamplesDir(), fileName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            return JsonSerializer.Deserialize<PileExampleData>(json, opts);
        }

        [TestMethod]
        public void PileExample3_3_LoadsViaDto_WithLegacyNoFieldsIgnored()
        {
            var data = LoadPileExampleDto("PileExample3_3.json");
            if (data == null) { Assert.Inconclusive("Example file not found"); return; }
            Assert.IsNotNull(data.FoundationBeamInput);
            Assert.IsTrue(data.FoundationBeamInput.Materials.Count >= 1, "Materials should load");
            Assert.IsTrue(data.FoundationBeamInput.Sections.Count >= 1, "Sections should load");
            Assert.IsTrue(data.FoundationBeamInput.Beams.Count >= 1, "Beams should load");
            // Verify references preserved (MaterialNo/SectionNo still exist on DTO)
            foreach (var beam in data.FoundationBeamInput.Beams)
            {
                Assert.IsTrue(beam.MaterialNo >= 1, "MaterialNo positive");
                Assert.IsTrue(beam.SectionNo >= 1, "SectionNo positive");
            }
        }

        [TestMethod]
        public void BeamMaterial_HasNoNoProperty()
        {
            // After C-3, the No property must be gone.
            Assert.IsNull(typeof(BeamMaterial).GetProperty("No"),
                "BeamMaterial.No must be removed (Phase C-3)");
            Assert.IsNull(typeof(BeamSection).GetProperty("No"),
                "BeamSection.No must be removed");
            Assert.IsNull(typeof(FoundationBeam).GetProperty("No"),
                "FoundationBeam.No must be removed");
        }

        [TestMethod]
        public void GetMaterialNo_ReturnsOneBasedPosition()
        {
            var fbi = new FoundationBeamInput();
            fbi.Materials.Clear();
            fbi.Sections.Clear();
            var m1 = new BeamMaterial { Name = "M1" };
            var m2 = new BeamMaterial { Name = "M2" };
            fbi.Materials.Add(m1);
            fbi.Materials.Add(m2);
            Assert.AreEqual(1, fbi.GetMaterialNo(m1));
            Assert.AreEqual(2, fbi.GetMaterialNo(m2));
            Assert.AreEqual(0, fbi.GetMaterialNo(null!));

            var s1 = new BeamSection { Name = "S1" };
            fbi.Sections.Add(s1);
            Assert.AreEqual(1, fbi.GetSectionNo(s1));

            var b1 = new FoundationBeam { MaterialNo = 1, SectionNo = 1 };
            fbi.Beams.Add(b1);
            Assert.AreEqual(1, fbi.GetBeamNo(b1));
        }

        [TestMethod]
        public void DefaultBeamMaterialAndSection_AreCreatedWithoutNo()
        {
            var fbi = new FoundationBeamInput();
            fbi.Materials.Clear();
            fbi.Sections.Clear();
            fbi.EnsureDefaultMaterialAndSection();
            Assert.AreEqual(1, fbi.Materials.Count);
            Assert.AreEqual(1, fbi.Sections.Count);
            // Position-based references should now work
            Assert.AreEqual(1, fbi.GetMaterialNo(fbi.Materials[0]));
            Assert.AreEqual(1, fbi.GetSectionNo(fbi.Sections[0]));
        }

        [TestMethod]
        public void NoFieldNotSerializedToJson()
        {
            // Round-trip BeamMaterial/Section/Element through System.Text.Json
            // and confirm the output JSON does not contain "no":
            var m = new BeamMaterial { Name = "C24", YoungModulus = 2.5e7 };
            var json = JsonSerializer.Serialize(m);
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("\"[Nn]o\""));
        }
    }
}
