using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// F.T.Pile 構法の FT キャップを杭径から決める (FT キャップは杭径ごとの製品で、利用者が選ぶのは引張定着筋だけ)。
    ///
    /// キャップを選ばないとキャップの杭径 Phi が 0 のままで支圧係数 φc が 0 になり、杭頭の M-θ と許容回転角が数値にならない。
    /// 杭径と違うキャップは製品として意味がない。解析の前に杭径から決め直し、カタログに杭径が無ければ知らせて止める。
    /// </summary>
    public static class FTCapSelection
    {
        /// <summary>F.T.Pile 構法の杭体ごとの (番号 0 始まり, 杭体, 杭径 mm)。</summary>
        private static IEnumerable<(int Index, PileBodyInput Body, double Diameter)> FTBodies(InputModel? inputModel)
        {
            var bodies = inputModel?.PileBodies;
            if (bodies == null) yield break;
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (body?.PileTop?.FTPile == null || !(body.PileTopType ?? "").Contains("FT-Pile構法")) continue;
                double diameter = body.PileBodySegments?.FirstOrDefault()?.PileSection?.PileDiameter ?? 0.0;
                yield return (i, body, diameter);
            }
        }

        /// <summary>キャップが杭径と合っていない (未選択か、違う径の) F.T.Pile 構法の杭体の番号 (0 始まり)。</summary>
        public static List<int> FindMismatched(InputModel? inputModel)
            => FTBodies(inputModel)
                .Where(x => x.Body.PileTop!.FTPile!.IsCapUnselected() || Math.Abs(x.Body.PileTop!.FTPile!.FTCap.Phi - x.Diameter) >= 0.5)
                .Select(x => x.Index).ToList();

        /// <summary>杭径に合う FT キャップがカタログに無い杭体の説明 (入力を書き換えずに調べる)。</summary>
        public static List<(int Index, string Message)> FindWithoutCatalogCap(InputModel? inputModel)
            => FTBodies(inputModel)
                .Where(x => x.Body.PileTop!.FTPile!.CapForPileDiameter(x.Diameter) == null)
                .Select(x => (x.Index, $"杭体 {x.Index + 1}: 杭径 {x.Diameter:N0} mm に合う F.T.Pile 構法の FT キャップがカタログにありません。"
                                       + "杭頭の曲げモーメント－回転角関係と許容回転角を求められません。"))
                .ToList();

        /// <summary>
        /// キャップが杭径と合っていない杭体のキャップを、杭径から決め直す。決め直した杭体の数と、
        /// カタログに杭径が無くて決められなかった杭体の説明を返す。
        /// </summary>
        public static (int Changed, List<string> Unavailable) AutoSelect(InputModel inputModel)
        {
            int changed = 0;
            var unavailable = new List<string>();
            var mismatched = new HashSet<int>(FindMismatched(inputModel));
            foreach (var (index, body, _) in FTBodies(inputModel))
            {
                if (!mismatched.Contains(index)) continue;
                if (body.PileTop!.AutoSelectFTCap(body.PileBodySegments?.FirstOrDefault()?.PileSection)) changed++;
            }
            unavailable.AddRange(FindWithoutCatalogCap(inputModel).Select(x => x.Message));
            return (changed, unavailable);
        }
    }
}
