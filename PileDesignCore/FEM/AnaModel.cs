using System.Collections.Generic;

namespace PileDesignCore
{
    internal class AnaModel
    {
        public List<Node> Nodes { get; }
        public List<Beam> Beams { get; }
        //public Matrix<double> K { get; }
        public double[,] K { get; }
        //public Vector<double> F { get; }
        public double[] F { get; }

        public AnaModel(List<Node> listNode, List<Beam> listBeam)
        {
            Nodes = listNode;
            Beams = listBeam;

            // 方程式番号
            int countFree = 0;
            int countFix = 0;
            foreach (var node in Nodes)
            {
                for (int index = 0; index < 6; index++)
                {
                    if (node.GetBoundary(index) == BoundaryType.Free)
                    {
                        // 自由 -> 正値
                        countFree++;
                        node.SetEquationNumber(index, countFree - 1);
                    }
                    else
                    {
                        // 固定 -> 負値
                        countFix--;
                        node.SetEquationNumber(index, countFix);
                    }
                }
            }

            // 全体剛性マトリクス
            //K = new Matrix<double>(countFree, countFree);
            double[,] K = new double[countFree, countFree];
            foreach (var beam in Beams)
            {
                //var beamStiffness = beam.TransElemStiffToGlobal();
                //MapOnMatrix(K, beamStiffness, beam.NodeI.EquationNumber, beam.NodeJ.EquationNumber);
                beam.MapOnGlobalStiff(K);
            }

            // 全体荷重ベクトル
            /*F = new Vector<double>(new double[countFree])*/;
            double[] F = new double[countFree];
            foreach (var node in Nodes)
            {
                //MapOnVec/*tor(*/F, node.GlobalLoad, node.EquationNumber);
                node.MapOnGlobalLoad(F);
            }
        }


        private void MapOnMatrix(Matrix<double> matrix, Matrix<double> src, int[] rows, int[] cols)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                for (int j = 0; j < cols.Length; j++)
                {
                    matrix[rows[i], cols[j]] += src[i, j];
                }
            }
        }

        private void MapOnVector(Vector<double> vector, Vector<double> src, int[] indices)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                vector[indices[i]] += src[i];
            }
        }
        // Matrix and Vector classes are not built-in C# types, so you may need to implement or use a third-party library for these types.
        // The following is a simplified implementation of Matrix and Vector classes for demonstration purposes.
    }
}
class Matrix<T>
{
    private T[,] _data;

    public Matrix(int rows, int columns)
    {
        _data = new T[rows, columns];
    }

    public T this[int row, int column]
    {
        get { return _data[row, column]; }
        set { _data[row, column] = value; }
    }
}

class Vector<T>
{
    private T[] _data;

    public Vector(T[] data)
    {
        _data = data;
    }

    public T this[int index]
    {
        get { return _data[index]; }
        set { _data[index] = value; }
    }
}

