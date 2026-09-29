using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PileDesign.Views.AutoIsFrontPilesWindow;
using static PileDesign.Views.EditPileLayoutWindow;
using static PileDesign.Views.MoveCopyWindow;
using Point = System.Windows.Point;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;

namespace PileDesign.ViewModels
{
    // MainWindowViewModel partial: モデル編集操作（杭配置・並べ替え・要素分割・基礎梁自動生成・重複整理・平面調整・前面杭・群杭沈下）
    public partial class MainWindowViewModel
    {
        // 杭配置追加コマンドの実行メソッド
        [RelayCommand]
        private void OnAddPile()
        {

            // スナップショットを保存

            Point3D nextPoint3D = new();
            if (CurrentInputModel.PileLayoutItems.Count != 0)
            {
                // 直前の杭から X 方向に 7.2m オフセット
                nextPoint3D = CurrentInputModel.PileLayoutItems.Last().Point3D + new Vector3D() { X = 7.2 };
            }

            if (!IsFinitePosition(nextPoint3D)) { RejectSplit("追加する杭の座標が数値の範囲外です。"); return; }
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;
            var before = CaptureInputEdit();
            // UIスレッドから呼ばれるため直接実行
            CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem() { X = nextPoint3D.X, Y = nextPoint3D.Y, Z = nextPoint3D.Z });
            CurrentInputModel.PileLayoutItems[^1].SetMainWindowViewModel(this);
            // 要素未分割の場合は自動で SoiPile を再生成
            if (!IsElementSplit)
                RequestGenerateSoilPiles();

            // 変更後（以下の箇所で適用）
            UpdatePileLayoutNo();
            CompleteInputEdit(before);
        }

        // 群杭係数 ξ・杭間隔比 R/B の自動計算コマンドは置かない。
        // 以前 OnComputePileGroupFactor / OnComputePileSpacingFactor があったが、中身は
        // 杭の本数を数えて何もせず返る空実装で、しかも呼ばれると先に解析結果を捨てる確認だけ
        // 走った。どこからも参照されていなかったので 2026-09-18 に撤去した。
        // ξ は「群杭係数グラフ確認」(GroupPileFactorCommand) で読み取って全杭へ適用する。

        /// <summary>X優先整列: X昇順 → Y昇順でPileLayoutItemsをソート</summary>
        [RelayCommand]
        private void SortPileLayoutXFirst()
        {
            SortPileLayoutCore(piles => piles.OrderBy(p => p.X).ThenBy(p => p.Y));
        }

        /// <summary>Y優先整列: Y昇順 → X昇順でPileLayoutItemsをソート</summary>
        [RelayCommand]
        private void SortPileLayoutYFirst()
        {
            SortPileLayoutCore(piles => piles.OrderBy(p => p.Y).ThenBy(p => p.X));
        }

        /// <summary>杭配置ソート共通処理: Move方式で最小限のイベント発火</summary>
        private void SortPileLayoutCore(Func<IEnumerable<PileLayoutDataItem>, IOrderedEnumerable<PileLayoutDataItem>> orderFunc)
        {
            var col = CurrentInputModel.PileLayoutItems;
            if (col.Count == 0) return;

            // 旧No→新Noマッピングを構築
            var sorted = orderFunc(col).ToList();
            if (col.SequenceEqual(sorted)) return;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;
            var before = CaptureInputEdit();
            var oldToNewNo = new Dictionary<int, int>();
            var oldPileToNewNo = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                oldToNewNo[sorted[i].No] = i + 1;
                oldPileToNewNo[sorted[i].PileNo] = i + 1;
            }

            // Move方式: Clear+Addの大量イベント発火を回避
            for (int i = 0; i < sorted.Count; i++)
            {
                int currentIndex = col.IndexOf(sorted[i]);
                if (currentIndex != i)
                    col.Move(currentIndex, i);
            }

            UpdatePileLayoutNo();

