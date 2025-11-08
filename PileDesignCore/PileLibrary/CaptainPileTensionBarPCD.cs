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
    public class CaptainPileTensionBarPCD
    {
        public double D { get; set; }
        public double Nu { get; set; }
        public double TDorTB { get; set; }
    }

    public class CaptainPileTensionBarPCDLoader
    {
        public List<CaptainPileTensionBarPCD> LoadFromCsv(string filePath)
        {
            var _captainPileTensionBarPCDs = new List<CaptainPileTensionBarPCD>();

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                reader.ReadLine();
                try
                {
                    while (csv.Read())
                    {
                        var _captainPileTensionBarPCD = new CaptainPileTensionBarPCD
                        {
                            D = csv.GetField<double>(0), // 直径
                            Nu = csv.GetField<double>(1), // 絞り率
                            TDorTB = csv.GetField<double>(2), // 辺長または直径
                        };

                        _captainPileTensionBarPCDs.Add(_captainPileTensionBarPCD);
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
            return _captainPileTensionBarPCDs;
        }
    }
}
