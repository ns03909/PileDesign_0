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

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                reader.ReadLine();
                try
                {
                    while (csv.Read())
                    {
                        var _FTCap = new FTCap
                        {
                            Phi = csv.GetField<double>(0),
                            T = csv.GetField<double>(1),
                            D1 = csv.GetField<double>(2),
                            D2 = csv.GetField<double>(3),
                            H = csv.GetField<double>(4),
                            W = csv.GetField<double>(5),
                        };
                        _FTCaps.Add(_FTCap);
                    }
                }
                catch (CsvHelper.TypeConversion.TypeConverterException ex)
                {
                    // フィールドの読み取りに失敗した場合の処理
                    // 例外を適切にハンドリングする
                    Console.WriteLine("CSVファイルの形式が正しくありません。");
                    Console.WriteLine(ex.Message);
                }
            }
            return _FTCaps;
        }
    }
}
