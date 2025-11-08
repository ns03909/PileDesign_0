using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PileDesignCore
{
    [Serializable]
    public class BaseDataItem : INotifyPropertyChanged
    {
        // INotifyPropertyChanged を実装するためのイベントハンドラ
        public event PropertyChangedEventHandler PropertyChanged;

        // プロパティ名によって自動的にセットされる
        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // 新たに追加されたコマンドを処理するメソッド
        //protected bool SetProperty<T>(ref T , T value, [CallerMemberName] string propertyName = null)
        //{
        //    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        //    field = value;
        //    OnPropertyChanged(propertyName);
        //    return true;
        //}

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false; // プロパティの値が変更されなかった場合はfalseを返す
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true; // プロパティの値が変更された場合はtrueを返す
        }
    }
}
