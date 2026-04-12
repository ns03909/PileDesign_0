using PileDesign.FEM;
using PileDesign.Models.InputData;
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

        // 保存メソッド
        public static void SaveProject(string filePath, InputModel inputModel, AnaModel anaModel)
        {
            var projectData = new ProjectData
            {
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
