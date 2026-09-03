using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 限界曲線（N-M・Q-N）の出所を 1 か所に保つ。
    ///
    /// 損傷限界は<b>地震動レベルで低減係数が変わる</b>（レベル1 は β2 を乗じない、
    /// レベル2 は β1×β2）。ところがキャッシュ済みプロパティ
    /// <c>FactoredDamageNM</c> / <c>FactoredDamageNQ</c> はレベルを持たない。
    /// これを画面・計算書・検定がそれぞれ直に読んでいたため、
    /// <b>同じ「低減後損傷限界」でも経路ごとに違う曲線</b>を使っていた
    /// （計算書はレベル2 固定、画面はグレード別、検定は荷重ケース別）。
    ///
    /// 出所を <c>GetFactoredDamageNM(level)</c> / <c>GetQNCurvesForLevel(level)</c> に集約したので、
    /// レベルを持たないプロパティを外から読み直さないことをここで固定する。
    /// </summary>
    [TestClass]
    public class LimitCurveSourceTests
    {
        /// <summary>定義そのものと、レベル別の入口を持つファイル。</summary>
        private static readonly string[] AllowedFiles =
        {
            "PileSection.cs",        // 定義と GetFactoredDamageNM / GetQNCurvesForLevel
            "PrecastPileSection.cs", // 断面側の組み立て
            // 杭頭部の断面はレベル別の曲線を持たない (FactoredDamageNMLevel1 = FactoredDamageNM)
            // ので、レベル抜きで読んでも曲線は変わらない。
            "PileTop.cs",
        };

        private static string SolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(LimitCurveSourceTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new DirectoryNotFoundException("ソリューションルートが見つかりません");
        }

        private static List<string> FindReaders(string propertyName)
        {
            var readers = new List<string>();
            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(SolutionRoot(), "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (AllowedFiles.Contains(Path.GetFileName(cs))) continue;

                var lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? lines[i][..comment] : lines[i];
                    // 「.FactoredDamageNMLevel1」「.FactoredDamageNMRaw」等と区別するため語境界で見る
                    if (!Regex.IsMatch(code, @"\." + propertyName + @"\b(?!\w)")) continue;
                    readers.Add($"{Path.GetFileName(cs)}:{i + 1}  {code.Trim()}");
                }
            }
            return readers;
        }

        /// <summary>
        /// 低減後損傷限界の N-M をレベル抜きで読んでいないこと。
        /// 読み手が残っていると、その経路だけレベル2 固定の曲線で描く／検定することになる。
        /// </summary>
        [TestMethod]
        public void NothingReadsFactoredDamageNMWithoutLevel()
        {
            var readers = FindReaders("FactoredDamageNM");
            Assert.AreEqual(0, readers.Count,
                "レベルを持たない FactoredDamageNM を読んでいます。"
                + "PileSection.GetFactoredDamageNM(level) を使ってください:\n  "
                + string.Join("\n  ", readers));
        }

        /// <summary>
        /// 低減後損傷限界の Q-N をレベル抜きで読んでいないこと。
        /// </summary>
        [TestMethod]
        public void NothingReadsFactoredDamageNQWithoutLevel()
        {
            var readers = FindReaders("FactoredDamageNQ");
            Assert.AreEqual(0, readers.Count,
                "レベルを持たない FactoredDamageNQ を読んでいます。"
                + "PileSection.GetQNCurvesForLevel(level) を使ってください:\n  "
                + string.Join("\n  ", readers));
        }

        /// <summary>
        /// Q-N 曲線のキャッシュ済みプロパティを外から読んでいないこと。
        ///
        /// レベルの問題に加えて、場所打ちRC の安全限界せん断は
        /// <b>帯筋を仮値 (pw=0.002 / σwy=295) で作った曲線</b>が入っている
        /// (断面クラスが帯筋の入力を持たないため)。実際の帯筋を反映した曲線は
        /// <c>PileSection.ComputeQNForMonQd</c> が作るので、読む側は
        /// <c>GetQNCurvesForLevel</c> を通さなければならない。
        /// 直に読むと、画面のグラフと計算書・検定で違う耐力になる (実際にそうなっていた)。
        /// </summary>
        [TestMethod]
        public void NothingReadsCachedNQProperties()
        {
            var readers = new List<string>();
            foreach (string name in new[]
                     {
                         "UnfactoredServiceNQ", "FactoredServiceNQ",
                         "UnfactoredDamageNQ", "FactoredDamageNQ",
                         "UnfactoredUltimateNQ", "FactoredUltimateNQ",
                     })
            {
                readers.AddRange(FindReaders(name));
            }

            Assert.AreEqual(0, readers.Count,
                "Q-N のキャッシュ済みプロパティを直に読んでいます。"
                + "PileSection.GetQNCurvesForLevel(level) を使ってください:" + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", readers));
        }

        /// <summary>
        /// レベル別の入口が実際にレベルで違う曲線を返すこと。
        /// 返さないなら、レベルを渡して回っても何も守っていない。
        /// </summary>
        [TestMethod]
        public void DamageCurves_DifferBetweenLevels()
        {
            var s = ShearAxialDependenceTableTests.CreateInsituRcSectionForCurveTests();

            // レベル1 は β2 を乗じないので、どの軸力でもレベル2 を下回らない。
            // (β2 が 1.0 の杭種では一致する。下回ったらレベルの取り違え)
            var nm1 = s.GetFactoredDamageNM(1);
            var nm2 = s.GetFactoredDamageNM(2);
            Assert.AreEqual(nm2.M.Count, nm1.M.Count, "レベル別の N-M で点数が違う");
            for (int i = 0; i < nm1.M.Count; i++)
            {
                Assert.IsTrue(nm1.M[i] >= nm2.M[i] - 1e-6,
                    $"損傷限界 N-M のレベル1 がレベル2 より小さい "
                    + $"(N={nm1.N[i]:F0} kN で L1={nm1.M[i]:F1} < L2={nm2.M[i]:F1} kN·m)");
            }

            // せん断は β2 = 0.65〜0.75 がレベル2 にだけ掛かるので、実際に差が出る
            var q1 = s.GetQNCurvesForLevel(1).FactoredDamage;
            var q2 = s.GetQNCurvesForLevel(2).FactoredDamage;
            double qMax1 = q1.Q.Max(), qMax2 = q2.Q.Max();
            Assert.IsTrue(qMax1 > qMax2 * 1.05,
                $"場所打ちRC の損傷限界 Q-N がレベルで変わっていない "
                + $"(L1 max={qMax1:F1} / L2 max={qMax2:F1} kN)。β2 が効いていない。");
        }
    }
}
