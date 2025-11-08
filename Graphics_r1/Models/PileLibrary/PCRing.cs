using CsvHelper;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    public class Spec(string item, string mark, string value, string unit, string note = "")
    {
        public string Item { get; set; } = item;
        public string Mark { get; set; } = mark;
        public string Value { get; set; } = value;
        public string Unit { get; set; } = unit;
        public string Note { get; set; } = note;
    }
    public class PCRing : BaseModel
    {
        public double D { get; set; }
        public double RD1 { get; set; }
        public double RD2 { get; set; }
        public double Tc { get; set; }
        public double Hr { get; set; }
        public int BarNum { get; set; }
        public string BarSize { get; set; }
        public double L1 { get; set; }
        public double L2 { get; set; }
        public string Name { get; set; }
        public double RingSteelTs { get; set; }
        public string RingSteelGrade { get; set; }
        public string SpiralDia { get; set; }
        public int SpiralNum { get; set; }
        public double PCD { get; set; }

        public ObservableCollection<Spec> GetSpecs()
        {
            ObservableCollection<Spec> specs =
            [
                new Spec("PCリングタイプ", "", Name, ""),
                new Spec("杭径", "D", $"{D:N0}", "mm"),
                new Spec("PCリング内径", "RD1", $"{RD1:N0}", "mm"),
                new Spec("PCリング外径", "RD2", $"{RD2:N0}", "mm"),
                new Spec("PCリングコンクリート厚", "Tc", $"{Tc:N0}", "mm"),
                new Spec("PCリング高さ", "hr",  $"{Hr:N0}", "mm"),
                new Spec("PCリング定着筋", "",  $"{BarNum:N0}" +"-"+ $"{BarSize:N0}", ""),
                //new Spec("PCリング定着筋呼び径", "", BarSize, ""),
                new Spec("PCリング定着筋定着長さ", "L1", $"{L1:N0}", "mm"),
                new Spec("PCリング定着筋長さ", "L2", $"{L2:N0}", "mm"),
                new Spec("PCリング鋼管厚", "", $"{RingSteelTs:N1}", "mm"),
                new Spec("PCリング鋼管鋼種", "", RingSteelGrade, ""),
                new Spec("PCリングスパイラル筋", "", SpiralDia + "(" + $"{SpiralNum:N0}" + "巻)", ""),
                //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            ];
            return specs;
        }
    }

    public class PCRingLoader
    {
        public static ObservableCollection<PCRing> LoadFromCsv(string filePath)
        {
            var _PCRings = new ObservableCollection<PCRing>();

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                reader.ReadLine();
                try
                {
                    while (csv.Read())
                    {
                        var _PCRing = new PCRing
                        {
                            D = csv.GetField<double>(0),
                            RD1 = csv.GetField<double>(1),
                            Tc = csv.GetField<double>(2),
                            RD2 = csv.GetField<double>(1) + 2 * csv.GetField<double>(2) + 2 * csv.GetField<double>(9),
                            Hr = csv.GetField<double>(3),
                            BarNum = csv.GetField<int>(4),
                            BarSize = csv.GetField<string>(5),
                            L1 = csv.GetField<double>(6), // 
                            L2 = csv.GetField<double>(7), // 
                            Name = csv.GetField<string>(8), // 
                            RingSteelTs = csv.GetField<double>(9),
                            RingSteelGrade = csv.GetField<string>(10),
                            SpiralDia = csv.GetField<string>(11), // プレストレス
                            SpiralNum = csv.GetField<int>(12), // コンクリート縦弾性係数

                        };

                        _PCRings.Add(_PCRing);
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
            return _PCRings;
        }
    }
}
