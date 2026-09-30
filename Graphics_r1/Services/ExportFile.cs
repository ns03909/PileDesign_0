using System;
using System.IO;
using System.Text;

namespace PileDesign.Services
{
    /// <summary>
    /// 書き出すファイルの形式ごとの約束 (拡張子・文字コード)。
    ///
    /// <para>以前は書き出しごとに文字コードの指定がばらばらで (<c>Encoding.UTF8</c>・<c>new UTF8Encoding(true)</c>・
    /// 指定なし)、拡張子を付けるかどうか、失敗したときの知らせ方も画面ごとに違った。書き出しを足すときはここから選ぶ。</para>
    /// </summary>
    /// <param name="Extension">拡張子 (ピリオドつき)。保存先に付いていなければ付ける。</param>
    /// <param name="Encoding">文字コード。</param>
    /// <param name="Label">知らせに使う名前。</param>
    public sealed record ExportFormat(string Extension, Encoding Encoding, string Label)
    {
        /// <summary>CSV。Excel で開いて文字化けしないよう、UTF-8 の BOM を付ける。</summary>
        public static ExportFormat Csv { get; } = new(".csv", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), "CSV");

        /// <summary>テキスト (ログなど)。メモ帳・Excel で開けるよう BOM を付ける。</summary>
        public static ExportFormat Text { get; } = new(".txt", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), "テキスト");

        /// <summary>JSON。ほかのプログラムが読むので BOM を付けない。</summary>
        public static ExportFormat Json { get; } = new(".json", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), "JSON");
    }

    /// <summary>
    /// 利用者が選んだ場所へのファイルの書き出し。
    /// <list type="bullet">
    /// <item>一時ファイルに書き切ってから差し替える (<see cref="FileOperationService.WriteAtomically"/>)。途中で失敗しても既存のファイルは壊れない。</item>
    /// <item>拡張子と文字コードは形式 (<see cref="ExportFormat"/>) で決める。</item>
    /// <item>失敗したら、原因 (使用中・権限・空き容量など) と、もう一度できることを知らせる (<see cref="DescribeFailure"/>)。</item>
    /// </list>
    /// </summary>
    public static class ExportFile
    {
        /// <summary>保存先に形式の拡張子が付いていなければ付ける (別の拡張子を選んでいればそのまま)。</summary>
        public static string EnsureExtension(string path, ExportFormat format)
            => string.IsNullOrEmpty(Path.GetExtension(path)) ? path + format.Extension : path;

        /// <summary>
        /// 文字列を書き出す。成功したら書いた場所を返し、失敗したら知らせて null を返す
        /// (<paramref name="what"/> は「グラフの CSV」など、何を書き出していたか)。
        /// </summary>
        public static string? TryWriteText(string path, string text, ExportFormat format, string what)
        {
            string target = EnsureExtension(path, format);
            try
            {
                WriteText(target, text, format);
                Serilog.Log.Information("[書き出し] {What} を書き出しました ({Format})", what, format.Label);
                return target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Serilog.Log.Warning(ex, "[書き出し] {What} を書き出せませんでした", what);
                MessageService.Show(DescribeFailure(ex, what), $"{what}の書き出し",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return null;
            }
        }

        /// <summary>文字列を書き出す (失敗は例外のまま)。一時ファイルに書き切ってから差し替える。</summary>
        public static void WriteText(string path, string text, ExportFormat format)
        {
            byte[] bytes = format.Encoding.GetBytes(text);
            byte[] preamble = format.Encoding.GetPreamble();
            FileOperationService.WriteAtomically(path, stream =>
            {
                stream.Write(preamble);
                stream.Write(bytes);
            });
        }

        // Windows のエラー番号 (HRESULT の下位 16 ビット)
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;
        private const int ErrorHandleDiskFull = 39;
        private const int ErrorDiskFull = 112;

        /// <summary>
        /// 書き出しに失敗したときの知らせ。原因ごとに、利用者にできること (閉じる・別の場所を選ぶ・空ける) を書き、
        /// 既存のファイル・入力・計算結果は失われていないことを添える (もう一度書き出せる)。
        /// </summary>
        public static string DescribeFailure(Exception ex, string what)
        {
            int code = ex.HResult & 0xFFFF;
            string cause = ex switch
            {
                UnauthorizedAccessException =>
                    "保存先に書き込む権限がありません。別のフォルダ (ドキュメントなど) を選んでください。"
                    + "読み取り専用のファイルを上書きしようとしたときも、この知らせになります。",
                DirectoryNotFoundException => "保存先のフォルダが見つかりません (ネットワークのフォルダが切れた・USB を外したなど)。別の場所を選んでください。",
                PathTooLongException => "保存先の場所の名前が長すぎます。短い名前のフォルダを選んでください。",
                IOException when code is ErrorSharingViolation or ErrorLockViolation =>
                    "同じ名前のファイルがほかのアプリ (Excel・Word など) で開かれています。そのアプリで閉じてから、もう一度書き出してください。",
                IOException when code is ErrorDiskFull or ErrorHandleDiskFull =>
                    "保存先の空き容量が足りません。不要なファイルを消すか、別のドライブを選んでください。",
                ArgumentException or NotSupportedException => "保存先の名前に使えない文字が含まれています。名前を変えてください。",
                _ => "ファイルを書き出せませんでした。",
            };
            return $"{what}を書き出せませんでした。\n\n{cause}\n\n"
                 + "既存のファイルはそのまま残っています (書き終えてから差し替えるため)。入力と計算結果も失われていないので、もう一度書き出せます。"
                 + "\n(詳しい理由はログに記録しています)";
        }
    }
}
