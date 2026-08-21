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
    /// HorizontalSoilSprings / PileTopRotationalSpring は解析ランタイム状態として
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
        /// </summary>
        public static void Apply(PileFemLinkTable? table, InputModel? input, AnaModel? model)
        {
            if (table?.Piles == null || input?.PileLayoutItems == null || model == null) return;

            var byNo = new Dictionary<int, PileFemLink>();
            foreach (var link in table.Piles)
                byNo[link.PileNo] = link;

            foreach (var pile in input.PileLayoutItems)
            {
                if (pile == null || !byNo.TryGetValue(pile.No, out var link)) continue;

                pile.PileNodes = FromIndices(link.PileNodeIndices, model.Nodes);
                pile.SoilNodes = FromIndices(link.SoilNodeIndices, model.Nodes);
                pile.Beams = FromIndices(link.BeamIndices, model.Beams);
                pile.HorizontalSoilSprings =
                    FromIndices(link.HorizontalSoilSpringIndices, model.HorizontalSoilSprings);

                pile.PileTopRotationalSpring =
                    link.RotationalSpringIndex >= 0
                    && model.RotationalSprings != null
                    && link.RotationalSpringIndex < model.RotationalSprings.Count
                        ? model.RotationalSprings[link.RotationalSpringIndex]
                        : null;
            }
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

        private static ObservableCollection<T> FromIndices<T>(List<int>? indices, IList<T>? source) where T : class
        {
            var result = new ObservableCollection<T>();
            if (indices == null || source == null) return result;
            foreach (int i in indices)
                if (i >= 0 && i < source.Count) result.Add(source[i]);
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

        /// <summary>杭頭回転ばねのインデックス。無ければ -1。</summary>
        public int RotationalSpringIndex { get; set; } = -1;
    }
}
