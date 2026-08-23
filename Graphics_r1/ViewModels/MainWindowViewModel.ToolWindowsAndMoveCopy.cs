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
    // MainWindowViewModel partial: 補助ウィンドウ起動（ヘルプ等の別UIスレッド表示）と杭・梁・節点の移動/コピー編集
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// STA バックグラウンドスレッド上でウィンドウを表示する。
        /// モーダルダイアログ中でも独立して入力を受け付けられる。
        /// 既に開いていれば対象スレッドで Activate するだけ。
        /// </summary>
        private static void OpenOnSeparateUiThread(SeparateUiWindowHost host, Func<Window> factory, string errorPrefix, Action<Window>? onActivate = null)
        {
            try
            {
                lock (host.Lock)
                {
                    if (host.Dispatcher != null && host.Window != null)
                    {
                        var existing = host.Window;
                        var dispatcher = host.Dispatcher;
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                existing.Activate();
                                onActivate?.Invoke(existing);
                            }
                            catch { /* ウィンドウが閉じ中の場合は無視 */ }
                        }));
                        return;
                    }

                    host.Thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            var window = factory();
                            host.Window = window;
                            host.Dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                            window.Closed += (_, _) =>
                            {
                                lock (host.Lock)
                                {
                                    host.Window = null;
                                    host.Dispatcher = null;
                                }
                                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                            };
                            window.Show();
                            System.Windows.Threading.Dispatcher.Run();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[{errorPrefix}Thread]");
                            lock (host.Lock)
                            {
                                host.Window = null;
                                host.Dispatcher = null;
                            }
                        }
                    });
                    host.Thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    host.Thread.IsBackground = true;
                    host.Thread.Start();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Window} ウィンドウの表示に失敗", errorPrefix);
                MessageService.Show(GuardMessages.WindowOpenFailed($"{errorPrefix}ウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public static void OpenHelpWindow()
        {
            OpenOnSeparateUiThread(_helpWindowHost,
                () => new HelpWindow(),
                "ヘルプ");
        }

        /// <summary>
        /// ヘルプウィンドウを指定 anchor または見出しタイトルへスクロールして開く (チャットからの遷移用)。
        /// 既に開いていれば NavigateTo で更新、未オープンなら新規作成。
        /// </summary>
        public static void OpenHelpWindowAt(string? anchor, string? scrollToTitle)
        {
            OpenOnSeparateUiThread(_helpWindowHost,
                () => new HelpWindow(anchor, scrollToTitle),
                "ヘルプ",
                existing =>
                {
                    if (existing is HelpWindow hw)
                        hw.NavigateTo(anchor, scrollToTitle);
                });
        }

        /// <summary>
        /// ヘルプの「クイックスタートガイド」を開く。
        /// ヘルプは 1 万行を超えるため、先頭から読ませずに入門の章へ直接送る。
        /// </summary>
        [RelayCommand]
        public static void OpenQuickStartHelp()
        {
            OpenHelpWindowAt("quickstart", "クイックスタートガイド");
        }

        /// <summary>
        /// バージョン情報。バージョンと更新履歴を利用者が自分で確かめられるようにする。
        /// </summary>
        [RelayCommand]
        public static void OpenAboutWindow()
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            var window = new AboutWindow();
            if (owner != null && !ReferenceEquals(owner, window)) window.Owner = owner;
            window.ShowDialog();
        }

        [RelayCommand]
        public static void OpenHelpChatWindow()
        {
            OpenOnSeparateUiThread(_helpChatWindowHost,
                () => new HelpChatWindow { Topmost = true },
                "ヘルプチャット");
        }

        // 設計例によるプログラムの検証ウィンドウ表示
        [RelayCommand]
        public static void OpenVerificationWindow()
        {
            OpenOnSeparateUiThread(_verificationWindowHost,
                () => new VerificationWindow { Topmost = true },
                "検証");
        }

        // 2026-05-19: PileDesign.Mcp (prototype) を廃止したため RegisterMcpServer コマンドと
        // FindClaudeDesktopConfigPath を削除。UI へのバインドも元々存在せず orphan だった。

        [RelayCommand]
        public void OnQuickHint()
        {
            IsQuickHintVisible = true;
        }

        [RelayCommand]
        public void OpenChangWindow()
        {
            // ChangViewModel に現在の InputModel を注入して作成
            var vm = new ChangViewModel(CurrentInputModel);
            //var win = new ChangWindow();
            var win = new ChangWindow { DataContext = vm };

            // イベントハンドラを設定
            if (vm is ICloseable closeableViewModel)
            {
                if (win.IsLoaded && win.IsVisible)
                    win.Close();
            }

            try
            {
                // ★ 重要: ダイアログを開く前に現在のフォーカスをクリア
                // これにより IME/TextStore が解放され、COMException を回避できる
                var focusedElement = Keyboard.FocusedElement;
                if (focusedElement is TextBox)
                {
                    // フォーカスを MainWindow に移動
                    Application.Current.MainWindow?.Focus();

                    // Dispatcher で UI を更新して IME を解放する時間を与える
                    Application.Current.Dispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => { }));
                }

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Chang 計算ウィンドウの表示に失敗");
                MessageService.Show(GuardMessages.WindowOpenFailed("Chang 計算ウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 変更後（以下の箇所で適用）
            UpdateWindowImmediate();
        }

        [RelayCommand]
        public static void OpenPileSectionLibraryWindow()
        {
            try
            {
                var win = new PileDesign.Views.PileLibraryWindow
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                PileDesign.Services.MessageService.Show($"杭ライブラリ表示に失敗しました: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public static void OpenShortcutKeysWindow()
        {
            // 別スレッド上のウィンドウに対して、メインウィンドウを Owner に設定することはできない。
            // WindowStartupLocation は CenterScreen で代替する。
            OpenOnSeparateUiThread(_shortcutKeysWindowHost,
                () => new PileDesign.Views.ShortcutKeysWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = false,
                    Topmost = true
                },
                "ショートカット一覧");
        }

        /// <summary>
        /// .pdj 拡張子を現在のユーザーで PileDesign に関連付ける。
        /// Portable (zip 配布) でも admin 権限不要。HKCU\Software\Classes に書込後、
        /// Windows の「既定のアプリ」設定ページを開いてユーザーに最終選択を促す。
        /// </summary>
        [RelayCommand]
        public void RegisterPdjAssociation()
        {
            var ok = PileDesign.Services.FileAssociationService.Register();
            if (!ok)
            {
                MessageService.Show(
                    "拡張子 .pdj の関連付け登録に失敗しました。詳細はログを参照してください。",
                    "関連付け", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var msg =
                ".pdj ファイルが PileDesign に関連付けられました (現在のユーザー)。\n\n" +
                "ダブルクリックで PileDesign を既定アプリとして開くには、\n" +
                "Windows の「既定のアプリ」設定で .pdj に対して PileDesign を選択してください。\n\n" +
                "今すぐ設定画面を開きますか？";

            var result = MessageService.Show(msg, "関連付け完了",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (result == MessageBoxResult.Yes)
            {
                PileDesign.Services.FileAssociationService.OpenDefaultAppsSettings();
            }
        }

        [RelayCommand]
        private async Task MoveCopyPiles()
        {
            try
            {
                // 選択節点がない場合は処理を中止してメッセージ表示
                // 杭配置・一般節点・梁要素のいずれかが選択されていればOK
                bool hasPileLayoutSelected = CurrentInputModel?.PileLayoutItems?.Any(p => p.IsSelected) ?? false;
                bool hasGeneralNodesSelected = CurrentInputModel?.InputNodes?.Any(n => n.Type == NodeType.General && n.IsSelected) ?? false;
                bool hasBeamsSelected = CurrentInputModel?.FoundationBeamInput?.Beams?.Any(b => b.IsSelected) ?? false;

                if (!hasPileLayoutSelected && !hasGeneralNodesSelected && !hasBeamsSelected)
                {
                    MessageService.Show("杭配置・一般節点・梁要素のいずれも選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Undoポイントを追加
                SaveUndoState();

                // MoveWindowをインスタンス化して表示
                MoveCopyWindow moveCopyWindow = new();

                var tcs = new TaskCompletionSource<bool>();
                bool operationExecuted = false;

                moveCopyWindow.MoveCopyCompleted += async (sender, e) =>
                {
                    operationExecuted = true;
                    await MoveCopyWindow_MoveCopyCompletedAsync(sender, e);
                    tcs.TrySetResult(true);
                };

                // ウィンドウが閉じられたら（キャンセル含む）TaskCompletionSourceを完了させる
                moveCopyWindow.Closed += (sender, e) =>
                {
                    tcs.TrySetResult(false);
                };

                moveCopyWindow.ShowDialog(); // モーダルダイアログとして表示

                // 操作が実行された場合のみ待機と更新を行う
                if (operationExecuted)
                {
                    // ★ 待機カーソルを表示
                    Mouse.OverrideCursor = Cursors.Wait;
                    try
                    {
                        await tcs.Task; // 非同期に完了を待つ

                        // コレクション自体の変更通知
                        OnPropertyChanged(nameof(GroupPileSettlementXMin));
                        OnPropertyChanged(nameof(GroupPileSettlementXMax));
                        OnPropertyChanged(nameof(GroupPileSettlementYMin));
                        OnPropertyChanged(nameof(GroupPileSettlementYMax));

                        // 変更: デバウンス付きで更新
                        RequestUpdateWindow();
                    }
                    finally
                    {
                        // ★ カーソルを元に戻す
                        Mouse.OverrideCursor = null;
                    }
                }
            }
            catch (Exception ex)
            {
                // 例外発生時もカーソルをリセット
                Mouse.OverrideCursor = null;
                MessageService.Show($"杭の移動・複製中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveCopyWindow_MoveCopyCompletedAsync(object sender, MoveCopyEventArgs e)
        {
            // 新しいウィンドウでの操作の結果を処理する
            if (e.IsMove)
            {
                MoveNodes(e.DX, e.DY, e.DZ, e.IsInputNodesIncluded, e.IsPileLayoutIncluded);
                if (e.IsBeamsIncluded) MoveBeams(e.DX, e.DY, e.DZ, EditDistanceThreshold);
            }
            else if (e.IsCopy)
            {
                await CopyNodesAsync(e.DX, e.DY, e.DZ, e.RepetitionNumber, e.IsInputNodesIncluded, e.IsPileLayoutIncluded);
                if (e.IsBeamsIncluded) CopyBeams(e.DX, e.DY, e.DZ, e.RepetitionNumber, EditDistanceThreshold);
            }
        }

        // ───────── 梁要素の移動・コピー (端点ノード解決ロジック付き) ─────────
        // 端点解決の優先順位 (ResolveOrCreateNodeAt):
        //   1. 杭頭節点 (PileLayout の杭頭+ΔZc 位置) との距離 ≤ tolerance → そこを参照
        //   2. 一般節点 (InputNode, Type=General) との距離 ≤ tolerance → そこを参照
        //   3. どちらも見つからなければ新規 InputNode を destination 位置に生成し、それを参照
        //
        // 移動 (Move): 元の梁の NodeI/J 参照を destination の参照に付け替える。
        //   元の端点ノード (FoundationNode / InputNode) はそのまま残す (ユーザー仕様)。
        //   杭頭節点は移動しない (杭自体は元位置のまま)。
        // コピー (Copy): 同じロジックで新規 FoundationBeam を生成して追加。

        private void MoveBeams(double dX, double dY, double dZ, double tolerance)
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;
            var selectedBeams = fb.Beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0) return;

            foreach (var beam in selectedBeams)
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                var destI = new Point3D { X = posI.Value.X + dX, Y = posI.Value.Y + dY, Z = posI.Value.Z + dZ };
                var destJ = new Point3D { X = posJ.Value.X + dX, Y = posJ.Value.Y + dY, Z = posJ.Value.Z + dZ };
                var (typeI, idI) = ResolveOrCreateNodeAt(destI, tolerance);
                var (typeJ, idJ) = ResolveOrCreateNodeAt(destJ, tolerance);

                beam.NodeI_Type = typeI;
                beam.NodeI_Id = idI;
                beam.NodeJ_Type = typeJ;
                beam.NodeJ_Id = idJ;
            }
        }

        private void CopyBeams(double dX, double dY, double dZ, int repetitionNumber, double tolerance)
        {
            var fb = CurrentInputModel?.FoundationBeamInput;
            if (fb?.Beams == null) return;
            var selectedBeams = fb.Beams.Where(b => b.IsSelected).ToList();
            if (selectedBeams.Count == 0) return;

            foreach (var beam in selectedBeams)
            {
                var posI = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
                var posJ = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
                if (posI == null || posJ == null) continue;

                for (int rep = 1; rep <= repetitionNumber; rep++)
                {
                    var destI = new Point3D { X = posI.Value.X + dX * rep, Y = posI.Value.Y + dY * rep, Z = posI.Value.Z + dZ * rep };
                    var destJ = new Point3D { X = posJ.Value.X + dX * rep, Y = posJ.Value.Y + dY * rep, Z = posJ.Value.Z + dZ * rep };
                    var (typeI, idI) = ResolveOrCreateNodeAt(destI, tolerance);
                    var (typeJ, idJ) = ResolveOrCreateNodeAt(destJ, tolerance);

                    var newBeam = new FoundationBeam
                    {
                        // No プロパティ廃止 (位置 = ID)
                        NodeI_Type = typeI,
                        NodeI_Id = idI,
                        NodeJ_Type = typeJ,
                        NodeJ_Id = idJ,
                        MaterialNo = beam.MaterialNo,
                        SectionNo = beam.SectionNo,
                        SectionName = beam.SectionName,
                        Width = beam.Width,
                        Height = beam.Height,
                        YoungModulus = beam.YoungModulus,
                        ShearModulus = beam.ShearModulus,
                        AngleBeta = beam.AngleBeta,
                        IsVisible = beam.IsVisible,
                    };
                    fb.Beams.Add(newBeam);
                }
            }
        }

        /// <summary>
        /// 節点参照タイプ + Id から、その節点の実際の取付位置 (3D 座標) を返す。
        /// PileLayout: 接合節点 (X,Y,Z) — v2 セマンティクスでは pile.Z は接合節点 Z
        /// GeneralNode: InputNode の Point3D
        /// FoundationNode: FoundationNode の Point3D
        /// </summary>
        private Point3D? GetNodeAttachPosition(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.PileLayout:
                {
                    var pile = CurrentInputModel?.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    if (pile == null) return null;
                    return new Point3D { X = pile.X, Y = pile.Y, Z = pile.Z };
                }
                case NodeReferenceType.GeneralNode:
                {
                    var node = CurrentInputModel?.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return node?.Point3D;
                }
                case NodeReferenceType.FoundationNode:
                {
                    var fn = CurrentInputModel?.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fn != null ? new Point3D { X = fn.X, Y = fn.Y, Z = fn.Z } : null;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// 梁要素の端点候補となるノードを (Type + Guid + Position) のタプルで列挙する。
        /// 列挙順は ResolveOrCreateNodeAt の優先順位に対応:
        ///   1. PileLayout (接合節点位置 — v2 セマンティクスでは pile.Z 自体)
        ///   2. GeneralNode (InputNode, Type=General)
        ///   3. FoundationNode (基礎梁節点) ※ includeFoundationNodes=true のときのみ
        /// </summary>
        private IEnumerable<(NodeReferenceType Type, Guid Id, Point3D Pos)> EnumerateAllCandidateNodes(
            bool includeFoundationNodes = true)
        {
            if (CurrentInputModel?.PileLayoutItems != null)
            {
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                {
                    yield return (NodeReferenceType.PileLayout, pile.UniqueId,
                        new Point3D(pile.X, pile.Y, pile.Z));
                }
            }
            if (CurrentInputModel?.InputNodes != null)
            {
                foreach (var n in CurrentInputModel.InputNodes)
                {
                    if (n.Type != NodeType.General) continue;
                    yield return (NodeReferenceType.GeneralNode, n.UniqueId, n.Point3D);
                }
            }
            if (includeFoundationNodes && CurrentInputModel?.FoundationBeamInput?.Nodes != null)
            {
                foreach (var fn in CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    yield return (NodeReferenceType.FoundationNode, fn.Id,
                        new Point3D(fn.X, fn.Y, fn.Z));
                }
            }
        }

        /// <summary>
        /// 指定位置にある既存節点を解決、無ければ新規 InputNode (一般節点) を生成して返す。
        /// 優先順位: PileLayout (杭頭+ΔZc) → GeneralNode → 新規 InputNode 生成。
        /// FoundationNode は対象外 (snap 先として基礎梁節点を選ぶのは利用シーンとして想定外のため)。
        /// </summary>
        private (NodeReferenceType type, Guid id) ResolveOrCreateNodeAt(Point3D pos, double tolerance)
        {
            foreach (var (type, id, candPos) in EnumerateAllCandidateNodes(includeFoundationNodes: false))
            {
                if (Distance3D(candPos.X, candPos.Y, candPos.Z, pos.X, pos.Y, pos.Z) <= tolerance)
                    return (type, id);
            }
            // 該当なし → 新規 InputNode を生成
            var newNode = new InputNode
            {
                No = (CurrentInputModel?.InputNodes?.Count ?? 0) + 1,
                Type = NodeType.General,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                IsVisible = true
            };
            if (CurrentInputModel != null)
            {
                CurrentInputModel.InputNodes ??= [];
                CurrentInputModel.InputNodes.Add(newNode);
            }
            return (NodeReferenceType.GeneralNode, newNode.UniqueId);
        }

        private static double Distance3D(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            double dx = x1 - x2, dy = y1 - y2, dz = z1 - z2;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private async Task CopyNodesAsync(double dX, double dY, double dZ, int repetitionNumber, bool isInputNodesIncluded, bool isPileLayoutIncluded)
        {
            // 変更を行う前に、選択されたアイテムのリストを作成
            var selectedItems = isPileLayoutIncluded
                ? CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList()
                : new List<PileLayoutDataItem>();
            var selectedInputNodes = isInputNodesIncluded
                ? (CurrentInputModel.InputNodes?.Where(n => n.IsSelected).ToList() ?? new List<InputNode>())
                : new List<InputNode>();
            int totalCount = (selectedItems.Count + selectedInputNodes.Count) * repetitionNumber;

            // ★ 大量コピー時は待機カーソルを表示
            bool showWaitCursor = totalCount > 10;
            if (showWaitCursor)
                Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // サービスを使ってコピー実行
                var combined = _pileLayoutService.CopySelectedPiles(
                    CurrentInputModel.PileLayoutItems,
                    dX,
                    dY,
                    dZ,
                    repetitionNumber,
                    item => item.SetMainWindowViewModel(this));

                // InputNodes（一般節点）のコピー
                var newInputNodes = new List<InputNode>();
                foreach (var selectedNode in selectedInputNodes)
                {
                    for (int i = 0; i < repetitionNumber; i++)
                    {
                        var newNode = new InputNode
                        {
                            No = CurrentInputModel.InputNodes.Count + newInputNodes.Count + 1,
                            Type = selectedNode.Type,
                            X = selectedNode.X + dX * (i + 1),
                            Y = selectedNode.Y + dY * (i + 1),
                            Z = selectedNode.Z + dZ * (i + 1),
                            LinkedPileNo = selectedNode.LinkedPileNo,
                            IsVisible = selectedNode.IsVisible
                        };
                        newInputNodes.Add(newNode);
                    }
                }

                // ★ UIスレッドで一括置換（CollectionChangedを1回だけ発火）
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // コレクション全体を置換（CollectionChangedは1回のみ）
                    CurrentInputModel.PileLayoutItems = combined;
                    CurrentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
                    CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
                    OnPropertyChanged(nameof(PileCountText));

                    // InputNodes を追加
                    foreach (var newNode in newInputNodes)
                    {
                        CurrentInputModel.InputNodes.Add(newNode);
                    }

                    // SoilPiles を1回だけ再生成
                    if (!IsElementSplit)
                        RequestGenerateSoilPiles();

                    UpdatePileLayoutNo();
                    NotifyUIChanged();
                });
            }
            finally
            {
                if (showWaitCursor)
                    Mouse.OverrideCursor = null;
            }
        }

        // 移動操作を行う
        private void MoveNodes(double dX, double dY, double dZ, bool isInputNodesIncluded, bool isPileLayoutIncluded)
        {
            // 杭配置の移動
            if (isPileLayoutIncluded)
            {
                _pileLayoutService.MoveSelectedPiles(CurrentInputModel.PileLayoutItems, dX, dY, dZ);
            }

            // InputNodes（一般節点）の移動
            if (isInputNodesIncluded)
            {
                var selectedInputNodes = CurrentInputModel.InputNodes?.Where(n => n.IsSelected).ToList();
                if (selectedInputNodes != null && selectedInputNodes.Count > 0)
                {
                    foreach (var node in selectedInputNodes)
                    {
                        node.X += dX;
                        node.Y += dY;
                        node.Z += dZ;
                    }
                }
            }
        }

        // コピーを作成して操作を行う
        private void CopyNodes(double dX, double dY, int repetitionNumber)
        {
            CurrentInputModel.PileLayoutItems = _pileLayoutService.CopySelectedPiles(
                CurrentInputModel.PileLayoutItems,
                dX,
                dY,
                0,
                repetitionNumber,
                item => item.SetMainWindowViewModel(this));
            CurrentInputModel.PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
            CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;
            OnPropertyChanged(nameof(PileCountText));

            UpdatePileLayoutNo();
        }

        // 杭配置の編集・追加コマンド
        [RelayCommand]
        private void EditAddPiles()
        {
            var editPileLayoutWindow = new EditPileLayoutWindow(this);

            editPileLayoutWindow.EditPileLayoutCompleted += EditPileLayoutWindow_EditPileLayoutCompleted;

            editPileLayoutWindow.ShowDialog();
            // 変更: ダイアログ後は即時実行
            UpdateWindowImmediate();
        }

        private void EditPileLayoutWindow_EditPileLayoutCompleted(object sender, EditPileLayoutEventArgs e)
        {
            var options = new PileLayoutService.BulkEditOptions
            {
                ApplyPileBodyNo = e.IsApplicablePileRefNo,
                PileBodyNo = e.SelectedPileRefNo,

                ApplyGroundNo = e.IsApplicableGroundRefNo,
                GroundNo = e.SelectedGroundRefNo,

                ApplyPileTopLevel = e.IsApplicablePileTopLevel,
                IsAddPileTopLevel = e.IsAddPileTopLevel,
                PileTopLevel = e.PileTopLevel,

                ApplyFoundationBeamDeltaZc = e.IsApplicableFoundationBeamDeltaZc,
                IsAddFoundationBeamDeltaZc = e.IsAddFoundationBeamDeltaZc,
                FoundationBeamDeltaZc = e.FoundationBeamDeltaZc,

                ApplyPileGroupFactor = e.IsApplicablePileGroupFactor,
                IsAddPileGroupFactor = e.IsAddPileGroupFactor,
                PileGroupFactor = e.PileGroupFactor,

                ApplyAxialForceVL = e.IsApplicableVL,
                IsAddAxialForceVL = e.IsAddVL,
                AxialForceVL = e.VL,

                ApplyAxialForceVLAdditional = e.IsApplicableVLadd,
                IsAddAxialForceVLAdditional = e.IsAddVLadd,
                AxialForceVLAdditional = e.VLadd,

                ApplyLevel1 =
                [
                    e.IsApplicableE1_1, e.IsApplicableE1_2, e.IsApplicableE1_3, e.IsApplicableE1_4
                ],
                IsAddLevel1 =
                [
                    e.IsAddE1_1, e.IsAddE1_2, e.IsAddE1_3, e.IsAddE1_4
                ],
                Level1Values =
                [
                    e.E1_1, e.E1_2, e.E1_3, e.E1_4
                ],

                ApplyLevel2 =
                [
                    e.IsApplicableE2_1, e.IsApplicableE2_2, e.IsApplicableE2_3, e.IsApplicableE2_4
                ],
                IsAddLevel2 =
                [
                    e.IsAddE2_1, e.IsAddE2_2, e.IsAddE2_3, e.IsAddE2_4
                ],
                Level2Values =
                [
                    e.E2_1, e.E2_2, e.E2_3, e.E2_4
                ]
            };

            _pileLayoutService.BulkEditSelectedPiles(CurrentInputModel.PileLayoutItems, options);

            // IsFrontPile フラグの処理
            var selectedItems = CurrentInputModel.PileLayoutItems.Where(p => p.IsSelected).ToList();
            ApplyIsFrontPileFlags(
                selectedItems,
                [e.IsApplicableIsFrontPile1, e.IsApplicableIsFrontPile2, e.IsApplicableIsFrontPile3, e.IsApplicableIsFrontPile4],
                [e.IsFrontPile1, e.IsFrontPile2, e.IsFrontPile3, e.IsFrontPile4]);
        }

    }
}
