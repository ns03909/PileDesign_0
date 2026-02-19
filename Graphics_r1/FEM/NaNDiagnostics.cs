using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PileDesign.FEM
{
    /// <summary>
    /// NaN発生の根本原因を特定するための診断ロガー。
    /// 解析実行中にファイルに詳細ログを書き出し、NaN発生箇所を追跡する。
    /// </summary>
    internal static class NaNDiagnostics
    {
        private static readonly object _lock = new();
        private static StreamWriter _writer;
        private static string _logPath;
        private static int _iteration;
        private static bool _nanDetected;

        /// <summary>NaNが検出されたか</summary>
        public static bool NaNDetected => _nanDetected;

        /// <summary>ログファイルのパス</summary>
        public static string LogPath => _logPath;

        /// <summary>解析開始時に呼び出し、ログファイルを初期化</summary>
        public static void Begin()
        {
            lock (_lock)
            {
                _logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"NaN_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _writer = new StreamWriter(_logPath, false, Encoding.UTF8) { AutoFlush = true };
                _iteration = 0;
                _nanDetected = false;
                _writer.WriteLine($"=== NaN Diagnostics Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _writer.WriteLine();
            }
        }

        /// <summary>解析終了時に呼び出し、ログファイルを閉じる</summary>
        public static void End()
        {
            lock (_lock)
            {
                if (_writer != null)
                {
                    _writer.WriteLine();
                    _writer.WriteLine($"=== NaN Diagnostics Ended: {DateTime.Now:HH:mm:ss} ===");
                    _writer.WriteLine($"NaN detected: {_nanDetected}");
                    _writer.Close();
                    _writer = null;
                }
            }
        }

        /// <summary>反復回数をセット</summary>
        public static void SetIteration(int iter)
        {
            _iteration = iter;
        }

        /// <summary>ログ出力</summary>
        public static void Log(string message)
        {
            lock (_lock)
            {
                _writer?.WriteLine($"[iter={_iteration}] {message}");
            }
        }

        /// <summary>NaN警告ログ出力（NaN検出フラグも立てる）</summary>
        public static void LogNaN(string message)
        {
            lock (_lock)
            {
                _nanDetected = true;
                _writer?.WriteLine($"[iter={_iteration}] *** NaN *** {message}");
            }
        }

        /// <summary>ベクトルのNaN/Inf検査。NaN/Infが見つかった場合、該当インデックスをログ出力</summary>
        public static bool CheckVector(Vector<double> vec, string name, AnaModel model = null)
        {
            if (vec == null) { Log($"{name}: null"); return false; }
            var badIndices = new List<int>();
            for (int i = 0; i < vec.Count; i++)
            {
                if (!double.IsFinite(vec[i]))
                    badIndices.Add(i);
            }
            if (badIndices.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"{name}: {badIndices.Count}/{vec.Count} NaN/Inf entries at eq=[");
                foreach (var idx in badIndices.Take(20))
                {
                    string dofInfo = model != null ? IdentifyDof(model, idx) : $"eq{idx}";
                    sb.Append($"{idx}({dofInfo})={vec[idx]:E3}, ");
                }
                if (badIndices.Count > 20) sb.Append("...");
                sb.Append(']');
                LogNaN(sb.ToString());
                return true; // NaN found
            }
            return false; // clean
        }

        /// <summary>行列の対角要素のNaN/Inf検査</summary>
        public static bool CheckMatrixDiag(Matrix<double> mat, string name, AnaModel model = null)
        {
            if (mat == null) { Log($"{name}: null"); return false; }
            var badIndices = new List<int>();
            int n = Math.Min(mat.RowCount, mat.ColumnCount);
            for (int i = 0; i < n; i++)
            {
                if (!double.IsFinite(mat[i, i]))
                    badIndices.Add(i);
            }
            if (badIndices.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"{name} diag: {badIndices.Count}/{n} NaN/Inf at [");
                foreach (var idx in badIndices.Take(20))
                {
                    string dofInfo = model != null ? IdentifyDof(model, idx) : $"eq{idx}";
                    sb.Append($"{idx}({dofInfo})={mat[idx, idx]:E3}, ");
                }
                if (badIndices.Count > 20) sb.Append("...");
                sb.Append(']');
                LogNaN(sb.ToString());
                return true;
            }
            return false;
        }

        /// <summary>行列全体のNaN/Inf件数を検査（対角だけでなく全要素）</summary>
        public static int CountMatrixNaN(Matrix<double> mat)
        {
            if (mat == null) return 0;
            int count = 0;
            foreach (var (_, _, val) in mat.EnumerateIndexed(MathNet.Numerics.LinearAlgebra.Zeros.AllowSkip))
            {
                if (!double.IsFinite(val)) count++;
            }
            return count;
        }

        /// <summary>ノードの累積変位にNaNがあるか検査</summary>
        public static void CheckNodeDisplacements(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                var d = node.CumulativeDisp;
                if (d == null) continue;
                bool hasNaN = !double.IsFinite(d.Ux) || !double.IsFinite(d.Uy) || !double.IsFinite(d.Uz)
                           || !double.IsFinite(d.Rx) || !double.IsFinite(d.Ry) || !double.IsFinite(d.Rz);
                if (hasNaN)
                {
                    LogNaN($"Node '{node.Name}' CumulativeDisp has NaN: " +
                           $"Ux={d.Ux:E3} Uy={d.Uy:E3} Uz={d.Uz:E3} Rx={d.Rx:E3} Ry={d.Ry:E3} Rz={d.Rz:E3}");
                }
            }
        }

        /// <summary>ビーム内力にNaNがあるか検査</summary>
        public static void CheckBeamForces(IEnumerable<Beam> beams)
        {
            foreach (var beam in beams)
            {
                var f = beam.CumulativeForce;
                if (f == null) continue;
                bool hasNaN = false;
                for (int i = 0; i < 12; i++)
                {
                    if (!double.IsFinite(f.GetByIndex(i)))
                    {
                        hasNaN = true;
                        break;
                    }
                }
                if (hasNaN)
                {
                    var sb = new StringBuilder();
                    sb.Append($"Beam '{beam.Name}' CumulativeForce has NaN: [");
                    for (int i = 0; i < 12; i++)
                        sb.Append($"{f.GetByIndex(i):E3}, ");
                    sb.Append(']');
                    LogNaN(sb.ToString());
                }
            }
        }

        /// <summary>方程式番号からノード:DOF名を特定</summary>
        private static string IdentifyDof(AnaModel model, int eq)
        {
            string[] dofNames = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];
            foreach (var node in model.Nodes)
            {
                for (int d = 0; d < 6; d++)
                {
                    if (node.EquationNumber[d] == eq)
                        return $"{node.Name}:{dofNames[d]}";
                }
            }
            return $"eq{eq}";
        }

        /// <summary>NormsROnNormsFintの計算をトレースする診断版</summary>
        public static void DiagnoseResidual(Vector<double> vectorF, Vector<double> vectorT, Vector<double> vectorR, double normsROnNormsFint)
        {
            double normF = vectorF?.L2Norm() ?? 0;
            double normT = vectorT?.L2Norm() ?? 0;
            double normR = vectorR?.L2Norm() ?? 0;

            Log($"FindR: ||F||={normF:E6}, ||T||={normT:E6}, ||R||={normR:E6}, ratio={normsROnNormsFint:E6}");

            if (!double.IsFinite(normsROnNormsFint))
            {
                LogNaN($"NormsROnNormsFint is NaN! ||F||={normF:E6} (normsqF={normF * normF:E6}), ||T||={normT:E6}, ||R||={normR:E6}");
                if (normF < 1e-20)
                    LogNaN("ROOT CAUSE: ||F|| ≈ 0 → normsqR/normsqF = 0/0 = NaN");
                CheckVector(vectorF, "VectorF");
                CheckVector(vectorT, "VectorT");
                CheckVector(vectorR, "VectorR");
            }
        }
    }
}
