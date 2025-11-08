using System.Windows;

namespace PileDesign.Common
{
    public class ContourGrid
    {
        // 格子点の値（[i, j]：i=行, j=列）
        public double[,] Values { get; set; }

        // 格子点の座標（[i, j]：Point型でXY座標を保持）
        public Point[,] Points { get; set; }

        // 行数・列数
        public int RowCount => Values.GetLength(0);
        public int ColCount => Values.GetLength(1);

        public ContourGrid(int rowCount, int colCount)
        {
            Values = new double[rowCount, colCount];
            Points = new Point[rowCount, colCount];
        }
    }
}
