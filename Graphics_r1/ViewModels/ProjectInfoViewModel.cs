using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using System;
using System.ComponentModel;
using System.Windows.Input;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 「プロジェクト情報」ウィンドウ。
    ///
    /// プロジェクト番号・名称と、計算書の標高表記に使う 標高記号 / Z=0 の標高 を扱う。
    /// もとは基本設定ウィンドウの先頭にあったが、材料モデル化の選択とは性質が違い
    /// (最初に一度入れたら以後ほとんど触らない)、材料オプションへ到達するまでの
    /// 邪魔になっていたので別ウィンドウに分けた。
    ///
    /// 保存先は <see cref="FundamentalInput"/> のままで、基本設定と同じインスタンスを共有する。
    /// </summary>
    public partial class ProjectInfoViewModel : ObservableObject, ICloseable
    {
        private readonly UndoManager _undoManager = new();
        private readonly MainWindowViewModel _mainWindowViewModel;

        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private string _projectNo;

        partial void OnProjectNoChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.ProjectNo;
            _undoManager.PushAction(
                () => InputModel.FundamentalInput.ProjectNo = oldValue,
                () => InputModel.FundamentalInput.ProjectNo = value,
                "プロジェクト番号の変更");
            InputModel.FundamentalInput.ProjectNo = value;
        }

        [ObservableProperty]
        private string _projectName;

        partial void OnProjectNameChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.ProjectName;
            _undoManager.PushAction(
                () => InputModel.FundamentalInput.ProjectName = oldValue,
                () => InputModel.FundamentalInput.ProjectName = value,
                "プロジェクト名の変更");
            InputModel.FundamentalInput.ProjectName = value;
        }

        [ObservableProperty]
        private string _refLevel;

        partial void OnRefLevelChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.RefLevel;
            _undoManager.PushAction(
                () => InputModel.FundamentalInput.RefLevel = oldValue,
                () => InputModel.FundamentalInput.RefLevel = value,
                "標高記号の変更");
            InputModel.FundamentalInput.RefLevel = value;
        }

        [ObservableProperty]
        private double _referenceAltitude;

        // 確認ダイアログのキャンセルで値を戻す間、再入を抑制するフラグ
        private bool _suppressReferenceAltitudeConfirm;

        /// <summary>
        /// Z=0 の標高を変えると、地盤の標高（絶対）を保ったまま Z 座標が全体にずれる。
        /// ジオメトリが動くので解析結果と杭要素分割を捨てる必要があり、確認を挟む。
        /// </summary>
        partial void OnReferenceAltitudeChanged(double value)
        {
            if (_suppressReferenceAltitudeConfirm) return;

            var oldValue = InputModel.FundamentalInput.ReferenceAltitude;
            if (oldValue == value) return;

            if (!_mainWindowViewModel.ConfirmResetAllForGeometryChange("Z=0 の標高の変更"))
            {
                _suppressReferenceAltitudeConfirm = true;
                try { ReferenceAltitude = oldValue; }
                finally { _suppressReferenceAltitudeConfirm = false; }
                return;
            }

            var delta = value - oldValue;

            _undoManager.PushAction(
                () =>
                {
                    // Undo: ReferenceAltitude を戻し、地盤 Z を逆方向にシフト
                    InputModel.FundamentalInput.ReferenceAltitude = oldValue;
                    InputModel.ShiftGroundZByDelta(+delta);
                },
                () =>
                {
                    // Redo: ReferenceAltitude を進め、地盤 Z をシフト
                    InputModel.FundamentalInput.ReferenceAltitude = value;
                    InputModel.ShiftGroundZByDelta(-delta);
                },
                "Z=0 の標高の変更");

            // 即時適用: 標高（絶対）は不変、ReferenceAltitude が変わった分だけ地盤 Z を逆方向にシフト
            InputModel.FundamentalInput.ReferenceAltitude = value;
            InputModel.ShiftGroundZByDelta(-delta);
        }

        // Undo/Redo コマンド
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }
        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public ICommand CloseWindowCommand { get; }

        public event EventHandler RequestClose;

        /// <summary>キャンセル時に戻すための、開いた時点の値。</summary>
        private FundamentalInput PrevFundamentalInput { get; set; }

        public ProjectInfoViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            PrevFundamentalInput = InputModel.FundamentalInput.ShallowCopy();

            ProjectNo = InputModel.FundamentalInput.ProjectNo;
            ProjectName = InputModel.FundamentalInput.ProjectName;
            RefLevel = InputModel.FundamentalInput.RefLevel;
            ReferenceAltitude = InputModel.FundamentalInput.ReferenceAltitude;

            InputModel.FundamentalInput.PropertyChanged += FundamentalInput_PropertyChanged;

            UndoCommand = new ToolkitRelayCommand(() => _undoManager.Undo(), () => _undoManager.CanUndo);
            RedoCommand = new ToolkitRelayCommand(() => _undoManager.Redo(), () => _undoManager.CanRedo);
            OkCommand = new ToolkitRelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
            CancelCommand = new ToolkitRelayCommand(OnCancel);
            CloseWindowCommand = new ToolkitRelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        }

        private void OnCancel()
        {
            InputModel.FundamentalInput = PrevFundamentalInput.ShallowCopy();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // モデル側が外から書き戻されたとき（Undo など）に画面へ追随させる
        private void FundamentalInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FundamentalInput.ProjectNo):
                    ProjectNo = InputModel.FundamentalInput.ProjectNo;
                    break;
                case nameof(FundamentalInput.ProjectName):
                    ProjectName = InputModel.FundamentalInput.ProjectName;
                    break;
                case nameof(FundamentalInput.RefLevel):
                    RefLevel = InputModel.FundamentalInput.RefLevel;
                    break;
                case nameof(FundamentalInput.ReferenceAltitude):
                    ReferenceAltitude = InputModel.FundamentalInput.ReferenceAltitude;
                    break;
            }
        }
    }
}
