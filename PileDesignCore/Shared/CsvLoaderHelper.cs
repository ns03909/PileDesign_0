using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PileDesignCore.Shared
{
    /// <summary>
    /// CSVファイルからデータを読み込むための汎用ヘルパークラス
    /// </summary>
    public static class CsvLoaderHelper
    {
        /// <summary>
        /// CSVファイルを読み込み、各行をパースして結果を返す
        /// </summary>
        /// <typeparam name="T">変換後の型</typeparam>
        /// <param name="filePath">CSVファイルのパス</param>
        /// <param name="parser">各行をパースする関数</param>
        /// <param name="skipHeader">ヘッダー行をスキップするか</param>
        /// <returns>パースされたデータのリスト</returns>
        public static List<T> LoadFromCsv<T>(string filePath, Func<string[], T> parser, bool skipHeader = false)
        {
            var results = new List<T>();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"CSVファイルが見つかりません: {filePath}");
            }

            using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
            {
                string line;
                bool isFirstLine = true;

                while ((line = reader.ReadLine()) != null)
                {
                    // ヘッダー行をスキップ
                    if (skipHeader && isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }
                    isFirstLine = false;

                    // 空行をスキップ
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split(',');

                    try
                    {
                        T item = parser(parts);
                        if (item != null)
                        {
                            results.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        // パースエラーをログに記録（必要に応じて）
                        Console.WriteLine($"CSV行のパースに失敗: {line}, エラー: {ex.Message}");
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// CSVファイルを読み込み、名前のリストを作成する
        /// </summary>
        /// <typeparam name="T">変換後の型</typeparam>
        /// <param name="filePath">CSVファイルのパス</param>
        /// <param name="parser">各行をパースする関数</param>
        /// <param name="nameSelector">名前を取得する関数</param>
        /// <param name="skipHeader">ヘッダー行をスキップするか</param>
        /// <returns>名前のリスト</returns>
        public static List<string> LoadNamesFromCsv<T>(
            string filePath,
            Func<string[], T> parser,
            Func<T, string> nameSelector,
            bool skipHeader = false)
        {
            var items = LoadFromCsv(filePath, parser, skipHeader);
            var names = new List<string>();

            foreach (var item in items)
            {
                names.Add(nameSelector(item));
            }

            return names;
        }
    }
}
