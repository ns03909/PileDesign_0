using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 既製杭のひび割れモーメント Mcr と破壊モーメント Mu を、カタログ値に突き合わせる。
    ///
    /// カタログに Mcr がある 3 ライブラリ（MS-hi105 / DAM105 / BF.S 頭部）を使う。
    /// 2026-09-07 まで Mu しか突き合わせておらず、Mcr は
    /// 「Ze·(Ftd + σe + σ0) で Ftd = −0.56√Fc」と引張強度を<b>差し引いて</b>いたため
    /// 比が −0.16〜0.30（A 種は負）だったが、Mu の幅 0.90〜1.20 だけを見ていて通り抜けていた。
    ///
    /// 実測（修正後）: Mcr の比は 0.86〜0.94。Ie はカタログの CatalogIe と 1.000 で一致するので、
    /// 残る 8% は Ie ではなく式側の系統差（曲げ引張強度の取り方）。幅はそれを含めて引いてある。
    /// ここが崩れたら、Ftd の符号・プレストレスの扱い・Ze の算定を疑うこと。
    /// </summary>
    [TestClass]
    public class PrecastCatalogCrackMomentTests
    {
        private static List<Dictionary<string, string>> ReadRawCsv(string file)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "PileLibrary", file);
            var lines = File.ReadAllLines(path);
            var head = lines[0].TrimStart('﻿').Split(',');
            return [.. lines.Skip(1).Where(l => l.Length > 0)
                            .Select(l => head.Zip(l.Split(',')).ToDictionary(x => x.First, x => x.Second))];
        }

        private static double Num(Dictionary<string, string> r, string key) =>
            double.Parse(r[key], System.Globalization.CultureInfo.InvariantCulture);

        private static PileSection Precast(string sectionType, string productName)
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = sectionType,
            };
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        private static double MomentAtZeroAxial(List<double> ns, List<double> ms)
        {
            double best = 0.0;
            for (int i = 0; i + 1 < ns.Count; i++)
                if ((ns[i] <= 0 && ns[i + 1] >= 0) || (ns[i] >= 0 && ns[i + 1] <= 0))
                {
                    double d = ns[i + 1] - ns[i];
                    double t = Math.Abs(d) < 1e-12 ? 0.0 : -ns[i] / d;
                    best = Math.Max(best, ms[i] + t * (ms[i + 1] - ms[i]));
                }
            return best;
        }

        private static void AssertRatioInBand(List<(string Name, double Ratio)> ratios, double lo, double hi, string what)
        {
            var bad = ratios.Where(r => !(r.Ratio > lo && r.Ratio < hi)).ToList();
            Assert.AreEqual(0, bad.Count,
                $"{what}: 計算/カタログ が {lo:F2}〜{hi:F2} を外れた製品があります "
                + $"(全体 {ratios.Min(r => r.Ratio):F3}〜{ratios.Max(r => r.Ratio):F3}):\n  "
                + string.Join("\n  ", bad.Select(r => $"{r.Name}: {r.Ratio:F3}")));
        }

        [TestMethod]
        public void MsHi105_CrackMoment_TracksTheCatalog()
        {
            var ratios = new List<(string, double)>();
            foreach (var r in ReadRawCsv("pile_library_PHC_MSHI105.csv"))
            {
                var sec = (PHCSection)Precast(PileTypeNames.Phc, r["typ"]).CreateSectionCalculator()!;
                ratios.Add((r["typ"], sec.GetCrackMoment(0.0).Item1 / 1e6 / Num(r, "CatalogMcr")));
            }
            Assert.AreEqual(94, ratios.Count, "MS-hi105 の製品数");
            AssertRatioInBand(ratios, 0.85, 1.00, "MS-hi105 Mcr");   // 実測 0.882〜0.936
        }

        [TestMethod]
        public void Dam105_CrackMoment_TracksTheCatalog()
        {
            var ratios = new List<(string, double)>();
            foreach (var r in ReadRawCsv("pile_library_PRC_DAM105.csv"))
            {
                var sec = (PRCSection)Precast(PileTypeNames.Prc, r["typ"]).CreateSectionCalculator()!;
                ratios.Add((r["typ"], sec.GetCrackMoment(0.0).Item1 / 1e6 / Num(r, "CatalogMcr")));
            }
            Assert.AreEqual(218, ratios.Count, "DAM105 の製品数");
            AssertRatioInBand(ratios, 0.85, 1.00, "DAM105 Mcr");     // 実測 0.922〜0.939
        }

        /// <summary>
        /// DAM105 は Mu のカタログ突合が無かった（MS-hi105・BF.S にはある）ので足す。
        /// 実測 1.028〜1.115（PRC は異形棒鋼を持つぶん、計算がやや大きめに出る）。
        /// </summary>
        [TestMethod]
        public void Dam105_UltimateMoment_TracksTheCatalog()
        {
            var ratios = new List<(string, double)>();
            foreach (var r in ReadRawCsv("pile_library_PRC_DAM105.csv"))
            {
                var (ns, ms) = Precast(PileTypeNames.Prc, r["typ"]).UnfactoredUltimateNM;
                ratios.Add((r["typ"], MomentAtZeroAxial(ns, ms) / Num(r, "CatalogMu")));
            }
            AssertRatioInBand(ratios, 0.98, 1.18, "DAM105 Mu");
        }

        [TestMethod]
        public void BfsHead_CrackMoment_TracksTheCatalog()
        {
            var ratios = new List<(string, double)>();
            foreach (var p in PileSection.BfsPiles.Where(p => p.Fc == 105))
            {
                var sec = (PHCSection)Precast(PileTypeNames.BfsHead, p.DisplayName).CreateSectionCalculator()!;
                ratios.Add((p.DisplayName, sec.GetCrackMoment(0.0).Item1 / 1e6 / p.HeadMcr));
            }
            Assert.IsTrue(ratios.Count >= 20, $"BF.S Fc105 の製品数 {ratios.Count}");
            AssertRatioInBand(ratios, 0.80, 1.00, "BF.S 頭部 Mcr");  // 実測 0.861〜0.913
        }
    }
}
