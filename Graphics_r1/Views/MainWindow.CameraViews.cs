using PileDesign.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace PileDesign.Views
{
    // カメラビュー関連のボタンハンドラとアニメーションを提供する partial。
    public partial class MainWindow
    {
        // 角度指定でビューをアニメーション切替（角度差に応じて所要時間を調整）
        private async Task AnimateToAnglesAsync(double targetTht, double targetPhi)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            // 前回アニメをキャンセル
            _viewAnimationCts?.Cancel();
            _viewAnimationCts = new System.Threading.CancellationTokenSource();

            // 回転量からアニメ時間を決定（小回転は速く、大回転は少し長め）
            double angDelta =
                Math.Max(Math.Abs(DeltaAngle(vm.CanvasThreeDView.Tht, targetTht)),
                         Math.Abs(targetPhi - vm.CanvasThreeDView.Phi));
            int duration = (int)Math.Clamp(150 + angDelta * 4.0, 220, 700);

            await AnimateViewToAsync(targetTht, Math.Clamp(targetPhi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle), duration, _viewAnimationCts.Token);
        }

        // XY平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonXYPlane_Clicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.CanvasThreeDView.IsPerspective = false;
            await AnimateToAnglesAsync(-90, 90);
        }

        // YZ平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonYZPlane_Clicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.CanvasThreeDView.IsPerspective = false;
            await AnimateToAnglesAsync(0, 0);
        }

        // XZ平面モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonXZPlane_Clicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.CanvasThreeDView.IsPerspective = false;
            await AnimateToAnglesAsync(-90, 0);
        }

        // 3D（アイソメ）モードに切り替えるボタンがクリックされた時のメソッド
        private async void ButtonIsometric_Clicked(object sender, RoutedEventArgs e)
        {
            await AnimateToAnglesAsync(-45, 45);
        }
    }
}
