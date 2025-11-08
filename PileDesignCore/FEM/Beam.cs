using System;

namespace PileDesignCore
{
    internal class Beam
    {
        public string Name { get; }
        public Section Section { get; }
        public Node NodeI { get; }
        public Node NodeJ { get; }
        public double Length { get; }
        public double[,] Ke { get; }
        public OutBeam OutBeam { get; private set; }

        public Beam(string name, Section section, Node nodeI, Node nodeJ)
        {
            Name = name;
            Section = section;
            NodeI = nodeI;
            NodeJ = nodeJ;
            Length = Utils.GetLengthBetweenTwoNodes(nodeI, nodeJ);
            Ke = GetElemStiff();
        }

        private double[,] GetElemStiff()
        {
            double eA_per_L = Section.Material.E * Section.AX / Length;
            double gJ_per_L = Section.Material.G * Section.IX / Length;
            double eIy_per_L1 = Section.Material.E * Section.IY / Length;
            double eIz_per_L1 = Section.Material.E * Section.IZ / Length;
            double eIy_per_L2 = eIy_per_L1 / Length;
            double eIz_per_L2 = eIz_per_L1 / Length;
            double eIy_per_L3 = eIy_per_L2 / Length;
            double eIz_per_L3 = eIz_per_L2 / Length;

            double[,] ke = {
                { eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, -eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0 },
                { 0.0, 12.0 * eIz_per_L3, 0.0, 0.0, 0.0, 6.0 * eIz_per_L2, 0.0, -12.0 * eIz_per_L3, 0.0, 0.0, 0.0, 6.0 * eIz_per_L2 },
                { 0.0, 0.0, 12.0 * eIy_per_L3, 0.0, -6.0 * eIy_per_L2, 0.0, 0.0, 0.0, -12.0 * eIy_per_L3, 0.0, -6.0 * eIy_per_L2, 0.0 },
                { 0.0, 0.0, 0.0, gJ_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, -gJ_per_L, 0.0, 0.0 },
                { 0.0, 0.0, -6.0 * eIy_per_L2, 0.0, 4.0 * eIy_per_L1, 0.0, 0.0, 0.0, 6.0 * eIy_per_L2, 0.0, 2.0 * eIy_per_L1, 0.0 },
                { 0.0, 6.0 * eIz_per_L2, 0.0, 0.0, 0.0, 4.0 * eIz_per_L1, 0.0, -6.0 * eIz_per_L2, 0.0, 0.0, 0.0, 2.0 * eIz_per_L1 },
                { -eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0 },
                { 0.0, -12.0 * eIz_per_L3, 0.0, 0.0, 0.0, -6.0 * eIz_per_L2, 0.0, 12.0 * eIz_per_L3, 0.0, 0.0, 0.0, -6.0 * eIz_per_L2 },
                { 0.0, 0.0, -12.0 * eIy_per_L3, 0.0, 6.0 * eIy_per_L2, 0.0, 0.0, 0.0, 12.0 * eIy_per_L3, 0.0, 6.0 * eIy_per_L2, 0.0 },
                { 0.0, 0.0, 0.0, gJ_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, gJ_per_L, 0.0, 0.0 },
                { 0.0, 0.0, -6.0 * eIy_per_L2, 0.0, 2.0 * eIy_per_L1, 0.0, 0.0, 0.0, 6.0 * eIy_per_L2, 0.0, 4.0 * eIy_per_L1, 0.0 },
                { 0.0, 6.0 * eIz_per_L2, 0.0, 0.0, 0.0, 2.0 * eIz_per_L1, 0.0, -6.0 * eIz_per_L2, 0.0, 0.0, 0.0, 4.0 * eIz_per_L1 }
            };

            return ke;
        }

        public double[,] TransElemStiffToGlobal()
        {
            double[,] t = Utils.GetTransformMatrix(NodeI, NodeJ);
            double[,] kt = MatrixMultiply(Ke, t);
            double[,] tkt = MatrixMultiply(MatrixInverse(t), kt);
            return tkt;
        }

        public void MapOnGlobalStiff(double[,] matrixGlobalStiffness)
        {
            double[,] tkt = TransElemStiffToGlobal();
            int[] eq = new int[24];
            Array.Copy(NodeI.EquationNumber, eq, 6);
            Array.Copy(NodeJ.EquationNumber, 0, eq, 6, 6);

            for (int iRow = 0; iRow < 12; iRow++)
            {
                int eRow = eq[iRow];
                if (eRow < 0) continue;
                for (int iCol = 0; iCol < 12; iCol++)
                {
                    int eCol = eq[iCol];
                    if (eCol < 0) continue;
                    matrixGlobalStiffness[eRow, eCol] += tkt[iRow, iCol];
                }
            }
        }

        public void SetOutBeam(OutBeam outBeam)
        {
            OutBeam = outBeam;
        }

        private double[,] MatrixMultiply(double[,] matrixA, double[,] matrixB)
        {
            int rowsA = matrixA.GetLength(0);
            int colsA = matrixA.GetLength(1);
            int colsB = matrixB.GetLength(1);

            double[,] result = new double[rowsA, colsB];

            for (int i = 0; i < rowsA; i++)
            {
                for (int j = 0; j < colsB; j++)
                {
                    for (int k = 0; k < colsA; k++)
                    {
                        result[i, j] += matrixA[i, k] * matrixB[k, j];
                    }
                }
            }

            return result;
        }

        private double[,] MatrixInverse(double[,] matrix)
        {
            // Implement matrix inversion logic here
            // This is a simplified version, and you may want to use a library for matrix inversion in a real-world scenario
            // or implement more robust matrix inversion logic.

            int n = matrix.GetLength(0);
            int m = matrix.GetLength(1);

            double[,] invA = new double[n, m];

            if (n == m)
            {

                int max;
                double tmp;

                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        invA[j, i] = (i == j) ? 1 : 0;
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    max = k;
                    for (int j = k + 1; j < n; j++)
                    {
                        if (Math.Abs(matrix[j, k]) > Math.Abs(matrix[max, k]))
                        {
                            max = j;
                        }
                    }

                    if (max != k)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            // 入力行列側
                            tmp = matrix[max, i];
                            matrix[max, i] = matrix[k, i];
                            matrix[k, i] = tmp;
                            // 単位行列側
                            tmp = invA[max, i];
                            invA[max, i] = invA[k, i];
                            invA[k, i] = tmp;
                        }
                    }

                    tmp = matrix[k, k];

                    for (int i = 0; i < n; i++)
                    {
                        matrix[k, i] /= tmp;
                        invA[k, i] /= tmp;
                    }

                    for (int j = 0; j < n; j++)
                    {
                        if (j != k)
                        {
                            tmp = matrix[j, k] / matrix[k, k];
                            for (int i = 0; i < n; i++)
                            {
                                matrix[j, i] = matrix[j, i] - matrix[k, i] * tmp;
                                invA[j, i] = invA[j, i] - invA[k, i] * tmp;
                            }
                        }
                    }

                }


                //逆行列が計算できなかった時の措置
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (double.IsNaN(invA[j, i]))
                        {
                            Console.WriteLine("Error : Unable to compute inverse matrix");
                            invA[j, i] = 0;//ここでは，とりあえずゼロに置き換えることにする
                        }
                    }
                }

                return invA;

            }
            else
            {
                Console.WriteLine("Error : It is not a square matrix");
                return invA;
            }

        }
    }
}


