using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PileDesign.Services;

namespace PileDesign.Common
{
    /// <summary>
    /// 列の幅と並びを覚える (2026-10-16 のレビュー)。
    ///
    /// <para>利用者が列の幅や並びを変えた表は、ウィンドウを閉じるときにそれを保存し、次に開いたときに戻す。
    /// 既定のままの表は何も書かない (既定の幅を後の版で直したときに、古い幅で上書きしないため)。
    /// 列見出しの右クリックで「列の幅と並びを元に戻す」。</para>
    ///
    /// <para>覚えるのは <b>x:Name の付いた表</b>だけ。名前は「ウィンドウの型.表の名前.列数」
    /// (同じ名前の表がテンプレートの中に列の違う形で 2 つある画面があるため、列数も名前に入れる)。
    /// 列を動的に組み立てる表は <see cref="RemembersColumnLayout"/> を False にする。</para>
    ///
    /// <para>保存先 (<see cref="ColumnLayoutStore"/>) はアプリの起動時にだけ立てる。テストは利用者の設定ファイルに触れない。</para>
    /// </summary>
    public partial class EnhancedDataGrid
    {
        /// <summary>列の幅と並びの保存先。App.OnStartup で立てる。null のあいだは覚えない。</summary>
        internal static LayoutService? ColumnLayoutStore { get; set; }

        public static readonly DependencyProperty RemembersColumnLayoutProperty = DependencyProperty.Register(
            nameof(RemembersColumnLayout), typeof(bool), typeof(EnhancedDataGrid), new PropertyMetadata(true));

        /// <summary>列の幅と並びを覚えるか (既定 True)。列を動的に組み立てる表は False にする。</summary>
        public bool RemembersColumnLayout
        {
            get => (bool)GetValue(RemembersColumnLayoutProperty);
            set => SetValue(RemembersColumnLayoutProperty, value);
        }

        private string? _columnLayoutKey;
        private List<(DataGridColumn Column, DataGridLength Width, int DisplayIndex)>? _defaultColumnLayout;

        /// <summary>覚えるときの名前 (覚えない表は null)。</summary>
        internal string? ColumnLayoutKey => _columnLayoutKey;

        /// <summary>覚えるときの名前。覚えない表は null。</summary>
        internal static string? ColumnLayoutKeyFor(Type? hostType, string? gridName, int columnCount, bool remembers)
        {
            if (!remembers || hostType == null || string.IsNullOrEmpty(gridName) || columnCount == 0) return null;
            // AvalonDock の浮動ウィンドウなど、アプリの外の型に載ったときは名前が定まらないので覚えない
            if (hostType.Namespace?.StartsWith("PileDesign", StringComparison.Ordinal) != true) return null;
            return $"{hostType.Name}.{gridName}.{columnCount}";
        }

        /// <summary>
        /// 初めて画面に載ったときに一度だけ: 既定の幅と並びを控え、覚えている幅と並びを当てる。
        /// 名前はこのときのウィンドウで決める (あとで浮動ウィンドウに引き出しても変えない)。
        /// </summary>
        internal void RestoreColumnLayoutOnce(Type? hostType)
        {
            if (_defaultColumnLayout != null) return;
            _defaultColumnLayout = Columns.Select(c => (c, c.Width, DisplayIndexOf(c))).ToList();
            _columnLayoutKey = ColumnLayoutKeyFor(hostType, Name, Columns.Count, RemembersColumnLayout);
            if (_columnLayoutKey != null) ColumnLayoutStore?.RestoreDataGridSettings(_columnLayoutKey, this);
        }

        /// <summary>幅か並びが既定と違う列があるか。</summary>
        internal bool IsColumnLayoutChanged() =>
            _defaultColumnLayout != null && _defaultColumnLayout.Any(d => !SameWidth(d.Column.Width, d.Width) || DisplayIndexOf(d.Column) != d.DisplayIndex);

        /// <summary>表示順。表が組み上がる前は DisplayIndex が -1 のことがあり、そのときは列の順番が表示順。</summary>
        private int DisplayIndexOf(DataGridColumn column) => column.DisplayIndex >= 0 ? column.DisplayIndex : Columns.IndexOf(column);

        private static bool SameWidth(DataGridLength a, DataGridLength b) => a.UnitType == b.UnitType && a.Value.Equals(b.Value);

        /// <summary>ウィンドウを閉じるとき: 既定と違えば保存し、既定に戻っていれば覚えていた分を消す。</summary>
        internal void SaveColumnLayout()
        {
            if (_columnLayoutKey == null || ColumnLayoutStore is not { } store || _defaultColumnLayout == null) return;
            // 列を足し引きした表は控えと合わないので触らない
            if (_defaultColumnLayout.Count != Columns.Count) return;
            if (IsColumnLayoutChanged()) store.SaveDataGridSettings(_columnLayoutKey, this);
            else store.RemoveDataGridSettings(_columnLayoutKey);
        }

        /// <summary>列の幅と並びを既定に戻す (列見出しの右クリック)。</summary>
        internal void ResetColumnLayout()
        {
            if (_defaultColumnLayout == null) return;
            // 目標の表示順の小さい列から当てる (当てるたびにほかの列が押し出されるため)
            foreach (var d in _defaultColumnLayout.OrderBy(d => d.DisplayIndex))
                if (d.DisplayIndex >= 0 && d.DisplayIndex < Columns.Count) d.Column.DisplayIndex = d.DisplayIndex;
            foreach (var d in _defaultColumnLayout) d.Column.Width = d.Width;
        }

        /// <summary>
        /// 列見出しの右クリックで「列の幅と並びを元に戻す」を出す。セルの右クリックは表のメニューのまま。
        /// </summary>
        protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseRightButtonUp(e);
            if (e.Handled || _columnLayoutKey == null) return;
            if (FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) == null) return;

            var reset = new MenuItem
            {
                Header = "列の幅と並びを元に戻す",
                IsEnabled = IsColumnLayoutChanged(),
                ToolTip = "列の幅や並びを変えると、次にこの画面を開いたときもその幅と並びで出します。既定に戻すときに使います。",
            };
            reset.Click += (_, _) => ResetColumnLayout();
            var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
            menu.Items.Add(reset);
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
