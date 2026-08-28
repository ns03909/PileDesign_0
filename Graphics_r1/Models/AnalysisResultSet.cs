using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using Serilog;

namespace PileDesign.Models
{
    /// <summary>
    /// 「解析結果」と「その解析を行った時点の入力」を 1 組にして保持する。
    ///
    /// これが無いと、入力を少しでも編集した瞬間に結果の意味が壊れる。
    /// 結果 (<see cref="AnaModel"/>) は Beam → PileSection のように入力オブジェクトを参照で持ち、
    /// 結果の描画コードも入力を大量に読むため、入力を書き換えると
    /// 「変位は解析時・断面は編集後」という混在表示になってしまう。
    /// そのため従来は入力を変更するたびに結果を破棄していたが、
    /// 実務では結果を横目に見ながら入力を変えていくため、この運用は成り立たない。
    ///
    /// 解析完了時に入力と結果をまとめて複製して切り離しておけば、
    /// 以降の入力編集は結果に一切影響しない。表示系は
    /// <see cref="InputSnapshot"/> を見るので、混在も起きない。
    /// </summary>
    public sealed class AnalysisResultSet
    {
        /// <summary>解析を実行した時点の入力（結果と整合する唯一の入力）。</summary>
        public InputModel InputSnapshot { get; init; } = null!;

        /// <summary>解析結果（節点・要素・ばね）。<see cref="InputSnapshot"/> と参照が張られている。</summary>
        public AnaModel? AnaModel { get; init; }

        /// <summary>基礎梁考慮鉛直解析の結果。</summary>
        public List<VerticalBeamCaseResult>? VerticalBeamCaseResults { get; init; }

        /// <summary>解析を実行した時刻（表示用）。</summary>
        public DateTime CapturedAt { get; init; } = DateTime.Now;

        /// <summary>どの解析が実行済みかのフラグ（復元用）。</summary>
        public bool HasHorizontal { get; init; }
        public bool HasVertical { get; init; }
        public bool HasGroupPileSettlement { get; init; }
        public bool HasVerticalBeam { get; init; }
        public bool IsElementSplit { get; init; }

        /// <summary>
        /// 現在の入力と結果を 1 組に複製して切り離す。
        ///
        /// 複製は保存 (<see cref="ProjectData"/>) と同じ JSON 往復で行う。
        /// <c>ReferenceHandler.Preserve</c> によりオブジェクト参照がそのまま張り直されるため、
        /// 「入力と結果が相互に参照し合ったまま丸ごと切り離された複製」が得られる。
        /// ファイル保存／復元で結果ごと復元できることは既に実証済みの経路なので、
        /// 参照の張り替えを自前で書くより安全。
        ///
        /// 失敗した場合は null を返す（呼び出し側は従来どおり結果を保持しないだけ）。
        /// </summary>
        public static AnalysisResultSet? Capture(
            InputModel liveInput,
            AnaModel? anaModel,
            List<VerticalBeamCaseResult>? verticalBeamCaseResults,
            bool hasHorizontal,
            bool hasVertical,
            bool hasGroupPileSettlement,
            bool hasVerticalBeam,
            bool isElementSplit)
        {
            if (liveInput == null) return null;

            try
            {
                var payload = new ProjectData
                {
                    FormatVersion = 2,
                    InputModel = liveInput,
                    AnaModel = anaModel,
                    VerticalBeamCaseResults = verticalBeamCaseResults,
                };

                var options = new JsonSerializerOptions
                {
                    // WriteIndented は付けない（複製にはコストだけで意味が無い）
                    ReferenceHandler = ReferenceHandler.Preserve,
                };

                string json = JsonSerializer.Serialize(payload, options);
                var copy = JsonSerializer.Deserialize<ProjectData>(json, options);
                if (copy?.InputModel == null) return null;

                var set = new AnalysisResultSet
                {
                    InputSnapshot = copy.InputModel,
                    AnaModel = copy.AnaModel,
                    VerticalBeamCaseResults = copy.VerticalBeamCaseResults,
                    CapturedAt = DateTime.Now,
                    HasHorizontal = hasHorizontal,
                    HasVertical = hasVertical,
                    HasGroupPileSettlement = hasGroupPileSettlement,
                    HasVerticalBeam = hasVerticalBeam,
                    IsElementSplit = isElementSplit,
                };

                // JSON に載らない表示用の揮発状態を引き継ぐ（ばねはインデックス整合）
                CopyVolatileDisplayState(anaModel, set.AnaModel);

                // 杭 → FEM 要素の関連も JSON に載らないので張り直す
                RelinkPileFemAssociations(liveInput, anaModel, set.InputSnapshot, set.AnaModel);

                return set;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AnalysisResultSet] 解析結果のスナップショット作成に失敗しました");
                return null;
            }
        }

