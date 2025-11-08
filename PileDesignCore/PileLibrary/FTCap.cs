using CsvHelper;
using PileDesignCore.PileSection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PileDesignCore.PileLibrary
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
            ObservableCollection<Spec> specs = new ObservableCollection<Spec>();

            specs.Add(new Spec("杭径", "Phi", Phi.ToString(), "mm"));
            specs.Add(new Spec("FTキャップ鉄板厚さ", "T", T.ToString(), "mm"));
            specs.Add(new Spec("FTキャップ内径", "D1", D1.ToString(), "mm"));
            specs.Add(new Spec("FTキャップ外径", "D2", D2.ToString(), "mm"));
            specs.Add(new Spec("FTキャップ高さ", "H", H.ToString(), "mm"));
            specs.Add(new Spec("FTキャップ重量", "W", W.ToString(), "kg"));

            return specs;
        }
    }

    public class FTCapLoader
    {
        public List<FTCap> LoadFromCsv(string filePath)
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
