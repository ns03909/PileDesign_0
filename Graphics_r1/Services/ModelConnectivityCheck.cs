using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 解析を始める前に、モデルの「つながり」を検査する。
    ///
    /// 剛性行列を組んでから初めて分かる不安定は、利用者にとって原因が読み取れない。
    /// 実際に「剛性マトリクスにゼロ/負の対角成分が 6 個」というメッセージだけが出て、
    /// 真因が「どこにもつながっていない一般節点が 1 つある」ことだった例がある。
    ///
    /// ここでは<b>入力の段階で分かること</b>だけを見る。行列は組まない。
    ///
    /// <list type="bullet">
    /// <item>基礎梁の端点が実在しない (参照先を消したあと)</item>
    /// <item>長さが 0 の基礎梁</item>
    /// <item>杭につながっていない、基礎梁だけの島</item>
    /// <item>同じ位置に複数の杭</item>
    /// </list>
    ///
    /// つながっていない一般節点は <see cref="InputModel.GetUnconnectedGeneralNodes"/> が
    /// 受け持つ (解析モデル側が取り除くので、ここでは扱わない)。
    /// </summary>
    public static class ModelConnectivityCheck
    {
        /// <summary>解析を止めるべき問題。</summary>
        public static List<string> CollectErrors(InputModel inputModel)
        {
            var errors = new List<string>();
            if (inputModel == null) return errors;

            var fb = inputModel.FoundationBeamInput;
            var beams = fb?.Beams;
            if (beams == null || beams.Count == 0) return errors;

            // 実在する端点の一覧
            var generalIds = new HashSet<Guid>(
                (inputModel.InputNodes ?? []).Where(n => n != null).Select(n => n.UniqueId));
            var foundationIds = new HashSet<Guid>(
                (inputModel.FoundationBeamInput?.Nodes ?? []).Where(n => n != null).Select(n => n.Id));
            var pileIds = new HashSet<Guid>(
                (inputModel.PileLayoutItems ?? []).Where(p => p != null).Select(p => p.UniqueId));

            bool Exists(NodeReferenceType type, Guid id) => type switch
            {
                NodeReferenceType.GeneralNode => generalIds.Contains(id),
                NodeReferenceType.FoundationNode => foundationIds.Contains(id),
                NodeReferenceType.PileLayout => pileIds.Contains(id),
                _ => false,
            };

            // ── 1. 端点が実在しない基礎梁 ──
            // 参照先を消したときに残る。解析側は端点を解決できず、その梁を静かに落とす。
            // 「梁を描いたのに効いていない」形になるので止める。
            foreach (var b in beams)
            {
                if (b == null) continue;
                if (!Exists(b.NodeI_Type, b.NodeI_Id))
                    errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 始点の参照先が見つかりません。参照先を消していないか確認してください。");
                if (!Exists(b.NodeJ_Type, b.NodeJ_Id))
                    errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 終点の参照先が見つかりません。参照先を消していないか確認してください。");
            }

            // ── 2. 長さが 0 の基礎梁 ──
            // 両端が同じ点だと剛性が定義できない。
            foreach (var b in beams)
            {
                if (b == null) continue;
                if (b.NodeI_Type == b.NodeJ_Type && b.NodeI_Id == b.NodeJ_Id)
                {
                    errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 始点と終点が同じ点です。");
                    continue;
                }

                var pi = inputModel.GetNodeCoordinates(b.NodeI_Type, b.NodeI_Id);
                var pj = inputModel.GetNodeCoordinates(b.NodeJ_Type, b.NodeJ_Id);
                if (pi.HasValue && pj.HasValue)
                {
                    double dx = pi.Value.X - pj.Value.X;
                    double dy = pi.Value.Y - pj.Value.Y;
                    double dz = pi.Value.Z - pj.Value.Z;
                    if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < 1.0e-6)
                        errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 始点と終点が同じ位置にあります (長さ 0)。");
                }
            }

            // ── 3. 杭につながっていない、基礎梁だけの島 ──
            //
            // 杭頭は剛体を通して代表節点につながるので、杭を 1 本でも含む一群は支持される。
            // 杭を 1 本も含まない一群は、どこにも支えが無い。
            // (剛床のときは水平 3 成分だけが代表節点に従うので、鉛直と回転はやはり自由)
            foreach (var island in FindIslandsWithoutPiles(fb, pileIds))
                errors.Add($"基礎梁 {island}: どの杭にもつながっていません。支えが無いため解析できません。");

            return errors;
        }

        /// <summary>止めるほどではないが、意図しない入力の可能性が高いもの。</summary>
        public static List<string> CollectWarnings(InputModel inputModel)
        {
            var warnings = new List<string>();
            var piles = inputModel?.PileLayoutItems;
            if (piles == null) return warnings;

            // 同じ位置に複数の杭。剛体でつながるので解析は通るが、
            // 貼り付けの繰り返しなどで意図せず重なっていることがある。
            var seen = new Dictionary<(long, long), int>();
            foreach (var p in piles)
            {
                if (p == null) continue;
                var key = ((long)Math.Round(p.X * 1000.0), (long)Math.Round(p.Y * 1000.0));
                if (seen.TryGetValue(key, out int firstNo))
                    warnings.Add($"杭 No.{p.No}: 杭 No.{firstNo} と同じ位置にあります (X={p.X:N3}, Y={p.Y:N3})。");
                else
                    seen[key] = p.No;
            }
            return warnings;
        }

        /// <summary>
        /// 基礎梁でつながった一群のうち、杭を 1 本も含まないものを返す。
        ///
        /// 端点を「種別 + 識別子」で 1 つの点として扱い、梁を辺とみなして数え上げる。
        /// </summary>
        private static IEnumerable<string> FindIslandsWithoutPiles(
            FoundationBeamInput fb, HashSet<Guid> pileIds)
        {
            var parent = new Dictionary<(NodeReferenceType, Guid), (NodeReferenceType, Guid)>();

            (NodeReferenceType, Guid) Find((NodeReferenceType, Guid) x)
            {
                if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
                if (!p.Equals(x)) { parent[x] = Find(p); }
                return parent[x];
            }
            void Union((NodeReferenceType, Guid) a, (NodeReferenceType, Guid) b)
            {
                var ra = Find(a); var rb = Find(b);
                if (!ra.Equals(rb)) parent[ra] = rb;
            }

            var beams = fb.Beams;
            var beamsOfRoot = new Dictionary<(NodeReferenceType, Guid), List<int>>();
            foreach (var b in beams)
            {
                if (b == null) continue;
                Union((b.NodeI_Type, b.NodeI_Id), (b.NodeJ_Type, b.NodeJ_Id));
            }
            foreach (var b in beams)
            {
                if (b == null) continue;
                var root = Find((b.NodeI_Type, b.NodeI_Id));
                if (!beamsOfRoot.TryGetValue(root, out var list))
                    beamsOfRoot[root] = list = [];
                list.Add(fb.GetBeamNo(b));
            }

            // 杭を含む根を集める
            var rootsWithPile = new HashSet<(NodeReferenceType, Guid)>();
            foreach (var key in parent.Keys.ToList())
            {
                if (key.Item1 == NodeReferenceType.PileLayout && pileIds.Contains(key.Item2))
                    rootsWithPile.Add(Find(key));
            }

            foreach (var (root, nos) in beamsOfRoot)
            {
                if (rootsWithPile.Contains(root)) continue;
                var sorted = nos.Distinct().OrderBy(n => n).ToList();
                string label = sorted.Count <= 5
                    ? "No." + string.Join(", ", sorted)
                    : $"No.{string.Join(", ", sorted.Take(5))} ほか {sorted.Count - 5} 本";
                yield return label;
            }
        }
    }
}
