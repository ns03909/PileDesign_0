using CommunityToolkit.Mvvm.Input;

namespace PileDesign.ViewModels
{
    // カメラビュー切替コマンド（ズームフィット、XY/YZ/XZ平面、等角）を提供する partial。
    public partial class MainWindowViewModel
    {
        // ズームフィット
        [RelayCommand]
        private void ZoomFit()
        {
            ZoomFitAction?.Invoke();
        }

        // XY平面
        [RelayCommand]
        private void ViewXYPlane()
        {
            // θ=-90, φ=90、透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-90, 90);
            else
            {
                CanvasThreeDView.Tht = -90;
                CanvasThreeDView.Phi = 90;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // YZ平面
        [RelayCommand]
        private void ViewYZPlane()
        {
            // 透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(0, 0);
            else
            {
                CanvasThreeDView.Tht = 0;
                CanvasThreeDView.Phi = 0;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        [RelayCommand]
        private void ViewXZPlane()
        {
            // 透視投影を無効化
            CanvasThreeDView.IsPerspective = false;
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-90, 0);
            else
            {
                CanvasThreeDView.Tht = -90;
                CanvasThreeDView.Phi = 0;
                UpdateCanvas3DAction?.Invoke();
            }
        }

        [RelayCommand]
        private void ViewIsometric()
        {
            if (AnimateViewAnglesAction != null) AnimateViewAnglesAction(-45, 45);
            else
            {
                CanvasThreeDView.Tht = -45;
                CanvasThreeDView.Phi = 45;
                UpdateCanvas3DAction?.Invoke();
            }
        }
    }
}
