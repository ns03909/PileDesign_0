using PileDesign.Constants;
using System;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 既製杭断面のメーカー判定。
    ///
    /// 高支持力杭工法はメーカーごとの大臣認定・評定に基づくため、工法とメーカーの
    /// 組み合わせが正しいかを検査する必要がある（例: Smart-MAGNUM はジャパンパイル、
    /// Hybrid ニーディングは三谷セキサン）。
    ///
    /// 節杭ライブラリ (JP-NPH / JP-NPRC / BF.S) は Maker 列を持つが、
    /// PHC / PRC / SC の一般リストには複数メーカーの製品を追記しているため、
    /// 断面タイプと製品名の接頭辞を併用して判定する。
    /// JIS 規格品 (PHC- / CPRC- / SC-) はメーカーを特定できないので <c>null</c> を返す。
    /// </summary>
    public static class PileMakers
    {
        public const string JapanPile = "ジャパンパイル";
        public const string MitaniSekisan = "三谷セキサン";

        /// <summary>断面タイプだけでメーカーが決まる製品。</summary>
        private static string? MakerFromSectionType(string? sectionType) => sectionType switch
        {
            PileTypeNames.BfsHead or PileTypeNames.BfsTip => MitaniSekisan,
            _ => null,
        };

        /// <summary>製品名の接頭辞でメーカーが決まる製品。</summary>
        private static readonly (string Prefix, string Maker)[] NamePrefixes =
        [
            ("NPH-", JapanPile),
            ("NPRC-", JapanPile),
            ("MS-hi105", MitaniSekisan),
            ("Hi-SC105", MitaniSekisan),
            ("DAM105", MitaniSekisan),
            ("BF.S", MitaniSekisan),
        ];

        /// <summary>
        /// 断面のメーカー名を返す。特定できない場合（メーカーを問わない JIS 規格品など）は null。
        /// </summary>
        public static string? GetMaker(PileSection? section)
        {
            if (section == null) return null;

            string? byType = MakerFromSectionType(section.PileSectionType);
            if (byType != null) return byType;

            string name = section.SelectedPrecastPile?.Name ?? string.Empty;
            foreach (var (prefix, maker) in NamePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return maker;
            }
            return null;
        }

        /// <summary>
        /// 断面が指定メーカーの工法に使えるか。
        /// メーカーを特定できない JIS 規格品は、どのメーカーも供給しうるため許容する
        /// （false になるのは<b>別メーカーと特定できた</b>ときだけ）。
        /// </summary>
        public static bool IsUsableBy(PileSection? section, string expectedMaker, out string? actualMaker)
        {
            actualMaker = GetMaker(section);
            return actualMaker == null || actualMaker == expectedMaker;
        }
    }
}
