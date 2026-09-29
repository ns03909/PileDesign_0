using System;
using System.Security.Cryptography;
using System.Text;

namespace PileDesign.Common
{
    /// <summary>
    /// いま開いているモデルの識別子 (ログ用)。解析の失敗・性能の記録・診断のログに添え、
    /// 複数のファイルを扱ったログでどのモデルの記録かを取り違えないようにする。
    ///
    /// <para>ファイルの場所そのものはログに残さない (フォルダ名に案件名・顧客名が入ることがある)。
    /// 場所の SHA-256 の先頭 8 桁にする。同じファイルなら同じ値になるので、ログどうしを突き合わせられる。</para>
    /// </summary>
    internal static class ModelIdentity
    {
        internal const string Unsaved = "未保存";

        /// <summary>いまのモデルの識別子 (保存していなければ「未保存」)。</summary>
        internal static string Current { get; private set; } = Unsaved;

        /// <summary>開いた・保存したファイルの場所から識別子を決める (場所が無ければ「未保存」)。</summary>
        internal static void SetFromPath(string? path) => Current = Of(path);

        /// <summary>場所の識別子。大文字小文字・相対表記の違いは同じ値にする。</summary>
        internal static string Of(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Unsaved;
            string normalized;
            try { normalized = System.IO.Path.GetFullPath(path).ToUpperInvariant(); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
            {
                normalized = path.ToUpperInvariant();
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
        }
    }
}
