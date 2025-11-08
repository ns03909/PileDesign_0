using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace PileDesignCore
{
    [Serializable]
    public class ApplicationViewModel : INotifyPropertyChanged
    {
        // イベントハンドラー
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        public FundamentalViewModel FundamentalViewModel { get; set; }
        public LoadCaseViewModel LoadCaseViewModel { get; set; }
        public GroundLayerViewModel GroundLayerViewModel { get; set; }
        public PileBodyViewModel PileBodyViewModel { get; set; }
        public PileLayoutViewModel PileLayoutViewModel { get; set; }
        public EmbedmentViewModel EmbedmentViewModel { get; set; }
        public ThreeDViewModel DataContext3D { get; set; }
        //public ViewModelTest DataContextTest { get; set; }

        // コンストラクタ //
        public ApplicationViewModel()
        {
            FundamentalViewModel = new FundamentalViewModel();
            LoadCaseViewModel = new LoadCaseViewModel();
            GroundLayerViewModel = new GroundLayerViewModel();
            PileBodyViewModel = new PileBodyViewModel();
            PileLayoutViewModel = new PileLayoutViewModel();
            EmbedmentViewModel = new EmbedmentViewModel();
            DataContext3D = new ThreeDViewModel(this);
            //DataContextTest = new ViewModelTest();
        }

        // 保存ダイアログボックスを表示して、アプリケーションの状態を保存するメソッド
        public void SaveStateWithDialog()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
                FileName = "applicationState_" + DateTime.Now.ToString("yyMMdd_HHmmss") + ".bin"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SaveState(saveFileDialog.FileName);
            }
        }

        // アプリケーションの状態を保存するメソッド
        public void SaveState(string filePath)
        {
            // アプリケーションのデータをまとめて保存
            StateSaver.SaveState(this, filePath);
        }

        // ファイルを開いてアプリケーションの状態を読み込むメソッド
        public void LoadStateWithDialog()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadState(openFileDialog.FileName);
            }
        }

        // Word ファイルに保存するメソッド
        public void SaveWordFileWithDialog()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*",
                FileName = "document_" + DateTime.Now.ToString("yyMMdd_HHmmss") + ".docx"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                Output.WordDocument.CreateWordDocument(saveFileDialog.FileName, this);
            }
        }

        // アプリケーションの状態を読み込むメソッド
        private static void CopyProperties(object source, object destination)
        {
            if (source == null || destination == null)
            {
                throw new ArgumentNullException();
            }

            Type sourceType = source.GetType();
            Type destinationType = destination.GetType();

            // 同じ型でない場合は例外を投げるか、処理を続行するか選択できます
            if (sourceType != destinationType)
            {
                throw new ArgumentException("Source and destination types must be the same.");
            }

            // プロパティを取得
            PropertyInfo[] properties = sourceType.GetProperties();

            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead && property.CanWrite)
                {
                    object value = property.GetValue(source);
                    property.SetValue(destination, value);
                }
            }
        }

        // ファイルを開いてアプリケーションの状態を読み込むメソッド
        public void LoadState(string filePath)
        {
            if (File.Exists(filePath))
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open))
                {
                    IFormatter formatter = new BinaryFormatter();
                    object loadedObject = formatter.Deserialize(stream);

                    // アプリケーションのデータをまとめて読み込み
                    CopyProperties(loadedObject, this);
                }
            }
        }
    }
}
