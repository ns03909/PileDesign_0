using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 既製杭の終局圧縮ひずみが 1 つに揃っていること。
    ///
    /// 既製杭はプレストレスを断面ひずみに含めない規約なので、材料の全ひずみが εcu に
    /// なる断面ひずみは <c>EpsilonCu − Prestrain</c> でプレストレスぶん小さい。
    /// 安全限界 N-M 曲線もファイバー M-φ の終点もこの値で作る。
    ///
    /// ところが折線 M-φ の終点 Mu0 を解く基底のメソッドだけが 0.003 を直値で書いていた。
    /// 同じ軸力なのに N-M 曲線とは違う終局ひずみで解かれ、終点が曲線から外れていた。
    /// 「断面分割積分による M-φ」の切替で終点が変わるのはこれが理由。
    /// </summary>
    [TestClass]
    public class PrecastUltimateStrainTests
    {
        private static AbstractPileSection MakePhc()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.Phc,
            };
            s.PileDiameter = 600;
            s.ConcreteThickness = 90;
            s.ConcreteFc = 85;
            s.TendonDp = 420;
            s.TendonAp = 400;
            s.TendonSigmaPy = 1275;
            s.TendonSigmaPu = 1420;
            s.Prestress = 8.0;
            return (AbstractPileSection)s.CreateSectionCalculator()!;
        }

        /// <summary>
        /// 終局圧縮ひずみは、プレストレスぶん小さいこと。
        /// 基底の直値 0.003 のままではない。
        /// </summary>
        [TestMethod]
        public void APrecastSection_ReportsItsOwnUltimateStrain()
        {
            var section = MakePhc();

            Assert.AreNotEqual(SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN,
                section.UltimateCompressiveStrain,
                "既製杭が基底の直値 0.003 をそのまま返している");

            Assert.IsTrue(section.UltimateCompressiveStrain > 0.0,
                "終局圧縮ひずみが 0 以下");
            Assert.IsTrue(section.UltimateCompressiveStrain < SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN,
                "プレストレスぶん小さくなっていない");
        }

        /// <summary>
        /// ファイバー掃引の上限も同じ値であること。
        /// 片方だけ動かすと、2 つの M-φ が別の終点を持つ。
        /// </summary>
        [TestMethod]
        public void TheFiberSweepLimit_MatchesTheUltimateStrain()
        {
            var section = MakePhc();

            Assert.AreEqual(section.UltimateCompressiveStrain, section.FiberSweepEdgeStrainMax, 1e-12,
                "ファイバー掃引の上限と終局圧縮ひずみが食い違っている");
        }

        /// <summary>
        /// 折線 M-φ の終点を解くメソッドが、直値ではなく断面の終局ひずみを使うこと。
        ///
        /// 値の一致だけでは、直値と偶然そろっている断面で見逃す。式そのものを押さえる。
        /// </summary>
        [TestMethod]
        public void ThePolylineEndpoint_UsesTheSectionsOwnStrain()
        {
            var body = ExtractMethodBody(
                ReadSource("Graphics_r1", "Models", "InputData", "AbstractPileSection.cs"),
                "internal virtual (double, double) GetUltimateMomentForSpecificN(double NTarget)");

            StringAssert.Contains(body, "double epsilonC = UltimateCompressiveStrain;",
                "折線 M-φ の終点が、断面の終局ひずみではなく直値で解かれている");
            Assert.IsFalse(body.Contains("SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN"),
                "直値が残っている");
        }

        /// <summary>
        /// 場所打ち系は従来どおり 0.003 であること (KCTB オプション時を除く)。
        /// 既製杭側の変更が他の杭種へ漏れていないかの確認。
        /// </summary>
        [TestMethod]
        public void CastInPlaceSections_AreUnchanged()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
            };
            s.ConcreteOutDia = 1000;
            s.ConcreteGsi = 1.0;
            s.ConcreteFc = 27;
            s.MainBarNum = 20;
            s.MainBarSize = "D25";
            s.MainBarSpec = "SD390";
            s.MainBarDr = 850;
            s.PileDiameter = 1000;

            var section = (AbstractPileSection)s.CreateSectionCalculator()!;

            Assert.AreEqual(SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN,
                section.UltimateCompressiveStrain, 1e-12,
                "場所打ち鉄筋コンクリート杭の終局圧縮ひずみが変わっている");
        }

        // ── ソース走査の道具 ──

        private static string FindSolutionRoot([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(typeof(PrecastUltimateStrainTests).Assembly.Location), Path.GetDirectoryName(thisFile) })
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                        return dir.FullName;
                }
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static string ReadSource(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = FindSolutionRoot();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return File.ReadAllText(Path.Combine(parts));
        }

        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");

            int open = source.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source[open..(i + 1)];
                }
            }
            Assert.Fail($"本体の閉じ括弧が見つかりません: {signatureFragment}");
            return "";
        }
    }
}
