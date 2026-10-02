using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    /// <summary>
    /// 残差の大きい自由度 1 つ。どの節点のどの向きに、どれだけ力の釣り合いが残っているか。
    /// </summary>
    /// <param name="NodeName">節点の名前 (杭節点は <c>杭節点-{杭番号}-{節点順}</c>)</param>
    /// <param name="Dof">向き (Ux・Uy・Uz は力 kN、Rx・Ry・Rz はモーメント kN·m)</param>
    /// <param name="Residual">残差 (外力 − 内力)</param>
    /// <param name="Z">節点の Z 座標 [m]</param>
    /// <param name="PileNo">杭節点なら杭番号。そうでなければ null</param>
    /// <param name="Springs">この節点につながるばね (杭頭の回転ばね・地盤ばね) の名前</param>
    public sealed record ResidualHotspot(string NodeName, string Dof, double Residual, double Z, int? PileNo, IReadOnlyList<string> Springs)
    {
        public bool IsMoment => Dof.StartsWith('R');

        /// <summary>1 行の説明。例「杭 No.3 (Z=-2.50 m) の Ry: 残差 1.2E+01 kN·m (杭頭の回転ばね)」</summary>
        public string Describe()
        {
            string where = $"{ResidualHotspots.Label(NodeName)} (Z={Z:F2} m)";
            string unit = IsMoment ? "kN·m" : "kN";
            string springs = Springs.Count > 0 ? $" ({string.Join("・", Springs)})" : "";
            return $"{where} の {Dof}: 残差 {Residual:E2} {unit}{springs}";
        }
    }

    /// <summary>
    /// 収束しなかったステップの手掛かり: 理由・残差の推移とその傾向・残差の大きい箇所。
    ///
    /// <para>以前は未収束を「ケース名・ステップ・反復回数・最終残差」だけで知らせていた。どこで釣り合わないのか
    /// (どの杭のどの深さ・どのばね) は、開発用のログ (FindR の診断) にしか出ず、利用者は原因に辿り着けなかった。</para>
    /// </summary>
    public sealed record UnconvergedDiagnosis(string Reason, IReadOnlyList<double> ResidualTrend, string TrendLabel, IReadOnlyList<ResidualHotspot> Hotspots)
    {
        /// <summary>推移として残す反復の数 (最後から)。</summary>
        public const int TrendLength = 12;

        /// <summary>残差の大きい箇所として残す数。</summary>
        public const int HotspotCount = 5;

        public static UnconvergedDiagnosis Build(string reason, IReadOnlyList<double> residuals, IReadOnlyList<ResidualHotspot> hotspots)
        {
            var trend = residuals.Skip(Math.Max(0, residuals.Count - TrendLength)).ToArray();
            return new UnconvergedDiagnosis(reason, trend, ClassifyTrend(trend), hotspots);
        }

        /// <summary>
        /// 残差の推移の傾向。最後の数反復で見る。
        /// <list type="bullet">
        /// <item>数値でない: NaN・無限大が混じる (剛性が失われた・発散した)</item>
        /// <item>増加: 最後が最初の 2 倍を超える (発散)</item>
        /// <item>減少中: 最後が最初の半分未満 (反復が足りない。ステップを細かくすると収束しうる)</item>
        /// <item>振動: 増減が交互に入れ替わる (行き来している。剛性の切り替わり・除荷の境目にいる)</item>
        /// <item>停滞: そのほか (下がらない。耐力に近い・荷重が大きすぎる)</item>
        /// </list>
        /// </summary>
        public static string ClassifyTrend(IReadOnlyList<double> trend)
        {
            if (trend.Count == 0) return "記録なし";
            if (trend.Any(r => !double.IsFinite(r))) return "数値でない";
            if (trend.Count < 3) return "反復が少ない";
            double first = trend[0], last = trend[^1];
            if (last > 2.0 * first) return "増加 (発散)";
            if (last < 0.5 * first) return "減少中 (反復が足りない)";
            int turns = 0;
            for (int i = 2; i < trend.Count; i++)
                if (Math.Sign(trend[i] - trend[i - 1]) * Math.Sign(trend[i - 1] - trend[i - 2]) < 0) turns++;
            if (turns >= (trend.Count - 2) * 0.6) return "振動 (行き来している)";
            return "停滞";
        }

        /// <summary>レポート用の複数行 (字下げは呼び出し側)。</summary>
        public IEnumerable<string> DescribeLines()
        {
            yield return $"理由: {Reason}";
            if (ResidualTrend.Count > 0)
                yield return $"残差の推移 (最後の {ResidualTrend.Count} 反復): {string.Join(" → ", ResidualTrend.Select(r => r.ToString("E1")))}  傾向: {TrendLabel}";
            if (Hotspots.Count > 0)
            {
                yield return "残差の大きい箇所:";
                foreach (var h in Hotspots) yield return "  " + h.Describe();
            }
        }
    }

    public static class ResidualHotspots
    {
        private static readonly string[] DofNames = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];

        /// <summary>
        /// 節点の名前を利用者に通じる呼び方に。解析モデルの節点の名前は <see cref="AnalysisModelling"/> が付ける。
        /// </summary>
        public static string Label(string nodeName)
        {
            if (PileNodeNaming.PileNoOf(nodeName) is int no) return $"杭 No.{no}";
            if (nodeName == "ActionPoint") return "荷重の作用点 (基礎)";
            if (TrySuffix(nodeName, "CapNode-", out var cap)) return $"杭 No.{cap} の杭頭 (基礎側)";
            if (TrySuffix(nodeName, "FoundationNode-P", out var joint)) return $"杭 No.{joint} の接合節点";
            if (TrySuffix(nodeName, "FoundationNode-", out var fb)) return $"基礎梁の節点 {fb}";
            if (TrySuffix(nodeName, "InputNode-", out var input)) return $"節点 {input}";
            return nodeName;
        }

        /// <summary>杭にかかわる節点 (杭節点・杭頭・接合節点) なら杭番号。</summary>
        public static int? PileNoOf(string nodeName)
        {
            if (PileNodeNaming.PileNoOf(nodeName) is int no) return no;
            if (TrySuffix(nodeName, "CapNode-", out var cap) && int.TryParse(cap, out int c)) return c;
            if (TrySuffix(nodeName, "FoundationNode-P", out var joint) && int.TryParse(joint, out int j)) return j;
            return null;
        }

        private static bool TrySuffix(string name, string prefix, out string suffix)
        {
            suffix = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : "";
            return suffix.Length > 0;
        }

        /// <summary>
        /// いまの残差ベクトルで、残差の絶対値が大きい自由度を <paramref name="count"/> 個 (大きい順)。
        /// 強制変位の自由度は除く (反力なので残差ではない)。節点の名前・杭番号・つながるばねに読み替える。
        /// 力 (kN) とモーメント (kN·m) を同じ順位に並べるので、目安として使う。
        ///
        /// <para>荷重の作用点 (基礎の剛体) には全体の釣り合いの残りが集まり、ほぼ常に 1 位になる。それだけでは
        /// どの杭なのかが分からないので、全体の上位 2 つに加え、残りは杭にかかわる節点の上位で埋める。</para>
        /// </summary>
        public static IReadOnlyList<ResidualHotspot> Top(AnaModel model, int count)
        {
            var r = model.VectorR;
            var forced = model.VectorDOFForcedDisp;
            int countFree = model.CountFree;
            if (r == null || count <= 0) return [];
            int n = Math.Min(countFree, r.Count);
            var ranked = Enumerable.Range(0, n)
                .Where(i => !(i < forced.Count && forced[i]) && double.IsFinite(r[i]) && r[i] != 0.0)
                .OrderByDescending(i => Math.Abs(r[i])).ThenBy(i => i)
                .ToList();
            if (ranked.Count == 0) return [];

            var byEquation = new Dictionary<int, (Node Node, int Dof)>();
            foreach (var node in model.Nodes)
                for (int d = 0; d < 6; d++)
                {
                    int eq = node.EquationNumber[d];
                    if (eq >= 0 && eq < n && !byEquation.ContainsKey(eq)) byEquation[eq] = (node, d);
                }

            var springsByNode = new Dictionary<Node, List<string>>();
            void Attach(Node? node, string label)
            {
                if (node == null) return;
                if (!springsByNode.TryGetValue(node, out var list)) springsByNode[node] = list = [];
                if (!list.Contains(label)) list.Add(label);
            }
            foreach (var rs in model.RotationalSprings ?? [])
            {
                Attach(rs?.NodeI, "杭頭の回転ばね");
                Attach(rs?.NodeJ, "杭頭の回転ばね");
            }
            foreach (var hs in model.HorizontalSoilSprings ?? [])
            {
                Attach(hs?.NodeI, "地盤ばね");
                Attach(hs?.NodeJ, "地盤ばね");
            }

            ResidualHotspot Describe(int eq)
                => byEquation.TryGetValue(eq, out var hit)
                    ? new ResidualHotspot(hit.Node.Name, DofNames[hit.Dof], r[eq], hit.Node.Coord.Z,
                        PileNoOf(hit.Node.Name),
                        springsByNode.TryGetValue(hit.Node, out var springs) ? springs : [])
                    : new ResidualHotspot($"方程式 {eq}", "?", r[eq], double.NaN, null, []);

            var all = ranked.Select(Describe).ToList();
            var chosen = all.Take(Math.Min(2, count)).ToList();
            chosen.AddRange(all.Skip(chosen.Count).Where(h => h.PileNo != null).Take(count - chosen.Count));
            chosen.AddRange(all.Except(chosen).Take(count - chosen.Count));
            return chosen.OrderByDescending(h => Math.Abs(h.Residual)).ToList();
        }
    }
}
