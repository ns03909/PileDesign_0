//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Sample
//{
//    internal class Class1
//    {
//    }
//}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PileDesignCore
{
    internal class Solver
    {
        public static void Solve(AnaModel anaModel)
        {
            // 計算
            CalcNodeDisp(anaModel);
            CalcBeamStress(anaModel);
            // 出力
            OutputResult(anaModel);
        }

        private static void CalcNodeDisp(AnaModel anaModel)
        {
            double[] d = Utils.SolveLinearSystem(anaModel.K, anaModel.F);

            // 節点変位を結果用オブジェクトにセット
            for (int i = 0; i < anaModel.Nodes.Count; i++)
            {
                Node node = anaModel.Nodes[i];
                OutNode outNode = new OutNode();
                int size = 6;
                double[] disp = new double[size];
                for (int index = 0; index < size; index++)
                {
                    int eq = node.EquationNumber[index];
                    if (eq < 0)
                    {
                        disp[index] = 0.0;
                    }
                    else
                    {
                        disp[index] = d[eq];
                    }
                }
                outNode.SetDisp(disp);
                node.SetOutNode(outNode);
            }
        }

        private static void CalcBeamStress(AnaModel anaModel)
        {
            foreach (Beam beam in anaModel.Beams)
            {
                OutBeam outBeam = new OutBeam();
                Node nodeI = beam.NodeI;
                Node nodeJ = beam.NodeJ;

                // 節点の変位ベクトル（全体座標系）
                double[] dispNodeI = nodeI.OutNode.Disp;
                double[] dispNodeJ = nodeJ.OutNode.Disp;
                double[] disp = dispNodeI.Concat(dispNodeJ).ToArray();

                // 節点の変位ベクトル（要素座標系）
                double[,] t = Utils.GetTransformMatrix(nodeI, nodeJ);
                double[] d = Utils.MatrixVectorMultiply(t, disp);

                // 要素応力 f = k * d
                double[,] k = beam.Ke;
                double[] f = Utils.MatrixVectorMultiply(k, d);

                // 要素応力を結果用オブジェクトにセット
                outBeam.SetStress(f);
                beam.SetOutBeam(outBeam);
            }
        }

        private static void OutputResult(AnaModel anaModel)
        {
            string outFilePath = "Result/result.csv";
            CheckDirectory(outFilePath);
            List<string> lines = new List<string>();

            // 節点変位
            lines.Add("*Node\n");
            lines.Add("Name,UX,UY,UZ,TX,TY,TZ\n");
            foreach (Node node in anaModel.Nodes)
            {
                List<string> tmp = new List<string> { node.Name };
                tmp.AddRange(node.OutNode.Disp.Select(d => d.ToString()));
                lines.Add(string.Join(",", tmp) + "\n");
            }
            lines.Add("\n");

            // 梁要素応力
            lines.Add("*Beam\n");
            lines.Add("Name,UXI,UYI,UZI,TXI,TYI,TZI,UXJ,UYJ,UZJ,TXJ,TYJ,TZJ\n");
            foreach (Beam beam in anaModel.Beams)
            {
                List<string> tmp = new List<string> { beam.Name };
                tmp.AddRange(beam.OutBeam.Stress.Select(s => s.ToString()));
                lines.Add(string.Join(",", tmp) + "\n");
            }
            lines.Add("\n");

            File.WriteAllLines(outFilePath, lines, Encoding.UTF8);
        }

        private static void CheckDirectory(string path)
        {
            string filePath = Path.GetDirectoryName(path);
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
        }


    }
}
