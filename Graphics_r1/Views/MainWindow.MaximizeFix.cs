using System;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// RibbonWindow を最大化したときに上下左右の窓枠が画面外にはみ出す
    /// (タイトルバー上端が見えなくなる) WPF/WindowChrome 既知バグの修正。
    ///
    /// アプローチ: WindowState.Maximized は維持しつつ、root Grid に
    /// 上 8px (タイトルバー押し戻しに必要) / 左右下 2px (最低限の余白) の
    /// Margin を当てて可視コンテンツを内側に押し戻す。
    /// 実 OS の最大化挙動 (Maximize/Restore ボタン、Aero Snap、Win+矢印など)
    /// は完全に保持される。
    /// </summary>
    public partial class MainWindow
    {
        // WindowChrome の resize border (8px) で押し出される分の補正値。
        // 上のみタイトルバー対策で 8px、左右下は最低限の 3px。
        private static readonly Thickness MaximizedInset = new(left: 3, top: 8, right: 3, bottom: 3);
        private static readonly Thickness NormalInset = new(0);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyInset();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            ApplyInset();
        }

        private void ApplyInset()
        {
            if (RootGrid == null) return;
            RootGrid.Margin = this.WindowState == WindowState.Maximized
                ? MaximizedInset
                : NormalInset;
        }
    }
}
