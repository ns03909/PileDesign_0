using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace PileDesign.ViewModels
{
    public partial class VerticalCalculationViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;
        //private InputModel _inputModel = InputModel.Instance;

        // DataGrid上の選択中のGroundLayerデータ
        [ObservableProperty]
        private GroundLayerInput _selectedGroundLayerOnDataGrid;

        // GroundLayer
        [ObservableProperty]
        private ObservableCollection<GroundLayerInput> _selectedGroundLayerCollection = [];

        // 選択地盤番号
        [ObservableProperty]
        private int _selectedGroundNo = 1;

        //// 孔口Z
        //[ObservableProperty]
        //private double _groundTopAltitude;

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;


        //public IRelayCommand OkCommand { get; }
        //public IRelayCommand CancelCommand { get; }


        // コンストラクタ
        public VerticalCalculationViewModel()
        {
            //// コマンドの初期化
            //OkCommand = new RelayCommand(OnOk);
            //CancelCommand = new RelayCommand(OnCancel);
        }

        [RelayCommand]
        private void OnOk()
        {
            // ダイアログを閉じる
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // ダイアログを閉じる
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public static void GroundTextBox_TextChanged(/*string textBoxName, string newValue*/)
        {
            //if (int.TryParse(SelectedGroundNo.ToString(), out int selectedGroundNo))
            //{
            //}
        }

        // GroundNoコンボボックス
        public void ComboBoxGroundNo_SelectionChanged(int selectedGroundNo, int previousSelectedGroundNo)
        {
            if (previousSelectedGroundNo != -1)
            {
                MessageBoxResult result = MessageBox.Show($"地盤番号{previousSelectedGroundNo}の定義を保存しますか？", "確認", MessageBoxButton.YesNoCancel);

                if (result == MessageBoxResult.Yes)
                {
                    SaveStates(previousSelectedGroundNo);
                }
                else if (result == MessageBoxResult.No)
                {
                    // 変更を保存しない場合、何もしない
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // 変更をキャンセルする場合、元の値に戻す
                    return;
                }
            }

            ReadStates(selectedGroundNo);
        }

        private void ReadStates(int groundNo)
        {
            SelectedGroundLayerCollection = InputModel.GroundsInput[groundNo - 1].GroundLayers;
        }

        private void SaveStates(int groundNo)
        {
            InputModel.GroundsInput[groundNo - 1].GroundLayers = SelectedGroundLayerCollection;
        }
    }
}
