using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    public partial class FundamentalViewModel : ObservableObject, ICloseable
    {
        private readonly UndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;

        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private string _refLevel;

        partial void OnRefLevelChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.RefLevel;

            _undoManager.PushAction(
                () => InputModel.FundamentalInput.RefLevel = oldValue,
                () => InputModel.FundamentalInput.RefLevel = value,
                "RefLevel変更"
            );
            InputModel.FundamentalInput.RefLevel = value;
        }

        [ObservableProperty]
        private double _referenceAltitude;

        // 確認ダイアログキャンセル時の revert 中は再入を抑制するフラグ
        private bool _suppressReferenceAltitudeConfirm;

        partial void OnReferenceAltitudeChanged(double value)
        {
            if (_suppressReferenceAltitudeConfirm) return;

            var oldValue = InputModel.FundamentalInput.ReferenceAltitude;
            if (oldValue == value) return;

            // 解析結果・杭要素分割があればユーザーに確認（キャンセルなら値を戻す）
            if (!_mainWindowViewModel.ConfirmResetAllForGeometryChange("Z=0 の標高の変更"))
            {
                _suppressReferenceAltitudeConfirm = true;
                try { ReferenceAltitude = oldValue; }
                finally { _suppressReferenceAltitudeConfirm = false; }
                return;
            }

            var delta = value - oldValue;

            _undoManager.PushAction(
                () => {
                    // Undo: ReferenceAltitude を戻し、地盤 Z を逆方向にシフト
                    InputModel.FundamentalInput.ReferenceAltitude = oldValue;
                    InputModel.ShiftGroundZByDelta(+delta);
                },
                () => {
                    // Redo: ReferenceAltitude を進め、地盤 Z をシフト
                    InputModel.FundamentalInput.ReferenceAltitude = value;
                    InputModel.ShiftGroundZByDelta(-delta);
                },
                "基準標高変更"
            );

            // 即時適用: 標高（絶対）は不変、ReferenceAltitude が変わった分だけ地盤 Z を逆方向にシフト
            InputModel.FundamentalInput.ReferenceAltitude = value;
            InputModel.ShiftGroundZByDelta(-delta);
        }

        [ObservableProperty]
        private string _projectNo;

        partial void OnProjectNoChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.ProjectNo;

            _undoManager.PushAction(
                () => InputModel.FundamentalInput.ProjectNo = oldValue,
                () => InputModel.FundamentalInput.ProjectNo = value,
                "ProjectNo変更"
            );
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
                "ProjectName変更"
            );
            InputModel.FundamentalInput.ProjectName = value;
        }

        [ObservableProperty]
        private string _seismicGrade;

        partial void OnSeismicGradeChanged(string value)
        {
            // 性能グレード変更は解析自体（変位・応力）には影響せず、
            // 検定（NM 曲線の選択：損傷限界 / 安全限界）の規則だけが変わるため、
            // 解析結果の削除は行わない（次回の検定で自動的に新しいルールが適用される）。
            var oldValue = InputModel.FundamentalInput.SeismicGrade;

            _undoManager.PushAction(
                () => InputModel.FundamentalInput.SeismicGrade = oldValue,
                () => InputModel.FundamentalInput.SeismicGrade = value,
                "SeismicGrade変更"
            );
            InputModel.FundamentalInput.SeismicGrade = value;
        }

        // バイリニア型コンクリートの引張側降伏応力度を 0 とする
        [ObservableProperty]
        private bool _ignoreConcreteTensileStrength;

        // バイリニア型コンクリートの圧縮側降伏応力度を 0.85·Gsi·Fc とする
        [ObservableProperty]
        private bool _useReducedConcreteCompressiveStrength;

        // 確認ダイアログのキャンセルで値を戻す間、再入を抑制するフラグ
        private bool _suppressConcreteOptionConfirm;

        partial void OnIgnoreConcreteTensileStrengthChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.IgnoreConcreteTensileStrength,
                v => InputModel.FundamentalInput.IgnoreConcreteTensileStrength = v,
                v => IgnoreConcreteTensileStrength = v,
                "コンクリート引張無視の変更");
        }

        partial void OnUseReducedConcreteCompressiveStrengthChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength,
                v => InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength = v,
                v => UseReducedConcreteCompressiveStrength = v,
                "コンクリート圧縮低減の変更");
        }

        // 鉄筋を 1.1×F で降伏する完全バイリニア型とする
        [ObservableProperty]
        private bool _rebarYieldAt11F;

        partial void OnRebarYieldAt11FChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.RebarYieldAt11F,
                v => InputModel.FundamentalInput.RebarYieldAt11F = v,
                v => RebarYieldAt11F = v,
                "鉄筋1.1F完全バイリニアの変更");
        }

        // 鋼管を 1.1×F で降伏する完全バイリニア型とする
        [ObservableProperty]
        private bool _steelPipeYieldAt11F;

        partial void OnSteelPipeYieldAt11FChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.SteelPipeYieldAt11F,
                v => InputModel.FundamentalInput.SteelPipeYieldAt11F = v,
                v => SteelPipeYieldAt11F = v,
                "鋼管1.1F完全バイリニアの変更");
        }

        // コンクリートのヤング係数 Ec の算定で ξ=1.0 とする
        [ObservableProperty]
        private bool _useUnitGsiForConcreteE;

        partial void OnUseUnitGsiForConcreteEChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseUnitGsiForConcreteE,
                v => InputModel.FundamentalInput.UseUnitGsiForConcreteE = v,
                v => UseUnitGsiForConcreteE = v,
                "コンクリートEc算定のξ=1.0オプション変更");
        }

        /// <summary>
        /// バイリニアコンクリート・オプションの変更を処理する共通ハンドラ。
        /// これらは M-φ（→ 非線形 FEM 解析）に影響するため、解析結果があれば確認のうえリセットする
        /// （杭要素分割＝メッシュは材料変更では不変のため保持）。確認キャンセル時はチェックを元に戻す。
        /// </summary>
        private void HandleConcreteOptionChanged(
            bool value, Func<bool> getter, Action<bool> setModel, Action<bool> setVm, string reason)
        {
            if (_suppressConcreteOptionConfirm) return;

            bool oldValue = getter();
            if (oldValue == value) return;

            if (!_mainWindowViewModel.CheckAndResetAnalysisResultsKeepingSplit(reason))
            {
                _suppressConcreteOptionConfirm = true;
                try { setVm(oldValue); }
                finally { _suppressConcreteOptionConfirm = false; }
                return;
            }

            _undoManager.PushAction(
                () => { setModel(oldValue); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                () => { setModel(value); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                reason);

            setModel(value);
            _mainWindowViewModel.ApplyConcreteModelOptions();
        }

        [ObservableProperty]
        private Point3D _point3D0;

        // Undo/Redoコマンド
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public ICommand CloseWindowCommand { get; }

        // RequestCloseイベントの実装
        public event EventHandler RequestClose;

        // 
        private FundamentalInput PrevFundamentalInput { get; set; }

        public string[] SeismicGradeOptions { get; } = ["S", "A"];

        // コンストラクタ
        public FundamentalViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // ShallowCopyメソッドを使用して値渡し
            PrevFundamentalInput = InputModel.FundamentalInput.ShallowCopy();

            RefLevel = InputModel.FundamentalInput.RefLevel;
            ReferenceAltitude = InputModel.FundamentalInput.ReferenceAltitude;
            ProjectNo = InputModel.FundamentalInput.ProjectNo;
            ProjectName = InputModel.FundamentalInput.ProjectName;
            Point3D0 = InputModel.FundamentalInput.Point3D0;
            SeismicGrade = InputModel.FundamentalInput.SeismicGrade;
            IgnoreConcreteTensileStrength = InputModel.FundamentalInput.IgnoreConcreteTensileStrength;
            UseReducedConcreteCompressiveStrength = InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength;
            RebarYieldAt11F = InputModel.FundamentalInput.RebarYieldAt11F;
            SteelPipeYieldAt11F = InputModel.FundamentalInput.SteelPipeYieldAt11F;
            UseUnitGsiForConcreteE = InputModel.FundamentalInput.UseUnitGsiForConcreteE;

            InputModel.FundamentalInput.PropertyChanged += FundamentalInput_PropertyChanged;


            // ToolkitRelayCommand (パラメータ無し Action / Func<bool> 対応)
            UndoCommand = new ToolkitRelayCommand(Undo, () => _undoManager.CanUndo);
            RedoCommand = new ToolkitRelayCommand(Redo, () => _undoManager.CanRedo);
            OkCommand = new ToolkitRelayCommand(OnOk);
            CancelCommand = new ToolkitRelayCommand(OnCancel);
            CloseWindowCommand = new ToolkitRelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        }

        private void Undo() => _undoManager.Undo();
        private void Redo() => _undoManager.Redo();


        private void OnOk()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancel()
        {
            // プロパティを元に戻す処理
            InputModel.FundamentalInput = PrevFundamentalInput.ShallowCopy();
            // 復元した基本設定の値で静的オプションを再同期（解析結果は既に確認のうえ削除済み）
            _mainWindowViewModel.ApplyConcreteModelOptions();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void FundamentalInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FundamentalInput.RefLevel):
                    RefLevel = InputModel.FundamentalInput.RefLevel;
                    break;
                case nameof(FundamentalInput.ReferenceAltitude):
                    ReferenceAltitude = InputModel.FundamentalInput.ReferenceAltitude;
                    break;
                case nameof(FundamentalInput.ProjectNo):
                    ProjectNo = InputModel.FundamentalInput.ProjectNo;
                    break;
                case nameof(FundamentalInput.ProjectName):
                    ProjectName = InputModel.FundamentalInput.ProjectName;
                    break;
                case nameof(FundamentalInput.SeismicGrade):
                    SeismicGrade = InputModel.FundamentalInput.SeismicGrade;
                    break;
                case nameof(FundamentalInput.IgnoreConcreteTensileStrength):
                    IgnoreConcreteTensileStrength = InputModel.FundamentalInput.IgnoreConcreteTensileStrength;
                    break;
                case nameof(FundamentalInput.UseReducedConcreteCompressiveStrength):
                    UseReducedConcreteCompressiveStrength = InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength;
                    break;
                case nameof(FundamentalInput.RebarYieldAt11F):
                    RebarYieldAt11F = InputModel.FundamentalInput.RebarYieldAt11F;
                    break;
                case nameof(FundamentalInput.SteelPipeYieldAt11F):
                    SteelPipeYieldAt11F = InputModel.FundamentalInput.SteelPipeYieldAt11F;
                    break;
                case nameof(FundamentalInput.UseUnitGsiForConcreteE):
                    UseUnitGsiForConcreteE = InputModel.FundamentalInput.UseUnitGsiForConcreteE;
                    break;
            }
        }
    }
}