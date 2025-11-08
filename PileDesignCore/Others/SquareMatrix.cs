//namespace PileDesignCore
//{
//    /// <summary>正方行列管理クラス</summary>
//    public class SquareMatrix : Matrix
//    {
//        /// <summary>サイズ</summary>
//        public int Size
//        {
//            get
//            {
//                return this.Rows;
//            }
//        }
//        /// <summary>コンストラクタ。サイズ設定</summary>
//        　　/// <param name="nCount">サイズ</param>
//        public SquareMatrix(int nCount)
//        {
//            _elements = new double[nCount, nCount];
//        }
//        /// <summary>転置行列を取得</summary>
//        public SquareMatrix TransposedMatrix
//        {
//            get
//            {
//                SquareMatrix matrix = (SquareMatrix)this.Clone();
//                matrix.Transpose();
//                return matrix;
//            }
//        }

//        /// <summary>クローン作成</summary>
//        　　/// <returns>クローン</returns>
//        public override Matrix Clone()
//        {
//            SquareMatrix clone = new SquareMatrix(this.Size);
//            for (int i = 0; i < this.Rows; i++)
//            {
//                for (int j = 0; j < this.Cols; j++)
//                {
//                    clone[i, j] = this[i, j];
//                }
//            }
//            return clone;
//        }
//        /// <summary>転置する</summary>
//        public void Transpose()
//        {
//            for (int i = 0; i < this.Rows; i++)
//            {
//                for (int j = i + 1; j < this.Cols; j++)
//                {
//                    double dSwap = this[i, j];
//                    this[i, j] = this[j, i];
//                    this[j, i] = dSwap;
//                }
//            }
//        }
//        /// <summary>単位行列に設定する</summary>
//        　　/// <param name="dUnit">単位行列値</param>
//        public void SetUnitMatrix(double dUnit)
//        {
//            for (int i = 0; i < this.Rows; i++)
//            {
//                for (int j = 0; j < this.Cols; j++)
//                {
//                    if (i == j)
//                    {
//                        this[i, j] = dUnit;
//                    }
//                    else
//                    {
//                        this[i, j] = 0;
//                    }
//                }
//            }
//        }
//        /// <summary>単位行列に設定する</summary>
//        public void SetUnitMatrix()
//        {
//            SetUnitMatrix(1);
//        }
//        /// <summary>行列の積</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dat2">指定の行列</param>
//        /// <returns>結果行列</returns>
//        public static SquareMatrix operator *(SquareMatrix dat1, SquareMatrix dat2)
//        {
//            if (dat1.Cols != dat2.Rows) return null;

//            SquareMatrix matrix = new SquareMatrix(dat1.Rows);

//            for (int i = 0; i < matrix.Rows; i++)
//            {
//                for (int j = 0; j < matrix.Cols; j++)
//                {
//                    for (int t = 0; t < dat1.Cols; t++)
//                    {
//                        matrix[i, j] += dat1[i, t] * dat2[t, j];
//                    }
//                }
//            }
//            return matrix;
//        }
//    }

//}
