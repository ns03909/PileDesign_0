//using System;

//namespace PileDesignCore
//{
//    /// <summary>行列管理クラス</summary>
//    public class Matrix
//    {
//        // エレメント次元配列
//        protected double[,] _elements;

//        /// <summary>行数を取得</summary>
//        public int Rows
//        {
//            get
//            {
//                return _elements.GetLength(0);
//            }
//        }
//        /// <summary>列数を取得</summary>
//        public int Cols
//        {
//            get
//            {
//                return _elements.GetLength(1);
//            }
//        }

//        public Matrix()
//        {
//            _elements = new double[0, 0];
//        }
//        /// <summary>コンストラクタ。サイズ設定</summary>
//        /// <param name="nRowCount">行サイズ</param>
//        /// <param name="nColCount">列サイズ</param>
//        public Matrix(int nRowCount, int nColCount)
//        {
//            _elements = new double[nRowCount, nColCount];
//        }
//        /// <summary>インデクサ。行列の値を取得、設定</summary>
//        /// <param name="nRow">行</param>
//        /// <param name="nCol">列</param>
//        /// <returns>値</returns>
//        public double this[int nRow, int nCol]
//        {
//            get
//            {
//                return _elements[nRow, nCol];
//            }
//            set
//            {
//                _elements[nRow, nCol] = value;
//            }
//        }

//        /// <summary>クローン作成</summary>
//        /// <returns>クローン</returns>
//        public virtual Matrix Clone()
//        {
//            Matrix clone = new Matrix(this.Rows, this.Cols);
//            for (int i = 0; i < this.Rows; i++)
//            {
//                for (int j = 0; j < this.Cols; j++)
//                {
//                    clone[i, j] = this[i, j];
//                }
//            }
//            return clone;
//        }

//        /// <summary>一行分のデータを一気に設定する。</summary>
//        /// <param name="nRow">行インデックス</param>
//        /// <param name="dElements">要素配列</param>
//        public void SetRow(int nRow, params double[] dElements)
//        {
//            int nMin = Math.Min(dElements.Length, this.Cols);

//            for (int i = 0; i < nMin; i++)
//            {
//                this[nRow, i] = dElements[i];
//            }
//        }
//        /// <summary>一列分のデータを一気に設定する。</summary>
//        /// <param name="nCol">列インデックス</param>
//        /// <param name="dElements">要素配列</param>
//        public void SetCol(int nCol, params double[] dElements)
//        {
//            int nMin = Math.Min(dElements.Length, this.Rows);

//            for (int i = 0; i < nMin; i++)
//            {
//                this[i, nCol] = dElements[i];
//            }
//        }
//        public double GetMaxElement(out int nRow, out int nCol)
//        {
//            nRow = 0;
//            nCol = 0;
//            double dMax = 0.0;
//            for (int i = 0; i < this.Rows; i++)
//            {
//                for (int j = 0; j < this.Cols; j++)
//                {
//                    if (i == j) continue;
//                    if (Math.Abs(this[i, j]) <= dMax) continue;

//                    dMax = Math.Abs(this[i, j]);
//                    nRow = i;
//                    nCol = j;
//                }
//            }
//            return dMax;
//        }
//        #region Operator

//        /// <summary>行列の和</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dat2">指定の行列</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator +(Matrix dat1, Matrix dat2)
//        {
//            if (dat1.Rows != dat2.Rows) return null;
//            if (dat1.Cols != dat2.Cols) return null;

//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] + dat2[i, j];
//                }
//            }
//            return ret;
//        }

//        /// <summary>行列の和拡大</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dDat">拡大率</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator +(Matrix dat1, double dDat)
//        {
//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] + dDat;
//                }
//            }
//            return ret;
//        }
//        /// <summary>行列の差</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dat2">指定の行列</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator -(Matrix dat1, Matrix dat2)
//        {
//            if (dat1.Rows != dat2.Rows) return null;
//            if (dat1.Cols != dat2.Cols) return null;

//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] - dat2[i, j];
//                }
//            }
//            return ret;
//        }
//        /// <summary>行列の差拡大</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dDat">拡大率</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator -(Matrix dat1, double dDat)
//        {
//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] - dDat;
//                }
//            }
//            return ret;
//        }
//        /// <summary>行列の積</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dat2">指定の行列</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator *(Matrix dat1, Matrix dat2)
//        {
//            if (dat1.Cols != dat2.Rows) return null;

//            Matrix matrix = new Matrix(dat1.Rows, dat2.Cols);

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
//        /// <summary>行列とベクトルの積</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dat2">指定のベクトル</param>
//        /// <returns>結果ベクトル</returns>
//        public static Vector operator *(Matrix dat1, Vector dat2)
//        {
//            if (dat1.Cols != dat2.Count) return null;

//            Vector vector = (Vector)dat2.Clone();

//            for (int nRow = 0; nRow < dat1.Rows; nRow++)
//            {
//                vector[nRow] = 0;
//                for (int nCol = 0; nCol < dat1.Cols; nCol++)
//                {
//                    vector[nRow] += dat1[nRow, nCol] * dat2[nCol];
//                }
//            }
//            return vector;
//        }
//        /// <summary>行列の積拡大</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dDat">拡大率</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator *(Matrix dat1, double dDat)
//        {
//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] * dDat;
//                }
//            }
//            return ret;
//        }
//        /// <summary>行列の商拡大</summary>
//        /// <param name="dat1">指定の行列</param>
//        /// <param name="dDat">拡大率</param>
//        /// <returns>結果行列</returns>
//        public static Matrix operator /(Matrix dat1, double dDat)
//        {
//            Matrix ret = (Matrix)dat1.Clone();
//            for (int i = 0; i < dat1.Rows; i++)
//            {
//                for (int j = 0; j < dat1.Cols; j++)
//                {
//                    ret[i, j] = dat1[i, j] / dDat;
//                }
//            }
//            return ret;
//        }
//        #endregion
//    }
//}
