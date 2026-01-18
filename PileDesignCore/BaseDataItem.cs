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

        // プロパティ変更メソッド
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // 数値バリデーション支援メソッド - NaNやInfinityをfallback値に置き換える
        protected bool SetFiniteDouble(
            ref double field,
            double value,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = fallback;
            return SetProperty(ref field, value, propertyName);
        }

        // 数値バリデーション支援メソッド - 範囲制限付き
        protected bool SetFiniteClampedDouble(
            ref double field,
            double value,
            double min = double.NegativeInfinity,
            double max = double.PositiveInfinity,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = fallback;
            }
            if (value < min) value = min;
            if (value > max) value = max;
            return SetProperty(ref field, value, propertyName);
        }

        // プロパティ変更後にアクションを実行するヘルパーメソッド
        protected bool SetPropertyWithAction<T>(
            ref T field,
            T value,
            System.Action onChanged,
            [CallerMemberName] string propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }

        // プロパティ変更後に複数のプロパティ変更通知を発行するヘルパーメソッド
        protected bool SetPropertyWithNotifications<T>(
            ref T field,
            T value,
            params string[] additionalPropertyNames)
        {
            if (SetProperty(ref field, value))
            {
                foreach (var name in additionalPropertyNames)
                {
                    OnPropertyChanged(name);
                }
                return true;
            }
            return false;
        }
    }
}
