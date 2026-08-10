using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    public class PrecastPile
    {
        public int No { get; set; }
        public string ThicknessType { get; set; }
        public string PrestressType { get; set; }
        public string Name { get; set; }
        public string PileType { get; set; }
        public double PileDiameter { get; set; }
        public double PileThickness { get; set; }
        public double Fc { get; set; }
        public double SFc { get; set; }
        public double Fbc { get; set; }
        public double SigmaE { get; set; }
        public double Ec { get; set; }
        public double Ap { get; set; }
        public double Dp { get; set; }
        public double Ftp { get; set; }
        public double SigmaPu { get; set; }
        public double Ep { get; set; }
        public bool HasReinf { get; set; }
        public int Nr { get; set; }
        public string RDesignation { get; set; }
        public double Ag { get; set; }
        public double Dr { get; set; }
        public double Ftr { get; set; }
        public double Er { get; set; }
        public double Ts { get; set; }
        public double Fts { get; set; }
        public double Es { get; set; }
        public double PsSigmaY { get; set; }
    }

    public class PrecastPileLoader
    {
        public static List<PrecastPile> LoadFromCsv(string filePath)
        {
            var precastPiles = new List<PrecastPile>();

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                reader.ReadLine();
                try
                {
                    while (csv.Read())
                    {
                        var precastPile = new PrecastPile
                        {
                            No = csv.GetField<int>(0),
                            ThicknessType = csv.GetField<string>(1),
                            PrestressType = csv.GetField<string>(2),
                            Name = csv.GetField<string>(3),
                            PileType = csv.GetField<string>(4),
                            PileDiameter = csv.GetField<double>(5), // 杭径
                            PileThickness = csv.GetField<double>(6), //肉厚
                            Fc = csv.GetField<double>(7), // コンクリート強度
                            SFc = csv.GetField<double>(8),
                            Fbc = csv.GetField<double>(9),
                            SigmaE = csv.GetField<double>(10), // プレストレス
                            Ec = csv.GetField<double>(11), // コンクリート縦弾性係数
                            Ap = csv.GetField<double>(12), // PC鋼線断面積
                            Dp = csv.GetField<double>(13), // PC鋼線配置径
                            Ftp = csv.GetField<double>(14), // PC鋼線Py
                            SigmaPu = csv.GetField<double>(15), // PC鋼線Pu
                            Ep = csv.GetField<double>(16), // PC鋼線E
                            HasReinf = csv.GetField<bool>(17),
                            Nr = csv.GetField<int>(18), // 鉄筋数
                            RDesignation = csv.GetField<string>(19), // 鉄筋径
                            Ag = csv.GetField<double>(20), // 鉄筋断面積
                            Dr = csv.GetField<double>(21), // 鉄筋配置径
                            Ftr = csv.GetField<double>(22),
                            Er = csv.GetField<double>(23), // 主筋縦弾性係数
                            Ts = csv.GetField<double>(24), // 鋼管径
                            Fts = csv.GetField<double>(25),
                            Es = csv.GetField<double>(26), // 鋼管縦弾性係数
                            PsSigmaY = csv.GetField<double>(27) // せん断補強筋量
                        };

                        precastPiles.Add(precastPile);
                    }
                }
                catch (CsvHelper.TypeConversion.TypeConverterException ex)
                {
                    // フィールドの読み取りに失敗した場合の処理
                    // 例外を適切にハンドリングする
                    Serilog.Log.Debug("CSVファイルの形式が正しくありません。");
                    Serilog.Log.Debug(ex.Message);
                }
            }
            return precastPiles;
        }
    }
}