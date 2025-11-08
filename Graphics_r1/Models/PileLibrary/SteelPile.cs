using CsvHelper;
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

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                while (csv.Read())
                {
                    if (csv.TryGetField(0, out double diameter) && csv.TryGetField(1, out double thickness))
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

