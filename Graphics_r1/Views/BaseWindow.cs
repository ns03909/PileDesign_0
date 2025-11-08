//using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PileDesign.Views
{
    public class BaseWindow : Window
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // プロパティ変更通知を発行するヘルパーメソッド
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
