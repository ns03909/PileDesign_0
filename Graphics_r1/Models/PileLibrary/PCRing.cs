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
        public string? BarSize { get; set; }
        public double L1 { get; set; }
        public double L2 { get; set; }
        public string? Name { get; set; }
        public double RingSteelTs { get; set; }
        public string? RingSteelGrade { get; set; }
        public string? SpiralDia { get; set; }
        public int SpiralNum { get; set; }
        public double PCD { get; set; }

        public ObservableCollection<Spec> GetSpecs()
        {
            ObservableCollection<Spec> specs =
            [
                new Spec("PCリングタイプ", "", Name ?? "", ""),
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
                new Spec("PCリング鋼管鋼種", "", RingSteelGrade ?? "", ""),
                new Spec("PCリングスパイラル筋", "", SpiralDia + "(" + $"{SpiralNum:N0}" + "巻)", ""),
                //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            ];
            return specs;
        }
    }

    public class PCRingLoader
    {
        //public static ObservableCollection<PCRing> LoadFromCsv(string filePath)
        //{
        //    var _PCRings = new ObservableCollection<PCRing>();

        //    using (var reader = new StreamReader(filePath))
        //    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
        //    {
        //        reader.ReadLine();
        //        try
        //        {
        //            while (csv.Read())
        //            {
        //                var _PCRing = new PCRing
        //                {
        //                    D = csv.GetField<double>(0),
        //                    RD1 = csv.GetField<double>(1),
        //                    Tc = csv.GetField<double>(2),
        //                    RD2 = csv.GetField<double>(1) + 2 * csv.GetField<double>(2) + 2 * csv.GetField<double>(9),
        //                    Hr = csv.GetField<double>(3),
        //                    BarNum = csv.GetField<int>(4),
        //                    BarSize = csv.GetField<string>(5),
        //                    L1 = csv.GetField<double>(6), // 
        //                    L2 = csv.GetField<double>(7), // 
        //                    Name = csv.GetField<string>(8), // 
        //                    RingSteelTs = csv.GetField<double>(9),
        //                    RingSteelGrade = csv.GetField<string>(10),
        //                    SpiralDia = csv.GetField<string>(11), // プレストレス
        //                    SpiralNum = csv.GetField<int>(12), // コンクリート縦弾性係数

        //                };

        //                _PCRings.Add(_PCRing);
        //            }
        //        }
        //        catch (CsvHelper.TypeConversion.TypeConverterException ex)
        //        {
        //            // フィールドの読み取りに失敗した場合の処理
        //            // 例外を適切にハンドリングする
        //            Console.WriteLine("CSVファイルの形式が正しくありません。");
        //            Console.WriteLine(ex.Message);
        //        }
        //    }
        //    return _PCRings;
        //}
        public static ObservableCollection<PCRing> LoadFromCsv(string filePath)
        {
            var _PCRings = new ObservableCollection<PCRing>();

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            // ヘッダを読み取る（CsvHelper に任せる）
            if (!csv.Read()) return _PCRings;
            csv.ReadHeader();

            while (csv.Read())
            {
                try
                {
                    // 先頭フィールドにヘッダ文字列が入っている行をスキップ
                    var firstRaw = csv.GetField(0);
                    if (string.IsNullOrWhiteSpace(firstRaw)) continue;
                    if (firstRaw.Trim().Equals("D", StringComparison.OrdinalIgnoreCase)) continue;

                    // 安全にフィールドを取得（TryGetField を使う）
                    csv.TryGetField(0, out double d);
                    csv.TryGetField(1, out double rd1);
                    csv.TryGetField(2, out double tc);
                    csv.TryGetField(3, out double hr);
                    csv.TryGetField(4, out int barNum);
                    csv.TryGetField(5, out string? barSize);
                    csv.TryGetField(6, out double l1);
                    csv.TryGetField(7, out double l2);
                    csv.TryGetField(8, out string? name);
                    csv.TryGetField(9, out double ts);
                    csv.TryGetField(10, out string? ringSteelGrade);
                    csv.TryGetField(11, out string? spiralDia);
                    csv.TryGetField(12, out int spiralNum);

                    var _PCRing = new PCRing
                    {
                        D = d,
                        RD1 = rd1,
                        Tc = tc,
                        RD2 = rd1 + 2 * tc + 2 * ts,
                        Hr = hr,
                        BarNum = barNum,
                        BarSize = barSize?.Trim() ?? string.Empty,
                        L1 = l1,
                        L2 = l2,
                        Name = name?.Trim() ?? string.Empty,
                        RingSteelTs = ts,
                        RingSteelGrade = ringSteelGrade?.Trim() ?? string.Empty,
                        SpiralDia = spiralDia?.Trim() ?? string.Empty,
                        SpiralNum = spiralNum,
                    };

                    _PCRings.Add(_PCRing);
                }
                catch (CsvHelper.TypeConversion.TypeConverterException ex)
                {
                    // 型変換失敗の行はスキップしてデバッグ出力（運用時はログ出力に置き換える）
                    continue;
                }
                catch (Exception ex)
                {
                    continue;
                }
            }

            return _PCRings;
        }
    }
}
