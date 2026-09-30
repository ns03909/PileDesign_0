using System.Collections.Generic;

namespace PileDesign.Models.InputData
{
    /// <summary>断面の限界状態 (曲げ・せん断の限界曲線の種類)。</summary>
    public enum SectionLimitState
    {
        /// <summary>使用限界 (長期)。</summary>
        Service,
        /// <summary>損傷限界 (レベル1・耐震グレード S のレベル2)。</summary>
        Damage,
        /// <summary>安全限界 (レベル2)。</summary>
        Ultimate,
    }

    /// <summary>
    /// せん断の限界値の根拠: 適用した式・その式に入れた値 (係数・入力と単位)・式で求めた値 [N]。
    /// 検定の各行について、計算書で「どの式にどの値を入れてこの限界値になったか」を示すために使う。
    /// </summary>
    public sealed record ShearLimitBasis(string Formula, IReadOnlyList<(string Item, string Value)> Terms, double ValueN);
}
