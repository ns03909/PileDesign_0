using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
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
            if (!CheckAndResetAnalysisResults()) return;

            // スナップショットを保存
            TrySaveUndoSnapshotSafely();

            Point3D nextPoint3D = new();
            if (CurrentInputModel.PileLayoutItems.Count != 0)
            {
                // 直前の杭から X 方向に 7.2m オフセット
                nextPoint3D = CurrentInputModel.PileLayoutItems.Last().Point3D + new Vector3D() { X = 7.2 };
            }

            // UIスレッドから呼ばれるため直接実行
            CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem() { X = nextPoint3D.X, Y = nextPoint3D.Y, Z = nextPoint3D.Z });
            CurrentInputModel.PileLayoutItems[^1].SetMainWindowViewModel(this);
            // 要素未分割の場合は自動で SoiPile を再生成
            if (!IsElementSplit)
                RequestGenerateSoilPiles();

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
            UpdatePileLayoutNo();
        }

        [RelayCommand]
        private void OnComputePileGroupFactor()
        {
            if (!CheckAndResetAnalysisResults()) return;

            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

        [RelayCommand]
        private void OnComputePileSpacingFactor()
        {
            if (!CheckAndResetAnalysisResults()) return;

            double pileCount = CurrentInputModel.PileLayoutItems.Count;
            if (pileCount == 0)
                return;
        }

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
            if (!CheckAndResetAnalysisResults()) return;
            TrySaveUndoSnapshotSafely();

            // 旧No→新Noマッピングを構築
            var sorted = orderFunc(col).ToList();
            var oldToNewNo = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++)
                oldToNewNo[sorted[i].No] = i + 1;

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

            RequestUpdateWindow();
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
            if (!CheckAndResetAnalysisResults()) return;
            TrySaveUndoSnapshotSafely();

            var sorted = orderFunc(col).ToList();

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

            RequestUpdateWindow();
        }

        /// <summary>梁要素: 要素番号昇順で整列（表示順のみ変更、解析結果に影響なし）</summary>
        [RelayCommand]
        private void SortBeamsByNo()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;
            TrySaveUndoSnapshotSafelyOptimized();

            // 旧 No プロパティ廃止につき、現状並びを維持 (No-op)。
            // 将来この整列コマンドが必要な場合は別の基準 (Node 順等) に基づいて実装する。
            RequestUpdateWindow();
        }

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

            TrySaveUndoSnapshotSafelyOptimized();

            foreach (var b in targets)
            {
                (b.NodeI_Type, b.NodeJ_Type) = (b.NodeJ_Type, b.NodeI_Type);
                (b.NodeI_Id, b.NodeJ_Id) = (b.NodeJ_Id, b.NodeI_Id);

                // ローカル x 軸反転で y 軸が 180° 回る分を β で相殺し、物理的に同じ断面向きを維持する
                b.AngleBeta = ((180.0 - b.AngleBeta) % 360.0 + 360.0) % 360.0;
            }

            RequestUpdateWindow();
        }

        /// <summary>梁要素: I端節点→J端節点昇順で整列（表示順のみ変更、解析結果に影響なし）</summary>
        [RelayCommand]
        private void SortBeamsByNode()
        {
            var beams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return;
            TrySaveUndoSnapshotSafelyOptimized();

            var sorted = beams
                .OrderBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeI_Type, b.NodeI_Id))
                .ThenBy(b => CurrentInputModel.GetNodeDisplayNo(b.NodeJ_Type, b.NodeJ_Id))
                .ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int cur = beams.IndexOf(sorted[i]);
                if (cur != i) beams.Move(cur, i);
            }
            // 旧 No プロパティは廃止: 番号 = 位置インデックスとして自動的に追従

            RequestUpdateWindow();
        }

        // 要素の節点位置での分割
        // 旧実装は FoundationNode (基礎梁節点) のみ参照していたが、ToolTip 「重なる一般節点で分割」の通り
        // PileLayout (杭頭)・InputNode (一般)・FoundationNode の全種類を対象にする。
        // 端点参照は NodeReferenceType + Guid の現代式で生成する。
        [RelayCommand]
        public void OnSplitElementsByNodes()
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            var beams = fb.Beams;
            double tolerance = EditDistanceThreshold;

            // 候補ノード一覧 (Type + Guid + 位置) を共通ヘルパで列挙 (PileLayout / GeneralNode / FoundationNode 全種)
            var candidates = EnumerateAllCandidateNodes(includeFoundationNodes: true).ToList();

            var newBeams = new List<FoundationBeam>();
            var toRemove = new List<FoundationBeam>();
            const double endEps = 1e-6;

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
                    if (t <= endEps || t >= 1.0 - endEps) continue;

                    Point3D projection = pI + t * line;
                    double dist = (cand.Pos - projection).Length;
                    if (dist > tolerance) continue;

                    splits.Add((cand.Type, cand.Id, t));
                }

                if (splits.Count == 0) continue;

                // t の昇順でソート
                splits.Sort((a, b) => a.T.CompareTo(b.T));

                // 同一 t に近い候補は重複扱い (杭頭+ΔZc と一般節点が同位置にある場合等)
                var dedupedSplits = new List<(NodeReferenceType Type, Guid Id, double T)>();
                foreach (var s in splits)
                {
                    if (dedupedSplits.Count > 0 && Math.Abs(dedupedSplits[^1].T - s.T) < endEps)
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
                    newBeams.Add(new FoundationBeam
                    {
                        NodeI_Type = endpoints[i].Type,
                        NodeI_Id = endpoints[i].Id,
                        NodeJ_Type = endpoints[i + 1].Type,
                        NodeJ_Id = endpoints[i + 1].Id,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        SectionName = beam.SectionName,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        AngleBeta = beam.AngleBeta,
                        IsVisible = beam.IsVisible,
                    });
                }
                toRemove.Add(beam);
            }

            foreach (var beam in toRemove)
                beams.Remove(beam);
            foreach (var beam in newBeams)
                beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

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

            if (!CheckAndResetAnalysisResults()) return;

            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0)
            {
                MessageService.Show("分割する梁要素を選択してください。");
                return;
            }

            SaveUndoState();

            int n = EqualDivisionCount;
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
                        No = CurrentInputModel.InputNodes.Count + divisionNodes.Count + 1,
                        Type = NodeType.General,
                        X = coordsI.Value.X + (coordsJ.Value.X - coordsI.Value.X) * t,
                        Y = coordsI.Value.Y + (coordsJ.Value.Y - coordsI.Value.Y) * t,
                        Z = coordsI.Value.Z + (coordsJ.Value.Z - coordsI.Value.Z) * t
                    };
                    divisionNodes.Add(newNode);
                }

                foreach (var node in divisionNodes)
                    CurrentInputModel.InputNodes.Add(node);

                // 分割ビームを生成（I → div1 → div2 → ... → J）
                // 最初のセグメント: 元のNodeI → 最初の分割節点
                toAdd.Add(new FoundationBeam
                {
                    NodeI_Type = beam.NodeI_Type,
                    NodeI_Id = beam.NodeI_Id,
                    NodeJ_Type = NodeReferenceType.GeneralNode,
                    NodeJ_Id = divisionNodes[0].UniqueId,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });

                // 中間セグメント
                for (int i = 0; i < divisionNodes.Count - 1; i++)
                {
                    toAdd.Add(new FoundationBeam
                    {
                        NodeI_Type = NodeReferenceType.GeneralNode,
                        NodeI_Id = divisionNodes[i].UniqueId,
                        NodeJ_Type = NodeReferenceType.GeneralNode,
                        NodeJ_Id = divisionNodes[i + 1].UniqueId,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        AngleBeta = beam.AngleBeta,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        SectionName = beam.SectionName
                    });
                }

                // 最後のセグメント: 最後の分割節点 → 元のNodeJ
                toAdd.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = divisionNodes.Last().UniqueId,
                    NodeJ_Type = beam.NodeJ_Type,
                    NodeJ_Id = beam.NodeJ_Id,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });

                toRemove.Add(beam);
            }

            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

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
                result.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.FoundationNode,
                    NodeI_Id = allSplitNodes[i].Id,
                    NodeJ_Type = NodeReferenceType.FoundationNode,
                    NodeJ_Id = allSplitNodes[i + 1].Id,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });
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
        private (Point3D point, double t, double s)? FindSegmentIntersection(
            Point3D p1, Point3D p2, Point3D p3, Point3D p4, double tolerance)
        {
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

            // 平行（または非常に近い）線分
            if (Math.Abs(denom) < 1e-12) return null;

            double t = (b * f - c * e) / denom;
            double s = (a * f - b * c) / denom;

            // 端点付近は除外（端点での接続は交差ではない）
            const double endEps = 1e-6;
            if (t <= endEps || t >= 1.0 - endEps) return null;
            if (s <= endEps || s >= 1.0 - endEps) return null;

            // 最近接点
            var closestA = p1 + t * d1;
            var closestB = p3 + s * d2;
            double dist = (closestA - closestB).Length;

            if (dist > tolerance) return null;

            // 交差点は両最近接点の中点
            var intersection = new Point3D(
                (closestA.X + closestB.X) * 0.5,
                (closestA.Y + closestB.Y) * 0.5,
                (closestA.Z + closestB.Z) * 0.5);

            return (intersection, t, s);
        }

        /// <summary>
        /// 1つの梁要素を複数の交差点(InputNode)で分割し、分割後の要素リストを返す。
        /// </summary>
        private List<FoundationBeam> SplitBeamAtPoints(
            FoundationBeam beam,
            List<(InputNode node, double t)> splitPoints)
        {
            if (splitPoints.Count == 0) return [beam];

            // tの昇順にソート
            var sorted = splitPoints.OrderBy(sp => sp.t).ToList();

            var result = new List<FoundationBeam>();

            // 最初のセグメント: 元のNodeI → 最初の分割節点
            result.Add(new FoundationBeam
            {
                NodeI_Type = beam.NodeI_Type,
                NodeI_Id = beam.NodeI_Id,
                NodeJ_Type = NodeReferenceType.GeneralNode,
                NodeJ_Id = sorted[0].node.UniqueId,
                MaterialNo = beam.MaterialNo,
                SectionNo = beam.SectionNo,
                AngleBeta = beam.AngleBeta,
                Width = beam.Width,
                Height = beam.Height,
                YoungModulus = beam.YoungModulus,
                ShearModulus = beam.ShearModulus,
                SectionName = beam.SectionName
            });

            // 中間セグメント
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                result.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.GeneralNode,
                    NodeI_Id = sorted[i].node.UniqueId,
                    NodeJ_Type = NodeReferenceType.GeneralNode,
                    NodeJ_Id = sorted[i + 1].node.UniqueId,
                    MaterialNo = beam.MaterialNo,
                    SectionNo = beam.SectionNo,
                    AngleBeta = beam.AngleBeta,
                    Width = beam.Width,
                    Height = beam.Height,
                    YoungModulus = beam.YoungModulus,
                    ShearModulus = beam.ShearModulus,
                    SectionName = beam.SectionName
                });
            }

            // 最後のセグメント: 最後の分割節点 → 元のNodeJ
            result.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.GeneralNode,
                NodeI_Id = sorted.Last().node.UniqueId,
                NodeJ_Type = beam.NodeJ_Type,
                NodeJ_Id = beam.NodeJ_Id,
                MaterialNo = beam.MaterialNo,
                SectionNo = beam.SectionNo,
                AngleBeta = beam.AngleBeta,
                Width = beam.Width,
                Height = beam.Height,
                YoungModulus = beam.YoungModulus,
                ShearModulus = beam.ShearModulus,
                SectionName = beam.SectionName
            });

            return result;
        }

        // 交差点で杭要素分割
        [RelayCommand]
        private void SplitElementsAtIntersections()
        {
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams;
            if (beams == null) return;

            if (!CheckAndResetAnalysisResults()) return;

            var selectedBeams = beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count < 2)
            {
                MessageService.Show("交差判定するには梁要素を2本以上選択してください。",
                    "交差点分割", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveUndoState();

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
            var beamSplitPoints = new Dictionary<FoundationBeam, List<(InputNode node, double t)>>();

            // 全ペアの交差判定
            var beamList = beamCoords.Keys.ToList();
            int intersectionCount = 0;

            for (int i = 0; i < beamList.Count; i++)
            {
                for (int j = i + 1; j < beamList.Count; j++)
                {
                    var beamA = beamList[i];
                    var beamB = beamList[j];
                    var (pi1, pi2) = beamCoords[beamA];
                    var (pj1, pj2) = beamCoords[beamB];

                    var result = FindSegmentIntersection(pi1, pi2, pj1, pj2, tolerance);
                    if (result == null) continue;

                    var (point, tA, tB) = result.Value;

                    // 同座標に既存節点があるかチェック（重複防止）
                    bool alreadyExists = false;

                    // 既にこの要素ペアで同じ位置に分割点が登録されていないかチェック
                    if (beamSplitPoints.TryGetValue(beamA, out var existingA))
                    {
                        if (existingA.Any(sp => (new Point3D(sp.node.X, sp.node.Y, sp.node.Z) - point).Length < tolerance))
                            alreadyExists = true;
                    }

                    if (alreadyExists) continue;

                    // 交差点に一般節点を生成
                    var newNode = new InputNode
                    {
                        No = CurrentInputModel.InputNodes.Count + 1,
                        Type = NodeType.General,
                        X = point.X,
                        Y = point.Y,
                        Z = point.Z
                    };
                    CurrentInputModel.InputNodes.Add(newNode);

                    // 要素Aの分割点リストに追加
                    if (!beamSplitPoints.ContainsKey(beamA))
                        beamSplitPoints[beamA] = [];
                    beamSplitPoints[beamA].Add((newNode, tA));

                    // 要素Bの分割点リストに追加
                    if (!beamSplitPoints.ContainsKey(beamB))
                        beamSplitPoints[beamB] = [];
                    beamSplitPoints[beamB].Add((newNode, tB));

                    intersectionCount++;
                }
            }

            if (intersectionCount == 0)
            {
                ShowToast("選択要素間に交差点が見つかりませんでした。", 2); // Warning
                return;
            }

            // 交差が検出された要素を分割
            var toRemove = new List<FoundationBeam>();
            var toAdd = new List<FoundationBeam>();

            foreach (var (beam, splitPoints) in beamSplitPoints)
            {
                var splitBeams = SplitBeamAtPoints(beam, splitPoints);
                toRemove.Add(beam);
                toAdd.AddRange(splitBeams);
            }

            foreach (var beam in toRemove) beams.Remove(beam);
            foreach (var beam in toAdd) beams.Add(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            ShowToast($"{intersectionCount} 個の交差点で {toRemove.Count} → {toAdd.Count} 要素に分割");
        }

        // 基礎梁節点削除 (接続された梁要素もカスケード削除)
        [RelayCommand]
        private void DeleteFoundationNode(FoundationNode node)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Nodes == null) return;

            // 接続されている梁要素を抽出 (NodeI/J_Type=FoundationNode かつ Id が一致するもの)
            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            var connectedBeams = beams.Where(b =>
                (b.NodeI_Type == NodeReferenceType.FoundationNode && b.NodeI_Id == node.Id) ||
                (b.NodeJ_Type == NodeReferenceType.FoundationNode && b.NodeJ_Id == node.Id)
            ).ToList();

            if (connectedBeams.Count > 0)
            {
                var beamNos = connectedBeams.Select(b => beams.IndexOf(b) + 1).OrderBy(n => n).ToList();
                string list = string.Join(", ", beamNos.Take(20).Select(n => $"#{n}"));
                if (beamNos.Count > 20) list += $" ほか {beamNos.Count - 20} 件";
                var result = PileDesign.Services.MessageService.Show(
                    $"節点 {node.No} を削除します。\n" +
                    $"同時に接続された一般梁要素 {beamNos.Count} 本 ({list}) も削除されます。\n" +
                    $"よろしいですか?",
                    "削除確認",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            TrySaveUndoSnapshotSafely();
            foreach (var beam in connectedBeams)
                beams.Remove(beam);
            CurrentInputModel.FoundationBeamInput.Nodes.Remove(node);
            RenumberFoundationNodes();
            RequestUpdateWindow();
        }

        // 基礎梁削除
        [RelayCommand]
        private void DeleteFoundationBeam(FoundationBeam beam)
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            TrySaveUndoSnapshotSafely();
            CurrentInputModel.FoundationBeamInput.Beams.Remove(beam);
            RenumberFoundationBeams();
            RequestUpdateWindow();
        }

        // 重複要素削除
        [RelayCommand]
        private void OnDeleteDupulicateElements()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            SaveUndoState();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            var toRemove = new List<FoundationBeam>();
            // 既に確認済みのペアを記録（順序なし）
            var seenPairs = new HashSet<(NodeReferenceType, Guid, NodeReferenceType, Guid)>();

            foreach (var beam in beams)
            {
                // 順序を正規化して比較（I,J と J,I を同一視）
                var key1 = (beam.NodeI_Type, beam.NodeI_Id, beam.NodeJ_Type, beam.NodeJ_Id);
                var key2 = (beam.NodeJ_Type, beam.NodeJ_Id, beam.NodeI_Type, beam.NodeI_Id);

                if (seenPairs.Contains(key1) || seenPairs.Contains(key2))
                {
                    toRemove.Add(beam);
                }
                else
                {
                    seenPairs.Add(key1);
                }
            }

            foreach (var beam in toRemove)
                beams.Remove(beam);

            RenumberFoundationBeams();
            RequestUpdateWindow();

            PileDesign.Services.MessageService.Show(
                $"{toRemove.Count} 個の重複要素を削除しました。",
                "重複削除完了",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        // 自動梁要素生成（X同一・Y同一の杭配置を基礎梁で連結）
        [RelayCommand]
        private void OnAutoGenerateFoundationBeams()
        {
            if (CurrentInputModel?.PileLayoutItems == null ||
                CurrentInputModel?.FoundationBeamInput?.Beams == null) return;

            string message = "梁要素を自動生成しますか？\n\n選択中の杭配置について、X成分・Y成分がそれぞれ同一の隣り合う杭配置の接合節点を基礎梁で連結します。";
            if (HasAnyAnalysisResult)
                message += "\n\n※ 既存の解析結果は消去されます。";

            var result = PileDesign.Services.MessageService.Show(
                message,
                "自動梁要素生成",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            if (!CheckAndResetAnalysisResults()) return;

            var piles = CurrentInputModel.PileLayoutItems;
            if (piles.Count < 2) return;

            TrySaveUndoSnapshotSafely();

            var beams = CurrentInputModel.FoundationBeamInput.Beams;
            const double tolerance = 1e-3; // 座標一致の許容誤差 (m)

            // 既存ビームのペアセット（重複チェック用）
            var existingPairs = new HashSet<(Guid, Guid)>();
            foreach (var b in beams)
            {
                if (b.NodeI_Type == NodeReferenceType.PileLayout && b.NodeJ_Type == NodeReferenceType.PileLayout)
                {
                    existingPairs.Add((b.NodeI_Id, b.NodeJ_Id));
                    existingPairs.Add((b.NodeJ_Id, b.NodeI_Id));
                }
            }

            // 新規要素を一時リストに蓄積（ObservableCollection への逐次Add を回避）
            var newBeams = new List<FoundationBeam>();

            // X座標が同一の杭をグルーピング → Y座標昇順でソートし隣接杭間にビーム生成
            var xGroups = piles
                .GroupBy(p => Math.Round(p.X / tolerance) * tolerance)
                .Where(g => g.Count() >= 2);

            foreach (var group in xGroups)
            {
                var sorted = group.OrderBy(p => p.Y).ToList();
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    var p2 = sorted[i + 1];
                    var pair = (p1.UniqueId, p2.UniqueId);
                    if (existingPairs.Contains(pair)) continue;

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
                    existingPairs.Add(pair);
                    existingPairs.Add((p2.UniqueId, p1.UniqueId));
                }
            }

            // Y座標が同一の杭をグルーピング → X座標昇順でソートし隣接杭間にビーム生成
            var yGroups = piles
                .GroupBy(p => Math.Round(p.Y / tolerance) * tolerance)
                .Where(g => g.Count() >= 2);

            foreach (var group in yGroups)
            {
                var sorted = group.OrderBy(p => p.X).ToList();
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    var p2 = sorted[i + 1];
                    var pair = (p1.UniqueId, p2.UniqueId);
                    if (existingPairs.Contains(pair)) continue;

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
                    existingPairs.Add(pair);
                    existingPairs.Add((p2.UniqueId, p1.UniqueId));
                }
            }

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
            RequestUpdateWindow();

            MessageService.Show(
                $"{addedCount} 本の基礎梁を自動生成しました。",
                "自動梁要素生成完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            TrySaveUndoSnapshotSafelyOptimized();

            // BoundingBoxCalculator を使用して境界を計算
            var boundingBox = BoundingBoxCalculator.Calculate(
                CurrentInputModel.PileLayoutItems,
                RectLoadPileDistance
            );

            // 全杭のVL軸力合計を荷重として設定
            double totalVL = 0;
            foreach (var pile in CurrentInputModel.PileLayoutItems)
                totalVL += pile.AxialForceVL;

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

            UpdateWindowImmediate();
        }




        // 根入部平面の自動調整
        [RelayCommand]
        private void OnAdjustEmbedmentPlan()
        {
            if (!CheckAndResetAnalysisResults()) return;

            if (CurrentInputModel.PileLayoutItems.Count == 0 || CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count == 0)
                return;

            // BoundingBoxCalculator を使用して境界を計算
            var boundingBox = BoundingBoxCalculator.Calculate(
                CurrentInputModel.PileLayoutItems,
                EmbedmentPileDistance
            );

            foreach (var embedmentDataItem in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                embedmentDataItem.X1 = boundingBox.MinX;
                embedmentDataItem.X2 = boundingBox.MaxX;
                embedmentDataItem.Y1 = boundingBox.MinY;
                embedmentDataItem.Y2 = boundingBox.MaxY;
            }

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 慣性力作用点をすべての接合節点の図心に移動するメソッド
        [RelayCommand]
        private void OnMoveForceActionPointToAverageCenter()
        {
            if (!CheckAndResetAnalysisResults()) return;

            if (CurrentInputModel.PileLayoutItems.Count == 0)
            {
                MessageService.Show("杭配置データがありません。");
                return;
            }

            TrySaveUndoSnapshotSafely();

            // 接合節点（接合節点 = pile.Z）の図心を計算 (v2 セマンティクス)
            var piles = CurrentInputModel.PileLayoutItems;
            double centerX = piles.Average(p => p.X);
            double centerY = piles.Average(p => p.Y);
            double centerZ = piles.Average(p => p.Z);

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
            RequestUpdateWindow();
        }

        [RelayCommand]
        private void AutoIsFrontPiles()
        {
            if (!CheckAndResetAnalysisResults()) return;

            TrySaveUndoSnapshotSafely();

            var viewModel = new AutoIsFrontPileViewModel();
            var autoIsFrontPilesWindow = new AutoIsFrontPilesWindow();
            autoIsFrontPilesWindow.AutoIsFrontPileCompleted += AutoIsFrontPilesWindow_AutoIsFrontPileCompleted;
            autoIsFrontPilesWindow.ShowDialog();
            IsFrontPileLabelVisible = true;
            RequestUpdateWindow();
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
        [RelayCommand]
        private void PileGroupSettlementAnalysis()
        {
            // 荷重タイプ別の事前チェック
            var loadingType = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (loadingType == "任意矩形")
            {
                // 群杭荷重（矩形荷重）が定義されているかチェック
                var rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
                if (rectLoads == null || rectLoads.Count == 0)
                {
                    MessageService.Show("群杭荷重（矩形荷重）が定義されていません。\n荷重タブで矩形荷重を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 荷重値が全て0かチェック
                if (rectLoads.All(r => r.QA == 0))
                {
                    MessageService.Show("値が0の群杭荷重（矩形荷重）しか定義されていません。\n荷重タブで荷重値を設定してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別十字" || loadingType == "個別矩形")
            {
                // 個別十字・個別矩形は杭位置と軸力から矩形荷重を自動生成するため、杭が必要
                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (piles.All(p => (p.AxialForceVL0 + p.AxialForceVLAdditional) == 0))
                {
                    MessageService.Show("全ての杭の軸力（VL0+VLadd）が0です。\n杭タブで軸力を設定してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別十字（基礎梁反力）")
            {
                if (!IsVerticalBeamAnalysisDone || VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0)
                {
                    MessageService.Show("基礎梁考慮鉛直解析が実行されていません。\n先に基礎梁考慮鉛直解析を実行してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (loadingType == "個別矩形（基礎梁考慮）")
            {
                // 個別矩形（基礎梁考慮）は基礎梁が必須 (将来の反復ばね解析用)
                var piles = CurrentInputModel.PileLayoutItems;
                if (piles == null || piles.Count == 0)
                {
                    MessageService.Show("杭が配置されていません。\n杭タブで杭を追加してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var beams = CurrentInputModel.FoundationBeamInput?.Beams;
                if (beams == null || beams.Count == 0)
                {
                    MessageService.Show("基礎梁が定義されていません。\n基礎梁を入力してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var rectLoads = CurrentInputModel.PileGroupSettlement.RectLoads;
                if (rectLoads == null || rectLoads.Count == 0 || rectLoads.All(r => r.QA == 0))
                {
                    MessageService.Show("矩形荷重が定義されていません (または全て 0)。\n荷重面等価径を入力すると自動生成されます。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                // "なし" またはその他
                MessageService.Show("荷重タイプが設定されていません。\n荷重タブで荷重タイプを選択してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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

            // 一般解析実行時に pgs.RectLoads が反復で書き換えられた状態 (= 現在 反復モード) なら、
            // ユーザー入力スナップショットから 一般入力を復元してから Steinbrenner を回す。
            // (反復後に直接「一般解析実行」を押した場合に、収束反力で一般を再計算してしまう問題への対策)
            if (loadingType != "個別矩形（基礎梁考慮）"
                && pgs.ActiveLoadingType == "個別矩形（基礎梁考慮）"
                && pgs.NonBeamRectLoadsSnapshot != null
                && pgs.NonBeamRectLoadsSnapshot.Count > 0)
            {
                pgs.RectLoads = new System.Collections.ObjectModel.ObservableCollection<Models.InputData.RectLoad>(
                    pgs.NonBeamRectLoadsSnapshot.Select(r => new Models.InputData.RectLoad
                    {
                        X1 = r.X1, X2 = r.X2, Y1 = r.Y1, Y2 = r.Y2,
                        QA = r.QA, LinkedPileNo = r.LinkedPileNo,
                    }));
            }

            double topAlt = pgs.SoilLayersTopAltitude;
            double loadAlt = pgs.LoadingPlaneAltitude;
            double bottomAlt = pgs.SettlementSoilLayers[^1].BottomAltitude;
            if (loadAlt > topAlt + NumericalConstants.NEAR_ZERO_EPSILON)
            {
                MessageService.Show($"荷重面 Z ({loadAlt:N3} m) が土層上端 Z ({topAlt:N3} m) より高くなっています。\n荷重面を土層上端以下に設定してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (loadAlt < bottomAlt - NumericalConstants.NEAR_ZERO_EPSILON)
            {
                MessageService.Show($"荷重面 Z ({loadAlt:N3} m) が最下層下端 Z ({bottomAlt:N3} m) より低くなっています。\n荷重面を最下層下端以上に設定してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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

            CurrentInputModel.PileGroupSettlement.SettlementGridData = result.SettlementGridData;

            // 個別矩形（基礎梁考慮）以外の解析結果を CaseRecord として永続化
            // (個別矩形（基礎梁考慮）は OpenGroupSettlementWithBeamWindow 側で既に保存済み)
            if (loadingType != "個別矩形（基礎梁考慮）")
            {
                UpsertNonBeamAwareCaseRecord(loadingType, result.SettlementGridData);
                // 一般解析は VL ケース 1 件のみ保存するため、解析直後は表示荷重ケースを VL に切替えて
                // 結果コンタを表示する。setter 経由で ActiveCase 同期 + 再描画が走るが、既に VL を
                // 選択中の場合は setter が発火しないため、明示的にも同期しておく。
                SelectedLoadCaseName = "VL";
                SyncGroupSettlementActiveCaseFromLoadCase("VL");
            }

            ShowToast("スタインブレナーの近似式による解析が終了しました。");

            IsGroupPileGridDeformationVisible = true;
            IsGroupPileSettlementAnalysisDone = true;
            CaptureAnalysisResultSet();
            //IsAnalysisResultVisible = true;
            IsBubbleVisible = true;
            IsArrowVisible = true;
            DisplacementDiagramRatio = 0.3;
        }

        // 自動前方杭設定の処理メソッド
        private void AutoIsFrontPilesWindow_AutoIsFrontPileCompleted(object sender, AutoIsFrontEventArgs e)
        {
            double cosAlpha = Math.Cos((e.Angle * Math.PI / 180.0));

            for (int i = 0; i < 4; i++)
            {
                if (e.IsChecked[i])
                {
                    LoadCase loadCase = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i];

                    foreach (PileLayoutDataItem pileLayout0 in CurrentInputModel.PileLayoutItems)
                    {
                        // 前方杭かどうかを判定
                        pileLayout0.IsFrontPiles[i] = IsFrontPile(pileLayout0, loadCase, cosAlpha);
                    }
                }
            }
        }

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
