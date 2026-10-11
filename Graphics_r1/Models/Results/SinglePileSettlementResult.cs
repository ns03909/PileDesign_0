using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 単杭沈下解析の結果 (土層-杭セットごとの荷重-沈下曲線)。
    ///
    /// 曲線は <c>SoilPile.LoadDisplacements</c> が持ち続ける。<b>置き場所は変えていない。</b>
    /// 基礎梁考慮沈下の杭頭ばねと、水平解析の杭先端 P-S ばねが<b>次の解析の入力として</b>
    /// これを読むので、結果側へ動かすと数値の出る経路に手が入るためである。
    ///
    /// 変えたのは<b>保存の仕方</b>だけ。以前は入力の一部として書き出していたので、
    /// <list type="bullet">
    /// <item>現在の入力と解析時のスナップショットの<b>両方</b>に同じ曲線が入っていた</item>
    /// <item>保存・Undo のたびに曲線ぶんの JSON 直列化が走っていた</item>
    /// </list>
    /// いまはこの型がファイルの専用の節を受け持ち、入力の側には書き出さない。
    /// </summary>
    public sealed class SinglePileSettlementResult
    {
        /// <summary>土層-杭セット 1 つぶんの曲線。</summary>
        public sealed class Entry
        {
            /// <summary>どの土層-杭セットかを表す 3 つ組。<c>InputModel</c> の索引と同じ決め方。</summary>
            public int GroundNo { get; set; }
            public int PileBodyNo { get; set; }
            public double Z { get; set; }

            /// <summary>常時の荷重-沈下曲線。</summary>
            public List<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacements { get; set; } = [];

            /// <summary>極限の荷重-沈下曲線。</summary>
            public List<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacementsLimit { get; set; } = [];

            /// <summary>
            /// 各荷重段階の全節点の変位 [m] (<c>SoilPile.NodeDisplacements</c>。杭側・地盤側の対の自由度の並び)。
            /// 単杭の変形・反力の図と、水平解析の杭先端 P-S ばね・鉛直の節点ばねがこれを読む。
            ///
            /// <para>以前は保存しておらず、ファイルを開き直すと曲線はあるのに図が空になり、P-S ばねも付かなかった
            /// (単杭沈下解析をやり直すまで)。省略可能で、旧ファイルには無い (null)。数値の配列で持つ
            /// (1 セット 約 45〜60 KB。NaN は保存の設定どおり "NaN" の文字で書く)。
            /// 旧版のアプリは知らないプロパティを読み飛ばす (System.Text.Json の既定) ので、このファイルも開ける。</para>
            /// </summary>
            public List<double[]>? NodeDisplacements { get; set; }

            /// <summary>各荷重段階の全節点の反力 [kN] (<c>SoilPile.NodeReactions</c>)。<see cref="NodeDisplacements"/> と同じ。</summary>
            public List<double[]>? NodeReactions { get; set; }
        }

        public List<Entry> Entries { get; set; } = [];

        /// <summary>1 件でも曲線を持っているか。</summary>
        public bool HasResults => Entries.Any(e => e.LoadDisplacements.Count > 0);

        /// <summary>
        /// 入力モデルの土層-杭セットから曲線を集める。1 件も無ければ null を返す
        /// (保存側は null なら節ごと書かない)。
        /// </summary>
        /// <param name="includeNodeHistories">各荷重段階の全節点の変位・反力も集めるか (保存では集める)。</param>
        public static SinglePileSettlementResult? Capture(InputModel? input, bool includeNodeHistories = true)
        {
            var soilPiles = input?.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return null;

            var result = new SinglePileSettlementResult();
            foreach (var sp in soilPiles)
            {
                if (sp == null) continue;
                if ((sp.LoadDisplacements?.Count ?? 0) == 0
                    && (sp.LoadDisplacementsLimit?.Count ?? 0) == 0) continue;

                result.Entries.Add(new Entry
                {
                    GroundNo = sp.GroundNo,
                    PileBodyNo = sp.PileBodyNo,
                    Z = sp.Z,
                    LoadDisplacements = [.. sp.LoadDisplacements ?? []],
                    LoadDisplacementsLimit = [.. sp.LoadDisplacementsLimit ?? []],
                    NodeDisplacements = includeNodeHistories ? ToArrays(sp.NodeDisplacements) : null,
                    NodeReactions = includeNodeHistories ? ToArrays(sp.NodeReactions) : null,
                });
            }
            return result.Entries.Count > 0 ? result : null;
        }

        /// <summary>
        /// 集めた曲線を入力モデルの土層-杭セットへ戻す。
        ///
        /// 対応付けは (地盤番号, 杭体番号, Z) で行う。<c>InputModel</c> が土層-杭セットを
        /// 索く鍵と同じで、Z は座標許容差で丸める。
        /// 見つからない記録は捨てる (杭配置を変えたあとのファイルなど)。
        /// </summary>
        public void ApplyTo(InputModel? input)
        {
            var soilPiles = input?.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            var byKey = new Dictionary<(int, int, double), SoilPile>();
            foreach (var sp in soilPiles)
            {
                if (sp != null) byKey[KeyOf(sp.GroundNo, sp.PileBodyNo, sp.Z)] = sp;
            }

            foreach (var e in Entries)
            {
                if (!byKey.TryGetValue(KeyOf(e.GroundNo, e.PileBodyNo, e.Z), out var sp)) continue;
                sp.LoadDisplacements = [.. e.LoadDisplacements];
                sp.LoadDisplacementsLimit = [.. e.LoadDisplacementsLimit];

                // 節点の履歴は曲線と段数がそろうときだけ戻す (補間は曲線の荷重を引く。段数が違うと別の段の値を読む)。
                // 記録が無い (旧ファイル・保存しなかった) ときは触らない
                var displacements = ToVectors(e.NodeDisplacements);
                var reactions = ToVectors(e.NodeReactions);
                if (displacements != null && reactions != null
                    && displacements.Count == sp.LoadDisplacements.Count && reactions.Count == sp.LoadDisplacements.Count)
                {
                    sp.NodeDisplacements = displacements;
                    sp.NodeReactions = reactions;
                }
            }
        }

        private static List<double[]>? ToArrays(List<MathNet.Numerics.LinearAlgebra.Vector<double>>? vectors)
            => vectors == null || vectors.Count == 0 ? null : [.. vectors.Select(v => v?.ToArray() ?? [])];

        private static List<MathNet.Numerics.LinearAlgebra.Vector<double>>? ToVectors(List<double[]>? arrays)
            => arrays == null || arrays.Count == 0 || arrays.Any(a => a == null || a.Length == 0) ? null
             : [.. arrays.Select(a => MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(a))];

        /// <summary>
        /// 解析時のスナップショットへ曲線を写す。
        ///
        /// スナップショットは JSON 往復で作るが、曲線は入力の節に書き出さないので往復では
        /// 落ちる。結果表示 (グラフ・計算書) はスナップショットを読むため、写さないと
        /// 単杭沈下のグラフが空になる。
        /// </summary>
        public static void CopyCurves(InputModel? from, InputModel? to)
        {
            Capture(from, includeNodeHistories: false)?.ApplyTo(to);

            // 各荷重段階の全節点の変位・反力も写す (曲線と同じく往復で落ちる。保存ファイルにはこの型の節で書く)。
            // 結果の図 (単杭の反力・基礎梁考慮の反力・単杭の変形) はスナップショットのこれを読み、杭先端 P-S ばねも
            // これから組む。以前は写しておらず、単杭沈下を実行した直後でも図が空になり、変形が 0 で描かれていた。
            // 解析で作った値を共有する (SoilPile.DeepCopy と同じ。結果は書き換えない)
            var fromSets = from?.ElementDivision?.SoilPiles;
            var toSets = to?.ElementDivision?.SoilPiles;
            if (fromSets == null || toSets == null) return;
            var byKey = new Dictionary<(int, int, double), SoilPile>();
            foreach (var sp in toSets)
                if (sp != null) byKey[KeyOf(sp.GroundNo, sp.PileBodyNo, sp.Z)] = sp;
            foreach (var sp in fromSets)
            {
                if (sp?.NodeDisplacements == null && sp?.NodeReactions == null) continue;
                if (!byKey.TryGetValue(KeyOf(sp!.GroundNo, sp.PileBodyNo, sp.Z), out var target)) continue;
                target.NodeDisplacements = sp.NodeDisplacements;
                target.NodeReactions = sp.NodeReactions;
            }
        }

        private static (int, int, double) KeyOf(int groundNo, int pileBodyNo, double z)
            => (groundNo, pileBodyNo,
                Math.Round(z / NumericalConstants.COORDINATE_TOLERANCE) * NumericalConstants.COORDINATE_TOLERANCE);
    }
}
