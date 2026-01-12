using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.Defaults;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
//using System.Windows.Media;
using static PileDesign.ViewModels.MainWindowViewModel;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// GroundLayerViewModel�N���X
    /// </summary>
    public partial class GroundLayerViewModel : ObservableObject, ICloseable
    {
        public readonly UndoManager _undoManager = new();

        public GroundWindow GroundWindowInstance { get; set; } // GroundWindow �̃C���X�^���X��ێ�����v���p�e�B��ǉ�
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // Ground
        private ObservableCollection<GroundInput> _groundsInput;
        public ObservableCollection<GroundInput> GroundsInput
        {
            get => _groundsInput;
            set => SetProperty(ref _groundsInput, value);
        }

        // �ē��h�~�t���O
        private bool _isSyncingGroundInput;

        // GroundInput �v���p�e�B: �w�ǂ̕t���ւ������
        private GroundInput _groundInput;
        public GroundInput GroundInput
        {
            get => _groundInput;
            set
            {
                if (_groundInput == value) return;

                UnsubscribeFromGroundInput(_groundInput);
                SetProperty(ref _groundInput, value);
                SubscribeToGroundInput(_groundInput);
            }
        }

        // �R���X�g���N�^��: ������ Update() �Ăяo���O�ɍw�Ǎς݂ɂȂ�悤�� GroundInput �̑���o�H��ʂ��Ă����OK
        public GroundLayerViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            PrevGroundsInput = new ObservableCollection<GroundInput>(
                InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
            );

            GroundsInput = new ObservableCollection<GroundInput>(
                InputModel.GroundsInput.Select(groundInput => groundInput.DeepCopy())
            );

            if (GroundsInput.Count == 0)
                GroundsInput.Add(new GroundInput());

            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            UpdateGroundsCountPlusOneList();

            // ������ GroundInput �Z�b�^�[��ʂ��i�w�ǂ����j
            GroundInput = GroundsInput[Math.Clamp(GroundNo - 1, 0, GroundsInput.Count - 1)];

            // Update(); �� Initialize() �ŌĂ΂��
        }
        // GroundInput �ύX�Ď��̍w�ǁE����
        private void SubscribeToGroundInput(GroundInput gi)
        {
            if (gi == null) return;
            gi.PropertyChanged += OnGroundInputPropertyChanged;
        }

        private void UnsubscribeFromGroundInput(GroundInput gi)
        {
            if (gi == null) return;
            gi.PropertyChanged -= OnGroundInputPropertyChanged;
        }

        // �Ď��Ώۃv���p�e�B��
        private static readonly HashSet<string> GroundInputTriggerProps =
        [
            nameof(GroundInput.GroundTopAltitude),
            nameof(GroundInput.GroundWaterTableAltitude),
            nameof(GroundInput.StressAltitude),
            nameof(GroundInput.GroundWaterGLDepth),
            nameof(GroundInput.StressGLDepth),
            // �K�v�Ȃ�����x����@�ύX��������:
            // nameof(GroundInput.GroundAcceleration1),
            // nameof(GroundInput.GroundAcceleration2),
            // nameof(GroundInput.ShallowSoilType),
            // nameof(GroundInput.CalculationMethod),
        ];

        // ���݊��Z�i�W��Z��GL�[���j�̓���
        private void SyncDepthAltitude(GroundInput gi, string propertyName)
        {
            if (gi == null) return;

            // �ē��h�~
            if (_isSyncingGroundInput) return;
            _isSyncingGroundInput = true;
            try
            {
                switch (propertyName)
                {
                    case nameof(GroundInput.GroundTopAltitude):
                        // �E��Z���ς������A����/���͂̕W��Z��[������č쐬
                        gi.GroundWaterTableAltitude = gi.GroundWaterGLDepth + gi.GroundTopAltitude;
                        gi.StressAltitude = gi.StressGLDepth + gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.GroundWaterTableAltitude):
                        gi.GroundWaterGLDepth = gi.GroundWaterTableAltitude - gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.GroundWaterGLDepth):
                        gi.GroundWaterTableAltitude = gi.GroundWaterGLDepth + gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.StressAltitude):
                        gi.StressGLDepth = gi.StressAltitude - gi.GroundTopAltitude;
                        break;

                    case nameof(GroundInput.StressGLDepth):
                        gi.StressAltitude = gi.StressGLDepth + gi.GroundTopAltitude;
                        break;
                }
            }
            finally
            {
                _isSyncingGroundInput = false;
            }
        }

        // GroundInput �� PropertyChanged �n���h��
        private void OnGroundInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not GroundInput gi) return;

            if (string.IsNullOrEmpty(e.PropertyName)) return;

            if (GroundInputTriggerProps.Contains(e.PropertyName))
            {
                // ���݊��Z�̓���
                SyncDepthAltitude(gi, e.PropertyName);

                // �Čv�Z�E�ĕ`��
                Update();
            }
        }

        // GroundNo �ύX���� GroundInput �Z�b�^�[�ōw�ǂ��t���ւ�����
        public void ComboBoxGroundNo_SelectionChanged(int selectedIndex/*, int previousSelectedIndex*/)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            if (selectedIndex == GroundCountPlusOneList.Count - 1)
            {
                int newNo = GroundsInput.Count + 1;
                GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + newNo.ToString() + ")" });
                UpdateGroundsCountPlusOneList();
                GroundNo = newNo;
                GroundInput = GroundsInput.Last(); // �Z�b�^�[�o�R�ōw��
            }
            else
            {
                if (selectedIndex >= 0 && selectedIndex < GroundsInput.Count)
                {
                    GroundNo = selectedIndex + 1;
                    GroundInput = GroundsInput[selectedIndex]; // �Z�b�^�[�o�R�ōw��
                }
            }
            Update();
        }

        [RelayCommand]
        public void Undo()
        {
            _undoManager.Undo();
            if (_undoManager.CurrentState is IEnumerable<GroundInput> state)
            {
                GroundsInput = new ObservableCollection<GroundInput>(state.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.Redo();
            if (_undoManager.CurrentState is IEnumerable<GroundInput> state)
            {
                GroundsInput = new ObservableCollection<GroundInput>(state.Select(x => x.DeepCopy()));
                if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
                    GroundInput = GroundsInput[GroundNo - 1];
                else if (GroundsInput.Count > 0)
                    GroundInput = GroundsInput[0];
                else
                    GroundInput = null;

                Update();
            }
        }
        // Undo
        //[RelayCommand]
        //public void Undo()
        //{
        //    _undoManager.Undo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];   // �Z�b�^�[�o�R�ōw��
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;

        //        Update();
        //    }
        //}

        //// Redo
        //[RelayCommand]
        //public void Redo()
        //{
        //    _undoManager.Redo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];   // �Z�b�^�[�o�R�ōw��
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;

        //        Update();
        //    }
        //}
        //// GroundInput
        //private GroundInput _groundInput;
        //public GroundInput GroundInput
        //{
        //    get => _groundInput;
        //    set => SetProperty(ref _groundInput, value);
        //}

        // �n�Ր�+1���X�g
        //private ObservableCollection<int> _groundCountPlusOneList;
        //public ObservableCollection<int> GroundCountPlusOneList
        //{
        //    get => _groundCountPlusOneList;
        //    set => SetProperty(ref _groundCountPlusOneList, value);
        //}
        private ObservableCollection<string> _groundCountPlusOneList;
        public ObservableCollection<string> GroundCountPlusOneList
        {
            get => _groundCountPlusOneList;
            set => SetProperty(ref _groundCountPlusOneList, value);
        }

        //private void UpdateGroundsCountPlusOneList()
        //{
        //    GroundCountPlusOneList = new ObservableCollection<int>(Enumerable.Range(1, GroundsInput.Count + 1));
        //}
        private void UpdateGroundsCountPlusOneList()
        {
            var list = new ObservableCollection<string>();
            int count = GroundsInput.Count;
            for (int i = 1; i <= count; i++)
            {
                list.Add(i.ToString());
            }
            list.Add($"{count + 1} (New)");
            GroundCountPlusOneList = list;
        }

        // �I��n�Քԍ�
        private int _groundNo = 1;
        public int GroundNo
        {
            get => _groundNo;
            set => SetProperty(ref _groundNo, value);
        }

        // DataGrid��̑I�𒆂�GroundInput�f�[�^
        private GroundMassDataInput _selectedGroundMassOnDataGrid;
        public GroundMassDataInput SelectedGroundMassOnDataGrid
        {
            get => _selectedGroundMassOnDataGrid;
            set => SetProperty(ref _selectedGroundMassOnDataGrid, value);
        }

        // DataGrid��̑I�𒆂�GroundLayer�f�[�^
        private GroundLayerInput _selectedGroundLayerOnDataGrid;
        public GroundLayerInput SelectedGroundLayerOnDataGrid
        {
            get => _selectedGroundLayerOnDataGrid;
            set => SetProperty(ref _selectedGroundLayerOnDataGrid, value);
        }

        public LayoutAnchorable NValueTab { get; set; }
        public LayoutAnchorable CuValueTab { get; set; }
        public LayoutAnchorable VsValueTab { get; set; }
        public LayoutAnchorable EsValueTab { get; set; }
        public LayoutAnchorable DefTab { get; set; }
        public LayoutAnchorable FsTab { get; set; }

        public string[] AgeCategoryOption { get; } = ["���ϑw", "�^�ϑw"];
        //public enum AgeCategoryOption { ���ϑw, �^�ϑw }

        public string[] ShallowSoilTypeOption { get; } =
        [
            "�S���y",
            "�����y"
        ];
        //public enum ShallowSoilTypeOption { �S���y, �����y }

        //// �Z��@
        public string[] CalculationMethodOption { get; } =
        [
            "a1(b1)",
            "a2(b2)"
        ];

        public string[] ChartDispContentOption { get; } =
        [
            "DmaxU*(���x��1)",
            "DmaxU*(���x��2)",
            "DmaxU*(���x��1,2)",
            "DmaxU*+����cyH(���x��1)",
            "DmaxU*+����cyH(���x��2)",
            "DmaxU*+����cyH(���x��1,2)",
        ];

        public ObservableCollection<string> ChartDispContents { get; } = [];
        private string _ChartDispContent = "DmaxU*(���x��1,2)";
        public string ChartDispContent
        {
            get => _ChartDispContent;
            set => SetProperty(ref _ChartDispContent, value);
        }

        // �O���t2���e
        public string[] ChartFLContentOption { get; } =
        [
            "FL(���x��1)",
            "FL(���x��2)",
            "FL(���x��1,2)",
        ];

        public ObservableCollection<string> ChartFLContents { get; } = [];
        private string _ChartFLContent = "FL(���x��1,2)";
        public string ChartFLContent
        {
            get => _ChartFLContent;
            set => SetProperty(ref _ChartFLContent, value);
        }

        private object _dataContextFundamental;
        public object DataContextFundamental
        {
            get => _dataContextFundamental;
            set => SetProperty(ref _dataContextFundamental, value);
        }

        public ObservableCollection<ExampleItem> ExampleItems { get; } = [];

        private ExampleItem? _selectedExampleItem;
        public ExampleItem? SelectedExampleItem
        {
            get => _selectedExampleItem;
            set
            {
                // null ���Z�b�g����ꍇ�͂��̂܂ܔ��f�iUI�N���A�p�j
                if (value == null)
                {
                    SetProperty(ref _selectedExampleItem, null);
                    return;
                }

                // �I�����ꂽ��A�܂����ݏ�Ԃ� undo �X�^�b�N�֕ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                // ���s�iExampleItem �� ICommand ��ێ����Ă���O��j
                value.Command?.Execute(null);

                // ���s��ɑI�����N���A���āA�������ڂ��đI���ł���悤�ɂ���
                _selectedExampleItem = null;
                OnPropertyChanged(nameof(SelectedExampleItem));
            }
        }

        [RelayCommand]
        private void OnSliderEngineeringBedrockValueChanged(double value)
        {
            int intValue = (int)value;
            int n = GroundInput.GroundLayers.Count;

            // i�s�̃`�F�b�N�{�b�N�X�̏�Ԃ��ύX���ꂽ�Ƃ��A1�`i-1�s�̃`�F�b�N�{�b�N�X��L�����Ai+1�s�ڈȍ~�̃`�F�b�N�{�b�N�X�𖳌���
            for (int i = 0; i < n; i++)
            {
                //if (n - 1 - i < intValue)
                //{
                //    GroundInput.GroundLayers[i].IsEngineeringBedrock = true;
                //}
                //else
                //{
                //    GroundInput.GroundLayers[i].IsEngineeringBedrock = false;
                //}
                GroundInput.GroundLayers[i].IsEngineeringBedrock = n - 1 - i < intValue;
            }
            Update();
        }

        // �͂��߂čH�w�I��ՂƂȂ�w�ȉ��̑w�����ׂčH�w�I��Ղɕς��郁�\�b�h
        public void UpdateBedrockChecks()
        {
            bool isEngineeringBedrock = false;
            foreach (var groundLayer in GroundInput.GroundLayers)
            {
                if (groundLayer.IsEngineeringBedrock)
                {
                    isEngineeringBedrock = true;
                }

                if (isEngineeringBedrock)
                {
                    groundLayer.IsEngineeringBedrock = true;
                }
            }
        }

        //[RelayCommand]
        //public void Undo()
        //{

        //    _undoManager.Undo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;

        //        Update(); // UI�ĕ`��
        //    }
        //}


        //[RelayCommand]
        //public void Redo()
        //{
        //    _undoManager.Redo();
        //    if (_undoManager.CurrentState != null)
        //    {
        //        GroundsInput = new ObservableCollection<GroundInput>(_undoManager.CurrentState.Select(x => x.DeepCopy()));
        //        if (GroundNo > 0 && GroundNo <= GroundsInput.Count)
        //            GroundInput = GroundsInput[GroundNo - 1];
        //        else if (GroundsInput.Count > 0)
        //            GroundInput = GroundsInput[0];
        //        else
        //            GroundInput = null;
        //        Update();
        //    }
        //}

        // �y�w�폜���\�b�h
        [RelayCommand]
        public void DeleteGroundLayer(object sender)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            if (sender is not GroundLayerInput itemToDelete) return;
            GroundInput.GroundLayers.Remove(itemToDelete);

            // �s�ԍ��� LoadingRow �Őݒ�ς݁B�K�v�Ȃ� Items.Refresh �̂�
            GroundWindowInstance?.DataGridGroundLayer?.Items.Refresh();

            UpdateGroundLayerNo();
            Update();
        }
        //public void DeleteGroundLayer(object sender)
        //{
        //    // �ύX�O�̏�Ԃ�ۑ�
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // sender �� GridDataItem �ł��邱�Ƃ��m�F
        //    if (sender is not GroundLayerInput itemToDelete) return;

        //    // �R���N�V��������폜
        //    GroundInput.GroundLayers.Remove(itemToDelete);

        //    // �ԍ��X�V
        //    UpdateAllRowNumbers(GroundWindowInstance.DataGridGroundLayer);

        //    UpdateGroundLayerNo();
        //    Update(); ///
        //}

        // �y���_�폜���\�b�h
        [RelayCommand]
        public void DeleteGroundMass(object sender)
        {
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            if (sender is not GroundMassDataInput itemToDelete) return;
            GroundInput.GroundMassesData.Remove(itemToDelete);

            GroundWindowInstance?.DataGridGroundMass?.Items.Refresh();
            UpdateGroundMassDataLayer();
            Update();
        }
        //public void DeleteGroundMass(object sender)
        //{
        //    // �ύX�O�̏�Ԃ�ۑ�
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // sender �� GridDataItem �ł��邱�Ƃ��m�F
        //    if (sender is not GroundMassDataInput itemToDelete) return;

        //    // �R���N�V��������폜
        //    GroundInput.GroundMassesData.Remove(itemToDelete);

        //    // �ԍ��X�V
        //    UpdateAllRowNumbers(GroundWindowInstance.DataGridGroundMass);

        //    UpdateGroundMassDataLayer();

        //    Update(); ///
        //}

        // ���ׂĂ̍s�̔ԍ����X�V
        private static void UpdateAllRowNumbers(DataGrid dataGrid)
        {
            for (int i = 0; i < dataGrid.Items.Count; i++)
            {
                if (dataGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                {
                    row.Header = (i + 1).ToString(); // �s�ԍ���ݒ�
                }
            }
        }

        public static Crosshair MyCrosshair_NValue { get; private set; }

        private string _crosshairPositionText_NValue;
        public string CrosshairPositionText_NValue
        {
            get => _crosshairPositionText_NValue;
            set => SetProperty(ref _crosshairPositionText_NValue, value);
        }

        public static Crosshair MyCrosshair_Cu { get; private set; }

        private string _crosshairPositionText_Cu;
        public string CrosshairPositionText_Cu
        {
            get => _crosshairPositionText_Cu;
            set => SetProperty(ref _crosshairPositionText_Cu, value);
        }

        public static Crosshair MyCrosshair_Vs { get; private set; }

        private string _crosshairPositionText_Vs;
        public string CrosshairPositionText_Vs
        {
            get => _crosshairPositionText_Vs;
            set => SetProperty(ref _crosshairPositionText_Vs, value);
        }

        public static Crosshair MyCrosshair_Es { get; private set; }

        private string _crosshairPositionText_Es;
        public string CrosshairPositionText_Es
        {
            get => _crosshairPositionText_Es;
            set => SetProperty(ref _crosshairPositionText_Es, value);
        }

        public static Crosshair MyCrosshair_Disp { get; private set; }

        private string _crosshairPositionText_Disp;
        public string CrosshairPositionText_Disp
        {
            get => _crosshairPositionText_Disp;
            set => SetProperty(ref _crosshairPositionText_Disp, value);
        }

        public static Crosshair MyCrosshair_FL { get; private set; }

        private string _crosshairPositionText_FL;
        public string CrosshairPositionText_FL
        {
            get => _crosshairPositionText_FL;
            set => SetProperty(ref _crosshairPositionText_FL, value);
        }

        // View����邽�߂̃C�x���g
        public event EventHandler RequestClose;
        private readonly ObservableCollection<GroundInput> PrevGroundsInput;
        private readonly Dictionary<string, object> previousPropertyValues = [];


        public void ShowGroundInputErrorAlert()
        {
            var gi = GroundInput;
            var errors = new List<string>();
            if (gi.IsErrorGroundWaterTableAltitude)
                errors.Add("�n������Z�͍E���W��Z�ȉ��ɂ��Ă��������B");
            if (gi.IsErrorStressAltitude)
                errors.Add("�n�����͌v�Z�pZ�͍E���W��Z�ȉ��ɂ��Ă��������B");
            if (gi.IsErrorGroundWaterGLDepth)
                errors.Add("�n�����ʐ[�x��0�ȉ��ɂ��Ă��������B");
            if (gi.IsErrorStressGLDepth)
                errors.Add("�n�����͌v�Z�p�[�x��0�ȉ��ɂ��Ă��������B");

            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "���̓G���[", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void GroundDelete()
        {
            // �n�Ղ�1�����Ȃ��ꍇ�͍폜�s��
            if (GroundsInput.Count <= 1)
            {
                MessageBox.Show("�n�Ղ�1�������݂��Ȃ����߁A�폜�ł��܂���B", "�x��", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // �I�𒆂̒n�Քԍ�
            int index = GroundNo - 1;
            if (index < 0 || index >= GroundsInput.Count)
            {
                MessageBox.Show("�폜�Ώۂ��I������Ă��܂���B", "�G���[", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // �m�F���b�Z�[�W
            var result = MessageBox.Show(
                $"�n�Քԍ� {GroundNo} ���폜���܂����H\n���ɖ߂��܂���B",
                "�m�F",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // �ύX�O�̏�Ԃ�ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                GroundsInput.RemoveAt(index);
                UpdateGroundsCountPlusOneList();

                // �폜��̑I����Ԃ𒲐�
                if (GroundsInput.Count > 0)
                {
                    GroundNo = Math.Min(GroundNo, GroundsInput.Count);
                    GroundInput = GroundsInput[GroundNo - 1];
                }
                else
                {
                    GroundNo = 1;
                    GroundInput = null;
                }

                Update();
            }
        }

        // GroundWindowInstance ���ݒ肳�ꂽ��ɏ������������s��
        public void Initialize()
        {
            // �R���e�L�X�g�iWindow, DataContext�j���������ꂽ�i�K�ŌĂ΂��z��Ȃ̂ł����ŏ������ڂ�p��
            InitializeExampleItems();

            Update();
        }

        // �K�i��f�[�^�̍쐬���\�b�h
        private static (List<double>, List<double>) GetSteppedData(List<double> originalX, List<double> originalY)
        {
            // �K�[�h��
            if (originalX == null || originalY == null || originalX.Count == 0 || originalY.Count == 0)
                return ([], []);

            // �X�e�b�v��̃f�[�^�𐶐�
            List<double> steppedX = [];
            List<double> steppedY = [];

            for (int i = 0; i < originalX.Count; i++)
            {
                if (i == 0)
                {
                    steppedX.Add(0);
                    steppedY.Add(0);

                    steppedX.Add(originalX[i]);
                    steppedY.Add(0);
                }
                else
                {
                    steppedX.Add(originalX[i]);
                    steppedY.Add(originalY[i - 1]);
                }
                steppedX.Add(originalX[i]);
                steppedY.Add(originalY[i]);

                if (i == originalX.Count - 1)
                {
                    steppedX.Add(0);
                    steppedY.Add(originalY[i]);
                }
            }

            // �Ō�̃f�[�^�|�C���g��ǉ�
            steppedX.Add(originalX[^1]);
            steppedY.Add(originalY[^1]);

            return (steppedX, steppedY);
        }

        // rectangle
        private static List<CoordinateRect> GetRectangleGeometry(List<double> originalX, List<double> originalY)
        {
            List<CoordinateRect> coordinateRects = [];
            if (originalX.Count > 0)
            {
                for (int i = 0; i < originalX.Count; i++)
                {
                    if (i == 0)
                    {
                        coordinateRects.Add(new()
                        {
                            Bottom = originalY[i],
                            Top = 0,
                            Left = 0,
                            Right = originalX[i]
                        });
                    }
                    else
                    {
                        coordinateRects.Add(new()
                        {
                            Bottom = originalY[i],
                            Top = originalY[i - 1],
                            Left = 0,
                            Right = originalX[i]
                        });
                    }
                }
            }
            return coordinateRects;
        }

        private bool _hookedDispMouseMove, _hookedFLMouseMove, _hookedNMouseMove, _hookedVsMouseMove, _hookedEsMouseMove, _hookedCuMouseMove;

        // �n�Օψʕ`�惁�\�b�h
        private void DrawGroundDisplacementGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> gLDepths = [];

            foreach (var data in GroundInput.GroundMassesData)
            {
                double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
                                 data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
                double gLDepth = data.GLDepth + data.Spacing * _factor;
                gLDepths.Add(gLDepth);
            }

            var wpf = GroundWindowInstance.wpfPlotDisplacement;

            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            if (GroundInput.GroundLayers.Count != 0)
            {
                if (ChartDispContent.Contains("DmaxU*(���x��1)") || ChartDispContent.Contains("DmaxU*(���x��1,2)"))
                {
                    List<double> dMaxU1s = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU1s.Add(data.DmaxUStar[0]);
                    }
                    //if (dMaxU1s.Any(double.IsNaN))
                    //{ hasData=false; }
                    /*else */
                    if (dMaxU1s.Any(double.IsNaN) == false && dMaxU1s.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU1s], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                        scatter.LineWidth = 2;

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU1s[i]:N1}", new(dMaxU1s[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*(���x��2)") || ChartDispContent.Contains("DmaxU*(���x��1,2)"))
                {
                    List<double> dMaxU2s = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU2s.Add(data.DmaxUStar[1]);
                    }

                    if (dMaxU2s.Any(double.IsNaN) == false &&
                        dMaxU2s.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU2s], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                        scatter.LineWidth = 2;

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU2s[i]:N1}", new(dMaxU2s[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*+����cyH(���x��1)") || ChartDispContent.Contains("DmaxU*+����cyH(���x��1,2)"))
                {
                    List<double> dMaxU1Pluss = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU1Pluss.Add(data.DmaxUStarSigmaGammaCyH[0]);
                    }

                    if (dMaxU1Pluss.Any(double.IsNaN) == false &&
                        dMaxU1Pluss.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter([.. dMaxU1Pluss], gLDepths.ToArray());
                        scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
                        scatter.LineWidth = 2;

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU1Pluss[i]:N1}", new(dMaxU1Pluss[i], gLDepths[i]));
                        }
                    }
                }
                if (ChartDispContent.Contains("DmaxU*+����cyH(���x��2)") || ChartDispContent.Contains("DmaxU*+����cyH(���x��1,2)"))
                {
                    List<double> dMaxU2Pluss = [];
                    foreach (var data in GroundInput.GroundMassesData)
                    {
                        dMaxU2Pluss.Add(data.DmaxUStarSigmaGammaCyH[1]);
                    }

                    if (dMaxU2Pluss.Any(double.IsNaN) == false &&
                        dMaxU2Pluss.Count != 0 && gLDepths.Count != 0)
                    {
                        var scatter = wpf.Plot.Add.Scatter(dMaxU2Pluss.ToArray(), [.. gLDepths]);
                        scatter.Color = Color.FromSKColor(NikkenSKColor.DeepBlue);
                        scatter.LineWidth = 2;

                        for (int i = 0; i < gLDepths.Count; i++)
                        {
                            wpf.Plot.Add.Text($"{dMaxU2Pluss[i]:N1}", new(dMaxU2Pluss[i], gLDepths[i]));
                        }
                    }
                }
            }
            wpf.Plot.Legend.IsVisible = true;

            string title = "�n�Օψ�";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "�n�Օψ� (mm)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL��[��(m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            // �N���X�w�A�̏�����
            //MyCrosshair_Disp = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            MyCrosshair_Disp ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedDispMouseMove)
            {
                wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Disp", "�ψ�(mm)", "GL��[��(m)", 1, 3);
                _hookedDispMouseMove = true;
            }
            //wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Disp", "�ψ�(mm)", "GL��[��(m)", 1, 3);

            wpf.Refresh();
        }

        private void DrawFLScatter(List<double> gLDepths, int index, WpfPlot wpf, SKColor skColor)
        {
            List<List<double>> fL1ss = [];
            List<double> fL1s = [];
            List<List<double>> gLDepth1ss = [];
            List<double> gLDepth1s = [];

            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                if (GroundInput.GroundMassesData[i].FL[index] == null)
                {
                    fL1ss.Add(fL1s);
                    fL1s = [];
                    gLDepth1ss.Add(gLDepth1s);
                    gLDepth1s = [];
                }
                else
                {
                    fL1s.Add(GroundInput.GroundMassesData[i].FL[index].GetValueOrDefault());
                    gLDepth1s.Add(gLDepths[i]);
                }
            }

            if (fL1s.Count > 0)
            {
                fL1ss.Add(fL1s);
                gLDepth1ss.Add(gLDepth1s);
            }

            for (int i = 0; i < gLDepth1ss.Count; i++)
            {
                var scatter = wpf.Plot.Add.Scatter(fL1ss[i].ToArray(), [.. gLDepth1ss[i]]);
                scatter.Color = Color.FromSKColor(skColor);
                scatter.LineWidth = 2;
                for (int j = 0; j < gLDepth1ss[i].Count; j++)
                {
                    wpf.Plot.Add.Text($"{fL1ss[i][j]:N2}", new(fL1ss[i][j], gLDepth1ss[i][j]));
                }
            }
        }

        private void DrawFLGraph()
        {
            if (GroundWindowInstance == null) return;

            List<double> gLDepths = [];
            foreach (var data in GroundInput.GroundMassesData)
            {
                double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
                                 data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
                double gLDepth = data.GLDepth + data.Spacing * _factor;
                gLDepths.Add(gLDepth);
            }

            var wpf = GroundWindowInstance.wpfPlotFL;
            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            if (ChartFLContent.Contains("FL"))
            {
                if (ChartFLContent.Contains("FL(���x��1)") || ChartFLContent.Contains("FL(���x��1,2)"))
                    DrawFLScatter(gLDepths, 0, wpf, NikkenSKColor.SkyBlue);
                if (ChartFLContent.Contains("FL(���x��2)") || ChartFLContent.Contains("FL(���x��1,2)"))
                    DrawFLScatter(gLDepths, 1, wpf, NikkenSKColor.DeepBlue);
            }

            string title = "�t�󉻈��S�� FL�l���z";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "FL�l";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL��[��(m)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Plot.Axes.Bottom.Min = 0.0;
            wpf.Plot.Axes.Bottom.Max = 1.0;

            MyCrosshair_FL ??= PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedFLMouseMove)
            {
                wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_FL", "FL", "GL��[��(m)", 1, 3);
                _hookedFLMouseMove = true;
            }

            wpf.Refresh();
        }
        //private void DrawFLGraph()
        //{
        //    if (GroundWindowInstance == null)
        //    { return; }

        //    List<double> gLDepths = [];

        //    foreach (var data in GroundInput.GroundMassesData)
        //    {
        //        double _factor = data == GroundInput.GroundMassesData.First() ? 1.0 :
        //                         data == GroundInput.GroundMassesData.Last() ? 0.0 : 0.5;
        //        double gLDepth = data.GLDepth + data.Spacing * _factor;
        //        gLDepths.Add(gLDepth);
        //    }

        //    var wpf = GroundWindowInstance.wpfPlotFL;

        //    wpf.Plot.Clear();
        //    DrawSoilLayer(wpf);

        //    if (ChartFLContent.Contains("FL"))
        //    {
        //        if (ChartFLContent.Contains("FL(���x��1)") || ChartFLContent.Contains("FL(���x��1,2)"))
        //        {
        //            DrawFLScatter(gLDepths, 0, wpf, NikkenSKColor.SkyBlue);
        //        }

        //        if (ChartFLContent.Contains("FL(���x��2)") || ChartFLContent.Contains("FL(���x��1,2)"))
        //        {
        //            DrawFLScatter(gLDepths, 1, wpf, NikkenSKColor.DeepBlue);
        //        }
        //    }

        //    //var verticalLine = wpf.Plot.Add.VerticalLine(1, 1, Color.FromSKColor(NikkenSKColor.Red));

        //    string title = "�t�󉻈��S�� FL�l���z";
        //    wpf.Plot.Axes.Title.Label.Text = title;
        //    wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

        //    string xLabel = "FL�l";
        //    wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

        //    string yLabel = "GL��[��(m)";
        //    wpf.Plot.Axes.Left.Label.Text = yLabel;
        //    wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

        //    wpf.Plot.Axes.AutoScale();
        //    wpf.Plot.Axes.AutoScaleExpandY();

        //    wpf.Plot.Axes.Bottom.Min = 0.0;
        //    wpf.Plot.Axes.Bottom.Max = 1.0;

        //    // �N���X�w�A�̏�����
        //    MyCrosshair_FL = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

        //    // ��: �O���t��������
        //    wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_FL", "FL", "GL��[��(m)", 1, 3);

        //    wpf.Refresh();
        //}

        // N�l�O���t�`�惁�\�b�h
        private void DrawNValueGraph()
        {
            if (GroundWindowInstance == null) return;

            List<double> ns = [];
            List<double> _bottomGLDepths = [];
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                ns.Add(GroundInput.GroundMassesData[i].NValue);
                _bottomGLDepths.Add(GroundInput.GroundMassesData[i].GLDepth);
            }

            var wpfNValue = GroundWindowInstance.wpfPlotNValue;
            wpfNValue.Plot.Clear();
            DrawSoilLayer(wpfNValue);

            var scatter = wpfNValue.Plot.Add.Scatter(ns, _bottomGLDepths);
            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            for (int i = 0; i < _bottomGLDepths.Count; i++)
                wpfNValue.Plot.Add.Text($"{ns[i]:N0}", new(ns[i], _bottomGLDepths[i]));

            string title = "N�l���z";
            wpfNValue.Plot.Axes.Title.Label.Text = title;
            wpfNValue.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "N�l";
            wpfNValue.Plot.Axes.Bottom.Label.Text = xLabel;
            wpfNValue.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "GL��[��(m)";
            wpfNValue.Plot.Axes.Left.Label.Text = yLabel;
            wpfNValue.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpfNValue.Plot.Axes.AutoScale();
            wpfNValue.Plot.Axes.AutoScaleExpandY();
            wpfNValue.Plot.Axes.Bottom.Min = 0.0;
            wpfNValue.Plot.Axes.Bottom.Max = 60.0;

            MyCrosshair_NValue ??= PlotHelper.InitCrosshair(wpfNValue, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            if (!_hookedNMouseMove)
            {
                wpfNValue.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_NValue", "N�l", "GL��[��(m)", 1, 3);
                _hookedNMouseMove = true;
            }

            wpfNValue.Refresh();
        }
        //private void DrawNValueGraph()
        //{
        //    if (GroundWindowInstance == null)
        //    { return; }

        //    List<double> ns = [];
        //    List<double> _bottomGLDepths = [];

        //    for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
        //    {
        //        ns.Add(GroundInput.GroundMassesData[i].NValue);
        //        _bottomGLDepths.Add(GroundInput.GroundMassesData[i].GLDepth);
        //    }

        //    var wpfNValue = GroundWindowInstance.wpfPlotNValue;

        //    wpfNValue.Plot.Clear();
        //    DrawSoilLayer(wpfNValue);

        //    var scatter = wpfNValue.Plot.Add.Scatter(ns, _bottomGLDepths);

        //    scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
        //    scatter.LineWidth = 2;

        //    for (int i = 0; i < _bottomGLDepths.Count; i++)
        //    {
        //        wpfNValue.Plot.Add.Text($"{ns[i]:N0}", new(ns[i], _bottomGLDepths[i]));
        //    }

        //    string title = "N�l���z";
        //    wpfNValue.Plot.Axes.Title.Label.Text = title;
        //    wpfNValue.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

        //    string xLabel = "N�l";
        //    wpfNValue.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpfNValue.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

        //    string yLabel = "GL��[��(m)";
        //    wpfNValue.Plot.Axes.Left.Label.Text = yLabel;
        //    wpfNValue.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

        //    wpfNValue.Plot.Axes.AutoScale();

        //    wpfNValue.Plot.Axes.AutoScaleExpandY();

        //    wpfNValue.Plot.Axes.Bottom.Min = 0.0;
        //    wpfNValue.Plot.Axes.Bottom.Max = 60.0;

        //    // �N���X�w�A�̏�����
        //    MyCrosshair_NValue = PlotHelper.InitCrosshair(wpfNValue, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

        //    // ��: �O���t��������
        //    wpfNValue.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_NValue", "N�l", "GL��[��(m)", 1, 3);

        //    wpfNValue.Refresh();
        //}

        // �y�w�`�惁�\�b�h
        private void DrawSoilLayer(WpfPlot wpf)
        {
            //Color color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            Color color0 = Color.FromSKColor(NikkenSKColor.Yellow);
            Color grayColor = new(128, 128, 128, 255); // �O���[�F

            LinePattern linePattern = LinePattern.Solid;
            // �n�\
            wpf.Plot.Add.HorizontalLine(0, 2, grayColor, LinePattern.Solid);

            // �y�w���E���C��
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                wpf.Plot.Add.HorizontalLine(GroundInput.GroundLayers[i].BottomGLDepth, 1, color0, linePattern);
            }

            // �h��Ԃ��i�w���Ƃ̔w�i�j
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                double y1 = i == 0 ? 0 : GroundInput.GroundLayers[i - 1].BottomGLDepth;
                double y2 = GroundInput.GroundLayers[i].BottomGLDepth;

                Color fillColor = new(0, 0, 0, 255);
                if (GroundInput.GroundLayers[i].GranularityClass == "�S���y")
                { fillColor = new(210, 180, 140, 64); } // �������̔������F R G B alpha
                else if (GroundInput.GroundLayers[i].GranularityClass == "�����y")
                { fillColor = new(255, 165, 0, 64); } // �������̔����I�����W R G B alpha
                else if (GroundInput.GroundLayers[i].GranularityClass == "�I���y")
                { fillColor = new(144, 238, 144, 64); } // �������̔����� R G B alpha

                wpf.Plot.Add.VerticalSpan(y1, y2, fillColor);
            }

            // Y=0 �̊�c��
            Color blackColor = new(0, 0, 0, 255); // ���F
            wpf.Plot.Add.VerticalLine(0, 1, blackColor);

            // ---- �n�����ʕ\���ǉ��������� ----
            double gwDepth = GroundInput.GroundWaterGLDepth; // (�����̏ꍇ 0 �����l)

            // �n�����ʃ��C���i�j
            Color waterColor = Color.FromSKColor(NikkenSKColor.DeepBlue);
            wpf.Plot.Add.HorizontalLine(gwDepth, 2, waterColor, LinePattern.Solid);

            // Y�������W������Ԋu������F(maxY - minY) / 50 ���g�p�B�f�[�^�s�����͏]���� 0.12 ���g�p
            double yMax = 0.0;
            double yMin = 0.0;
            bool hasDepthData = false;

            // �n�w������ɂ���
            if (GroundInput.GroundLayers != null && GroundInput.GroundLayers.Count > 0)
            {
                yMin = GroundInput.GroundLayers.Min(l => l.BottomGLDepth);
                hasDepthData = true;
            }

            // �n���_�[�����l��
            if (GroundInput.GroundMassesData != null && GroundInput.GroundMassesData.Count > 0)
            {
                double minMassDepth = GroundInput.GroundMassesData.Min(m => m.GLDepth);
                if (!hasDepthData)
                {
                    yMin = minMassDepth;
                    hasDepthData = true;
                }
                else
                {
                    yMin = Math.Min(yMin, minMassDepth);
                }
            }

            double range = hasDepthData ? Math.Abs(yMax - yMin) : 0.0;
            double lineGap = range > 0.0 ? range / 100.0 : 0.12;

            // ���� 3 �{�̐������C���i���قǓ��ߓx����������蔖���j
            //double lineGap = 0.12;
            byte[] alphas = [200, 130, 70]; // �と��
            for (int i = 0; i < alphas.Length; i++)
            {
                double y = gwDepth - (i + 1) * lineGap;
                Color transLineColor = new(waterColor.Red, waterColor.Green, waterColor.Blue, alphas[i]);
                wpf.Plot.Add.HorizontalLine(y, 1, transLineColor, LinePattern.Solid);
            }
        }

        // �K�i��O���t�`�惁�\�b�h
        private void DrawSteppedGraph(List<double> originalX, List<double> originalY, WpfPlot wpf, string title, string xLabel, string yLabel)
        {
            if (GroundWindowInstance == null)
            { return; }

            (List<double> steppedVss, List<double> steppedGLDepths) = GetSteppedData(originalX, originalY);

            var dataX1 = steppedVss.ToArray();
            var dataY1 = steppedGLDepths.ToArray();

            wpf.Plot.Clear();
            DrawSoilLayer(wpf);

            var scatter = wpf.Plot.Add.Scatter(dataX1, dataY1);

            scatter.Color = Color.FromSKColor(NikkenSKColor.SkyBlue);
            scatter.LineWidth = 2;

            for (int i = 0; i < dataX1.Length; i++)
            {
                wpf.Plot.Add.Text($"{dataX1[i]:N0}", new(dataX1[i], dataY1[i]));
            }

            List<CoordinateRect> coordinateRects = GetRectangleGeometry(originalX, originalY);
            foreach (CoordinateRect coordinate in coordinateRects)
            {
                var rectangle = wpf.Plot.Add.Rectangle(coordinate);
                rectangle.FillColor = Color.FromSKColor(NikkenSKColor.SkyBlue);
                rectangle.LineColor = new(0, 0, 0, 255); // ���F
                rectangle.LineWidth = 1;
            }

            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();
            wpf.Plot.Axes.Bottom.Min = 0.0;

            wpf.Refresh();
        }

        // �S���̓O���t�`�惁�\�b�h
        private void DrawCuGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> cus = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                cus.Add(GroundInput.GroundLayers[i].Cohesive);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(cus, _bottomGLDepths, GroundWindowInstance.wpfPlotCu, "�S���͕��z", "�S����Cu (kN/m2)", "GL��[��(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotCu;

            // �N���X�w�A�̏�����
            MyCrosshair_Cu = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // ��: �O���t��������
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Cu", "Cu(kN/m2)", "GL��[��(m)", 1, 3);
        }

        // ����f���x�O���t�`�惁�\�b�h
        private void DrawVsGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> vss = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                vss.Add(GroundInput.GroundLayers[i].Vs);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(vss, _bottomGLDepths, GroundWindowInstance.wpfPlotVs, "����f�g���x���z", "����f�g���x Vs(m/s)", "GL��[��(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotVs;

            // �N���X�w�A�̏�����
            MyCrosshair_Vs = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // ��: �O���t��������
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Vs", "Vs(m/s)", "GL��[��(m)", 1, 3);
        }

        // �ό`�W���O���t�`�惁�\�b�h
        private void DrawEsGraph()
        {
            if (GroundWindowInstance == null)
            { return; }

            List<double> ess = [];
            List<double> _bottomGLDepths = [];

            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                ess.Add(GroundInput.GroundLayers[i].Es);
                _bottomGLDepths.Add(GroundInput.GroundLayers[i].BottomGLDepth);
            }
            DrawSteppedGraph(ess, _bottomGLDepths, GroundWindowInstance.wpfPlotEs, "�ό`�W�����z", "�ό`�W�� Es(kN/m2)", "GL��[��(m)");

            WpfPlot wpf = GroundWindowInstance.wpfPlotEs;

            // �N���X�w�A�̏�����
            MyCrosshair_Es = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));

            // ��: �O���t��������
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_Es", "Es(kN/m2)", "GL��[��(m)", 1, 3);
        }


        // ���c�E�㓡��
        [RelayCommand]
        private void OnCalculateOtaVs()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                double yg;
                if (groundMassData.AgeCategory == "���ϑw")
                { yg = 1.0; }
                else if (groundMassData.AgeCategory == "�^�ϑw")
                { yg = 1.3; }
                else
                { yg = 1.0; }

                double si;
                if (groundMassData.GranularityClass == "�S���y")
                { si = 1.0; }
                else if (groundMassData.GranularityClass == "�����y" || groundMassData.GranularityClass == "���I�y")
                { si = 1.1; }
                else if (groundMassData.GranularityClass == "�I���y")
                { si = 1.4; }
                else
                { si = 1.0; }

                groundMassData.VS0 = 69 * Math.Pow(groundMassData.NValue, 0.17) * Math.Pow(Math.Abs(groundMassData.GLDepth) / 1.0, 0.2) * yg * si;
            }
        }

        // ����E�a����
        [RelayCommand]
        private void OnCalculateImaiVs()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            double a;
            double b;
            double c;

            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.AgeCategory == "���ϑw" && groundMassData.GranularityClass == "�S���y")
                {
                    a = 50;
                    b = 0.42;
                    c = 80.0;
                }
                else if (groundMassData.AgeCategory == "���ϑw" && groundMassData.GranularityClass == "�����y")
                {
                    a = 90;
                    b = 0.30;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "���ϑw" && groundMassData.GranularityClass == "�I���y")
                {
                    a = 80;
                    b = 0.38;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "�^�ϑw" && groundMassData.GranularityClass == "�S���y")
                {
                    a = 130;
                    b = 0.29;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "�^�ϑw" && groundMassData.GranularityClass == "�����y")
                {
                    a = 110;
                    b = 0.30;
                    c = 0.0;
                }
                else if (groundMassData.AgeCategory == "�^�ϑw" && groundMassData.GranularityClass == "�I���y")
                {
                    a = 140;
                    b = 0.26;
                    c = 0.0;
                }
                else
                {
                    a = 50;
                    b = 0.42;
                    c = 80.0;
                }
                groundMassData.VS0 = a * Math.Pow(groundMassData.NValue, b) + c;
            }
        }

        // �y�w�ǉ����\�b�h
        [RelayCommand]
        private void OnAddGroundLayer()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var layers = GroundInput?.GroundLayers;
            if (layers == null) return;

            // �y�w��0���̂Ƃ��͏����l��ǉ����ďI��
            if (layers.Count == 0)
            {
                var firstLayer = new GroundLayerInput
                {
                    BottomGLDepth = -3.0, // GL��ŉ����������̑z��
                };
                layers.Add(firstLayer);
                SelectedGroundLayerOnDataGrid = firstLayer;

                UpdateBedrockChecks();
                UpdateGroundLayerNo();
                Update();
                return;
            }

            // �I���s�̒��� or �����֒ǉ�
            int selectedIndex = layers.IndexOf(SelectedGroundLayerOnDataGrid);
            int insertIndex;
            GroundLayerInput newGroundLayer;

            if (selectedIndex >= 0 && selectedIndex < layers.Count - 1)
            {
                // �I���s�Ƃ��̉��s�̒��Ԃɒǉ�
                double d1 = layers[selectedIndex].BottomGLDepth;
                double d2 = layers[selectedIndex + 1].BottomGLDepth;
                newGroundLayer = new GroundLayerInput
                {
                    BottomGLDepth = 0.5 * (d1 + d2),
                };
                insertIndex = selectedIndex + 1;
                layers.Insert(insertIndex, newGroundLayer);
            }
            else
            {
                // �����ɒǉ��i�Ō�̉��[������[��������j
                double last = layers[layers.Count - 1].BottomGLDepth;
                newGroundLayer = new GroundLayerInput
                {
                    BottomGLDepth = last - 3.0,
                };
                layers.Add(newGroundLayer);
                insertIndex = layers.Count - 1;
            }

            // �ǉ��s��I��
            SelectedGroundLayerOnDataGrid = layers[insertIndex];

            UpdateBedrockChecks();
            UpdateGroundLayerNo();
            Update();
        }
        //{
        //    // �ύX�O�̏�Ԃ�ۑ�
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);


        //    // �I������Ă���s�̃C���f�b�N�X���擾
        //    int selectedIndex = GroundInput.GroundLayers.IndexOf(SelectedGroundLayerOnDataGrid);

        //    // �I������Ă���s������ꍇ�A���̉��ɒǉ�
        //    if (0 <= selectedIndex && selectedIndex < GroundInput.GroundLayers.Count - 1)
        //    {
        //        // �V���� GroundLayerDataItem ���쐬
        //        var newGroundLayer = new GroundLayerInput
        //        {
        //            BottomGLDepth = (
        //            GroundInput.GroundLayers[selectedIndex].BottomGLDepth +
        //            GroundInput.GroundLayers[selectedIndex + 1].BottomGLDepth) * 0.5,
        //        };
        //        GroundInput.GroundLayers.Insert(selectedIndex + 1, newGroundLayer);
        //    }
        //    else
        //    {
        //        // �V���� GroundLayerDataItem ���쐬
        //        var newGroundLayer = new GroundLayerInput
        //        {
        //            BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - 3.0,
        //        };

        //        // �I���sindex���ŏI�s�ɂ��킹��
        //        selectedIndex = GroundInput.GroundLayers.Count - 1;

        //        // �I������Ă���s���Ȃ��ꍇ�A�����ɒǉ�
        //        GroundInput.GroundLayers.Add(newGroundLayer);
        //    }

        //    // �I���s��ǉ��s�ɂ��炷
        //    SelectedGroundLayerOnDataGrid = GroundInput.GroundLayers[selectedIndex + 1];

        //    UpdateBedrockChecks();
        //    UpdateGroundLayerNo();
        //    Update();
        //}

        // �S�y�w�폜���\�b�h
        [RelayCommand]
        private void OnDeleteAllGroundLayers()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());
            GroundInput.GroundLayers.Clear();
            UpdateBedrockChecks();
            UpdateGroundLayerNo();
            Update();
        }

        // GroundLayer�ԍ��̍X�V
        private void UpdateGroundLayerNo()
        {
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                GroundInput.GroundLayers[i].No = i + 1;
            }
        }

        // �y���_�ǉ����\�b�h
        [RelayCommand]
        private void OnAddGroundMass()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var masses = GroundInput?.GroundMassesData;
            if (masses == null) return;

            // 0�����͏����l��ǉ�
            if (masses.Count == 0)
            {
                var first = new GroundMassDataInput
                {
                    GLDepth = -1.0, // GL��ŉ����������̑z��
                };
                masses.Add(first);
                SelectedGroundMassOnDataGrid = first;

                UpdateGroundMassDataLayer();
                Update();
                return;
            }

            // �I���s�̒��� or �����֒ǉ�
            int selectedIndex = masses.IndexOf(SelectedGroundMassOnDataGrid);
            int insertIndex;
            GroundMassDataInput newMass;

            if (selectedIndex >= 0 && selectedIndex < masses.Count - 1)
            {
                // �I���s�Ƃ��̉��s�̒��Ԃɒǉ�
                double d1 = masses[selectedIndex].GLDepth;
                double d2 = masses[selectedIndex + 1].GLDepth;
                newMass = new GroundMassDataInput { GLDepth = 0.5 * (d1 + d2) };
                insertIndex = selectedIndex + 1;
                masses.Insert(insertIndex, newMass);
            }
            else
            {
                // �����ɒǉ��i�Ō��GLDepth������[��������j
                double last = masses[masses.Count - 1].GLDepth;
                newMass = new GroundMassDataInput { GLDepth = last - 1.0 };
                masses.Add(newMass);
                insertIndex = masses.Count - 1;
            }

            // �ǉ��s��I��
            SelectedGroundMassOnDataGrid = masses[insertIndex];

            UpdateGroundMassDataLayer();
            Update();
        }
        //{
        //    // �ύX�O�̏�Ԃ�ۑ�
        //    _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //    // �I������Ă���s�̃C���f�b�N�X���擾
        //    int selectedIndex = GroundInput.GroundMassesData.IndexOf(SelectedGroundMassOnDataGrid);

        //    // �I������Ă���s������ꍇ�A���̉��ɒǉ�
        //    if (0 <= selectedIndex && selectedIndex < GroundInput.GroundMassesData.Count - 1)
        //    {
        //        // �V���� GroundLayerDataItem ���쐬
        //        var newGroundMass = new GroundMassDataInput
        //        {
        //            GLDepth = (
        //            GroundInput.GroundMassesData[selectedIndex].GLDepth +
        //            GroundInput.GroundMassesData[selectedIndex + 1].GLDepth) * 0.5,
        //        };

        //        GroundInput.GroundMassesData.Insert(selectedIndex + 1, newGroundMass);
        //    }
        //    else
        //    {
        //        // �V���� GroundLayerDataItem ���쐬
        //        var newGroundMass = new GroundMassDataInput
        //        {
        //            GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
        //        };

        //        // �I���sindex���ŏI�s�ɂ��킹��
        //        selectedIndex = GroundInput.GroundMassesData.Count - 1;

        //        // �I������Ă���s���Ȃ��ꍇ�A�����ɒǉ�
        //        GroundInput.GroundMassesData.Add(newGroundMass);

        //    }

        //    // �I���s��ǉ��s�ɂ��炷
        //    SelectedGroundMassOnDataGrid = GroundInput.GroundMassesData[selectedIndex + 1];

        //    UpdateGroundMassDataLayer();
        //    Update();
        //}

        // �S�y���_�폜���\�b�h
        [RelayCommand]
        private void OnDeleteAllGroundMasses()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            GroundInput.GroundMassesData.Clear();
            UpdateGroundMassDataLayer();
            Update();
        }

        // �I���s��艺�̍s�̓y���_�̊Ԋu��1m�ɑ����郁�\�b�h
        [RelayCommand]
        private void OnMake1mSpacing()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var masses = GroundInput?.GroundMassesData;
            if (masses == null || masses.Count == 0) return;

            // �ΏۊJ�n�ʒu: �I���s�́u���̍s�v����B���I���Ȃ�2�s��(=index=1)����B
            int selectedIndex = masses.IndexOf(SelectedGroundMassOnDataGrid);
            int startIndex = (selectedIndex >= 0) ? selectedIndex + 1 : 1;

            if (startIndex >= masses.Count) return;

            // 1m �s�b�`�� GLDepth ���Ĕz�u�iGLDepth�͉����������j
            for (int i = startIndex; i < masses.Count; i++)
            {
                // �H�w�I��Ղɓ��B������ȍ~�͐G��Ȃ�
                if (masses[i].IsEngineeringBedrock) break;

                masses[i].GLDepth = masses[i - 1].GLDepth - 1.0;
            }

            // �ȍ~�̔h���l�iSpacing, Altitude �Ȃǁj���Čv�Z�E�`��
            UpdateGroundMassDataLayer();
            Update();

            // �G���[�`�F�b�N�i��s��菬�������j�����s���ăt���O���X�V
            bool hasError = ValidateGroundMassMonotone(out string errorMessage);

            // DataGrid �������X�V�i�o�C���f�B���O/�X�^�C���̍ĕ]���Őԕ\���𔽉f�j
            RevalidateAndRefreshGroundMassGrid();

            // �K�v�ɉ����ă��b�Z�[�W�\��
            if (hasError)
            {
                MessageBox.Show(errorMessage, "���̓G���[", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // �u���̍s��菬�����i���[���jGLDepth�ɂȂ��Ă��邩�v�����؂��A�ᔽ�s�� IsError �𗧂Ă�
        private bool ValidateGroundMassMonotone(out string message)
        {
            message = string.Empty;
            var masses = GroundInput?.GroundMassesData;
            if (masses == null || masses.Count == 0) return false;

            // ��������S�s�̃G���[�t���O���N���A
            foreach (var m in masses) m.IsError = false;

            bool hasError = false;
            var lines = new List<string>();

            for (int i = 1; i < masses.Count; i++)
            {
                // �H�w�I��Ոȍ~�͔C�ӂŃX�L�b�v
                if (masses[i].IsEngineeringBedrock) break;

                // ���[Z�̌��؂Ɠ���: ���s�͕K�����̍s���u�������v�K�v������
                if (masses[i].GLDepth >= masses[i - 1].GLDepth)
                {
                    masses[i].IsError = true;
                    hasError = true;
                    lines.Add($"�s {i + 1}: GLDepth �͈��̍s��菬�����l�i���[���l�j�ɂ��Ă��������B");
                }
            }

            if (hasError)
                message = string.Join("\n", lines);

            return hasError;
        }

        // �G���[�`�F�b�N�Ď��s�{�O���b�h�����X�V
        private void RevalidateAndRefreshGroundMassGrid()
        {
            // �K�v�Ȃ� GroundInput ���̐������؂𕹗p�i�G���[�t���O�X�V�������Ă���O��j
            _ = GroundInput?.ValidateForAnalysis(out _);

            var view = CollectionViewSource.GetDefaultView(GroundInput?.GroundMassesData);
            view?.Refresh();

            var grid = GroundWindowInstance?.DataGridGroundMass;
            if (grid == null) return;

            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            grid.Items.Refresh();

            foreach (var item in grid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row) continue;

                foreach (var col in grid.Columns)
                {
                    if (col is DataGridBoundColumn bc)
                    {
                        if (bc.GetCellContent(item) is FrameworkElement fe)
                        {
                            BindingOperations.GetBindingExpression(fe, TextBox.TextProperty)?.UpdateSource();
                            BindingOperations.GetBindingExpression(fe, TextBox.TextProperty)?.UpdateTarget();
                            BindingOperations.GetBindingExpression(fe, TextBlock.TextProperty)?.UpdateSource();
                            BindingOperations.GetBindingExpression(fe, TextBlock.TextProperty)?.UpdateTarget();
                        }
                    }
                }
            }
        }

        // GroundMassDataLayer�ԍ��̍X�V
        private void UpdateGroundMassDataLayer()
        {
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].No = i + 1;
            }
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // �s�ԍ���ݒ�
        }


        private void DataGridGroundLayer_Loaded(object sender, RoutedEventArgs e)
        {
            //if (DataGridGroundLayer.ItemsSource is ObservableCollection<GroundLayerDataItem> observableCollection)
            //{
            //    observableCollection.CollectionChanged += GroundLayerCollection_CollectionChanged;
            //}
        }

        private readonly bool initialSelection = true;


        // GroundNo�R���{�{�b�N�X
        //public void ComboBoxGroundNo_SelectionChanged(int selectedGroundNo, int previousSelectedGroundNo)
        //{
        //    if (selectedGroundNo != 1 && selectedGroundNo == GroundCountPlusOneList[^1])
        //    {
        //        GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + selectedGroundNo.ToString() + ")" });
        //        UpdateGroundsCountPlusOneList();
        //    }

        //    if (previousSelectedGroundNo != -1)
        //    {
        //        GroundInput = GroundsInput[GroundNo - 1];
        //    }
        //    Update();
        //}
        //public void ComboBoxGroundNo_SelectionChanged(int selectedIndex, int previousSelectedIndex)
        //{
        //    // �ύX�O�̏�Ԃ�ۑ�
        //    _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

        //    // selectedIndex: 0-based
        //    if (selectedIndex == GroundCountPlusOneList.Count - 1)
        //    {
        //        // (New)���I�����ꂽ�ꍇ
        //        int newNo = GroundsInput.Count + 1;
        //        GroundsInput.Add(new GroundInput() { GroundRef = "(GR" + newNo.ToString() + ")" });
        //        UpdateGroundsCountPlusOneList();
        //        GroundNo = newNo; // �V�����n�Քԍ��ɐ؂�ւ�
        //        GroundInput = GroundsInput.Last();
        //    }
        //    else
        //    {
        //        if (selectedIndex >= 0 && selectedIndex < GroundsInput.Count)
        //        {
        //            GroundNo = selectedIndex + 1;
        //            GroundInput = GroundsInput[selectedIndex];
        //        }
        //    }
        //    Update();
        //}

        public void GroundTextBox_LostFocus()
        {
            Update();
        }

        [RelayCommand]
        private void OnComboBoxLevelSelectionChanged(int selectedLevel)
        {
            Update();
        }

        //�y���f�[�^�@�y���_�f�[�^�̕���N�l��������
        [RelayCommand]
        private void OnInputAverageNValue()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                List<double> nValues = [];
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundLayerDataItem.BottomAltitude + groundLayerDataItem.LayerThickness > groundMassData.AltitudeDepth &&
                        groundMassData.AltitudeDepth >= groundLayerDataItem.BottomAltitude)
                    {
                        nValues.Add(groundMassData.NValue);
                    }
                }
                if (nValues.Count > 0)
                {
                    groundLayerDataItem.NValue = nValues.Average();
                }
            }
            Update();
        }

        // �y�w�f�[�^�@�y���_�f�[�^�̕���Vs��������
        [RelayCommand]
        private void InputModelAverageVs()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                List<double> vS0 = [];
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundLayerDataItem.BottomAltitude + groundLayerDataItem.LayerThickness > groundMassData.AltitudeDepth &&
                        groundMassData.AltitudeDepth > groundLayerDataItem.BottomAltitude)
                    {
                        vS0.Add(groundMassData.VS0);
                    }
                }
                if (vS0.Count > 0)
                {
                    groundLayerDataItem.Vs = vS0.Average();
                }
            }
            Update(); // �O���t���X�V
        }

        // �y�w�f�[�^�@�ό`�W����N�l�~700��������
        [RelayCommand]
        private void OnInput700N()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                if (groundLayerDataItem.GranularityClass == "�����y" || groundLayerDataItem.GranularityClass == "���I�y")
                {
                    groundLayerDataItem.Es = groundLayerDataItem.NValue * 700;
                }
            }
            Update(); // �O���t���X�V
        }

        // �y�w�f�[�^�@Cu=12.5N, 25N��������
        [RelayCommand]
        private void OnInputC()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (GroundLayerInput groundLayerDataItem in GroundInput.GroundLayers)
            {
                if (groundLayerDataItem.GranularityClass == "�S���y" && groundLayerDataItem.AgeCategory == "���ϑw")
                {
                    groundLayerDataItem.Cohesive = 20 - groundLayerDataItem.BottomGLDepth * 2.0 - groundLayerDataItem.LayerThickness / 2.0;
                }
                else if (groundLayerDataItem.GranularityClass == "�S���y" && groundLayerDataItem.AgeCategory == "�^�ϑw")
                {
                    groundLayerDataItem.Cohesive = groundLayerDataItem.NValue * 12.5;
                }
            }
            Update(); // �O���t���X�V
        }

        [RelayCommand]
        private void OnApplyTypicalFc()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            foreach (var groundMassDataItem in GroundInput.GroundMassesData)
            {
                if (groundMassDataItem.GranularityClass == "�����y" || groundMassDataItem.GranularityClass == "���I�y")
                {
                    groundMassDataItem.Fc = 10;
                }
                else if (groundMassDataItem.GranularityClass == "�S���y")
                {
                    groundMassDataItem.Fc = 70;
                }

            }
            Update(); // �O���t���X�V
        }

        [RelayCommand]
        private void OnApplyGroundLayerNValue()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

        }


        // View����邽�߂̃��\�b�h
        [RelayCommand]
        private void CloseWindow()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }


        public bool ValidateForAnalysis(out string warningMessage)
        {
            bool hasWarning = false;
            warningMessage = "�ȉ��̍��ڂɖ�肪����܂�:\n";

            if (GroundsInput != null)
            {
                for (int i = 0; i < GroundsInput.Count; i++)
                {
                    if (!GroundsInput[i].ValidateForAnalysis(out string groundWarning))
                    {
                        hasWarning = true;
                        warningMessage += $"- �n�Քԍ�{i + 1}:\n{groundWarning}";
                    }
                }
            }
            return !hasWarning;
        }

        [RelayCommand]
        private void OnOk()
        {
            if (GroundsInput != null)
            {
                if (!_mainWindowViewModel.CheckAndResetElementSplit("�n��"))
                    return; // �L�����Z�����͏������f

                bool hasWarning = false;
                string warningMessage = "�ȉ��̍��ڂɖ�肪����܂�:\n";

                for (int i = 0; i < GroundsInput.Count; i++)
                {
                    if (!GroundsInput[i].ValidateForAnalysis(out string groundWarning))
                    {
                        hasWarning = true;
                        warningMessage += $"- �n�Քԍ�{i + 1}:\n{groundWarning}";
                    }
                }

                if (hasWarning)
                {
                    warningMessage += "\n��Ԃ�ۑ����ăE�B���h�E����܂����H";
                    MessageBoxResult result = MessageBox.Show(warningMessage, "�x��", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Cancel) return;
                }

                // �[���R�s�[���쐬���đ��
                InputModel.GroundsInput.Clear();

                foreach (var groundInput in GroundsInput)
                {
                    InputModel.GroundsInput.Add(groundInput.DeepCopy());
                }
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // InputModel.GroundsInput���N���A
            InputModel.GroundsInput.Clear();

            // PrevGroundsInput�̓��e��InputModel.GroundsInput�ɒǉ�
            foreach (var groundInput in PrevGroundsInput)
            {
                InputModel.GroundsInput.Add(groundInput.DeepCopy());
            }

            // �_�C�A���O�����
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void Update()
        {
            if (GroundInput.GroundLayers.Count != 0)
            {
                RecalculateGroundLayerNo();
                RecalculateLayerThickness();
                RecalculateBottomAltitude();
            }

            if (GroundInput.GroundMassesData.Count != 0)
            {
                RecalculateGroundMassDataNo();
                RecalculateMassSpacing();
                RecalculateAltitude();
                RecalculateName();
                RecalculateDensityIsEngineeringBedrock();
                RecalculateH();
                RecalculateSigmaZ();
                RecalculateSigmaZPrime();
                RecalculateIsLiquefaction();
                RecalculateNL();
                RecalculateTauLonSigmaZPrime();
                RecalculateTauDonSigmaZprime();
                RecalculateFL();
                RecalculateBetaL();
                RecalculateGammaCy();
                RecalculateSigmaGammaCyH();
                RecalculateMass();
                RecalculateVSE();
            }

            DrawNValueGraph();
            DrawCuGraph();
            DrawVsGraph();
            DrawEsGraph();

            DrawGroundDisplacementGraph();
            DrawFLGraph();
        }

        // �y�w�ԍ��̍Čv�Z
        internal void RecalculateGroundLayerNo()
        {
            for (int i = 0; i < GroundInput.GroundLayers.Count; i++)
            {
                GroundInput.GroundLayers[i].No = i + 1;
            }
        }

        // �y���_�ԍ��̍Čv�Z
        internal void RecalculateGroundMassDataNo()
        {
            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].No = i + 1;
            }
        }

        // ���[Z�̍Čv�Z
        internal void RecalculateBottomAltitude()
        {
            foreach (GroundLayerInput groundLayer in GroundInput.GroundLayers)
            {
                groundLayer.BottomAltitude = groundLayer.BottomGLDepth + GroundInput.GroundTopAltitude;
            }
        }

        // �[���̍Čv�Z
        internal void RecalculateGLDepth()
        {
            double totalThickness = 0;
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                totalThickness += groundMassData.Spacing;
                groundMassData.GLDepth = -totalThickness;
            }
        }

        // �����̍Čv�Z
        internal void RecalculateLayerThickness()
        {
            ObservableCollection<GroundLayerInput> groundLayerInput = GroundInput.GroundLayers;
            for (int i = 0; i < groundLayerInput.Count; i++)
            {
                if (i == 0)
                    groundLayerInput[i].LayerThickness = -groundLayerInput[i].BottomGLDepth;
                else
                    groundLayerInput[i].LayerThickness = -groundLayerInput[i].BottomGLDepth + groundLayerInput[i - 1].BottomGLDepth;
            }
        }


        // �[���̍Čv�Z
        internal void RecalculateBottomGLDepth()
        {
            double totalThickness = 0;
            foreach (GroundLayerInput groundLayer in GroundInput.GroundLayers)
            {
                totalThickness += groundLayer.LayerThickness;
                groundLayer.BottomGLDepth = -totalThickness;
            }
        }

        // �����̍Čv�Z
        internal void RecalculateMassSpacing()
        {
            ObservableCollection<GroundMassDataInput> groundMassesData = GroundInput.GroundMassesData;
            for (int i = 0; i < groundMassesData.Count; i++)
            {
                if (i == 0)
                    groundMassesData[i].Spacing = -groundMassesData[i].GLDepth;
                else
                    groundMassesData[i].Spacing = -groundMassesData[i].GLDepth + groundMassesData[i - 1].GLDepth;
            }
        }

        // Z�̍Čv�Z
        internal void RecalculateAltitude()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.AltitudeDepth = groundMassData.GLDepth + GroundInput.GroundTopAltitude;
            }
        }

        // �����̍Čv�Z
        internal void RecalculateH()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            int count = groundMassesData.Count;

            for (int i = 0; i < count; i++)
            {
                var current = groundMassesData[i];
                if (current.IsEngineeringBedrock)
                    current.H = null;
                else if (i == 0)
                    current.H = (count == 1 || groundMassesData[1].IsEngineeringBedrock) ? current.Spacing : current.Spacing + groundMassesData[1].Spacing * 0.5;
                else if (i == count - 1 || groundMassesData[i + 1].IsEngineeringBedrock)
                    current.H = current.Spacing * 0.5;
                else
                    current.H = groundMassesData[i - 1].Spacing * 0.5 + current.Spacing * 0.5;
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    int count = groundMassesData.Count;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        for (int i = 0; i < count; i++)
        //        {
        //            var current = groundMassesData[i];
        //            if (current.IsEngineeringBedrock)
        //            {
        //                current.H = null;
        //            }
        //            else if (i == 0)
        //            {
        //                if (count == 1 || groundMassesData[1].IsEngineeringBedrock)
        //                {
        //                    current.H = current.Spacing;
        //                }
        //                else
        //                {
        //                    current.H = current.Spacing + groundMassesData[1].Spacing * 0.5;
        //                }
        //            }
        //            else if (i == count - 1 || groundMassesData[i + 1].IsEngineeringBedrock)
        //            {
        //                current.H = current.Spacing * 0.5;
        //            }
        //            else
        //            {
        //                current.H = groundMassesData[i - 1].Spacing * 0.5 + current.Spacing * 0.5;
        //            }
        //        }
        //    }
        //}

        // ���x�A�H�w�I��Ղ̍Čv�Z���\�b�h
        internal void RecalculateDensityIsEngineeringBedrock()
        {
            var masses = GroundInput.GroundMassesData;
            var layers = GroundInput.GroundLayers;

            foreach (var m in masses)
            {
                foreach (var l in layers)
                {
                    if (m.GLDepth >= l.BottomGLDepth)
                    {
                        m.Density = l.Density;
                        m.GranularityClass = l.GranularityClass;
                        m.AgeCategory = l.AgeCategory;
                        m.IsEngineeringBedrock = l.IsEngineeringBedrock;
                        break;
                    }
                }
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    var groundLayers = GroundInput.GroundLayers;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var massData in groundMassesData)
        //        {
        //            foreach (var layer in groundLayers)
        //            {
        //                if (massData.GLDepth >= layer.BottomGLDepth)
        //                {
        //                    massData.Density = layer.Density;
        //                    massData.GranularityClass = layer.GranularityClass;
        //                    massData.AgeCategory = layer.AgeCategory;
        //                    massData.IsEngineeringBedrock = layer.IsEngineeringBedrock;
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}

        // �t�󉻂̍Čv�Z���\�b�h
        //internal void RecalculateIsLiquefaction()
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    double groundWaterGLDepth = GroundInput.GroundWaterGLDepth;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            if (groundMassData.IsEngineeringBedrock)
        //            {
        //                groundMassData.IsLiquefactionLayer = false;
        //            }
        //            else
        //            {
        //                double Fc = groundMassData.Fc;
        //                double z = groundMassData.GLDepth;
        //                groundMassData.IsLiquefactionLayer = Liquefaction.IsLiquefactionLayer(groundWaterGLDepth, z, Fc);
        //            }
        //        }
        //    }
        //}
        internal void RecalculateIsLiquefaction()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            double groundWaterGLDepth = GroundInput.GroundWaterGLDepth;

            foreach (var groundMassData in groundMassesData)
            {
                if (groundMassData.IsEngineeringBedrock)
                {
                    groundMassData.IsLiquefactionLayer = false;
                }
                else
                {
                    double Fc = groundMassData.Fc;
                    double z = groundMassData.GLDepth;
                    groundMassData.IsLiquefactionLayer = Liquefaction.IsLiquefactionLayer(groundWaterGLDepth, z, Fc);
                }
            }
        }

        internal void RecalculateNL()
        {
            var groundMassesData = GroundInput.GroundMassesData;

            foreach (var groundMassData in groundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double CN = Math.Sqrt(100.0 / groundMassData.SigmaZPrime);
                    groundMassData.N1 = CN * groundMassData.NValue;
                    groundMassData.DeltaNf = 0.0;

                    double Fc = groundMassData.Fc;
                    if (Fc >= 5.0 && Fc < 10.0)
                        groundMassData.DeltaNf = 6.0 / 5.0 * (Fc - 5.0);
                    else if (Fc >= 10.0 && Fc < 20.0)
                        groundMassData.DeltaNf = 0.2 * (Fc - 10.0) + 6.0;
                    else if (Fc >= 20.0 && Fc <= 50.0)
                        groundMassData.DeltaNf = 0.1 * (Fc - 20.0) + 8.0;

                    groundMassData.NL = groundMassData.N1 + groundMassData.DeltaNf;
                }
                else
                {
                    groundMassData.N1 = null;
                    groundMassData.DeltaNf = null;
                    groundMassData.NL = null;
                }
            }
        }
        //internal void RecalculateNL()
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double CN = Math.Sqrt(100.0 / groundMassData.SigmaZPrime);
        //                groundMassData.N1 = CN * groundMassData.NValue;
        //                groundMassData.DeltaNf = 0.0;

        //                double Fc = groundMassData.Fc;
        //                if (Fc >= 5.0 && Fc < 10.0)
        //                {
        //                    groundMassData.DeltaNf = 6.0 / 5.0 * (Fc - 5.0);
        //                }
        //                else if (Fc >= 10.0 && Fc < 20.0)
        //                {
        //                    groundMassData.DeltaNf = 0.2 * (Fc - 10.0) + 6.0;
        //                }
        //                else if (Fc >= 20.0 && Fc <= 50.0)
        //                {
        //                    groundMassData.DeltaNf = 0.1 * (Fc - 20.0) + 8.0;
        //                }
        //                groundMassData.NL = groundMassData.N1 + groundMassData.DeltaNf;
        //            }
        //            else
        //            {
        //                groundMassData.N1 = null;
        //                groundMassData.DeltaNf = null;
        //                groundMassData.NL = null;
        //            }
        //        }
        //    }
        //}

        // ��L/��z'
        internal void RecalculateTauLonSigmaZPrime()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double _NL = groundMassData.NL.GetValueOrDefault();
                    groundMassData.TauLonSigmaZPrime = 0.0410 * (Math.Sqrt(_NL) + 0.00903 * Math.Pow(_NL / 10, 7));
                }
                else
                {
                    groundMassData.TauLonSigmaZPrime = null;
                }
            }
        }
        //internal void RecalculateTauLonSigmaZPrime()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double _NL = groundMassData.NL.GetValueOrDefault();
        //                groundMassData.TauLonSigmaZPrime = 0.0410 * (Math.Sqrt(_NL) + 0.00903 * Math.Pow(_NL / 10, 7));
        //            }
        //            else
        //            {
        //                groundMassData.TauLonSigmaZPrime = null;
        //            }
        //        }
        //    }
        //}

        // Kohji Tokimatsu and Yoshiaki Yoshimi (1983) Empirical correlation of
        // soil Liquefaction based on SPT N-value and fines content,
        // "Soils and Foundations, vol 23, No. 4, pp. 56-74
        //internal void RecalculateTauLonSigmaZPrime2()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            if (groundMassData.IsLiquefactionLayer)
        //            {
        //                double _NL = groundMassData.NL.GetValueOrDefault();
        //                double Cs = 80;
        //                double a = 0.45;
        //                double Cr = 0.57;
        //                double n = 14;
        //                groundMassData.TauLonSigmaZPrime = a * Cr * (16 * Math.Sqrt(_NL) / 100.0 + Math.Pow(16 * Math.Sqrt(_NL) / Cs, n));

        //            }
        //            else
        //            {
        //                groundMassData.TauLonSigmaZPrime = null;
        //            }
        //        }
        //    }
        //}
        internal void RecalculateTauLonSigmaZPrime2()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                if (groundMassData.IsLiquefactionLayer)
                {
                    double _NL = groundMassData.NL.GetValueOrDefault();
                    double Cs = 80;
                    double a = 0.45;
                    double Cr = 0.57;
                    double n = 14;
                    groundMassData.TauLonSigmaZPrime = a * Cr * (16 * Math.Sqrt(_NL) / 100.0 + Math.Pow(16 * Math.Sqrt(_NL) / Cs, n));
                }
                else
                {
                    groundMassData.TauLonSigmaZPrime = null;
                }
            }
        }

        // ��d/��z'
        internal void RecalculateTauDonSigmaZprime()
        {
            double magnitude = 7.5;
            double rn = 0.1 * (magnitude - 1.0);
            double alphaMax = 3.5;
            double gravity = 9.8;

            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {

                if (levelIndex == 0)
                { alphaMax = GroundInput.GroundAcceleration1; }
                else if (levelIndex == 1)
                { alphaMax = GroundInput.GroundAcceleration2; }

                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.RD = 1.0 - 0.015 * Math.Abs(groundMassData.GLDepth);
                        double sigmaZ = groundMassData.SigmaZ;
                        double sigmaZPrime = groundMassData.SigmaZPrime;
                        groundMassData.TauDonSigmaZPrime[levelIndex] = rn * alphaMax / gravity * sigmaZ / sigmaZPrime * groundMassData.RD;
                    }
                    else
                    {
                        groundMassData.RD = null;
                        groundMassData.TauDonSigmaZPrime[levelIndex] = null;
                    }
                }
            }
        }

        // FL
        internal void RecalculateFL()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.FL[levelIndex] = groundMassData.TauLonSigmaZPrime / groundMassData.TauDonSigmaZPrime[levelIndex];
                    }
                    else
                    {
                        groundMassData.FL[levelIndex] = null;
                    }
                }
            }
        }

        // ��cy
        internal void RecalculateGammaCy()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.GammaCy[levelIndex]
                            = Liquefaction.CalculateGammaCy(
                                groundMassData.NL.GetValueOrDefault(), groundMassData.TauDonSigmaZPrime[levelIndex].GetValueOrDefault());
                    }
                    else
                    {
                        groundMassData.GammaCy[levelIndex] = null;
                    }
                }
            }
        }

        // ����cyH
        internal void RecalculateSigmaGammaCyH()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                double _sigmaGammaCyH = 0;
                for (int i = GroundInput.GroundMassesData.Count - 1; i >= 0; i--)
                {
                    if (GroundInput.GroundMassesData[i].IsEngineeringBedrock == true)
                    {
                        GroundInput.GroundMassesData[i].SigmaGammaCyH[levelIndex] = 0.0;
                    }
                    else if (GroundInput.GroundMassesData[i].IsEngineeringBedrock == false)
                    {
                        _sigmaGammaCyH += GroundInput.GroundMassesData[i].GammaCy[levelIndex].GetValueOrDefault() / 100.0
                            * GroundInput.GroundMassesData[i].H.GetValueOrDefault() * 1000.0;
                        GroundInput.GroundMassesData[i].SigmaGammaCyH[levelIndex] = _sigmaGammaCyH;
                    }
                }
            }
        }

        // ��L
        internal void RecalculateBetaL()
        {
            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
                {
                    if (groundMassData.IsLiquefactionLayer)
                    {
                        groundMassData.BetaL[levelIndex] = Liquefaction.CalculateBetaL(groundMassData.GLDepth, groundMassData.NL.GetValueOrDefault());
                    }
                    else
                    {
                        groundMassData.BetaL[levelIndex] = null;
                    }
                }
            }
        }

        internal void RecalculateName()
        {
            var masses = GroundInput.GroundMassesData;
            var layers = GroundInput.GroundLayers;

            foreach (var m in masses)
            {
                bool found = false;
                foreach (var l in layers)
                {
                    if (m.GLDepth >= l.BottomGLDepth)
                    {
                        m.LayerNo = layers.IndexOf(l) + 1;
                        m.Name = l.Name;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    m.LayerNo = null;
                    m.Name = "";
                }
            }
        }
        //{
        //    var groundMassesData = GroundInput.GroundMassesData;
        //    var groundLayers = GroundInput.GroundLayers;

        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (var groundMassData in groundMassesData)
        //        {
        //            bool found = false;
        //            foreach (var layer in groundLayers)
        //            {
        //                if (groundMassData.GLDepth >= layer.BottomGLDepth)
        //                {
        //                    groundMassData.LayerNo = groundLayers.IndexOf(layer) + 1;
        //                    groundMassData.Name = layer.Name;
        //                    found = true;
        //                    break;
        //                }
        //            }
        //            if (!found)
        //            {
        //                groundMassData.LayerNo = null;
        //                groundMassData.Name = "";
        //            }
        //        }
        //    }
        //}

        // ��z
        internal void RecalculateSigmaZ()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.SigmaZ = 0.0;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
                    {
                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
                    }
                    else
                    {
                        if (j == 0)
                        {
                            groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * (0 - groundMassData.GLDepth);
                        }
                        else
                        {
                            groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
                                * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
                        }
                        break;
                    }
                }
            }
        }
        //internal void RecalculateSigmaZ()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            groundMassData.SigmaZ = 0.0;

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
        //                {
        //                    groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
        //                }
        //                else
        //                {
        //                    if (j == 0)
        //                    {
        //                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
        //                            * (0 - groundMassData.GLDepth);
        //                    }
        //                    else
        //                    {
        //                        groundMassData.SigmaZ += GroundInput.GroundLayers[j].Density
        //                            * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
        //                    }
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}

        // ��z'
        internal void RecalculateSigmaZPrime()
        {
            foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
            {
                groundMassData.SigmaZPrime = 0.0;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
                    {
                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
                    }
                    else
                    {
                        if (j == 0)
                        {
                            groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * (0 - groundMassData.GLDepth);
                        }
                        else
                        {
                            groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
                                * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
                        }
                    }
                }
                groundMassData.SigmaZPrime -= 10.0 * Math.Max(0.0, GroundInput.GroundWaterGLDepth - groundMassData.GLDepth);
            }
        }
        //internal void RecalculateSigmaZPrime()
        //{
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        foreach (GroundMassDataInput groundMassData in GroundInput.GroundMassesData)
        //        {
        //            groundMassData.SigmaZPrime = 0.0;

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                if (groundMassData.GLDepth <= GroundInput.GroundLayers[j].BottomGLDepth)
        //                {
        //                    groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density * GroundInput.GroundLayers[j].LayerThickness;
        //                }
        //                else
        //                {
        //                    if (j == 0)
        //                    {
        //                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
        //                            * (0 - groundMassData.GLDepth);
        //                    }
        //                    else
        //                    {
        //                        groundMassData.SigmaZPrime += GroundInput.GroundLayers[j].Density
        //                            * Math.Max(0, GroundInput.GroundLayers[j - 1].BottomGLDepth - groundMassData.GLDepth);
        //                    }
        //                }
        //            }
        //            groundMassData.SigmaZPrime -= 10.0 * Math.Max(0.0, GroundInput.GroundWaterGLDepth - groundMassData.GLDepth);
        //        }
        //    }
        //}

        // M
        internal void RecalculateMass()
        {
            double zi1;
            double zi2;
            double zj1;
            double zj2;

            for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
            {
                GroundInput.GroundMassesData[i].Mass = 0.0;

                if (i == 0)
                    zi1 = 0;
                else
                    zi1 = (GroundInput.GroundMassesData[i - 1].GLDepth + GroundInput.GroundMassesData[i].GLDepth) / 2.0;

                if (i != GroundInput.GroundMassesData.Count - 1)
                    zi2 = (GroundInput.GroundMassesData[i].GLDepth + GroundInput.GroundMassesData[i + 1].GLDepth) / 2.0;
                else
                    zi2 = GroundInput.GroundMassesData[i].GLDepth;

                for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
                {
                    zj1 = GroundInput.GroundLayers[j].BottomGLDepth + GroundInput.GroundLayers[j].LayerThickness;
                    zj2 = GroundInput.GroundLayers[j].BottomGLDepth;

                    GroundInput.GroundMassesData[i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0)
                        * GroundInput.GroundLayers[j].Density / 9.806665;
                }
            }
        }
        //internal void RecalculateMass()
        //{
        //    double zi1;
        //    double zi2;
        //    double zj1;
        //    double zj2;
        //    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
        //    {
        //        for (int i = 0; i < GroundInput.GroundMassesData.Count; i++)
        //        {
        //            GroundInput.GroundMassesData[i].Mass = 0.0;
        //            if (i == 0)
        //            {
        //                zi1 = 0;
        //            }
        //            else
        //            {
        //                zi1 = (GroundInput.GroundMassesData[i - 1].GLDepth + GroundInput.GroundMassesData[i].GLDepth) / 2.0;
        //            }

        //            if (i != GroundInput.GroundMassesData.Count - 1)
        //            {
        //                zi2 = (GroundInput.GroundMassesData[i].GLDepth + GroundInput.GroundMassesData[i + 1].GLDepth) / 2.0;
        //            }
        //            else
        //            {
        //                zi2 = GroundInput.GroundMassesData[i].GLDepth;
        //            }

        //            for (int j = 0; j < GroundInput.GroundLayers.Count; j++)
        //            {
        //                zj1 = GroundInput.GroundLayers[j].BottomGLDepth + GroundInput.GroundLayers[j].LayerThickness;
        //                zj2 = GroundInput.GroundLayers[j].BottomGLDepth;

        //                GroundInput.GroundMassesData[i].Mass += Math.Max(Math.Min(zi1, zj1) - Math.Max(zi2, zj2), 0) * GroundInput.GroundLayers[j].Density / 9.806665;
        //            }
        //        }
        //    }
        //}

        //Vse
        internal void RecalculateVSE()
        {
            var groundMassesData = GroundInput.GroundMassesData;
            //var groundLayers = GroundInput.GroundLayers;
            var bedrockDensity = GroundInput.BedrockDensity;
            var bedrockShearWaveVelocity = GroundInput.BedrockShearWaveVelocity;
            var shallowSoilType = GroundInput.ShallowSoilType;
            var calculationMethod = GroundInput.CalculationMethod;

            for (int levelIndex = 0; levelIndex < 2; levelIndex++)
            {
                // �n�k�׏d�ɂ�茈�܂�W��
                double L = (levelIndex == 0) ? 0.2 : 1.0;

                // �n��W��
                double Z = 1.0;

                // �\�w�̓y���̓��I�ό`�������猈�܂�萔
                double CAlpha = (shallowSoilType == "�S���y") ? 25.0 : 40.0;

                double T0 = 0.0; // �����l
                double SigmaH = 0.0; // �����l
                double SigmaGammaVS0H = 0.0; // �����l
                foreach (var groundMassData in groundMassesData)
                {
                    if (groundMassData.IsEngineeringBedrock) break;

                    var h = groundMassData.H.GetValueOrDefault();
                    var vs0 = groundMassData.VS0;
                    var density = groundMassData.Density;

                    T0 += 4.0 * h / vs0;
                    SigmaH += h;
                    SigmaGammaVS0H += density * vs0 * h;
                }

                // �n�Ղ̒n�k���̌ŗL�����̂̂�
                double alpha = Math.Min(1 + L * Z * CAlpha * T0 / SigmaH, 4.0);

                GroundInput.NaturalPeriod = T0;
                GroundInput.NaturalPeriods[levelIndex] = alpha * T0;

                // �n�Ղ̕\�w�ƍH�w�I��Ղ̏����C���s�[�_���X��
                double Rz0 = SigmaGammaVS0H / (bedrockDensity * bedrockShearWaveVelocity * SigmaH);
                double beta = 3.0 / 4.0 * (1.0 - 1.0 / Math.Pow(2.0, alpha - 1.0)) / (1 - Rz0);

                double mu = 0.0;
                double uNPlusOne = 0.0;

                for (int i = 0; i < groundMassesData.Count; i++)
                {
                    var groundMassData = groundMassesData[i];
                    var density = groundMassData.Density;
                    var vs0 = groundMassData.VS0;
                    //var h = groundMassData.H.GetValueOrDefault();

                    // ����S�g���x
                    groundMassData.VSE[levelIndex] = Math.Pow(density * vs0 / bedrockDensity / bedrockShearWaveVelocity, beta) * vs0;

                    // ��������f�΂ˍ���
                    groundMassData.K[levelIndex] = density / 9.80665 * Math.Pow(groundMassData.VSE[levelIndex], 2.0) / groundMassData.Spacing;

                    if (i == 0)
                    {
                        groundMassData.U[levelIndex] = 1.0; // �n�\�ɂ�����ψ�
                    }
                    else
                    {
                        mu += groundMassesData[i - 1].Mass * groundMassesData[i - 1].U[levelIndex];
                        groundMassData.U[levelIndex] = groundMassesData[i - 1].U[levelIndex] - 40.0 / groundMassesData[i - 1].K[levelIndex] / Math.Pow(alpha * T0, 2.0) * mu;
                    }

                    if (groundMassData.IsEngineeringBedrock && i < groundMassesData.Count - 1)
                    {
                        uNPlusOne = groundMassData.U[levelIndex];
                        for (int j = i + 1; j < groundMassesData.Count; j++)
                        {
                            groundMassesData[j].U[levelIndex] = 0.0;
                        }
                        break;
                    }
                    else if (i == groundMassesData.Count - 1)
                    {
                        uNPlusOne = groundMassData.U[levelIndex];
                    }
                }

                foreach (var groundMassData in groundMassesData)
                {
                    groundMassData.UStar[levelIndex] = (groundMassData.U[levelIndex] - uNPlusOne) / (1 - uNPlusOne);
                    if (groundMassData.IsEngineeringBedrock)
                    {
                        for (int j = groundMassesData.IndexOf(groundMassData) + 1; j < groundMassesData.Count; j++)
                        {
                            groundMassesData[j].UStar[levelIndex] = 0.0;
                        }
                        break;
                    }
                }

                double fA = Math.Min(1.6 * alpha * T0, 1);
                double C1 = (shallowSoilType == "�S���y") ? 0.0028 : 0.0015;
                double C2 = (shallowSoilType == "�S���y") ? 0.53 : 0.666;
                double Dmax = 0;

                if (calculationMethod == "a1(b1)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH * (C2 * (1 - 1 / Math.Pow(alpha, 2.0)) + 2.0 * Rz0 / alpha);
                }
                else if (calculationMethod == "a2(b2)")
                {
                    Dmax = C1 * (Math.Pow(alpha, 2.0) - 1.0) * fA * SigmaH;
                }

                foreach (var groundMassData in groundMassesData)
                {
                    groundMassData.DmaxUStar[levelIndex] = Dmax * groundMassData.UStar[levelIndex] * 1000.0;
                    groundMassData.DmaxUStarSigmaGammaCyH[levelIndex] = groundMassData.DmaxUStar[levelIndex] + groundMassData.SigmaGammaCyH[levelIndex];
                }
            }
        }

        public void DataGridGroundLayer_CellEditEnding()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            Update();
        }

        private void DataGridGroundMass_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.EditingElement is not TextBox editedTextBox) return;
            if (!double.TryParse(editedTextBox.Text, out double doubleValue)) return;
            if (e.Column is not DataGridBoundColumn boundColumn || boundColumn.Binding is not Binding binding) return;

            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            var targetData = e.Row?.Item as GroundMassDataInput;
            if (targetData == null) return;

            switch (binding.Path.Path)
            {
                case nameof(GroundMassDataInput.Spacing): targetData.Spacing = doubleValue; break;
                case nameof(GroundMassDataInput.Fc): targetData.Fc = doubleValue; break;
                case nameof(GroundMassDataInput.NValue): targetData.NValue = doubleValue; break;
                case nameof(GroundMassDataInput.VS0): targetData.VS0 = doubleValue; break;
            }

            Update();
            RevalidateAndRefreshGroundMassGrid();
        }
        //{
        //    if (e.EditAction == DataGridEditAction.Commit && e.EditingElement is TextBox editedTextBox)
        //    {
        //        if (double.TryParse(editedTextBox.Text, out double doubleValue))
        //        {
        //            if (e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is System.Windows.Data.Binding binding)
        //            {
        //                // �ύX�O�̏�Ԃ�ۑ�
        //                _undoManager.PushState([.. GroundsInput.Select(x => x.DeepCopy())]);

        //                string bindingPath = binding.Path.Path;
        //                Console.WriteLine($"Binding Name: {bindingPath}");

        //                var targetData = GroundInput.GroundMassesData[GroundNo - 1];
        //                switch (bindingPath)
        //                {
        //                    case "Spacing":
        //                        targetData.Spacing = doubleValue;
        //                        break;
        //                    case "Fc":
        //                        targetData.Fc = doubleValue;
        //                        break;
        //                    case "NValue":
        //                        targetData.NValue = doubleValue;
        //                        break;
        //                    case "VS0":
        //                        targetData.VS0 = doubleValue;
        //                        break;
        //                }
        //            }
        //        }
        //    }
        //}

        private bool _isUpdatingValues = true;

        public void TextBoxGroundWaterTableAltitude_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // �ύX�O�̏�Ԃ�ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                _isUpdatingValues = false;
                GroundInput.GroundWaterGLDepth = GroundInput.GroundWaterTableAltitude - GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                // UI�E�O���t���̍ĕ`��
                Update();
            }
        }

        public void TextBoxGroundStressAltitude_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // �ύX�O�̏�Ԃ�ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                _isUpdatingValues = false;
                GroundInput.StressGLDepth = GroundInput.StressAltitude - GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                // UI�E�O���t���̍ĕ`��
                Update();
            }
        }

        public void TextBoxStressGLDepth_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // �ύX�O�̏�Ԃ�ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                _isUpdatingValues = false;
                GroundInput.StressAltitude = GroundInput.StressGLDepth + GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;

                //UI�X�V
                Update();
            }
        }

        public void TextBoxGroundWaterGLDepth_LostFocus()
        {
            if (_isUpdatingValues)
            {
                // �ύX�O�̏�Ԃ�ۑ�
                _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

                _isUpdatingValues = false;
                GroundInput.GroundWaterTableAltitude = GroundInput.GroundWaterGLDepth + GroundInput.GroundTopAltitude;
                _isUpdatingValues = true;
                Update();
            }
        }

        public void DataGridGroundLayer_RowEditEnding(/*string newText*/)
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            Update();
        }

        public void GroundTopAltitudeTextBox_LostFocus()
        {
            // �ύX�O�̏�Ԃ�ۑ�
            _undoManager.PushState(GroundsInput.Select(x => x.DeepCopy()).ToList());

            GroundInput.GroundWaterTableAltitude = GroundInput.GroundWaterGLDepth + GroundInput.GroundTopAltitude;
            GroundInput.StressAltitude = GroundInput.StressGLDepth + GroundInput.GroundTopAltitude;

            // UI�E�O���t���̍ĕ`��
            Update();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                // �e�L�X�g�{�b�N�X���t�H�[�J�X�������Ă��Ȃ��ꍇ�A�t�H�[�J�X��ݒ肵�A�S�e�L�X�g��I��
                textBox.Focus();
                e.Handled = true; // �}�E�X�N���b�N�C�x���g�̏����������Ŋ���������
            }
        }

        // �y�w���͓��R���{�{�b�N�X�ω����̃��\�b�h
        public void ComboBox_SelectionChangedCommand()
        {
            Update(); // Update() ���Ăяo���ăO���t���X�V
        }
    }

    [Serializable]
    public class CustomScatterPoint(double x, double y, string text) : ObservablePoint(x, y)
    {
        // �e�L�X�g���x���̃v���p�e�B
        public string Text { get; set; } = text;
    }
}
