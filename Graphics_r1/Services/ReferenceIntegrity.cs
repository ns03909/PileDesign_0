using PileDesign.Common;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>番号の参照の検査の結果。</summary>
    /// <param name="NeedsReview">手で選び直す必要があるもの (杭が指す杭体・地盤が無いなど)。</param>
    /// <param name="Repairable">要素分割をやり直せば直るもの (土層-杭セットは杭配置・杭体・地盤から作る派生データ)。</param>
    public sealed record ReferenceIntegrityReport(IReadOnlyList<Diagnostic> NeedsReview, IReadOnlyList<Diagnostic> Repairable)
    {
        public bool IsClean => NeedsReview.Count == 0 && Repairable.Count == 0;

        public IEnumerable<Diagnostic> All => NeedsReview.Concat(Repairable);

        /// <summary>知らせる文 (問題が無ければ null)。直し方の違う 2 つを分けて書く。</summary>
        public string? Describe()
        {
            if (IsClean) return null;
            var lines = new List<string> { "入力の番号の参照に食い違いがあります。グラフ・計算書・解析で、その杭を扱えません。" };
            if (NeedsReview.Count > 0)
            {
                lines.Add("");
                lines.Add("■ 選び直しが必要なもの (杭配置の杭体・地盤の欄で選び直してください):");
                lines.AddRange(NeedsReview.Take(10).Select(d => "・" + d.Message));
                if (NeedsReview.Count > 10) lines.Add($"…ほか {NeedsReview.Count - 10} 件");
            }
            if (Repairable.Count > 0)
            {
                lines.Add("");
                lines.Add("■ 要素分割をやり直せば直るもの (土層-杭セットは杭配置・杭体・地盤から作り直せます。解析の前にも作り直します):");
                lines.AddRange(Repairable.Take(10).Select(d => "・" + d.Message));
                if (Repairable.Count > 10) lines.Add($"…ほか {Repairable.Count - 10} 件");
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// 杭配置・土層-杭セット・杭体・地盤のあいだの番号の参照を確かめる。ファイルを開いた直後に行う。
    ///
    /// <para>以前は解析の前にしか確かめておらず、保存データの食い違いや削除したあとの古い番号は、
    /// グラフや計算書を開いたときに初めて「インデックスが範囲外です」で落ちた。読み込んだ時点で、
    /// どの杭がどの番号を指しているかを知らせる。データは書き換えない (直し方は利用者が選ぶ)。</para>
    /// </summary>
    public static class ReferenceIntegrity
    {
        public static ReferenceIntegrityReport Check(InputModel? input)
        {
            var needsReview = new List<Diagnostic>();
            var repairable = new List<Diagnostic>();
            if (input == null) return new(needsReview, repairable);

            var soilPiles = input.ElementDivision?.SoilPiles;
            bool hasSoilPiles = soilPiles != null && soilPiles.Count > 0;

            foreach (var pile in input.PileLayoutItems ?? [])
            {
                if (pile == null) continue;
                bool bodyOk = input.PileBodyAt(pile.PileBodyNo) != null;
                bool groundOk = input.GroundAt(pile.GroundNo) != null;
                if (!bodyOk)
                    needsReview.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No, pile.PileBodyNo),
                        $"杭 No.{pile.No}: 杭体番号 {pile.PileBodyNo} の杭体がありません (杭体は {input.PileBodies?.Count ?? 0} 個)。"));
                if (!groundOk)
                    needsReview.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 地盤番号 {pile.GroundNo} の地盤がありません (地盤は {input.GroundsInput?.Count ?? 0} 個)。"));

                // 土層-杭セットとの対応は、セットを作ったあとだけ見る (作る前は対応が無いのが普通)
                if (!hasSoilPiles || !bodyOk || !groundOk) continue;
                var soilPile = pile.SoilPileAt(input);
                if (soilPile == null)
                    repairable.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 土層-杭セットとの対応がありません (番号 {pile.SoilPileAltNo}、セットは {soilPiles!.Count} 個)。"));
                else if (soilPile.PileBodyNo != pile.PileBodyNo || soilPile.GroundNo != pile.GroundNo)
                    repairable.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 土層-杭セットが別の杭体・地盤を指しています "
                        + $"(杭は杭体{pile.PileBodyNo}・地盤{pile.GroundNo}、セットは杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo})。"));
            }

            if (hasSoilPiles)
            {
                for (int i = 0; i < soilPiles!.Count; i++)
                {
                    var sp = soilPiles[i];
                    if (sp == null) continue;
                    if (input.PileBodyAt(sp.PileBodyNo) == null)
                        repairable.Add(Diagnostic.Input(DiagnosticTarget.PileBody(sp.PileBodyNo),
                            $"土層-杭セット {i + 1}: 杭体番号 {sp.PileBodyNo} の杭体がありません。"));
                    if (input.GroundAt(sp.GroundNo) == null)
                        repairable.Add(Diagnostic.Input(DiagnosticTarget.Ground(sp.GroundNo),
                            $"土層-杭セット {i + 1}: 地盤番号 {sp.GroundNo} の地盤がありません。"));
                }
            }

            var embedment = input.EmbedmentInput;
            if (embedment != null && embedment.EmbedmentLayersCount > 0 && input.GroundAt(embedment.GroundNo) == null)
                needsReview.Add(Diagnostic.Input(DiagnosticTarget.Embedment(),
                    $"根入部: 地盤番号 {embedment.GroundNo} の地盤がありません (地盤は {input.GroundsInput?.Count ?? 0} 個)。根入部の地盤を選び直してください。"));

            return new(needsReview, repairable);
        }
    }
}
