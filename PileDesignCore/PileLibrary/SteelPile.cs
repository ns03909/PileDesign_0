using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PileDesignCore.PileLibrary
{
    public class SteelPipePile
    {
        public double Diameter { get; set; }
        public double Thickness { get; set; }
        // その他のプロパティやメソッドを追加することもできます
    }

    public class SteelPipePileLoader
    {
        public List<SteelPipePile> LoadFromCsv(string filePath)
        {
            var steelPipePiles = new List<SteelPipePile>();

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                // ヘッダーがない場合はfalseに設定
                //csv.Configuration.HasHeaderRecord = false;

                while (csv.Read())
                {
                    double diameter, thickness;
                    if (csv.TryGetField<double>(0, out diameter) && csv.TryGetField<double>(1, out thickness))
                    {
                        steelPipePiles.Add(new SteelPipePile { Diameter = diameter, Thickness = thickness });
                    }
                    else
                    {
                        Console.WriteLine("Invalid data format. Skipping line.");
                    }
                }
            }

            return steelPipePiles;
        }
    }
}

