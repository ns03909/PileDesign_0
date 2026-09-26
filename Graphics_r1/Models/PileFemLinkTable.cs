using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Models
{
    /// <summary>
    /// 杭 → FEM 要素の対応をインデックスで保存するための表。
    ///
    /// <see cref="PileLayoutDataItem"/> の Beams / PileNodes / SoilNodes /
    /// HorizontalSoilSprings / VerticalNodeSprings / PileTopRotationalSpring は解析ランタイム状態として
    /// [JsonIgnore] になっており、ファイルには残らない。これらは
    /// <see cref="AnalysisModelling"/> が FEM モデルを組むときにだけ設定されるため、
    /// 解析結果を含むファイルを開き直しても杭からは要素を辿れず、
    /// M-φ グラフや限界線など「杭ごとに結果を引く」表示が空になる。
    ///
    /// 要素そのものは <see cref="AnaModel"/> に保存されているので、
    /// 「どの杭がどのインデックスの要素を持つか」だけを別表で持てば復元できる。
    /// 入力モデル側のスキーマを汚さないよう <see cref="ProjectData"/> 直下に置く。
    /// </summary>
    public sealed class PileFemLinkTable
    {
        public List<PileFemLink> Piles { get; set; } = [];

        /// <summary>
        /// 現在張られている関連からインデックス表を作る。
        /// </summary>
        public static PileFemLinkTable? Build(InputModel? input, AnaModel? model)
        {
            if (input?.PileLayoutItems == null || model == null) return null;

            var nodeIndex = BuildIndex(model.Nodes);
            var beamIndex = BuildIndex(model.Beams);
            var springIndex = BuildIndex(model.HorizontalSoilSprings);
            var rotIndex = BuildIndex(model.RotationalSprings);

            var table = new PileFemLinkTable();
            foreach (var pile in input.PileLayoutItems)
            {
                if (pile == null) continue;
                table.Piles.Add(new PileFemLink
                {
                    PileNo = pile.No,
                    BeamIndices = ToIndices(pile.Beams, beamIndex),
                    PileNodeIndices = ToIndices(pile.PileNodes, nodeIndex),
                    SoilNodeIndices = ToIndices(pile.SoilNodes, nodeIndex),
                    HorizontalSoilSpringIndices = ToIndices(pile.HorizontalSoilSprings, springIndex),
                    // 杭Zばね (P-S 非線形ばね)。AnaModel では HorizontalSoilSprings に
                    // 登録されているので、索引は水平ばねと同じものを使う。
                    VerticalNodeSpringIndices = ToIndices(pile.VerticalNodeSprings, springIndex),
                    RotationalSpringIndex = pile.PileTopRotationalSpring != null
                        && rotIndex.TryGetValue(pile.PileTopRotationalSpring, out int ri) ? ri : -1,
                });
            }

            // 1 本も関連が無いなら保存する意味がない
            return table.Piles.Any(p => p.BeamIndices.Count > 0 || p.PileNodeIndices.Count > 0)
                ? table : null;
        }

        /// <summary>
        /// インデックス表から関連を張り直す。表が無い（旧ファイル）場合は何もしない。
        /// 張り直せなかったもの (利用者向けの文) を返す。無ければ空。
        ///
        /// 以前は範囲の外の番号を黙って飛ばし、同じ杭番号が 2 回あれば後のもので上書きしていた。
        /// 杭の結果が一部だけ欠けた (あるいは別の杭の要素を指す) まま表示され、気づく手掛かりが無かった。
        /// 範囲の外の番号は従来どおり張らずに知らせる。杭番号が重なる対応はどちらが正しいか決められないので、
        /// その杭には張らずに知らせる。対応の無い杭も知らせる。
        /// </summary>
        public static IReadOnlyList<string> Apply(PileFemLinkTable? table, InputModel? input, AnaModel? model)
        {
            var problems = new List<string>();
            if (table?.Piles == null || input?.PileLayoutItems == null || model == null) return problems;

            var byNo = new Dictionary<int, PileFemLink>();
            var duplicated = new SortedSet<int>();
            foreach (var link in table.Piles)
            {
                if (link == null) continue;
                if (!byNo.TryAdd(link.PileNo, link)) duplicated.Add(link.PileNo);
            }
            foreach (int no in duplicated)
            {
                byNo.Remove(no);
                problems.Add($"杭 No.{no}: 解析結果との対応が重複しているため、結び付けませんでした");
            }

            foreach (var pile in input.PileLayoutItems)
            {
                if (pile == null || duplicated.Contains(pile.No)) continue;
                if (!byNo.TryGetValue(pile.No, out var link))
                {
                    problems.Add($"杭 No.{pile.No}: 解析結果との対応がありません");
                    continue;
                }

                var outOfRange = new List<string>();
                pile.PileNodes = FromIndices(link.PileNodeIndices, model.Nodes, "杭節点", outOfRange);
                pile.SoilNodes = FromIndices(link.SoilNodeIndices, model.Nodes, "地盤節点", outOfRange);
                pile.Beams = FromIndices(link.BeamIndices, model.Beams, "杭要素", outOfRange);
                pile.HorizontalSoilSprings =
                    FromIndices(link.HorizontalSoilSpringIndices, model.HorizontalSoilSprings, "水平地盤ばね", outOfRange);
                pile.VerticalNodeSprings =
                    [.. FromIndices(link.VerticalNodeSpringIndices, model.HorizontalSoilSprings, "鉛直地盤ばね", outOfRange)];

                int rotationalCount = model.RotationalSprings?.Count ?? 0;
                if (link.RotationalSpringIndex >= 0 && link.RotationalSpringIndex < rotationalCount)
                {
                    pile.PileTopRotationalSpring = model.RotationalSprings![link.RotationalSpringIndex];
                }
                else
                {
                    pile.PileTopRotationalSpring = null;
                    // -1 は「杭頭回転ばねが無い」の意味で正常
                    if (link.RotationalSpringIndex != -1)
                        outOfRange.Add($"杭頭回転ばね {link.RotationalSpringIndex} (全 {rotationalCount} 個)");
                }

                if (outOfRange.Count > 0)
                    problems.Add($"杭 No.{pile.No}: 解析結果に無い要素を指しています ({string.Join(", ", outOfRange)})");
            }

            // 入力に無い杭の対応 (解析のあとに杭を減らしたなど)
            var inputNos = new HashSet<int>(input.PileLayoutItems.Where(p => p != null).Select(p => p.No));
            foreach (int no in byNo.Keys.Where(no => !inputNos.Contains(no)).OrderBy(no => no))
                problems.Add($"杭 No.{no}: 解析結果の対応はありますが、解析時の入力にこの杭がありません");

            return problems;
        }

        /// <summary>読込時に対応表を張り直せなかったことを知らせる文 (多いときは先頭 10 件と件数)。</summary>
        public static string DescribeProblems(IReadOnlyList<string> problems)
        {
            const int shown = 10;
            return "読み込んだ水平解析の結果の一部を、杭に結び付けられませんでした。\n"
                 + "該当する杭の結果 (杭ごとのグラフ・限界線・計算書など) は欠けて表示されます。再解析すると揃います。\n\n"
                 + string.Join("\n", problems.Take(shown))
                 + (problems.Count > shown ? $"\nほか {problems.Count - shown} 件" : "");
        }

        private static Dictionary<T, int> BuildIndex<T>(IList<T>? source) where T : class
        {
            var map = new Dictionary<T, int>(ReferenceEqualityComparer.Instance as IEqualityComparer<T>
                                             ?? EqualityComparer<T>.Default);
            if (source == null) return map;
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null) map[source[i]] = i;
            return map;
        }

        private static List<int> ToIndices<T>(IEnumerable<T>? items, Dictionary<T, int> index) where T : class
        {
            var result = new List<int>();
            if (items == null) return result;
            foreach (var item in items)
                if (item != null && index.TryGetValue(item, out int i)) result.Add(i);
            return result;
        }

        private static ObservableCollection<T> FromIndices<T>(List<int>? indices, IList<T>? source,
            string what, List<string> outOfRange) where T : class
        {
            var result = new ObservableCollection<T>();
            if (indices == null) return result;
            int count = source?.Count ?? 0;
            foreach (int i in indices)
            {
                if (i >= 0 && i < count) result.Add(source![i]);
                else outOfRange.Add($"{what} {i} (全 {count} 個)");
            }
            return result;
        }
    }

    /// <summary>1 本の杭が持つ FEM 要素のインデックス。</summary>
    public sealed class PileFemLink
    {
        /// <summary>杭番号 (<see cref="PileLayoutDataItem.No"/>)。並び順ではなく番号で対応付ける。</summary>
        public int PileNo { get; set; }

        public List<int> BeamIndices { get; set; } = [];
        public List<int> PileNodeIndices { get; set; } = [];
        public List<int> SoilNodeIndices { get; set; } = [];
        public List<int> HorizontalSoilSpringIndices { get; set; } = [];

        /// <summary>杭Zばね (P-S 非線形ばね)。索引は水平ばねと同じ AnaModel.HorizontalSoilSprings。</summary>
        public List<int> VerticalNodeSpringIndices { get; set; } = [];

        /// <summary>杭頭回転ばねのインデックス。無ければ -1。</summary>
        public int RotationalSpringIndex { get; set; } = -1;
    }
}
