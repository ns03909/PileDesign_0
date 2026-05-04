using CsvHelper;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// キャプリングパイル工法用 引張定着筋標準配筋。
    /// 標準 10 配筋 (3-D19 〜 5-D38) で、配置径 Dc・帯筋外径・定着長さ・適用最小杭径が定義される。
    /// 鋼種は SD345 または SD390 を使用。
    /// </summary>
    public class CapringTensionBar : BaseModel
    {
        public int No { get; set; }
        public int BarNum { get; set; }
        public string? BarSize { get; set; }
        /// <summary>引張定着筋の配置径 Dc (mm)</summary>
        public double Dc { get; set; }
        /// <summary>帯筋外径 (mm)</summary>
        public double HoopOutDia { get; set; }
        /// <summary>定着長さ (パイルキャップ側、定着版あり) (mm)</summary>
        public double AnchorLengthCapWithPlate { get; set; }
        /// <summary>定着長さ (パイルキャップ側、定着版なし) (mm)</summary>
        public double AnchorLengthCapWithoutPlate { get; set; }
        /// <summary>定着長さ (杭体側) (mm)</summary>
        public double AnchorLengthPileSide { get; set; }
        /// <summary>適用可能最小杭径 (mm)</summary>
        public double MinPileDia { get; set; }

        public string Name => $"{BarNum}-{BarSize}";

        public ObservableCollection<Spec> GetSpecs()
        {
            ObservableCollection<Spec> specs =
            [
                new Spec("配筋", "", Name, ""),
                new Spec("配置径", "Dc", $"{Dc:N0}", "mm"),
                new Spec("帯筋外径", "", $"{HoopOutDia:N0}", "mm"),
                new Spec("定着長さ (パイルキャップ側、定着版あり)", "", $"{AnchorLengthCapWithPlate:N0}", "mm"),
                new Spec("定着長さ (パイルキャップ側、定着版なし)", "", $"{AnchorLengthCapWithoutPlate:N0}", "mm"),
                new Spec("定着長さ (杭体側)", "", $"{AnchorLengthPileSide:N0}", "mm"),
                new Spec("適用可能最小杭径", "", $"{MinPileDia:N0}", "mm"),
            ];
            return specs;
        }
    }

    public class CapringTensionBarLoader
    {
        public static ObservableCollection<CapringTensionBar> LoadFromCsv(string filePath)
        {
            var bars = new ObservableCollection<CapringTensionBar>();

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            if (!csv.Read()) return bars;
            csv.ReadHeader();

            while (csv.Read())
            {
                try
                {
                    var firstRaw = csv.GetField(0);
                    if (string.IsNullOrWhiteSpace(firstRaw)) continue;
                    if (firstRaw.Trim().Equals("No", StringComparison.OrdinalIgnoreCase)) continue;

                    csv.TryGetField(0, out int no);
                    csv.TryGetField(1, out int barNum);
                    csv.TryGetField(2, out string? barSize);
                    csv.TryGetField(3, out double dc);
                    csv.TryGetField(4, out double hoopOutDia);
                    csv.TryGetField(5, out double anchorCapWithPlate);
                    csv.TryGetField(6, out double anchorCapWithoutPlate);
                    csv.TryGetField(7, out double anchorPileSide);
                    csv.TryGetField(8, out double minPileDia);

                    bars.Add(new CapringTensionBar
                    {
                        No = no,
                        BarNum = barNum,
                        BarSize = barSize?.Trim() ?? string.Empty,
                        Dc = dc,
                        HoopOutDia = hoopOutDia,
                        AnchorLengthCapWithPlate = anchorCapWithPlate,
                        AnchorLengthCapWithoutPlate = anchorCapWithoutPlate,
                        AnchorLengthPileSide = anchorPileSide,
                        MinPileDia = minPileDia,
                    });
                }
                catch
                {
                    continue;
                }
            }
            return bars;
        }
    }
}
