using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using static PileDesign.ViewModels.MainWindowViewModel;

namespace PileDesign.ViewModels
{
    public partial class FundamentalViewModel : ObservableObject, ICloseable
    {
        private readonly LoadCaseUndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;

        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private string _refLevel;

        partial void OnRefLevelChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.RefLevel;

            _undoManager.Execute(
                () => InputModel.FundamentalInput.RefLevel = value,
                () => InputModel.FundamentalInput.RefLevel = oldValue
            );
            InputModel.FundamentalInput.RefLevel = value;
        }

        [ObservableProperty]
        private string _projectNo;

        partial void OnProjectNoChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.ProjectNo;

            _undoManager.Execute(
                () => InputModel.FundamentalInput.ProjectNo = value,
                () => InputModel.FundamentalInput.ProjectNo = oldValue
            );
            InputModel.FundamentalInput.ProjectNo = value;
        }

        [ObservableProperty]
        private string _projectName;

        partial void OnProjectNameChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.ProjectName;

            _undoManager.Execute(
                () => InputModel.FundamentalInput.ProjectName = value,
                () => InputModel.FundamentalInput.ProjectName = oldValue
            );
            InputModel.FundamentalInput.ProjectName = value;
        }

        [ObservableProperty]
        private string _seismicGrade;

        partial void OnSeismicGradeChanged(string value)
        {
            var oldValue = InputModel.FundamentalInput.SeismicGrade;

            _undoManager.Execute(
                () => InputModel.FundamentalInput.SeismicGrade = value,
                () => InputModel.FundamentalInput.SeismicGrade = oldValue
            );
            InputModel.FundamentalInput.SeismicGrade = value;
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
            ProjectNo = InputModel.FundamentalInput.ProjectNo;
            ProjectName = InputModel.FundamentalInput.ProjectName;
            Point3D0 = InputModel.FundamentalInput.Point3D0;
            SeismicGrade = InputModel.FundamentalInput.SeismicGrade;

            InputModel.FundamentalInput.PropertyChanged += FundamentalInput_PropertyChanged;

            // コマンドの初期化
            UndoCommand = new RelayCommand(Undo, () => _undoManager.CanUndo);
            RedoCommand = new RelayCommand(Redo, () => _undoManager.CanRedo);

            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
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
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void FundamentalInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FundamentalInput.RefLevel):
                    RefLevel = InputModel.FundamentalInput.RefLevel;
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
            }
        }
    }
}