using CsvHelper;
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
    public class Spec
    {
        public string Item{ get; set; }
        public string Mark { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }

        // コンストラクタ
        public Spec(string item, string mark, string value, string unit)
        {
            Item = item;
            Mark = mark;
            Value = value;
            Unit = unit;
        }
    }
    public class PCRing : BaseViewModel
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

        public ObservableCollection<Spec> GetSpecs ()
        {
            ObservableCollection<Spec> specs = new ObservableCollection<Spec>
            {
                new Spec("PCリングタイプ", "", Name, ""),
                new Spec("杭径", "D", D.ToString(), "mm"),
                new Spec("PCリング内径", "RD1", RD1.ToString(), "mm"),
                new Spec("PCリング外径", "RD2", RD2.ToString(), "mm"),
                new Spec("PCリングコンクリート厚", "Tc", Tc.ToString(), "mm"),
                new Spec("PCリング高さ", "hr", Hr.ToString(), "mm"),
                new Spec("PCリング定着筋", "", BarNum.ToString() +"-"+ BarSize, ""),
                //new Spec("PCリング定着筋呼び径", "", BarSize, ""),
                new Spec("PCリング定着筋定着長さ", "L1", L1.ToString(), "mm"),
                new Spec("PCリング定着筋長さ", "L2", L2.ToString(), "mm"),
                new Spec("PCリング鋼管厚", "", RingSteelTs.ToString(), "mm"),
                new Spec("PCリング鋼管鋼種", "", RingSteelGrade, ""),
                new Spec("PCリングスパイラル筋", "", SpiralDia + "(" + SpiralNum.ToString() + "巻)", ""),
                //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            };

            return specs;
        }
    }

    public class PCRingLoader
    {
        public List<PCRing> LoadFromCsv(string filePath)
        {
            var _PCRings = new List<PCRing>();

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
