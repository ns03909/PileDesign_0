using System.Collections.ObjectModel;

namespace PileDesignCore.Shared
{
    /// <summary>
    /// ObservableCollectionの初期化を簡略化するヘルパークラス
    /// </summary>
    public static class CollectionHelper
    {
        /// <summary>
        /// 指定されたサイズのObservableCollection&lt;string&gt;を作成（空文字列で初期化）
        /// </summary>
        /// <param name="size">コレクションのサイズ</param>
        /// <returns>初期化されたObservableCollection</returns>
        public static ObservableCollection<string> CreateStringCollection(int size)
        {
            return new ObservableCollection<string>(new string[size]);
        }

        /// <summary>
        /// 指定されたサイズのObservableCollection&lt;double&gt;を作成（0.0で初期化）
        /// </summary>
        /// <param name="size">コレクションのサイズ</param>
        /// <returns>初期化されたObservableCollection</returns>
        public static ObservableCollection<double> CreateDoubleCollection(int size)
        {
            return new ObservableCollection<double>(new double[size]);
        }

        /// <summary>
        /// 指定されたサイズのObservableCollection&lt;int&gt;を作成（0で初期化）
        /// </summary>
        /// <param name="size">コレクションのサイズ</param>
        /// <returns>初期化されたObservableCollection</returns>
        public static ObservableCollection<int> CreateIntCollection(int size)
        {
            return new ObservableCollection<int>(new int[size]);
        }

        /// <summary>
        /// 指定されたサイズのObservableCollection&lt;T&gt;を作成（デフォルト値で初期化）
        /// </summary>
        /// <typeparam name="T">コレクションの要素の型</typeparam>
        /// <param name="size">コレクションのサイズ</param>
        /// <returns>初期化されたObservableCollection</returns>
        public static ObservableCollection<T> CreateCollection<T>(int size)
        {
            return new ObservableCollection<T>(new T[size]);
        }

        /// <summary>
        /// 文字列の配列からObservableCollection&lt;string&gt;を作成
        /// </summary>
        /// <param name="items">初期値の配列</param>
        /// <returns>初期化されたObservableCollection</returns>
        public static ObservableCollection<string> CreateStringCollection(params string[] items)
        {
            return new ObservableCollection<string>(items);
        }

        /// <summary>
        /// ソースコレクションを変換してObservableCollection&lt;string&gt;を作成
        /// </summary>
        /// <typeparam name="T">ソースの型</typeparam>
        /// <param name="source">ソースコレクション</param>
        /// <param name="selector">各要素を文字列に変換する関数</param>
        /// <returns>変換されたObservableCollection</returns>
        public static ObservableCollection<string> ToObservableStringCollection<T>(
            this System.Collections.Generic.IEnumerable<T> source,
            System.Func<T, string> selector)
        {
            var result = new ObservableCollection<string>();
            foreach (var item in source)
            {
                result.Add(selector(item));
            }
            return result;
        }

        /// <summary>
        /// ソースコレクションからObservableCollectionを作成
        /// </summary>
        /// <typeparam name="T">要素の型</typeparam>
        /// <param name="source">ソースコレクション</param>
        /// <returns>作成されたObservableCollection</returns>
        public static ObservableCollection<T> ToObservableCollection<T>(
            this System.Collections.Generic.IEnumerable<T> source)
        {
            return new ObservableCollection<T>(source);
        }
    }
}