        /// <summary>
        /// 杭 (<see cref="PileLayoutDataItem"/>) から FEM 要素への関連を複製側へ張り直す。
        ///
        /// <c>Beams</c> / <c>PileNodes</c> / <c>SoilNodes</c> / <c>HorizontalSoilSprings</c> /
        /// <c>PileTopRotationalSpring</c> はいずれも [JsonIgnore]（解析ランタイム状態）なので、
        /// JSON 往復では失われる。結果表示はここを辿って断面力や M-φ を引くため、
        /// 張り直さないと「解析時の入力」基準のグラフが軒並み空になる。
        ///
        /// 元モデルと複製はリストの順序が保たれるので、元の要素 → インデックス →
        /// 複製の同インデックス、で対応付けできる。
        /// </summary>
        private static void RelinkPileFemAssociations(
            InputModel? liveInput, AnaModel? liveModel,
            InputModel? snapshotInput, AnaModel? snapshotModel)
        {
            if (liveInput?.PileLayoutItems == null || snapshotInput?.PileLayoutItems == null) return;
            if (liveModel == null || snapshotModel == null) return;

            var nodeIndex = BuildIndex(liveModel.Nodes);
            var beamIndex = BuildIndex(liveModel.Beams);
            var springIndex = BuildIndex(liveModel.HorizontalSoilSprings);
            var rotIndex = BuildIndex(liveModel.RotationalSprings);

            int n = Math.Min(liveInput.PileLayoutItems.Count, snapshotInput.PileLayoutItems.Count);
            for (int i = 0; i < n; i++)
            {
                var src = liveInput.PileLayoutItems[i];
                var dst = snapshotInput.PileLayoutItems[i];
                if (src == null || dst == null) continue;

                dst.PileNodes = MapCollection(src.PileNodes, nodeIndex, snapshotModel.Nodes);
                dst.SoilNodes = MapCollection(src.SoilNodes, nodeIndex, snapshotModel.Nodes);
                dst.Beams = MapCollection(src.Beams, beamIndex, snapshotModel.Beams);
                dst.HorizontalSoilSprings =
                    MapCollection(src.HorizontalSoilSprings, springIndex, snapshotModel.HorizontalSoilSprings);
                // 杭Zばね (P-S 非線形ばね) も張り直す。ここが抜けていると、杭を一部だけ
                // 表示したときに杭Zばねの反力が消える (可視セットに入らないため)。
                dst.VerticalNodeSprings =
                    [.. MapCollection(src.VerticalNodeSprings, springIndex, snapshotModel.HorizontalSoilSprings)];

                dst.PileTopRotationalSpring = src.PileTopRotationalSpring != null
                    && rotIndex.TryGetValue(src.PileTopRotationalSpring, out int ri)
                    && snapshotModel.RotationalSprings != null
                    && ri < snapshotModel.RotationalSprings.Count
                        ? snapshotModel.RotationalSprings[ri]
                        : null;
            }
        }

        /// <summary>参照 → インデックスの対応表を作る（参照一致で引く）。</summary>
        private static Dictionary<T, int> BuildIndex<T>(IList<T>? source) where T : class
        {
            var map = new Dictionary<T, int>(ReferenceEqualityComparer.Instance as IEqualityComparer<T>
                                             ?? EqualityComparer<T>.Default);
            if (source == null) return map;
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item != null) map[item] = i;
            }
            return map;
        }

        /// <summary>元コレクションの各要素を、複製側の同インデックスの要素へ置き換える。</summary>
        private static System.Collections.ObjectModel.ObservableCollection<T> MapCollection<T>(
            IEnumerable<T>? source, Dictionary<T, int> index, IList<T>? destination) where T : class
        {
            var result = new System.Collections.ObjectModel.ObservableCollection<T>();
            if (source == null || destination == null) return result;

            foreach (var item in source)
            {
                if (item != null && index.TryGetValue(item, out int i) && i < destination.Count)
                    result.Add(destination[i]);
            }
            return result;
        }

        /// <summary>
        /// [JsonIgnore] のため往復で失われるが、結果表示が読む状態を複製側へ引き継ぐ。
        /// 対象は「ケース別 M-θ 構成」「セットアップ経路の説明」「地盤ばねの降伏フラグ」。
        /// リストは同じ順序で往復するのでインデックスで対応付けできる。
        /// </summary>
        private static void CopyVolatileDisplayState(AnaModel? from, AnaModel? to)
        {
            if (from == null || to == null) return;

            if (from.RotationalSprings != null && to.RotationalSprings != null)
            {
                int n = Math.Min(from.RotationalSprings.Count, to.RotationalSprings.Count);
                for (int i = 0; i < n; i++)
                {
                    var src = from.RotationalSprings[i];
                    var dst = to.RotationalSprings[i];
                    if (src == null || dst == null) continue;

                    dst.McrXY = src.McrXY;
                    dst.LastSetupReason = src.LastSetupReason;
                    foreach (var kv in src.CaseMThetaSnapshots)
                        dst.CaseMThetaSnapshots[kv.Key] = kv.Value;
                }
            }

            if (from.HorizontalSoilSprings != null && to.HorizontalSoilSprings != null)
            {
                int n = Math.Min(from.HorizontalSoilSprings.Count, to.HorizontalSoilSprings.Count);
                for (int i = 0; i < n; i++)
                {
                    var src = from.HorizontalSoilSprings[i];
                    var dst = to.HorizontalSoilSprings[i];
                    if (src == null || dst == null) continue;
                    dst.IsYielded = src.IsYielded;
                    dst.PreviousIsYielded = src.PreviousIsYielded;
                }
            }
        }
    }
}
