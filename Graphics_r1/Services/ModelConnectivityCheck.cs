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
    /// <item>同じ種類の節点に同じ識別子が 2 つ以上ある (端点がどちらを指すか決まらない)</item>
    /// <item>長さが 0 の基礎梁・端点の座標が数値でない基礎梁</item>
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

            // ── 0. 識別子の重複 ──
            // 基礎梁の端点は「種類 + 識別子」で引く。同じ種類に同じ識別子が 2 つあると、参照は一覧の検査を通るのに、
            // 座標を引くときはどちらか一方 (先に見つかった方) を黙って使う。ファイルを手で直した・複製の不具合などで起きる。
            AddDuplicateIdErrors(errors, "一般節点",
                (inputModel.InputNodes ?? []).Where(n => n != null).Select(n => (n.UniqueId, n.No)));
            AddDuplicateIdErrors(errors, "基礎梁節点",
                (fb!.Nodes ?? []).Where(n => n != null).Select(n => (n.Id, n.No)));
            AddDuplicateIdErrors(errors, "杭配置",
                (inputModel.PileLayoutItems ?? []).Where(p => p != null).Select(p => (p.UniqueId, p.No)));

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
                    // 座標が数値でない (NaN・無限大) と、下の長さの比較は偽になって素通りする。先に端点ごとに見る
                    bool badI = !IsFinite(pi.Value), badJ = !IsFinite(pj.Value);
                    if (badI || badJ)
                    {
                        if (badI) errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 始点 ({DescribeEndpoint(inputModel, b.NodeI_Type, b.NodeI_Id)}) の座標が数値ではありません {Format(pi.Value)}。");
                        if (badJ) errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 終点 ({DescribeEndpoint(inputModel, b.NodeJ_Type, b.NodeJ_Id)}) の座標が数値ではありません {Format(pj.Value)}。");
                        continue;
                    }
                    double dx = pi.Value.X - pj.Value.X;
                    double dy = pi.Value.Y - pj.Value.Y;
                    double dz = pi.Value.Z - pj.Value.Z;
                    double length = PileDesign.Common.StableNumerics.Norm(dx, dy, dz);
                    if (!double.IsFinite(length))
                        errors.Add($"基礎梁 No.{fb.GetBeamNo(b)}: 長さが数値になりません (始点 {DescribeEndpoint(inputModel, b.NodeI_Type, b.NodeI_Id)} / 終点 {DescribeEndpoint(inputModel, b.NodeJ_Type, b.NodeJ_Id)})。");
                    else if (PileDesign.Common.GeometryTolerance.IsZeroLength(length))
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

        /// <summary>同じ種類の節点に同じ識別子が 2 つ以上あれば、番号を並べて入力の誤りにする。</summary>
        private static void AddDuplicateIdErrors(List<string> errors, string kind, IEnumerable<(Guid Id, int No)> items)
        {
            foreach (var group in items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
            {
                string nos = string.Join("・", group.Select(i => $"No.{i.No}"));
                errors.Add($"{kind} {nos} が同じ識別子を持っています。基礎梁の端点がどれを指すか決まらないため解析できません"
                         + " (ファイルを手で直したか、複製の不具合の可能性があります。どれかを消して入力し直してください)。");
            }
        }

        private static bool IsFinite((double X, double Y, double Z) p)
            => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

        private static string Format((double X, double Y, double Z) p) => $"(X={p.X}, Y={p.Y}, Z={p.Z})";

        /// <summary>端点の種類と番号 (メッセージ用)。</summary>
        private static string DescribeEndpoint(InputModel input, NodeReferenceType type, Guid id) => type switch
        {
            NodeReferenceType.GeneralNode =>
                $"一般節点 No.{input.InputNodes?.FirstOrDefault(n => n?.UniqueId == id)?.No}",
            NodeReferenceType.FoundationNode =>
                $"基礎梁節点 No.{input.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n?.Id == id)?.No}",
            NodeReferenceType.PileLayout =>
                $"杭配置 No.{input.PileLayoutItems?.FirstOrDefault(p => p?.UniqueId == id)?.No}",
            _ => "不明な節点",
        };

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
                // 位置は mm に丸めた整数で比べる。数値でない座標・long に収まらない座標は丸められない
                // (範囲外の double を long にすると値が決まらず、離れた杭どうしが「同じ位置」になり得た)
                if (!TryPositionKey(p.X, p.Y, out var key))
                {
                    warnings.Add($"杭 No.{p.No}: 座標が数値でないか大きすぎるため、ほかの杭との重なりを判定できません (X={p.X}, Y={p.Y})。");
                    continue;
                }
                if (seen.TryGetValue(key, out int firstNo))
                    warnings.Add($"杭 No.{p.No}: 杭 No.{firstNo} と同じ位置にあります (X={p.X:N3}, Y={p.Y:N3})。");
                else
                    seen[key] = p.No;
            }
            return warnings;
        }

        /// <summary>
        /// 杭の平面位置を mm に丸めた整数の組にする。数値でない・long に収まらない座標は false。
        /// long の範囲 (約 ±9.2e18) の境目は double で正確に表せないので、少し内側 (±9.0e18) で切る。
        /// </summary>
        internal static bool TryPositionKey(double x, double y, out (long X, long Y) key)
        {
            const double limit = 9.0e18;
            double mx = Math.Round(x * 1000.0), my = Math.Round(y * 1000.0);
            if (!double.IsFinite(mx) || !double.IsFinite(my) || Math.Abs(mx) > limit || Math.Abs(my) > limit)
            {
                key = default;
                return false;
            }
            key = ((long)mx, (long)my);
            return true;
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
