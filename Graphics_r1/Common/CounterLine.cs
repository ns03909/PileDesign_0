using System.Collections.Generic;
using System.Windows;

namespace PileDesign.Common
{
    public class ContourLine
    {
        public List<Point> Points { get; set; } = [];
        public double Value { get; set; } // 等高値
    }
}
