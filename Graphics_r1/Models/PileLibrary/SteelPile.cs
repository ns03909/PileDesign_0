using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    public class SteelPipePile
    {
        public double Diameter { get; set; }
        public double Thickness { get; set; }
        // その他のプロパティやメソッドを追加することもできます
    }

    public class SteelPipePileLoader
    {
        public static List<SteelPipePile> LoadFromCsv(string filePath)
        {
            var steelPipePiles = new List<SteelPipePile>();

            using var reader = new StreamReader(filePath);

            // 先頭行を読み取り、ヘッダ行かデータ行かを判定する
            var firstLine = reader.ReadLine();
            if (firstLine == null)
                return steelPipePiles;

            // カンマ区切りで分割し、各トークンが数値に変換できるかで判定する
            string[] tokens = firstLine.Split(',');
            bool firstLineLooksLikeHeader = false;
            if (tokens.Length > 0)
            {
                // いずれかのトークンが double に変換できなければヘッダと判断する
                firstLineLooksLikeHeader = false;
                foreach (var t in tokens)
                {
                    if (!double.TryParse(t.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        firstLineLooksLikeHeader = true;
                        break;
                    }
                }
            }

            // CsvReader に渡す設定
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = firstLineLooksLikeHeader,
                MissingFieldFound = null // 欠損フィールドは無視
            };

            // ストリーム位置を調整：先頭行がヘッダなら現在位置はデータ先頭のまま（readerは2行目を指す）。
            // 先頭行がデータの場合はストリームを先頭に戻す
            if (!firstLineLooksLikeHeader)
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                reader.DiscardBufferedData();
            }

            using var csv = new CsvReader(reader, config);

            while (csv.Read())
            {
                // 文字列で取り出してから double.TryParse で安全に変換
                var s0 = csv.GetField(0);
                var s1 = csv.GetField(1);

                if (string.IsNullOrWhiteSpace(s0) && string.IsNullOrWhiteSpace(s1))
                    continue;

                if (double.TryParse(s0, NumberStyles.Any, CultureInfo.InvariantCulture, out double diameter) &&
                    double.TryParse(s1, NumberStyles.Any, CultureInfo.InvariantCulture, out double thickness))
                {
                    steelPipePiles.Add(new SteelPipePile { Diameter = diameter, Thickness = thickness });
                }
                else
                {
                    // ヘッダ行や不正行はログに出すが処理は継続
                    Serilog.Log.Debug($"Skipping invalid CSV line in '{filePath}': \"{s0}\", \"{s1}\"");
                }
            }

            return steelPipePiles;
        }
    }
}
