using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PileDesign.Models
{
    public class ProjectData
    {
        /// <summary>
        /// ファイルフォーマットのバージョン番号。
        /// スキーマ変更時にインクリメントし、読込時の互換性チェックに使用する。
        /// </summary>
        public int FormatVersion { get; set; } = 1;

        public InputModel InputModel { get; set; }
        public AnaModel AnaModel { get; set; }

        /// <summary>基礎梁鉛直解析結果</summary>
        public List<VerticalBeamCaseResult> VerticalBeamCaseResults { get; set; }

        /// <summary>
        /// 解析結果と整合する「解析を実行した時点の入力」。
        ///
        /// <see cref="InputModel"/> は現在編集中の入力で、解析後に編集していれば結果とは一致しない。
        /// 結果表示はこちらを基準にする。解析結果が無いファイル・旧ファイルでは null
        /// （読込側は <see cref="InputModel"/> にフォールバックする）。
        ///
        /// 省略可能なプロパティなので FormatVersion は上げていない
        /// （System.Text.Json は欠落プロパティを既定値で埋める）。
        /// なお <see cref="AnaModel"/> が同じ入力を参照しているため、
        /// ReferenceHandler.Preserve のもとでは実体は 1 つで $ref になり、増えるのは参照 1 個分。
        /// </summary>
        public InputModel? ResultInputSnapshot { get; set; }

        /// <summary>解析を実行した時刻（表示用）。</summary>
        public DateTime? ResultCapturedAt { get; set; }

        /// <summary>
        /// 保存した時点で、解析のあとに入力が編集されていたか。
        ///
        /// 以前はこれを保存せず、読込側で「スナップショットが現在の入力と別インスタンスか」で
        /// 代用していた。ところがスナップショットは解析時に必ず複製して作るので、
        /// 編集の有無にかかわらず常に別インスタンスになる。
        /// 結果、<b>解析結果を含むファイルを開くたびに「編集されています」と言われて</b>いた。
        /// 参照の同一性は編集の有無の代わりにならない。値そのものを持たせる。
        ///
        /// 旧ファイルには無いので null。その場合だけ従来の判定に落とす。
        /// </summary>
        public bool? InputChangedSinceAnalysis { get; set; }

        /// <summary>
        /// 杭 → FEM 要素 (梁・節点・地盤ばね・杭頭回転ばね) の対応表。
        ///
        /// これらの関連は解析ランタイム状態として [JsonIgnore] であり、
        /// FEM モデルを組むとき (AnalysisModelling) にしか設定されない。
        /// 表が無いと、解析結果を含むファイルを開き直しても杭から要素を辿れず、
        /// M-φ グラフや限界線など「杭ごとに結果を引く」表示が空になる。
        /// 省略可能なので旧ファイルは null（従来どおりの挙動）。
        /// </summary>
        public PileFemLinkTable? PileFemLinks { get; set; }

        /// <summary>
        /// 杭要素分割済みかどうか（保存時点の状態）。
        ///
        /// 以前は読込時に「AnaModel に節点があるか」で推定していたが、解析結果を保持したまま
        /// 杭要素分割だけを取り消せるようになったため、この推定は成り立たない
        /// （分割を取り消しても結果の AnaModel は残るので、開き直すと分割済みに戻ってしまい、
        ///  メイン画面の杭が「分割後」の色で描かれる）。状態そのものを保存する。
        /// 省略可能なので旧ファイルは null → 従来どおり推定する。
        /// </summary>
        public bool? IsElementSplit { get; set; }

        // 保存メソッド
        public static void SaveProject(string filePath, InputModel inputModel, AnaModel anaModel)
        {
            var projectData = new ProjectData
            {
                FormatVersion = 2,  // v2: PileLayoutItems[*].Z = 接合節点 Z (旧 v1 = 杭頭 Z)
                InputModel = inputModel,
                AnaModel = anaModel
            };

            JsonSerializerOptions jsonSerializerOptions = new()
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };
            JsonSerializerOptions options = jsonSerializerOptions;

            string json = JsonSerializer.Serialize(projectData, options);
            File.WriteAllText(filePath, json);
        }

        // 復元メソッド
        public static ProjectData LoadProject(string filePath)
        {
            JsonSerializerOptions jsonSerializerOptions = new()
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
            };
            JsonSerializerOptions options = jsonSerializerOptions;

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ProjectData>(json, options);
        }
    }


}
