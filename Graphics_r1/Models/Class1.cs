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
        /// 杭 → FEM 要素 (梁・節点・地盤ばね・杭頭回転ばね) の対応表。
        ///
        /// これらの関連は解析ランタイム状態として [JsonIgnore] であり、
        /// FEM モデルを組むとき (AnalysisModelling) にしか設定されない。
        /// 表が無いと、解析結果を含むファイルを開き直しても杭から要素を辿れず、
        /// M-φ グラフや限界線など「杭ごとに結果を引く」表示が空になる。
        /// 省略可能なので旧ファイルは null（従来どおりの挙動）。
        /// </summary>
        public PileFemLinkTable? PileFemLinks { get; set; }

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
