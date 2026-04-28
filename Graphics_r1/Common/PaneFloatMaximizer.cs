using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Serilog;
namespace PileDesign.Common
{
    /// <summary>
    /// AvalonDock の LayoutDocumentPane 配下にある LayoutAnchorable について
    /// 「ボタン押下でアンドック+最大化 / もう一度押すかフローティング窓を最大化解除/閉じると元の位置に再ドック」
    /// する挙動をボタンに紐付ける共通ヘルパー。
    /// </summary>
    public sealed class PaneFloatMaximizer
    {
        private readonly DockingManager _dockingManager;
        private readonly List<TargetEntry> _targets = new();
        private readonly Dictionary<LayoutAnchorable, OriginalLocation> _originalPanes = new();

        public PaneFloatMaximizer(DockingManager dockingManager)
        {
            _dockingManager = dockingManager ?? throw new ArgumentNullException(nameof(dockingManager));
            _dockingManager.LayoutChanged += (_, __) => RefreshLabelsLater();
        }

        /// <summary>
        /// 1 つの LayoutAnchorable とボタンの組を登録する。
        /// </summary>
        /// <param name="tag">識別用タグ (任意)</param>
        /// <param name="tab">対象の LayoutAnchorable</param>
        /// <param name="button">押下するボタン</param>
        /// <param name="label">通常時のボタン文言 (フロート中は「ドックに戻す」固定)</param>
        public void Register(string tag, LayoutAnchorable tab, Button button, string label)
        {
            if (tab == null || button == null) return;
            _targets.Add(new TargetEntry(tag, tab, button, label));
            button.Click += (s, e) => OnClick(tab);
        }

        private void OnClick(LayoutAnchorable tab)
        {
            if (tab.IsFloating)
            {
                DockBack(tab);
                RefreshLabelsLater();
                return;
            }

            CaptureOriginalLocation(tab);
            tab.Float();
            tab.Dispatcher.BeginInvoke(new Action(() => MaximizeFloatingHost(tab)),
                DispatcherPriority.ApplicationIdle);
            UpdateLabels();
        }

        private void CaptureOriginalLocation(LayoutAnchorable tab)
        {
            if (tab.Parent is LayoutDocumentPane origPane)
            {
                var origGroup = origPane.Parent as LayoutDocumentPaneGroup;
                var rootPanel = origGroup?.Parent as LayoutPanel;
                int groupIdx = -1;
                if (rootPanel != null && origGroup != null)
                {
                    for (int i = 0; i < rootPanel.Children.Count; i++)
                    {
                        if (ReferenceEquals(rootPanel.Children[i], origGroup))
                        {
                            groupIdx = i;
                            break;
                        }
                    }
                }
                int paneIdx = origGroup?.Children.IndexOf(origPane) ?? -1;
                int tabIdx = origPane.Children.IndexOf(tab);
                _originalPanes[tab] = new OriginalLocation(origPane, origGroup, rootPanel, groupIdx, paneIdx, tabIdx);
                Debug.WriteLine($"[PaneFloatMaximizer] Captured: tab={tab.Title} groupIdx={groupIdx} paneIdx={paneIdx} tabIdx={tabIdx}");
            }
        }

        private void MaximizeFloatingHost(LayoutAnchorable tab)
        {
            ILayoutElement? cur = tab;
            while (cur != null && cur is not LayoutAnchorableFloatingWindow)
            {
                cur = cur.Parent;
            }
            if (cur is not LayoutAnchorableFloatingWindow fwModel) return;

            foreach (var fw in _dockingManager.FloatingWindows)
            {
                if (fw is LayoutAnchorableFloatingWindowControl ctrl &&
                    ReferenceEquals(ctrl.Model, fwModel))
                {
                    ctrl.WindowState = WindowState.Maximized;
                    HookAutoDockBack(ctrl, tab);
                    return;
                }
            }
        }

