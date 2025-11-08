using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PileDesignCore
{
    [Serializable]
    public class BaseViewModel : INotifyPropertyChanged
    {
        // INotifyPropertyChanged を実装するためのイベントハンドラ
        public event PropertyChangedEventHandler PropertyChanged;

        // プロパティ名によって自動的にセットされる
        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // 新たに追加されたコマンドを処理するメソッド
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
