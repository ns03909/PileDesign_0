using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PileDesign.Models.PileLibrary
{
    /// <summary>
    /// PC リングの許容せん断力・終局せん断耐力 (kN)。工法協会のマニュアルの表の値で、式からは求めない
    /// (式 2.2.2 の Ty・Tb1・Tb2・Tc・d・Xn の算定法がマニュアルに無く、設計例も表の値で PC リングを選んでいる)。
    /// </summary>
    /// <param name="Name">PC リングのタイプ名 (キャプテンパイル「800-N」、キャプリング「800N」)</param>
    /// <param name="LongQa">長期許容せん断力 LQa</param>
    /// <param name="ShortQa">短期許容せん断力 sQa</param>
    /// <param name="RaU">終局の加力前面の抵抗 Ra'</param>
    /// <param name="Rb1U">終局の加力側面の抵抗 Rb1' (鋼板リング・スパイラル筋)</param>
    /// <param name="Rb2U">終局の加力側面の抵抗 Rb2' (定着筋・コンクリート)</param>
    /// <param name="UltNoFriction">終局せん断耐力 uQr (摩擦無視)。マニュアルが設計に用いるとする値</param>
    /// <param name="UltFrictionMax">終局せん断耐力の上限 (摩擦考慮上限)。圧縮軸力を受ける杭頭で摩擦 μN を足すときの頭打ち</param>
    public sealed record PCRingShearCapacity(
        string Name, double LongQa, double ShortQa, double RaU, double Rb1U, double Rb2U, double UltNoFriction, double UltFrictionMax)
    {
        /// <summary>摩擦係数 μ (両工法のマニュアル「せん断における軸圧縮の効果を参考に 0.1」)。</summary>
        public const double FrictionCoefficient = 0.1;

        /// <summary>
        /// 終局せん断耐力 (kN)。摩擦を見込むときは、圧縮軸力 N (kN、圧縮が正) の μN を摩擦無視の値に足し、
        /// 摩擦考慮上限で頭打ちにする (式 2.2.9 / 2-9 の uQr = Ra' + min(Rb1' + μN, Rb2') を表の値で表したもの。
        /// キャプリングの表は N タイプで摩擦無視が Ra' + min(Rb1', Rb2') より小さい行があり、その行では式より小さい側になる)。
        /// 引張・軸力 0 のときは摩擦無視の値。
        /// </summary>
        public double Ultimate(bool considersFriction, double axialN_kN)
        {
            if (!considersFriction || !(axialN_kN > 0.0)) return UltNoFriction;
            return Math.Min(UltNoFriction + FrictionCoefficient * axialN_kN, UltFrictionMax);
        }
    }

    /// <summary>PC リングのせん断耐力の表 (工法ごとの CSV)。読み込みは一度だけ。</summary>
    public static class PCRingShearTable
    {
        /// <summary>キャプテンパイル工法 (第 6 版 表 2.2.4・表 2.2.5、パイルキャップ Fc = 21 の場合)。</summary>
        public const string CaptainFile = "CaptainPCRingShear.csv";
        /// <summary>キャプリングパイル工法 (設計・施工マニュアル 第 6 版 2023 年 4 月 2.2(4))。</summary>
        public const string CapringFile = "CapringPCRingShear.csv";

        /// <summary>表の値の前提のパイルキャップの Fc (N/mm²)。キャプテンパイルは表の注記、キャプリングは使用材料の下限。</summary>
        public const double TableBasisFc = 21.0;

        private static readonly Dictionary<string, IReadOnlyDictionary<string, PCRingShearCapacity>> _cache = [];
        private static readonly object _lock = new();

        /// <summary>タイプ名で引く。表に無ければ null。</summary>
        public static PCRingShearCapacity? Find(string file, string? ringName)
        {
            if (string.IsNullOrWhiteSpace(ringName)) return null;
            return Load(file).TryGetValue(ringName.Trim(), out var c) ? c : null;
        }

        /// <summary>杭頭諸元 (画面・計算書) に、PC リングのせん断耐力の行を足す。表に無いときはそう書く。</summary>
        internal static void AddSpecs(System.Collections.ObjectModel.ObservableCollection<Spec> specs, PCRingShearCapacity? c, bool considersFriction)
        {
            if (c == null)
            {
                specs.Add(new Spec("PC リングのせん断耐力", "", "表に無いタイプ (検定できません)", ""));
                return;
            }
            specs.Add(new Spec("PC リングの長期許容せん断力", "LQa", $"{c.LongQa:N0}", "kN"));
            specs.Add(new Spec("PC リングの短期許容せん断力", "sQa", $"{c.ShortQa:N0}", "kN"));
            specs.Add(new Spec("PC リングの終局せん断耐力 (摩擦無視)", "uQr", $"{c.UltNoFriction:N0}", "kN"));
            specs.Add(new Spec("終局で圧縮側の杭頭に摩擦 μN を見込むか", "",
                considersFriction ? $"見込む (μ = {PCRingShearCapacity.FrictionCoefficient:0.0}、上限 {c.UltFrictionMax:N0} kN)" : "見込まない", ""));
        }

        internal static IReadOnlyDictionary<string, PCRingShearCapacity> Load(string file)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(file, out var cached)) return cached;
                var table = new Dictionary<string, PCRingShearCapacity>(StringComparer.Ordinal);
                string path = Path.Combine(AppContext.BaseDirectory, "Models", "PileLibrary", file);
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadLines(path).Skip(1))
                    {
                        var f = line.Split(',');
                        if (f.Length < 8) continue;
                        double V(int i) => double.Parse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                        var c = new PCRingShearCapacity(f[0].Trim(), V(1), V(2), V(3), V(4), V(5), V(6), V(7));
                        table[c.Name] = c;
                    }
                }
                // 読めなかったときは空のまま (検定は「表に無い」として検定不能にする)。キャッシュするのは読めたときだけ
                if (table.Count > 0) _cache[file] = table;
                return table;
            }
        }
    }
}