            // 一般節点のLinkedPileNoを追従更新
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.LinkedPileNo.HasValue && oldToNewNo.TryGetValue(node.LinkedPileNo.Value, out int newPileNo))
                        node.LinkedPileNo = newPileNo;
                }
            }

            foreach (var load in CurrentInputModel.PileGroupSettlement?.RectLoads ?? [])
                if (load.LinkedPileNo > 0 && oldPileToNewNo.TryGetValue(load.LinkedPileNo, out int newPileNo))
                    load.LinkedPileNo = newPileNo;
            CompleteInputEdit(before);
        }

        /// <summary>一般節点: X優先整列</summary>
        [RelayCommand]
        private void SortInputNodesXFirst()
        {
            SortInputNodesCore(nodes => nodes.OrderBy(n => n.X).ThenBy(n => n.Y));
        }

        /// <summary>一般節点: Y優先整列</summary>
        [RelayCommand]
        private void SortInputNodesYFirst()
        {
            SortInputNodesCore(nodes => nodes.OrderBy(n => n.Y).ThenBy(n => n.X));
        }

        /// <summary>一般節点ソート共通処理: Move方式で最小限のイベント発火</summary>
        private void SortInputNodesCore(Func<IEnumerable<InputNode>, IOrderedEnumerable<InputNode>> orderFunc)
        {
            var col = CurrentInputModel.InputNodes;
            if (col == null || col.Count == 0) return;

            var sorted = orderFunc(col).ToList();
            if (col.SequenceEqual(sorted)) return;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;
            var before = CaptureInputEdit();

            // Move方式: Clear+Addの大量イベント発火を回避
            for (int i = 0; i < sorted.Count; i++)
            {
                int currentIndex = col.IndexOf(sorted[i]);
                if (currentIndex != i)
                    col.Move(currentIndex, i);
            }

            // No振り直し
            for (int i = 0; i < col.Count; i++)
                col[i].No = i + 1;

            CompleteInputEdit(before);
        }

        /// <summary>旧コマンドとの互換用。梁番号は一覧順から決まるため、番号順の整列は不要。</summary>
        [RelayCommand]
        private void SortBeamsByNo() { }

        /// <summary>
        /// 梁要素: 選択要素（無選択なら全要素）の I/J 節点参照を入れ替える。
        /// 併せて AngleBeta を (180° − β) に反転し、局所 y 軸の世界空間向きを保つ。
        /// </summary>
        [RelayCommand]
        private void SwapBeamIJ()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;

            var targets = beams.Where(b => b.IsSelected).ToList();
            if (targets.Count == 0) targets = beams.ToList();

            var before = CaptureInputEdit();

            foreach (var b in targets)
            {
                (b.NodeI_Type, b.NodeJ_Type) = (b.NodeJ_Type, b.NodeI_Type);
                (b.NodeI_Id, b.NodeJ_Id) = (b.NodeJ_Id, b.NodeI_Id);

                // ローカル x 軸反転で y 軸が 180° 回る分を β で相殺し、物理的に同じ断面向きを維持する
                b.AngleBeta = ((180.0 - b.AngleBeta) % 360.0 + 360.0) % 360.0;
            }

            CompleteInputEdit(before);
        }

        /// <summary>梁要素: I端節点→J端節点昇順で整列（表示順のみ変更、解析結果に影響なし）</summary>
        [RelayCommand]
        private void SortBeamsByNode()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;

            var sorted = beams
                .OrderBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeI_Type, b.NodeI_Id))
                .ThenBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeJ_Type, b.NodeJ_Id))
                .ToList();
            if (beams.SequenceEqual(sorted)) return;
            var before = CaptureInputEdit();
            for (int i = 0; i < sorted.Count; i++)
            {
                int cur = beams.IndexOf(sorted[i]);
                if (cur != i) beams.Move(cur, i);
            }
            // 旧 No プロパティは廃止: 番号 = 位置インデックスとして自動的に追従

            CompleteInputEdit(before);
        }

        // 要素の節点位置での分割
        // 旧実装は FoundationNode (基礎梁節点) のみ参照していたが、ToolTip 「重なる一般節点で分割」の通り
        // PileLayout (杭頭)・InputNode (一般)・FoundationNode の全種類を対象にする。
        // 端点参照は NodeReferenceType + Guid の現代式で生成する。
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            if (!ValidateEditDistanceThreshold()) return;
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;

            // Undoポイントを追加


            var beams = fb.Beams;
            double tolerance = EditDistanceThreshold;

            // 候補ノード一覧 (Type + Guid + 位置) を共通ヘルパで列挙 (PileLayout / GeneralNode / FoundationNode 全種)
            var selected = beams.Where(b => b.IsSelected).ToList();
            if (!ValidateSplitBeams(selected)) return;
            var candidates = EnumerateAllCandidateNodes(includeFoundationNodes: true).ToList();
            if (candidates.Any(c => !IsFinitePosition(c.Pos)))
            {
                RejectSplit("分割候補の節点に有限ではない座標があります。");
                return;
            }

            var newBeams = new List<FoundationBeam>();
            var toRemove = new List<FoundationBeam>();
            const double endEps = SplitPointDistanceTolerance;

            foreach (var beam in beams.Where(b => b.IsSelected).ToList())
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                var pI = posI.Value;
                var pJ = posJ.Value;
                Vector3D line = pJ - pI;
                double lineLengthSq = line.LengthSquared;
                if (lineLengthSq < 1e-18) continue;

                // 線上にある中間ノードを探す (端点除外、線分上の t∈(0, 1)、距離 ≤ tolerance)
                var splits = new List<(NodeReferenceType Type, Guid Id, double T)>();
                foreach (var cand in candidates)
                {
                    // 自分の端点はスキップ
                    if (cand.Type == beam.NodeI_Type && cand.Id == beam.NodeI_Id) continue;
                    if (cand.Type == beam.NodeJ_Type && cand.Id == beam.NodeJ_Id) continue;

                    Vector3D v = cand.Pos - pI;
                    double t = Vector3D.DotProduct(v, line) / lineLengthSq;
                    if (!double.IsFinite(t)) { RejectSplit("分割位置の計算が数値の範囲外です。"); return; }
                    if (t <= 0 || t >= 1 || t * Math.Sqrt(lineLengthSq) <= endEps || (1 - t) * Math.Sqrt(lineLengthSq) <= endEps) continue;

                    Point3D projection = pI + t * line;
                    double dist = (cand.Pos - projection).Length;
                    if (!IsFinitePosition(projection) || !double.IsFinite(dist)) { RejectSplit("分割距離の計算が数値の範囲外です。"); return; }
                    bool Within(double value, double projected, double origin, double delta) =>
                        Math.Abs(value - projected) <= 8 * 2.2204460492503131e-16 *
                        (Math.Abs(value) + Math.Abs(origin) + Math.Abs(t * delta));
                    if (dist > tolerance && !(Within(cand.Pos.X, projection.X, posI.Value.X, line.X) &&
                        Within(cand.Pos.Y, projection.Y, posI.Value.Y, line.Y) &&
                        Within(cand.Pos.Z, projection.Z, posI.Value.Z, line.Z))) continue;

                    splits.Add((cand.Type, cand.Id, t));
                }

                if (splits.Count == 0) continue;

                // t の昇順でソート
                splits.Sort((a, b) =>
                {
                    int order = a.T.CompareTo(b.T);
                    if (order != 0) return order;
                    order = ((int)b.Type).CompareTo((int)a.Type);
                    return order != 0 ? order : a.Id.CompareTo(b.Id);
                });

                // 梁上の実距離で重複判定する。長い梁でも離れた分割点を残す。
                var dedupedSplits = new List<(NodeReferenceType Type, Guid Id, double T)>();
                foreach (var s in splits)
                {
                    if (dedupedSplits.Count > 0 && Math.Abs(dedupedSplits[^1].T - s.T) * Math.Sqrt(lineLengthSq) <= endEps)
                        continue;
                    dedupedSplits.Add(s);
                }

                // 分割セグメントを生成
                var endpoints = new List<(NodeReferenceType Type, Guid Id)>
                {
                    (beam.NodeI_Type, beam.NodeI_Id)
                };
                foreach (var s in dedupedSplits)
                    endpoints.Add((s.Type, s.Id));
                endpoints.Add((beam.NodeJ_Type, beam.NodeJ_Id));

                for (int i = 0; i < endpoints.Count - 1; i++)
                {
                    newBeams.Add(beam.CreateSegment(endpoints[i].Type, endpoints[i].Id, endpoints[i + 1].Type, endpoints[i + 1].Id));
                }
                toRemove.Add(beam);
            }

            if (toRemove.Count == 0)
            {
                ShowToast("選択要素上に分割できる中間節点が見つかりませんでした。", 2);
                return;
            }
            if (!CommitSplitEdit(out var before)) return;
            foreach (var beam in toRemove)
                beams.Remove(beam);
            foreach (var beam in newBeams)
                beams.Add(beam);

            RenumberFoundationBeams();
            CompleteInputEdit(before);

            if (toRemove.Count == 0)
            {
                ShowToast("選択要素上に分割できる中間節点が見つかりませんでした。", 2);
            }
            else
            {
                PileDesign.Services.MessageService.Show(
                    $"{toRemove.Count} 個の要素を {newBeams.Count} 個に分割しました。",
                    "節点分割完了",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }

        // 選択された梁要素を等分割するコマンド
        [RelayCommand]
        private void EqualDivideElements()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;



            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0)
            {
                MessageService.Show("分割する梁要素を選択してください。");
                return;
            }



            int n = EqualDivisionCount;
            if (!ValidateSplitBeams(selectedBeams)) return;
            if (MoveCopyValidation.DescribeSplitCountProblem(selectedBeams.Count, n) is string countProblem)
            {
                RejectSplit(countProblem);
                return;
            }
            var plannedNodes = new List<InputNode>();
            var toRemove = new List<FoundationBeam>();
            var toAdd = new List<FoundationBeam>();

            foreach (var beam in selectedBeams)
            {
                // 始終点の座標を取得（NodeI_Type/NodeI_Id 方式）
                var coordsI = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var coordsJ = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (coordsI == null || coordsJ == null) continue;

                // 分割点に一般節点を生成
                var divisionNodes = new List<InputNode>();
                for (int i = 1; i < n; i++)
                {
                    double t = (double)i / n;
                    var newNode = new InputNode
                    {
                        No = CurrentInputModel.InputNodes.Count + plannedNodes.Count + divisionNodes.Count + 1,
                        Type = NodeType.General,
                        X = coordsI.Value.X + (coordsJ.Value.X - coordsI.Value.X) * t,
                        Y = coordsI.Value.Y + (coordsJ.Value.Y - coordsI.Value.Y) * t,
                        Z = coordsI.Value.Z + (coordsJ.Value.Z - coordsI.Value.Z) * t
                    };
                    if (!IsFinitePosition(new Point3D(newNode.X, newNode.Y, newNode.Z)))
                    { RejectSplit("生成する節点の座標が数値の範囲外です。"); return; }
                    divisionNodes.Add(newNode);
                }

                plannedNodes.AddRange(divisionNodes);

                // 分割ビームを生成（I → div1 → div2 → ... → J）
                // 最初のセグメント: 元のNodeI → 最初の分割節点
                toAdd.Add(beam.CreateSegment(beam.NodeI_Type, beam.NodeI_Id, NodeReferenceType.GeneralNode, divisionNodes[0].UniqueId));

                // 中間セグメント
                for (int i = 0; i < divisionNodes.Count - 1; i++)
                {
                    toAdd.Add(beam.CreateSegment(NodeReferenceType.GeneralNode, divisionNodes[i].UniqueId, NodeReferenceType.GeneralNode, divisionNodes[i + 1].UniqueId));
                }

                // 最後のセグメント: 最後の分割節点 → 元のNodeJ
                toAdd.Add(beam.CreateSegment(NodeReferenceType.GeneralNode, divisionNodes.Last().UniqueId, beam.NodeJ_Type, beam.NodeJ_Id));

                toRemove.Add(beam);
            }

            if (toRemove.Count == 0 || !CommitSplitEdit(out var before)) return;
            foreach (var node in plannedNodes) CurrentInputModel.InputNodes.Add(node);
            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            CompleteInputEdit(before);

            MessageService.Show(
                $"{toRemove.Count} 個の要素を {n} 等分しました（{toAdd.Count} 個の要素、{toRemove.Count * (n - 1)} 個の節点を生成）。",
                "等分割完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 梁要素を節点で分割するメソッド (両端が FoundationNode のときのみ動作)
        private List<FoundationBeam> SplitBeamByNodes(FoundationBeam beam, ObservableCollection<FoundationNode> allNodes)
        {
            var result = new List<FoundationBeam>();

            // 両端が FoundationNode でない場合は分割しない (PileLayout / GeneralNode 経由は対象外)
            if (beam.NodeI_Type != NodeReferenceType.FoundationNode ||
                beam.NodeJ_Type != NodeReferenceType.FoundationNode)
                return [beam];

            // 始点・終点の節点を取得
            var nodeI = allNodes.FirstOrDefault(n => n.Id == beam.NodeI_Id);
            var nodeJ = allNodes.FirstOrDefault(n => n.Id == beam.NodeJ_Id);

            if (nodeI == null || nodeJ == null) return [beam]; // 節点が見つからない場合は分割しない

            Point3D pointI = new(nodeI.X, nodeI.Y, nodeI.Z);
            Point3D pointJ = new(nodeJ.X, nodeJ.Y, nodeJ.Z);

            // 線上にある中間節点を探す
            var intermediateNodes = new List<(FoundationNode node, double distance)>();

            foreach (var node in allNodes)
            {
                if (node.Id == beam.NodeI_Id || node.Id == beam.NodeJ_Id) continue; // 始点・終点は除外

                Point3D point = new(node.X, node.Y, node.Z);
                double dist = PointToLineDistance(point, pointI, pointJ);

                if (dist <= EditDistanceThreshold)
                {
                    double alongDist = DistanceAlongLine(point, pointI, pointJ);
                    if (alongDist > 0 && alongDist < (pointJ - pointI).Length)
                    {
                        intermediateNodes.Add((node, alongDist));
                    }
                }
            }

            // 中間節点がない場合は分割しない
            if (intermediateNodes.Count == 0) return [beam];

            // 距離順にソート
            var sortedNodes = intermediateNodes.OrderBy(n => n.distance).Select(n => n.node).ToList();

            // 始点から各中間節点、最後の中間節点から終点まで梁を作成
            var allSplitNodes = new List<FoundationNode> { nodeI };
            allSplitNodes.AddRange(sortedNodes);
            allSplitNodes.Add(nodeJ);

            for (int i = 0; i < allSplitNodes.Count - 1; i++)
            {
                result.Add(beam.CreateSegment(NodeReferenceType.FoundationNode, allSplitNodes[i].Id, NodeReferenceType.FoundationNode, allSplitNodes[i + 1].Id));
            }

            return result;
        }

        // 点から線分への距離を計算
        private static double PointToLineDistance(Point3D point, Point3D lineStart, Point3D lineEnd)
        {
            Vector3D line = lineEnd - lineStart;
            Vector3D pointVector = point - lineStart;

            double lineLength = line.Length;
            if (lineLength == 0) return (point - lineStart).Length;

            double t = Vector3D.DotProduct(pointVector, line) / (lineLength * lineLength);
            t = Math.Max(0, Math.Min(1, t)); // clamp to [0, 1]

            Point3D projection = lineStart + t * line;
            return (point - projection).Length;
        }

        // 線分に沿った距離を計算
        private static double DistanceAlongLine(Point3D point, Point3D lineStart, Point3D lineEnd)
        {
            Vector3D line = lineEnd - lineStart;
            Vector3D pointVector = point - lineStart;

            double lineLength = line.Length;
            if (lineLength == 0) return 0;

            double t = Vector3D.DotProduct(pointVector, line) / (lineLength * lineLength);
            return t * lineLength;
        }

        private static int GetIndexOfNthSmallestValue(List<double> distances, int n)
        {
            var indexedDistances = distances
                .Select((value, index) => new { Value = value, Index = index })
                .OrderBy(pair => pair.Value)
                .ToList();

            return indexedDistances[n].Index;
        }

        /// <summary>
        /// 2つの3D線分の最近接点を求め、交差判定を行う。
        /// 端点同士の交差（t≈0,1 or s≈0,1）は除外する。
        /// </summary>
        /// <returns>交差点と各線分上のパラメータ t, s。交差しない場合は null。</returns>
        private static (Point3D point, double t, double s)? FindSegmentIntersection(
            Point3D p1, Point3D p2, Point3D p3, Point3D p4, double tolerance, out string? problem)
        {
            problem = null;
            var d1 = p2 - p1; // 線分Aの方向ベクトル
            var d2 = p4 - p3; // 線分Bの方向ベクトル
            var r = p1 - p3;

            double a = Vector3D.DotProduct(d1, d1); // |d1|^2
            double e = Vector3D.DotProduct(d2, d2); // |d2|^2
            double f = Vector3D.DotProduct(d2, r);

            // 両方の線分が点に退化している場合
            if (a < 1e-12 && e < 1e-12) return null;

            double b = Vector3D.DotProduct(d1, d2);
            double c = Vector3D.DotProduct(d1, r);
            double denom = a * e - b * b;
            if (!new[] { a, e, f, b, c, denom }.All(double.IsFinite))
            { problem = "交差判定の計算が数値の範囲外です。"; return null; }

            // 平行（または非常に近い）線分
            if (Math.Abs(denom) < 1e-12) return null;

            double t = (b * f - c * e) / denom;
            double s = (a * f - b * c) / denom;
            if (!double.IsFinite(t) || !double.IsFinite(s))
            { problem = "交差位置の計算が数値の範囲外です。"; return null; }

            // 端点付近は除外（端点での接続は交差ではない）
            if (t <= 0 || t >= 1 || t * Math.Sqrt(a) <= SplitPointDistanceTolerance || (1 - t) * Math.Sqrt(a) <= SplitPointDistanceTolerance) return null;
            if (s <= 0 || s >= 1 || s * Math.Sqrt(e) <= SplitPointDistanceTolerance || (1 - s) * Math.Sqrt(e) <= SplitPointDistanceTolerance) return null;

            // 最近接点
            var closestA = p1 + t * d1;
            var closestB = p3 + s * d2;
            double dist = (closestA - closestB).Length;

            if (!double.IsFinite(dist)) { problem = "交差距離の計算が数値の範囲外です。"; return null; }
            if (dist > tolerance) return null;

            // 交差点は両最近接点の中点
            var intersection = new Point3D(
                (closestA.X + closestB.X) * 0.5,
                (closestA.Y + closestB.Y) * 0.5,
                (closestA.Z + closestB.Z) * 0.5);

            if (!IsFinitePosition(intersection)) { problem = "交差点の座標が数値の範囲外です。"; return null; }
            return (intersection, t, s);
        }

        /// <summary>
        /// 1つの梁要素を複数の交差点(InputNode)で分割し、分割後の要素リストを返す。
        /// </summary>
        private List<FoundationBeam> SplitBeamAtPoints(
            FoundationBeam beam,
            List<(NodeReferenceType Type, Guid Id, double t)> splitPoints, Dictionary<(NodeReferenceType Type, Guid Id), Point3D>? plannedPositions = null, System.Threading.CancellationToken token = default, int maxSegments = MoveCopyValidation.MaxGeneratedItems)
        {
            var endpoints = new List<(NodeReferenceType Type, Guid Id)>
            { (beam.NodeI_Type, beam.NodeI_Id) };
            Point3D? Position(NodeReferenceType type, Guid id) =>
                plannedPositions != null && plannedPositions.TryGetValue((type, id), out var known) ? known : GetNodeAttachPosition(type, id);
            var lastPosition = Position(beam.NodeI_Type, beam.NodeI_Id);
            var endPosition = Position(beam.NodeJ_Type, beam.NodeJ_Id);
            if (!lastPosition.HasValue || !endPosition.HasValue) return [beam];
            int sortChecks = 0;
            var orderedPoints = MaterializeForSplit(() => splitPoints.OrderBy(p => p.t,
                Comparer<double>.Create((a,b) =>
                {
                    if ((++sortChecks & 1023) == 0) token.ThrowIfCancellationRequested();
                    return a.CompareTo(b);
                })).ToArray(), token);
            foreach (var point in orderedPoints)
            {
                token.ThrowIfCancellationRequested();
                var position = Position(point.Type, point.Id);
                if (!double.IsFinite(point.t) || point.t <= 0 || point.t >= 1 || !position.HasValue) continue;
                if ((position.Value - lastPosition.Value).Length <= SplitPointDistanceTolerance ||
                    (position.Value - endPosition.Value).Length <= SplitPointDistanceTolerance) continue;
                if (endpoints.Count >= maxSegments)
                    throw new ArgumentException("分割で生成する梁・節点は合計10万件以下にしてください。");
                endpoints.Add((point.Type, point.Id));
                lastPosition = position;
            }
            if (endpoints.Count == 1) return [beam];
            endpoints.Add((beam.NodeJ_Type, beam.NodeJ_Id));
            var result = new List<FoundationBeam>();
            for (int i = 0; i < endpoints.Count - 1; i++)
            {
                token.ThrowIfCancellationRequested();
                result.Add(beam.CreateSegment(endpoints[i].Type, endpoints[i].Id,
                    endpoints[i + 1].Type, endpoints[i + 1].Id));
            }
            return result;
        }

        [RelayCommand]
        private void SplitElementsAtIntersections()
        {
            if (!ValidateEditDistanceThreshold()) return;
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;



            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count < 2)
            {
                MessageService.Show("交差判定するには梁要素を2本以上選択してください。",
                    "交差点分割", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!ValidateSplitBeams(selectedBeams)) return;
            var candidateNodes = EnumerateAllCandidateNodes(includeFoundationNodes: true).ToList();
            if (candidateNodes.Any(n => !IsFinitePosition(n.Pos)))
            { RejectSplit("分割候補の節点に有限ではない座標があります。"); return; }
            var plannedNodes = new List<InputNode>();
            var plannedPositions = candidateNodes.GroupBy(n => (n.Type, n.Id)).ToDictionary(g => g.Key, g => g.First().Pos);

            double tolerance = EditDistanceThreshold;

            // 各要素の座標を事前取得
            var beamCoords = new Dictionary<FoundationBeam, (Point3D pi, Point3D pj)>();
            foreach (var beam in selectedBeams)
            {
                var ci = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var cj = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (ci == null || cj == null) continue;
                beamCoords[beam] = (new Point3D(ci.Value.X, ci.Value.Y, ci.Value.Z),
                                    new Point3D(cj.Value.X, cj.Value.Y, cj.Value.Z));
            }

            // 各要素ごとの分割点リスト
            var beamSplitPoints = new Dictionary<FoundationBeam, List<(NodeReferenceType Type, Guid Id, double t)>>();

            // 全ペアの交差判定
            var beamList = beamCoords.Keys.ToList();
            var intersectionNodes = new HashSet<(NodeReferenceType Type, Guid Id)>();

            var originals = beamList.ToArray();
            beamList = originals.Select(b => b.CreateSegment(b.NodeI_Type, b.NodeI_Id, b.NodeJ_Type, b.NodeJ_Id)).ToList();
            var originalBySnapshot = beamList.Select((b,i) => (b,i)).ToDictionary(x => x.b, x => originals[x.i]);
            foreach (var beam in originals)
            {
                plannedPositions[(beam.NodeI_Type, beam.NodeI_Id)] = beamCoords[beam].pi;
                plannedPositions[(beam.NodeJ_Type, beam.NodeJ_Id)] = beamCoords[beam].pj;
            }
            int inputNodeCount = CurrentInputModel.InputNodes.Count;
            var segments = originals.Select(b => (beamCoords[b].pi, beamCoords[b].pj)).ToArray();
            var toRemove = new List<FoundationBeam>();
            var toAdd = new List<FoundationBeam>();
            List<InputNode> nodesToAdd = [];
            int intersectionCount = 0;
            try
            {
                RunIntersectionWork(beamList.Count, (token, progress) =>
                {
                    var intersections = SearchBeamIntersections(segments, tolerance, token, new SplitSearchProgress(progress));
                    var nodeIndex = new NodePositionIndex(candidateNodes, token);
                    int processed = 0;
                    foreach (var intersection in intersections)
                    {
                        token.ThrowIfCancellationRequested();
                        if ((processed++ & 255) == 0)
                            progress?.Report(new AnalysisProgress { Percentage = 60, CurrentStep = "交差点の節点を照合しています" });
                        var beamA = beamList[intersection.A];
                        var beamB = beamList[intersection.B];
                        var (point, tA, tB) = (intersection.Point, intersection.TA, intersection.TB);
                        var match = nodeIndex.Find(point, Math.Max(tolerance, 1e-9), token);
                        NodeReferenceType type;
                        Guid id;
                        if (match.HasValue)
                        {
                            type = match.Value.Type;
                            id = match.Value.Id;
                        }
                        else
                        {
                            if (plannedNodes.Count >= MoveCopyValidation.MaxGeneratedItems)
                                throw new ArgumentException("分割で生成する梁・節点は合計10万件以下にしてください。");
                            var node = new InputNode
                            {
                                No = inputNodeCount + plannedNodes.Count + 1, Type = NodeType.General,
                                X = point.X, Y = point.Y, Z = point.Z
                            };
                            plannedNodes.Add(node);
                            plannedPositions[(NodeReferenceType.GeneralNode, node.UniqueId)] = point;
                            nodeIndex.Add((NodeReferenceType.GeneralNode, node.UniqueId, point));
                            type = NodeReferenceType.GeneralNode;
                            id = node.UniqueId;
                        }
                        foreach (var (beam, t) in new[] { (beamA, tA), (beamB, tB) })
                        {
                            if (!beamSplitPoints.TryGetValue(beam, out var points))
                                beamSplitPoints[beam] = points = [];
                            if (!points.Any(p => p.Type == type && p.Id == id))
                                points.Add((type, id, t));
                        }
                        intersectionNodes.Add((type, id));
                    }

                    intersectionCount = intersectionNodes.Count;
                    processed = 0;
                    var plannedNodeIds = plannedNodes.Select(n => n.UniqueId).ToHashSet();
                    var usedPlannedIds = new HashSet<Guid>();
                    foreach (var (beam, splitPoints) in beamSplitPoints)
                    {
                        token.ThrowIfCancellationRequested();
                        if ((processed++ & 255) == 0) progress?.Report(new AnalysisProgress { Percentage = 60 + 40.0 * toRemove.Count / Math.Max(1, beamSplitPoints.Count), CurrentStep = "梁の分割案を作成しています" });
                        var splitBeams = SplitBeamAtPoints(beam, splitPoints, plannedPositions, token, MoveCopyValidation.MaxGeneratedItems - toAdd.Count);
                        if (splitBeams.Count == 1 && ReferenceEquals(splitBeams[0], beam)) continue;
                        toRemove.Add(originalBySnapshot[beam]);
                        foreach (var splitBeam in splitBeams)
                        {
                            token.ThrowIfCancellationRequested();
                            if (plannedNodeIds.Contains(splitBeam.NodeI_Id)) usedPlannedIds.Add(splitBeam.NodeI_Id);
                            if (plannedNodeIds.Contains(splitBeam.NodeJ_Id)) usedPlannedIds.Add(splitBeam.NodeJ_Id);
                            if ((long)toAdd.Count + 1 + usedPlannedIds.Count > MoveCopyValidation.MaxGeneratedItems)
                                throw new ArgumentException("分割で生成する梁・節点は合計10万件以下にしてください。");
                            toAdd.Add(splitBeam);
                        }
                    }

                    var used = toAdd.SelectMany(b => new[] { b.NodeI_Id, b.NodeJ_Id }).ToHashSet();
                    nodesToAdd = plannedNodes.Where(n => used.Contains(n.UniqueId)).ToList();
                    token.ThrowIfCancellationRequested();
                    if ((long)toAdd.Count + nodesToAdd.Count > MoveCopyValidation.MaxGeneratedItems)
                        throw new ArgumentException("分割で生成する梁・節点は合計10万件以下にしてください。");
                    progress?.Report(new AnalysisProgress { Percentage = 100, CurrentStep = "分割案の作成が完了しました" });
                    return true;
                });
            }
            catch (OperationCanceledException) { ShowToast("交差点分割を中断しました。入力は変更されていません。", 2); return; }
            catch (ArgumentException ex) { RejectSplit(ex.Message); return; }
            if (intersectionCount == 0) { ShowToast("選択要素間に交差点が見つかりませんでした。", 2); return; }
            if ((long)toAdd.Count + nodesToAdd.Count > MoveCopyValidation.MaxGeneratedItems)
            { RejectSplit("分割で生成する梁・節点は合計10万件以下にしてください。"); return; }
            if (toRemove.Count > 0 && selectedBeams.Count >= 100 &&
                MessageService.Show($"交差点 {intersectionCount:N0} 件で、梁 {toRemove.Count:N0} 本を {toAdd.Count:N0} 本に分割します。\n新規一般節点は {nodesToAdd.Count:N0} 個です。\n\n反映しますか？",
                    "交差点分割の確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (toRemove.Count == 0 || !CommitSplitEdit(out var before)) return;
            foreach (var node in nodesToAdd) CurrentInputModel.InputNodes.Add(node);
            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            CompleteInputEdit(before);

            ShowToast($"{intersectionCount} 個の交差点で {toRemove.Count} → {toAdd.Count} 要素に分割");
        }

        // 基礎梁節点削除 (接続された梁要素もカスケード削除)
        // 基礎梁節点の削除コマンドは置かない (どこからも辿れなかったので 2026-09-19 に撤去)。
        // 節点は梁を引くと作られ、梁を消しても残る。使われなくなった節点の掃除が要るなら、
        // 梁の削除でカスケードさせるか、梁要素タブに削除の操作を足すこと。

        // 基礎梁削除
        [RelayCommand]
        private void DeleteFoundationBeam(FoundationBeam beam)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            if (beam == null || !CurrentInputModel.FoundationBeamInput.Beams.Contains(beam)) return;
            var before = CaptureInputEdit();
            CurrentInputModel.FoundationBeamInput.Beams.Remove(beam);
            RemoveOrphanFoundationNodes();
            RenumberFoundationBeams();
            CompleteInputEdit(before);
        }

        /// <summary>
        /// どの梁からも参照されなくなった基礎梁節点を取り除く。
        ///
        /// <para>基礎梁節点は梁を引くと作られる (<c>MainWindow.FoundationBeamEditing</c>)。
        /// 梁を消しても節点は残り、<b>掃除する操作はどこにも無かった</b>ので、使われない節点が
        /// たまり続けていた (2026-09-19 に梁の削除でカスケードさせた)。</para>
        ///
        /// <para>杭頭の接合節点 (<c>NodeReferenceType.PileLayout</c>) と一般節点は対象外。
        /// ここで消すのは基礎梁節点 (<c>FoundationBeamInput.Nodes</c>) だけである。</para>
        /// </summary>
        private void RemoveOrphanFoundationNodes()
        {
            var fbInput = CurrentInputModel?.FoundationBeamInput;
            if (fbInput?.Nodes == null || fbInput.Nodes.Count == 0) return;

            var used = new HashSet<Guid>();
            foreach (var b in fbInput.Beams ?? [])
            {
                if (b == null) continue;
                if (b.NodeI_Type == NodeReferenceType.FoundationNode) used.Add(b.NodeI_Id);
                if (b.NodeJ_Type == NodeReferenceType.FoundationNode) used.Add(b.NodeJ_Id);
            }

            var orphans = fbInput.Nodes.Where(n => n != null && !used.Contains(n.Id)).ToList();
            if (orphans.Count == 0) return;

            foreach (var n in orphans) fbInput.Nodes.Remove(n);
            RenumberFoundationNodes();
        }

        // 重複要素削除
        [RelayCommand]
        private void OnDeleteDupulicateElements()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            var fbInput = CurrentInputModel.FoundationBeamInput;
            var beams = fbInput.Beams;
            var (toRemove, differing) = FindDuplicateBeams(beams);

            if (toRemove.Count > 0)
            {
                var before = CaptureInputEdit();
                foreach (var beam in toRemove)
                    beams.Remove(beam);
                RenumberFoundationBeams();
                CompleteInputEdit(before);
            }

            // 番号は消したあとの並びで示す (画面の表と同じ番号)
            string message = $"{toRemove.Count} 個の重複要素を削除しました。";
            if (differing.Count > 0)
            {
                const int shown = 10;
                var pairs = differing.Take(shown)
                    .Select(d => $"梁 No.{beams.IndexOf(d.Kept) + 1} と No.{beams.IndexOf(d.Other) + 1}");
                message += $"\n\n同じ節点を結ぶが、断面・材料などが違う梁が {differing.Count} 組あります。"
                         + "違う梁は削除せずに残しました。確認してください:\n"
                         + string.Join("\n", pairs)
                         + (differing.Count > shown ? $"\nほか {differing.Count - shown} 組" : "");
            }
            PileDesign.Services.MessageService.Show(
                message,
                "重複削除完了",
                System.Windows.MessageBoxButton.OK,
                differing.Count > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// 重複した基礎梁を探す。同じ節点を結ぶ (向きは問わない) 梁のうち、<b>属性もすべて同じ</b>ものは後の方を消す対象にし、
        /// 属性が違うものは消さずに組で返す (確認してもらう)。
        ///
        /// 以前は両端の節点だけで判断し、断面番号・材料番号の違う梁まで後の方を消していた。
        /// </summary>
        internal static (List<FoundationBeam> ToRemove, List<(FoundationBeam Kept, FoundationBeam Other)> Differing)
            FindDuplicateBeams(IEnumerable<FoundationBeam> beams)
        {
            var toRemove = new List<FoundationBeam>();
            var differing = new List<(FoundationBeam, FoundationBeam)>();
            var keptByPair = new Dictionary<(NodeReferenceType, Guid, NodeReferenceType, Guid), List<FoundationBeam>>();

            foreach (var beam in beams)
            {
                if (beam == null) continue;
                // 向きを揃えた両端 (I,J と J,I を同一視)
                var a = (beam.NodeI_Type, beam.NodeI_Id);
                var b = (beam.NodeJ_Type, beam.NodeJ_Id);
                var key = Comparer<(NodeReferenceType, Guid)>.Default.Compare(a, b) <= 0
                    ? (a.Item1, a.Item2, b.Item1, b.Item2)
                    : (b.Item1, b.Item2, a.Item1, a.Item2);

                if (!keptByPair.TryGetValue(key, out var kept))
                {
                    keptByPair[key] = [beam];
                    continue;
                }
                var same = kept.FirstOrDefault(k => BeamAttributes(k) == BeamAttributes(beam));
                if (same != null)
                {
                    toRemove.Add(beam);
                }
                else
                {
                    differing.Add((kept[0], beam));
                    kept.Add(beam);
                }
            }
            return (toRemove, differing);
        }

        /// <summary>重複判定に使う入力属性と表示状態。解析結果と選択状態は含めない。</summary>
        private static (int, int, string, double, double, double, double, double, bool) BeamAttributes(FoundationBeam b)
            => (b.MaterialNo, b.SectionNo, b.SectionName ?? "", b.Width, b.Height, b.YoungModulus, b.ShearModulus, b.AngleBeta, b.IsVisible);

        // 自動梁要素生成（X同一・Y同一の杭配置を基礎梁で連結）
        [RelayCommand]
        private void OnAutoGenerateFoundationBeams()
        {
            if (CurrentInputModel?.PileLayoutItems == null ||
                CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            var piles = CurrentInputModel.PileLayoutItems;
            if (piles.Count < 2) return;

            // 対象の杭: 選んでいればその杭だけ、選んでいなければ全ての杭。確認文にどちらかを書く。
            // 以前は「選択中の杭配置について」と案内しながら、選択を見ずに全ての杭を連結していた
            var selected = piles.Where(p => p != null && p.IsSelected).ToList();
            if (selected.Count == 1)
            {
                MessageService.Show(
                    "杭配置を 2 本以上選ぶか、選択を外して全ての杭配置を対象にしてください。",
                    "自動梁要素生成", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool useSelection = selected.Count >= 2;
            var targets = useSelection ? selected : piles.Where(p => p != null).ToList();

            // 追加する梁を先に求める。1 本も無ければ、解析結果を消さず・履歴も積まずに終える
            // (以前は結果を消してから探したので、すべて連結済みでも結果を失って「0 本生成」になった)
            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            List<FoundationBeam> newBeams;
            try { newBeams = FindAutoFoundationBeams(piles, targets, beams); }
            catch (ArgumentException ex) { RejectSplit(ex.Message); return; }
            string scope = useSelection ? $"選択中の杭配置 ({targets.Count} 本)" : $"全ての杭配置 ({targets.Count} 本。杭を選んでいないため)";
            if (newBeams.Count == 0)
            {
                MessageService.Show(
                    $"{scope}の間に、追加する基礎梁はありません (隣り合う杭配置はすでに連結されています)。",
                    "自動梁要素生成", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string message = $"梁要素を自動生成しますか？\n\n{scope}について、X成分・Y成分がそれぞれ同一の隣り合う杭配置の"
                           + $"接合節点を基礎梁で連結します ({newBeams.Count} 本を追加)。";
            if (HasAnyAnalysisResult)
                message += "\n\n※ 既存の解析結果は保持されますが、再解析が必要になります。";

            var result = PileDesign.Services.MessageService.Show(
                message,
                "自動梁要素生成",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;

            var before = CaptureInputEdit();

            int addedCount = newBeams.Count;

            // 既存 + 新規を結合して一括セット（CollectionChanged を1回だけ発火）
            var allBeams = new ObservableCollection<FoundationBeam>(beams.Concat(newBeams));
            CurrentInputModel.FoundationBeamInput.Beams = allBeams;

            // 自動生成梁は MaterialNo=1 / SectionNo=1 を参照するため、参照先のデフォルトを保証
            if (addedCount > 0)
            {
                CurrentInputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();
            }

            RenumberFoundationBeams();
            // 個別矩形（基礎梁考慮）の表示可否を即座に再評価 (Beams コレクション置換後の保険)
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            CompleteInputEdit(before);

            MessageService.Show(
                $"{addedCount} 本の基礎梁を自動生成しました。",
                "自動梁要素生成完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 自動生成で追加する基礎梁。X 成分 (または Y 成分) が同じ杭配置を並べ、<b>全ての杭の中で</b>隣り合う 2 本を結ぶ。
        /// そのうち両端が <paramref name="targets"/> にある梁だけを返す。既に杭配置どうしを結んでいる組は除く。
        ///
        /// 隣り合いは全ての杭で決める。選んだ杭だけで決めると、間にある選んでいない杭を飛び越える梁ができる。
        /// </summary>
        internal static List<FoundationBeam> FindAutoFoundationBeams(
            IEnumerable<PileLayoutDataItem> allPiles, IEnumerable<PileLayoutDataItem> targets, IEnumerable<FoundationBeam> existing)
        {
            const double tolerance = 1e-3; // 座標一致の許容誤差 (m)
            var piles = allPiles.Where(p => p != null).ToList();
            if (piles.Any(p => !IsFinitePosition(new Point3D(p.X, p.Y, p.Z))))
                throw new ArgumentException("自動梁生成の対象に有限ではない接合節点の座標があります。");
            var targetIds = new HashSet<Guid>(targets.Where(p => p != null).Select(p => p.UniqueId));

            // 既存ビームのペアセット（重複チェック用）
            var existingPairs = new HashSet<(Guid, Guid)>();
            foreach (var b in existing)
            {
                if (b == null) continue;
                if (b.NodeI_Type == NodeReferenceType.PileLayout && b.NodeJ_Type == NodeReferenceType.PileLayout)
                {
                    existingPairs.Add((b.NodeI_Id, b.NodeJ_Id));
                    existingPairs.Add((b.NodeJ_Id, b.NodeI_Id));
                }
            }

            var newBeams = new List<FoundationBeam>();
            void Connect(IEnumerable<IGrouping<double, PileLayoutDataItem>> groups, Func<PileLayoutDataItem, double> along)
            {
                foreach (var group in groups.Where(g => g.Count() >= 2))
                {
                    var sorted = group.OrderBy(along).ToList();
                    for (int i = 0; i < sorted.Count - 1; i++)
                    {
                        var p1 = sorted[i];
                        var p2 = sorted[i + 1];
                        if (!targetIds.Contains(p1.UniqueId) || !targetIds.Contains(p2.UniqueId)) continue;
                        if (existingPairs.Contains((p1.UniqueId, p2.UniqueId))) continue;

                        double length = (new Point3D(p2.X, p2.Y, p2.Z) - new Point3D(p1.X, p1.Y, p1.Z)).Length;
                        if (!double.IsFinite(length)) throw new ArgumentException("生成する梁の長さが数値の範囲外です。");
                        if (length <= 1e-9) continue;

                        newBeams.Add(new FoundationBeam
                        {
                            NodeI_Type = NodeReferenceType.PileLayout,
                            NodeI_Id = p1.UniqueId,
                            NodeJ_Type = NodeReferenceType.PileLayout,
                            NodeJ_Id = p2.UniqueId,
                            MaterialNo = 1,
                            SectionNo = 1,
                            AngleBeta = 0.0
                        });
                        existingPairs.Add((p1.UniqueId, p2.UniqueId));
                        existingPairs.Add((p2.UniqueId, p1.UniqueId));
                    }
                }
            }

            // X座標が同一の杭をグルーピング → Y座標昇順で隣接杭間にビーム生成
            Connect(piles.GroupBy(p => Math.Round(p.X / tolerance) * tolerance), p => p.Y);
            // Y座標が同一の杭をグルーピング → X座標昇順で隣接杭間にビーム生成
            Connect(piles.GroupBy(p => Math.Round(p.Y / tolerance) * tolerance), p => p.X);
            return newBeams;
        }

        // 基礎梁節点番号振り直し
        private void RenumberFoundationNodes()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            for (int i = 0; i < CurrentInputModel.FoundationBeamInput.Nodes.Count; i++)
                CurrentInputModel.FoundationBeamInput.Nodes[i].No = i + 1;
        }

        // 基礎梁番号振り直し: No プロパティ廃止により実体は何もしない (位置 = ID)。
        // 既存呼び出しサイトの互換維持のためメソッドは残置 (将来呼び出し側を整理して削除可)。
        private void RenumberFoundationBeams()
        {
            // No-op: 番号は Beams コレクションの位置から自動算出されるため不要
        }

        // 杭配置番号の更新
        public void UpdatePileLayoutNo()
        {
            for (int i = 0; i < CurrentInputModel.PileLayoutItems.Count; i++)
            {
                CurrentInputModel.PileLayoutItems[i].No = i + 1;
                CurrentInputModel.PileLayoutItems[i].PileNo = i + 1;
            }
        }

        // 荷重面の自動生成
        [RelayCommand]
        private void OnAdjustRectLoadPlan()
        {
            // 荷重面等価径 (GroupPileLoadDia) が 0 の地盤・杭・レベルセットがある場合は警告。
            // 0 のものは群杭沈下解析でスキップされるため、ユーザーに気付かせる。
            // ただし任意矩形モードでは GroupPileLoadDia は使われないため警告不要。
            var loadingType = CurrentInputModel?.PileGroupSettlement?.LoadingType;
            bool needsGroupPileLoadDia = loadingType == "個別十字" || loadingType == "個別十字（基礎梁反力）"
                                       || loadingType == "個別矩形" || loadingType == "個別矩形（基礎梁考慮）";
            var soilPiles = CurrentInputModel?.ElementDivision?.SoilPiles;
            if (needsGroupPileLoadDia && soilPiles != null && soilPiles.Count > 0)
            {
                var zeroDiaPiles = soilPiles.Where(sp => sp.GroupPileLoadDia <= 0.0).ToList();
                if (zeroDiaPiles.Count > 0)
                {
                    var sampleLines = zeroDiaPiles
                        .Take(10)
                        .Select(sp => $"  ・地盤{sp.GroundNo}・杭体{sp.PileBodyNo} (No.{sp.No})");
                    var moreNote = zeroDiaPiles.Count > 10
                        ? $"\n  …他 {zeroDiaPiles.Count - 10} 件"
                        : "";
                    var msg = $"荷重面等価径 (GroupPileLoadDia) が 0 (未入力) の地盤・杭・レベルセットが {zeroDiaPiles.Count} 件あります:\n" +
                              string.Join("\n", sampleLines) + moreNote +
                              "\n\n対象の杭は群杭沈下解析でスキップされます。\n続行しますか?";
                    var result = MessageService.Show(msg, "荷重面等価径未入力の確認",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }
            }

            // Undoポイントを追加


            // BoundingBoxCalculator を使用して境界を計算
            if (CurrentInputModel?.PileGroupSettlement == null || CurrentInputModel.PileLayoutItems?.Count == 0) return;
            BoundingBoxCalculator.BoundingBox boundingBox;
            try { boundingBox = BoundingBoxCalculator.Calculate(CurrentInputModel.PileLayoutItems, RectLoadPileDistance); }
            catch (ArgumentException ex) { MessageService.ShowInputRejected(ex); return; }
            if (!double.IsFinite(boundingBox.Width * boundingBox.Height) || boundingBox.Width * boundingBox.Height <= 0)
            { MessageService.Show("平面範囲の幅・高さは0より大きくしてください。"); return; }

            // 全杭のVL軸力合計を荷重として設定
            double totalVL = 0;
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                totalVL += pile.AxialForceVL;
                if (!double.IsFinite(totalVL)) { MessageService.Show("杭の軸力合計が数値の範囲外です。荷重面は追加しません。"); return; }
            }
            var before = CaptureInputEdit();

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad()
            {
                X1 = boundingBox.MinX,
                X2 = boundingBox.MaxX,
                Y1 = boundingBox.MinY,
                Y2 = boundingBox.MaxY,
                QA = totalVL
            }
            );

            // 個別十字系で手動自動生成された場合は「任意矩形」に切り替え
            SwitchToAnyRectIfCrossType();

            IsGroupPileSettlementAnalysisDone = false;

            CompleteInputEdit(before, scope: AnalysisInputScope.Settlement);
        }




        // 根入部平面の自動調整
        [RelayCommand]
        private void OnAdjustEmbedmentPlan()
        {


            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show(
                    "杭が 1 本も配置されていないため、根入部の平面を自動調整できません。\n" +
                    "メイン画面の「杭」タブで杭を追加してください。",
                    "根入部の自動調整", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count == 0)
            {
                MessageService.Show(
                    "根入部の層が 1 つも定義されていないため、平面を自動調整できません。\n" +
                    "メイン画面の「根入部」タブで層を追加してください。",
                    "根入部の自動調整", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // BoundingBoxCalculator を使用して境界を計算
            BoundingBoxCalculator.BoundingBox boundingBox;
            try { boundingBox = BoundingBoxCalculator.Calculate(CurrentInputModel.PileLayoutItems, EmbedmentPileDistance); }
            catch (ArgumentException ex) { MessageService.ShowInputRejected(ex); return; }
            if (!double.IsFinite(boundingBox.Width * boundingBox.Height) || boundingBox.Width * boundingBox.Height <= 0)
            { MessageService.Show("平面範囲の幅・高さは0より大きくしてください。"); return; }
            if (!ConfirmDiscardInvalidatedByInputChange(true, "根入部の平面調整")) return;
            var before = CaptureInputEdit();

            foreach (var embedmentDataItem in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                embedmentDataItem.X1 = boundingBox.MinX;
                embedmentDataItem.X2 = boundingBox.MaxX;
                embedmentDataItem.Y1 = boundingBox.MinY;
                embedmentDataItem.Y2 = boundingBox.MaxY;
            }

            // 変更後（以下の箇所で適用）
            CompleteInputEdit(before);
        }

        // 慣性力作用点をすべての接合節点の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {

            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show(GuardMessages.NoPileLayout);
                return;
            }


            // 接合節点（接合節点 = pile.Z）の図心を計算 (v2 セマンティクス)
            var piles = CurrentInputModel.PileLayoutItems;
            double centerX, centerY, centerZ;
            try
            {
                centerX = FiniteMean(piles.Select(p => p.X));
                centerY = FiniteMean(piles.Select(p => p.Y));
                centerZ = FiniteMean(piles.Select(p => p.Z));
            }
            catch (ArgumentException ex) { RejectSplit(ex.Message); return; }
            var cases = CurrentInputModel.LoadCasesInput;
            bool Same(double x, double y, double z) => x == centerX && y == centerY && z == centerZ;
            if (Same(cases.LoadCaseLevel1Common.ForceActionPointX, cases.LoadCaseLevel1Common.ForceActionPointY, cases.LoadCaseLevel1Common.ForceActionPointAltitude) &&
                Same(cases.LoadCaseLevel2Common.ForceActionPointX, cases.LoadCaseLevel2Common.ForceActionPointY, cases.LoadCaseLevel2Common.ForceActionPointAltitude) &&
                cases.LoadCasesLevel1.Concat(cases.LoadCasesLevel2).All(c => Same(c.ForceActionPointX,c.ForceActionPointY,c.ForceActionPointAltitude))) return;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;
            var before = CaptureInputEdit();

            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointY = centerY;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel1Common.ForceActionPointAltitude = centerZ;

            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointX = centerX;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointY = centerY;
            CurrentInputModel.LoadCasesInput.LoadCaseLevel2Common.ForceActionPointAltitude = centerZ;

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
                loadCase.ForceActionPointAltitude = centerZ;
            }

            foreach (LoadCase loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
            {
                loadCase.ForceActionPointX = centerX;
                loadCase.ForceActionPointY = centerY;
                loadCase.ForceActionPointAltitude = centerZ;
            }

            // 変更後（以下の箇所で適用）
            CompleteInputEdit(before);
        }

        [RelayCommand]
        private void AutoIsFrontPiles()
        {


            var autoIsFrontPilesWindow = new AutoIsFrontPilesWindow();
            autoIsFrontPilesWindow.AutoIsFrontPileCompleted += AutoIsFrontPilesWindow_AutoIsFrontPileCompleted;
            autoIsFrontPilesWindow.ShowDialog();
        }

        //群杭係数ウィンドウを開くメソッド
        [RelayCommand]
        private void GroupPileFactor()
        {
            // Windowをインスタンス化して表示
            GroupPileFactorWindow groupPileFactorWindow = new(this);

            groupPileFactorWindow.ShowDialog(); // モーダルダイアログとして表示

            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
        }


        // 群杭沈下解析の実行メソッド
        /// <summary>
        /// 群杭沈下解析 (一般解析) を実行できない理由。実行できるなら null。
        ///
        /// 以前は実行してから 6 種類のダイアログで叱っていた。押す前に分かるようにするため、
        /// 判定をここ 1 か所に集め、<see cref="PileGroupSettlementAnalysisCommand"/> の
        /// CanExecute とボタンの ToolTip の両方から使う。
        /// </summary>
        public string? GroupSettlementAnalysisDisabledReason => DescribeGroupSettlementBlocker()?.User;

        /// <summary>群杭沈下解析ボタンの説明。実行できないときはその理由を出す。</summary>
        public string GroupSettlementAnalysisToolTip =>
            DescribeGroupSettlementBlocker()?.User
            ?? "基礎梁を考慮しない単発のスタインブレナー解析を実行します。";

        /// <summary>群杭沈下の入力タブ。実行できない理由を出すとき、直す場所を開くのに使う。</summary>
        public enum GroupSettlementInputTab
        {
            /// <summary>土層。</summary>
            SoilLayers,
            /// <summary>グリッド。</summary>
            Grid,
            /// <summary>解析（一般）。荷重面・荷重タイプ・矩形荷重はここ。</summary>
            GeneralAnalysis,
        }

        /// <summary>
        /// 群杭沈下の入力タブを開く。タブの操作は画面の持ち物なので、View 側が差し込む。
        /// </summary>
        public Action<GroupSettlementInputTab>? ActivateGroupSettlementInputTabAction { get; set; }

        /// <summary>
        /// 実行できない理由があれば、<b>直す場所のタブを開いてから</b>知らせる。
        ///
        /// 「矩形荷重を追加してください」と言われても、どのタブかを探すところから始まる。
        /// 先にタブを開いておけば、OK を押した時点で入力の場所が出ている。
        /// </summary>
        /// <returns>理由を出した (＝実行できない) なら true。</returns>
        public bool ShowGroupSettlementBlockerIfAny()
        {
            if (DescribeGroupSettlementBlocker() is not { } blocker) return false;

            ActivateGroupSettlementInputTabAction?.Invoke(blocker.Tab);
            MessageService.Show(blocker.User, "群杭沈下解析（一般）",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private bool CanPileGroupSettlementAnalysis() => DescribeGroupSettlementBlocker() == null;

        /// <summary>
        /// 群杭沈下解析ボタンの可否と、押せない理由 (ツールチップ) を問い直す。
        ///
        /// <c>CommandManager.RequerySuggested</c> から呼ばれるので、判定は 1 回だけ行い、
        /// <b>文言が変わったときだけ</b>通知する (RequerySuggested はクリックや
        /// フォーカス移動のたびに飛んでくる)。
        /// </summary>
        internal void RefreshGroupSettlementGuard()
        {
            PileGroupSettlementAnalysisCommand.NotifyCanExecuteChanged();

            string? blocker = DescribeGroupSettlementBlocker()?.User;
            if (_hasEvaluatedGroupSettlementBlocker && blocker == _lastGroupSettlementBlocker) return;

            _hasEvaluatedGroupSettlementBlocker = true;
            _lastGroupSettlementBlocker = blocker;
            OnPropertyChanged(nameof(GroupSettlementAnalysisDisabledReason));
            OnPropertyChanged(nameof(GroupSettlementAnalysisToolTip));
        }

        /// <summary>
        /// 実行を妨げているものを 1 つ返す (利用者向けの文面)。無ければ null。
        /// 荷重タイプごとに必要な入力が違うので、まず荷重タイプで分岐する。
        /// </summary>
        private (string User, GroupSettlementInputTab Tab)? DescribeGroupSettlementBlocker()
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs == null) return ("群杭沈下解析の入力がありません。", GroupSettlementInputTab.GeneralAnalysis);

            var piles = CurrentInputModel?.PileLayoutItems;
            var rectLoads = pgs.RectLoads;

            switch (pgs.LoadingType)
            {
                case "任意矩形":
                    if (rectLoads == null || rectLoads.Count == 0)
                        return ("群杭荷重（矩形荷重）が定義されていません。\n「解析（一般）」タブで矩形荷重を追加してください。", GroupSettlementInputTab.GeneralAnalysis);
                    if (rectLoads.All(r => r.QA == 0))
                        return ("値が0の群杭荷重（矩形荷重）しか定義されていません。\n「解析（一般）」タブで荷重値を設定してください。", GroupSettlementInputTab.GeneralAnalysis);
                    break;

                case "個別十字":
                case "個別矩形":
                    // 杭位置と軸力から矩形荷重を自動生成するため、杭が必要
                    if (piles == null || piles.Count == 0)
                        return (GuardMessages.NoPileLayout, GroupSettlementInputTab.GeneralAnalysis);
                    if (piles.All(p => (p.AxialForceVL0 + p.AxialForceVLAdditional) == 0))
                        return ("全ての杭の軸力（VL0+VLadd）が0です。\n杭タブで軸力を設定してください。", GroupSettlementInputTab.GeneralAnalysis);
                    break;

                case "個別十字（基礎梁反力）":
                    if (!IsVerticalBeamAnalysisDone || VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0)
                        return ("単杭沈下解析（基礎梁考慮）が実行されていません。\n先に単杭沈下解析（基礎梁考慮）を実行してください。", GroupSettlementInputTab.GeneralAnalysis);
                    if (piles == null || piles.Count == 0)
                        return (GuardMessages.NoPileLayout, GroupSettlementInputTab.GeneralAnalysis);
                    break;

                case "個別矩形（基礎梁考慮）":
                    if (piles == null || piles.Count == 0)
                        return (GuardMessages.NoPileLayout, GroupSettlementInputTab.GeneralAnalysis);
                    if (CurrentInputModel?.FoundationBeamInput?.Beams is not { Count: > 0 })
                        return ("基礎梁が定義されていません。\n基礎梁を入力してください。", GroupSettlementInputTab.GeneralAnalysis);
                    if (rectLoads == null || rectLoads.Count == 0 || rectLoads.All(r => r.QA == 0))
                        return ("矩形荷重が定義されていません (または全て 0)。\n荷重面等価径を入力すると自動生成されます。", GroupSettlementInputTab.GeneralAnalysis);
                    break;

                default:
                    return ("荷重タイプが設定されていません。\n「解析（一般）」タブで荷重タイプを選択してください。", GroupSettlementInputTab.GeneralAnalysis);
            }

            if (pgs.SettlementSoilLayers == null || pgs.SettlementSoilLayers.Count == 0)
                return ("群杭沈下解析用の土層が1層以上必要です。\n「土層」タブで土層を追加してください。", GroupSettlementInputTab.SoilLayers);

            // 荷重面が土層の範囲に入っていること
            double topAlt = pgs.SoilLayersTopAltitude;
            double loadAlt = pgs.LoadingPlaneAltitude;
            double bottomAlt = pgs.SettlementSoilLayers[^1].BottomAltitude;
            if (loadAlt > topAlt + NumericalConstants.NEAR_ZERO_EPSILON)
                return ($"荷重面 Z ({loadAlt:N3} m) が土層上端 Z ({topAlt:N3} m) より高くなっています。\n荷重面を土層上端以下に設定してください。", GroupSettlementInputTab.GeneralAnalysis);
            if (loadAlt < bottomAlt - NumericalConstants.NEAR_ZERO_EPSILON)
                return ($"荷重面 Z ({loadAlt:N3} m) が最下層下端 Z ({bottomAlt:N3} m) より低くなっています。\n荷重面を最下層下端以上に設定してください。", GroupSettlementInputTab.GeneralAnalysis);

            return null;
        }

        [RelayCommand(CanExecute = nameof(CanPileGroupSettlementAnalysis))]
        private void PileGroupSettlementAnalysis()
        {
            // CanExecute で弾いているが、コマンドを直接 Execute された場合の最後の砦。
            if (ShowGroupSettlementBlockerIfAny()) return;

            var loadingType = CurrentInputModel.PileGroupSettlement.LoadingType;

            // 荷重面位置と土層プロファイルの整合性チェック
            var pgs = CurrentInputModel.PileGroupSettlement;
            if (pgs.SettlementSoilLayers == null || pgs.SettlementSoilLayers.Count == 0)
            {
                MessageService.Show("群杭沈下解析用の土層が1層以上必要です。\n土層タブで土層を追加してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // 一回解析 (基礎梁無し) の荷重面標高を採用 (per-route フィールドから現在値にコピー)
            // ※ 個別矩形（基礎梁考慮）は別ルート (反復解析) で OpenGroupSettlementWithBeamWindow が起動時にコピー
            string loadingTypeNow = pgs.LoadingType ?? "";
            if (loadingTypeNow != "個別矩形（基礎梁考慮）" && !double.IsNaN(pgs.LoadingPlaneAltitudeNonBeam))
                pgs.LoadingPlaneAltitude = pgs.LoadingPlaneAltitudeNonBeam;

            // 個別矩形（基礎梁考慮）は反復解析ウィンドウで実行 → 確定後に Steinbrenner グリッドコンタを更新
            if (loadingType == "個別矩形（基礎梁考慮）")
            {
                OpenGroupSettlementWithBeamWindow();
                // ウィンドウが OK で閉じられた場合は確定された RectLoads / 杭沈下が反映済みなので
                // 後続のグリッドコンター生成へ進む。Cancel された場合は IsSaved=false → 何もせず終了。
                // 簡略化のため確定/破棄に関わらず後続フローを継続 (Cancel 時は元の RectLoads が残る)。
            }

            var result = _settlementAnalysisService.PerformSettlementAnalysis(
                CurrentInputModel.PileGroupSettlement,
                CurrentInputModel.PileLayoutItems,
                CurrentInputModel.ElementDivision.SoilPiles,
                CurrentInputModel.GridXItems,
                CurrentInputModel.GridYItems,
                GroupPileSettlementXMin,
                GroupPileSettlementXMax,
                GroupPileSettlementYMin,
                GroupPileSettlementYMax,
                GroupPileSettlementXOffset,
                GroupPileSettlementYOffset,
                GroupPileSettlementXSpacing,
                GroupPileSettlementYSpacing,
                VerticalBeamCaseResults);

            if (!result.Success)
            {
                MessageService.Show(result.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (result.Warnings.Count > 0)
            {
                MessageService.Show("群杭沈下解析は終わりましたが、次の点を確認してください。\n\n" + string.Join("\n", result.Warnings),
                    "群杭沈下解析", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 沈下グリッドは記録 (CaseRecord) の側に持たせる。入力モデルの複製
            // (PileGroupSettlement.SettlementGridData) は旧ファイルを開くためだけに残してあり、
            // ここへ書くと保存ファイルにコンタが二重に入る。表示は ActiveSettlementGridData を読む。

            // 個別矩形（基礎梁考慮）以外の解析結果を CaseRecord として永続化
            // (個別矩形（基礎梁考慮）は OpenGroupSettlementWithBeamWindow 側で既に保存済み)
            if (loadingType != "個別矩形（基礎梁考慮）")
            {
                UpsertNonBeamAwareCaseRecord(loadingType, result.SettlementGridData, result.PileSettlements_mm);
                // 一般解析は VL ケース 1 件のみ保存するため、解析直後は表示荷重ケースを VL に切替えて
                // 結果コンタを表示する。setter 経由で ActiveCase 同期 + 再描画が走るが、既に VL を
                // 選択中の場合は setter が発火しないため、明示的にも同期しておく。
                SelectedLoadCaseName = "VL";
                SyncGroupSettlementActiveCaseFromLoadCase("VL");
            }

            ShowToast("スタインブレナーの近似式による解析が終了しました。");

            IsGroupPileGridDeformationVisible = true;
            IsGroupPileSettlementAnalysisDone = true;
            MarkSettlementResultsCurrent();
            CaptureAnalysisResultSet();
            //IsAnalysisResultVisible = true;
            IsBubbleVisible = true;
            IsArrowVisible = true;
            DisplacementDiagramRatio = 0.3;
        }

        // 自動前方杭設定の処理メソッド
        private void AutoIsFrontPilesWindow_AutoIsFrontPileCompleted(object sender, AutoIsFrontEventArgs e)
        {
            if (!double.IsFinite(e.Angle) || e.Angle <= 0 || e.Angle >= 90 || e.IsChecked == null || e.IsChecked.Count < 4)
            { e.Cancel = true; RejectSplit("前面杭の角度・荷重ケースの指定を確認してください。"); return; }
            var changes = new List<(PileLayoutDataItem Pile, int Index, bool Value)>();
            double cosAlpha = Math.Cos(e.Angle * Math.PI / 180.0);
            for (int i = 0; i < 4; i++)
            {
                if (!e.IsChecked[i]) continue;
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                {
                    bool value = IsFrontPile(pile, CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i], cosAlpha);
                    if (pile.IsFrontPiles[i] != value) changes.Add((pile,i,value));
                }
            }
            if (changes.Count == 0) return;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) { e.Cancel = true; return; }
            var before = CaptureInputEdit();
            foreach (var change in changes) change.Pile.IsFrontPiles[change.Index] = change.Value;
            IsFrontPileLabelVisible = true;
            CompleteInputEdit(before);
        }

        internal static double FiniteMean(IEnumerable<double> values) => PileDesign.Common.StableNumerics.Mean(values);

        /// <summary>
        /// 指定された杭が前方杭かどうかを判定
        /// </summary>
        private bool IsFrontPile(PileLayoutDataItem targetPile, LoadCase loadCase, double cosAlpha)
        {
            Point targetPosition = new(targetPile.Point3D.X, targetPile.Point3D.Y);
            Vector loadDirectionVector = PileDesign.Converters.VectorConverter.ConvertAngleToUnitVector(loadCase.LoadAngle);

            foreach (PileLayoutDataItem otherPile in CurrentInputModel.PileLayoutItems)
            {
                if (targetPile == otherPile)
                    continue;

                Point otherPosition = new(otherPile.Point3D.X, otherPile.Point3D.Y);
                Vector directionVector = otherPosition - targetPosition;

                // 内積を計算
                double dotProduct = Vector.Multiply(directionVector, loadDirectionVector);

                // 余弦を計算
                double cosTheta = dotProduct / (directionVector.Length * loadDirectionVector.Length);

                // 余弦が指定角度より大きい場合、前方杭ではない
                if (cosAlpha < cosTheta)
                {
                    return false;
                }
            }

            // すべての杭に対してチェックを通過したら前方杭
            return true;
        }

    }
}