        private void HookAutoDockBack(LayoutAnchorableFloatingWindowControl ctrl, LayoutAnchorable tab)
        {
            EventHandler? stateChanged = null;
            System.ComponentModel.CancelEventHandler? closing = null;

            void Unsubscribe()
            {
                if (stateChanged != null) ctrl.StateChanged -= stateChanged;
                if (closing != null) ctrl.Closing -= closing;
            }

            stateChanged = (s, e) =>
            {
                if (ctrl.WindowState != WindowState.Maximized)
                {
                    Unsubscribe();
                    if (tab.IsFloating)
                    {
                        ctrl.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            DockBack(tab);
                            RefreshLabelsLater();
                        }), DispatcherPriority.Background);
                    }
                }
            };
            closing = (s, e) =>
            {
                Unsubscribe();
                if (tab.IsFloating)
                {
                    e.Cancel = true;
                    ctrl.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DockBack(tab);
                        RefreshLabelsLater();
                    }), DispatcherPriority.Background);
                }
            };
            ctrl.StateChanged += stateChanged;
            ctrl.Closing += closing;
        }

        private void DockBack(LayoutAnchorable tab)
        {
            try
            {
                if (_originalPanes.TryGetValue(tab, out var info))
                {
                    Debug.WriteLine($"[PaneFloatMaximizer] DockBack: pane.Root={info.Pane?.Root != null} group.Root={info.ParentGroup?.Root != null} root.Root={info.RootPanel?.Root != null}");

                    // Step 1: Group が GC されている場合 → RootPanel に再挿入
                    if (info.ParentGroup != null && info.ParentGroup.Root == null
                        && info.RootPanel != null && info.RootPanel.Root != null
                        && info.GroupIndexInRoot >= 0)
                    {
                        int idx = Math.Max(0, Math.Min(info.GroupIndexInRoot, info.RootPanel.Children.Count));
                        info.RootPanel.Children.Insert(idx, info.ParentGroup);
                        Debug.WriteLine($"[PaneFloatMaximizer] Re-attached group at index {idx}");
                    }

                    // Step 2: Pane が GC されている場合 → Group に再挿入
                    if (info.Pane != null && info.Pane.Root == null
                        && info.ParentGroup != null && info.ParentGroup.Root != null
                        && info.PaneIndexInGroup >= 0)
                    {
                        int idx = Math.Max(0, Math.Min(info.PaneIndexInGroup, info.ParentGroup.Children.Count));
                        info.ParentGroup.Children.Insert(idx, info.Pane);
                        Debug.WriteLine($"[PaneFloatMaximizer] Re-attached pane at index {idx}");
                    }

                    // Step 3: Tab を Pane に挿入
                    if (info.Pane != null && info.Pane.Root != null)
                    {
                        if (tab.Parent is ILayoutContainer currentParent)
                        {
                            currentParent.RemoveChild(tab);
                        }
                        int tabIdx = Math.Max(0, Math.Min(info.TabIndexInPane, info.Pane.Children.Count));
                        info.Pane.Children.Insert(tabIdx, tab);
                        tab.IsActive = true;
                        _originalPanes.Remove(tab);
                        Debug.WriteLine($"[PaneFloatMaximizer] Inserted tab at index {tabIdx}");
                        return;
                    }
                }

                Debug.WriteLine($"[PaneFloatMaximizer] Fallback DockAsDocument");
                tab.DockAsDocument();
                _originalPanes.Remove(tab);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PaneFloatMaximizer] DockBack failed: {ex.GetType().Name}: {ex.Message}");
                try { tab.DockAsDocument(); } catch (Exception ex2) { Log.Warning(ex2, "[PaneFloatMaximizer] DockAsDocument fallback failed"); }
            }
        }

        private void RefreshLabelsLater()
        {
            _dockingManager.Dispatcher.BeginInvoke(new Action(UpdateLabels),
                DispatcherPriority.ContextIdle);
        }

        private void UpdateLabels()
        {
            foreach (var t in _targets)
            {
                if (t.Tab == null || t.Button == null) continue;
                t.Button.Content = t.Tab.IsFloating ? "ドックに戻す" : t.Label;
            }
        }

        private record OriginalLocation(
            LayoutDocumentPane Pane,
            LayoutDocumentPaneGroup? ParentGroup,
            LayoutPanel? RootPanel,
            int GroupIndexInRoot,
            int PaneIndexInGroup,
            int TabIndexInPane);

        private record TargetEntry(string Tag, LayoutAnchorable Tab, Button Button, string Label);
    }
}
