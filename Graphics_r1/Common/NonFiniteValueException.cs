using System;

namespace PileDesign.Common
{
    /// <summary>
    /// 有限でない数値 (NaN・±∞) を入力データに設定しようとしたときの例外。
    /// <see cref="Exception.Message"/> は利用者にそのまま見せられる文にする (どの値のどの欄か)。
    ///
    /// 0 に置き換えて黙って受け入れると、形状や解析が静かに変わり、入力の誤りに気づけない。
    /// 受け付けずに止め、何が悪いかを示す。ファイルの読み込みでこれが起きたときは、
    /// 読み込み全体を失敗させてメッセージを出す (<c>HandleFileLoadError</c>)。
    /// </summary>
    public sealed class NonFiniteValueException : ArgumentException
    {
        public NonFiniteValueException(string message, string? paramName = null)
            : base(message, paramName)
        {
        }

        /// <summary>
        /// <paramref name="value"/> が有限ならそのまま返し、そうでなければ例外を投げる。
        /// </summary>
        /// <param name="what">利用者向けの欄の名前 (例: 「節点 3 の X 座標」)。</param>
        public static double RequireFinite(double value, string what)
            => double.IsFinite(value)
                ? value
                : throw new NonFiniteValueException(
                    $"{what}に有限でない値 ({Describe(value)}) は設定できません。有限の数値にしてください。");

        /// <summary>「NaN」「+∞」「-∞」のように、利用者に見せる表記。</summary>
        public static string Describe(double value)
            => double.IsNaN(value) ? "NaN" : double.IsPositiveInfinity(value) ? "+∞" : "-∞";
    }
}
