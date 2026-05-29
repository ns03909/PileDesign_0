using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 水平解析の収束挙動スナップショット。例題ごとに 1 ファイル。
    /// テストはこの DTO を JSON シリアライズ/デシリアライズし、現在の解析結果と比較する。
    ///
    /// 退化検出ポリシー:
    ///   - 反復数: ケース別に「絶対 +10 以下 かつ 比率 ≤1.10」までを許容、超えたら fail
    ///   - 収束フラグ: 完全一致 (収束済→未収束 は即 fail)
    ///   - 残差: オーダー同等まで許容 (×10 まで)
    /// </summary>
    public class ConvergenceSnapshot
    {
        /// <summary>例題識別子 (例: "Example9")</summary>
        [JsonPropertyName("exampleName")]
        public string ExampleName { get; set; } = "";

        /// <summary>表示名 (デバッグ用)</summary>
        [JsonPropertyName("exampleDisplayName")]
        public string ExampleDisplayName { get; set; } = "";

        /// <summary>スナップショット形式のバージョン (フォーマット変更時に bump)</summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>スナップショット採取日時 (YYYY-MM-DD)</summary>
        [JsonPropertyName("capturedAt")]
        public string CapturedAt { get; set; } = "";

        /// <summary>解析実行時のオプション</summary>
        [JsonPropertyName("options")]
        public SnapshotOptions Options { get; set; } = new();

        /// <summary>ケース別の収束挙動</summary>
        [JsonPropertyName("cases")]
        public List<CaseRecord> Cases { get; set; } = [];

        /// <summary>集計サマリ (人間可読用、テストでは個別ケースで判定)</summary>
        [JsonPropertyName("summary")]
        public Summary SummaryStats { get; set; } = new();

        // ====== I/O ======

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static ConvergenceSnapshot Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ConvergenceSnapshot>(json, JsonOpts)
                ?? throw new InvalidDataException($"Failed to deserialize: {path}");
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(path, json);
        }
    }

    public class SnapshotOptions
    {
        [JsonPropertyName("level1Steps")]
        public int Level1Steps { get; set; }

        [JsonPropertyName("level2Steps")]
        public int Level2Steps { get; set; }

        [JsonPropertyName("liquefactionMode")]
        public string LiquefactionMode { get; set; } = "Yes";  // "Yes" | "None" | "Both"

        [JsonPropertyName("useLineSearch")]
        public bool UseLineSearch { get; set; } = true;

        [JsonPropertyName("parallelism")]
        public int Parallelism { get; set; } = 1;
    }

    /// <summary>
    /// 1 ケース (Level × LoadCaseNo × LoadCombination × Liquefaction) の収束挙動。
    /// 反復数とステップ数の合算でリグレッションを検出する。
    /// </summary>
    public class CaseRecord
    {
        /// <summary>ケース識別子 "L2-1.C1.Liq" のような形式</summary>
        [JsonPropertyName("caseKey")]
        public string CaseKey { get; set; } = "";

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("loadCaseNo")]
        public int LoadCaseNo { get; set; }

        [JsonPropertyName("combinationNo")]
        public int CombinationNo { get; set; }

        [JsonPropertyName("isLiquefaction")]
        public bool IsLiquefaction { get; set; }

        /// <summary>このケースが収束したか (全ステップ収束したら true)</summary>
        [JsonPropertyName("converged")]
        public bool Converged { get; set; }

        /// <summary>全ステップ通算の Newton-Raphson 反復数</summary>
        [JsonPropertyName("totalIterations")]
        public int TotalIterations { get; set; }

        /// <summary>実行ステップ数 (bisection 再試行で追加された分を含む)</summary>
        [JsonPropertyName("totalSteps")]
        public int TotalSteps { get; set; }

        /// <summary>bisection 再試行回数 (0 なら 1 回でクリア)</summary>
        [JsonPropertyName("bisectionRetries")]
        public int BisectionRetries { get; set; }

        /// <summary>最終ステップでの残差ノルム ‖R‖/‖F_int‖</summary>
        [JsonPropertyName("finalResidual")]
        public double FinalResidual { get; set; }

        // === 物理量スナップショット (A1: 数値正確性ネット) ===
        // 反復数だけでなく代表変位 / 最大反力もスナップショット化することで、
        // 「収束はするが値が変わった」(= サイレントな数値退化) も検出する。
        // tolerance: 相対 1% を許容 (RoundOff + library 更新で多少ぶれることを想定)

        /// <summary>代表点 (Nodes[0] = AP) の累積変位 [m, rad]</summary>
        [JsonPropertyName("apUx")] public double ApUx { get; set; }
        [JsonPropertyName("apUy")] public double ApUy { get; set; }
        [JsonPropertyName("apUz")] public double ApUz { get; set; }
        [JsonPropertyName("apRx")] public double ApRx { get; set; }
        [JsonPropertyName("apRy")] public double ApRy { get; set; }
        [JsonPropertyName("apRz")] public double ApRz { get; set; }

        /// <summary>全節点中の最大絶対水平変位 [m] (杭頭シア応答の代表値)</summary>
        [JsonPropertyName("maxAbsHorizDisp")]
        public double MaxAbsHorizDisp { get; set; }

        /// <summary>全水平地盤ばねの最大絶対反力 [kN] (杭周地盤の最大応答)</summary>
        [JsonPropertyName("maxAbsHorizSpringReaction")]
        public double MaxAbsHorizSpringReaction { get; set; }
    }

    public class Summary
    {
        [JsonPropertyName("totalCases")]
        public int TotalCases { get; set; }

        [JsonPropertyName("convergedCases")]
        public int ConvergedCases { get; set; }

        [JsonPropertyName("totalIterations")]
        public int TotalIterations { get; set; }

        [JsonPropertyName("totalSteps")]
        public int TotalSteps { get; set; }

        [JsonPropertyName("maxResidualOverAllCases")]
        public double MaxResidualOverAllCases { get; set; }
    }
}
