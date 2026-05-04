using CsvHelper;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// キャプリングパイル工法用 PC リング諸元 (既製杭用)。
    /// キャプテンパイル用 PCRing と並列に存在し、適用径 (300〜1200 mm) と仕様列が異なる。
    /// 標準仕様: N (標準) / S1 (高せん断耐力 1) / S2 (高せん断耐力 2) の 3 タイプ × 12 径。
    /// </summary>
    public class CapringPCRing : BaseModel
    {
        public double D { get; set; }
        public double RD1 { get; set; }
        public double RD2 { get; set; }
        public double SD { get; set; }
        public double Tc { get; set; }
        public double Hr { get; set; }
        public int BarNum { get; set; }
        public string? BarSize { get; set; }
        public double L1 { get; set; }
        public string? Name { get; set; }
        public double Ts { get; set; }
        public string? SteelGrade { get; set; }
        public string? SpiralDia { get; set; }
        public int SpiralNum { get; set; }

        public ObservableCollection<Spec> GetSpecs()
        {
            ObservableCollection<Spec> specs =
            [
                new Spec("PCリングタイプ", "", Name ?? "", ""),
                new Spec("杭径", "D", $"{D:N0}", "mm"),
                new Spec("PCリング内径", "rD1", $"{RD1:N0}", "mm"),
                new Spec("PCリング外径", "rD2", $"{RD2:N0}", "mm"),
                new Spec("スパイラル筋外径", "sD", $"{SD:N0}", "mm"),
                new Spec("PCリングコンクリート厚", "tc", $"{Tc:N0}", "mm"),
                new Spec("PCリング高さ", "hr", $"{Hr:N0}", "mm"),
                new Spec("PCリング定着筋", "", $"{BarNum}-{BarSize}", ""),
                new Spec("PCリング定着筋定着長さ", "L1", $"{L1:N0}", "mm"),
                new Spec("PCリング鋼管厚", "ts", $"{Ts:N1}", "mm"),
                new Spec("PCリング鋼管鋼種", "", SteelGrade ?? "", ""),
                new Spec("PCリングスパイラル筋", "", $"{SpiralDia} ({SpiralNum} 巻)", ""),
            ];
            return specs;
        }
    }

    public class CapringPCRingLoader
    {
        public static ObservableCollection<CapringPCRing> LoadFromCsv(string filePath)
        {
            var rings = new ObservableCollection<CapringPCRing>();

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            if (!csv.Read()) return rings;
            csv.ReadHeader();

            while (csv.Read())
            {
                try
                {
                    var firstRaw = csv.GetField(0);
                    if (string.IsNullOrWhiteSpace(firstRaw)) continue;
                    if (firstRaw.Trim().Equals("D", StringComparison.OrdinalIgnoreCase)) continue;

                    csv.TryGetField(0, out double d);
                    csv.TryGetField(1, out double rd1);
                    csv.TryGetField(2, out double rd2);
                    csv.TryGetField(3, out double sd);
                    csv.TryGetField(4, out double tc);
                    csv.TryGetField(5, out double hr);
                    csv.TryGetField(6, out int barNum);
                    csv.TryGetField(7, out string? barSize);
                    csv.TryGetField(8, out double l1);
                    csv.TryGetField(9, out string? name);
                    csv.TryGetField(10, out double ts);
                    csv.TryGetField(11, out string? steelGrade);
                    csv.TryGetField(12, out string? spiralDia);
                    csv.TryGetField(13, out int spiralNum);

                    rings.Add(new CapringPCRing
                    {
                        D = d,
                        RD1 = rd1,
                        RD2 = rd2,
                        SD = sd,
                        Tc = tc,
                        Hr = hr,
                        BarNum = barNum,
                        BarSize = barSize?.Trim() ?? string.Empty,
                        L1 = l1,
                        Name = name?.Trim() ?? string.Empty,
                        Ts = ts,
                        SteelGrade = steelGrade?.Trim() ?? string.Empty,
                        SpiralDia = spiralDia?.Trim() ?? string.Empty,
                        SpiralNum = spiralNum,
                    });
                }
                catch
                {
                    continue;
                }
            }
            return rings;
        }
    }
}
