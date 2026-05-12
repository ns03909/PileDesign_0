using CsvHelper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    public class FTCap
    {
        public double Phi { get; set; }
        public double T { get; set; }
        public double D1 { get; set; }
        public double D2 { get; set; }
        public double H { get; set; }
        public double W { get; set; }

        public ObservableCollection<Spec> GetSpecs()
        {
            ObservableCollection<Spec> specs =
            [
                new Spec("杭径", "Phi", $"{Phi:N0}", "mm"),
                new Spec("FTキャップ鉄板厚さ", "T", $"{T:N1}", "mm"),
                new Spec("FTキャップ内径", "D1", $"{D1:N0}", "mm"),
                new Spec("FTキャップ外径", "D2", $"{D2:N0}", "mm"),
                new Spec("FTキャップ高さ", "H", $"{H:N1}", "mm"),
                new Spec("FTキャップ重量", "W", $"{W:N1}" , "kg"),
            ];

            return specs;
        }
    }

    public class FTCapLoader
    {
        public static List<FTCap> LoadFromCsv(string filePath)
        {
            var _FTCaps = new List<FTCap>();

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            // ヘッダを読み取る（CsvHelper に任せる）
            if (!csv.Read()) return _FTCaps;
            csv.ReadHeader();

            while (csv.Read())
            {
                try
                {
                    // 先頭フィールドにヘッダ文字列が入っている行をスキップ
                    var firstRaw = csv.GetField(0);
                    if (string.IsNullOrWhiteSpace(firstRaw)) continue;
                    if (firstRaw.Trim().Equals("Phi", StringComparison.OrdinalIgnoreCase)) continue;

                    // 安全にフィールドを取得（TryGetField を使う）
                    csv.TryGetField(0, out double phi);
                    csv.TryGetField(1, out double t);
                    csv.TryGetField(2, out double d1);
                    csv.TryGetField(3, out double d2);
                    csv.TryGetField(4, out double h);
                    csv.TryGetField(5, out double w);

                    var _FTCap = new FTCap
                    {
                        Phi = phi,
                        T = t,
                        D1 = d1,
                        D2 = d2,
                        H = h,
                        W = w,
                    };
                    _FTCaps.Add(_FTCap);
                }
                catch (CsvHelper.TypeConversion.TypeConverterException)
                {
                    // 型変換失敗の行はスキップしてデバッグ出力
                    continue;
                }
                catch (Exception)
                {
                    continue;
                }
            }

            return _FTCaps;
        }
    }
}
