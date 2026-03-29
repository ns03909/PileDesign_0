using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PileDesign.Cli;
using PileDesign.Models.InputData;

namespace PileDesign.Mcp;

/// <summary>
/// MCP ツールの実装。各ツールは文字列（テキスト結果）を返す。
/// </summary>
public sealed class PileDesignTools
{
    private InputModel? _model;
    private AnalysisResult? _lastResult;

    private readonly JsonSerializerOptions _jsonPretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string LoadModel(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("path が指定されていません。");

        _model = InputModel.LoadHeadless(path);
        _lastResult = null;

        var sb = new StringBuilder();
        sb.AppendLine($"モデルを読み込みました: {path}");
        sb.AppendLine($"  杭本数: {_model.PileLayoutItems?.Count ?? 0}");
        sb.AppendLine($"  杭体定義数: {_model.PileBodies?.Count ?? 0}");
        sb.AppendLine($"  地盤定義数: {_model.GroundsInput?.Count ?? 0}");

        var lc = _model.LoadCasesInput;
        if (lc != null)
        {
            int lc1 = lc.LoadCasesLevel1?.Count ?? 0;
            int lc2 = lc.LoadCasesLevel2?.Count ?? 0;
            sb.AppendLine($"  荷重ケース: レベル1={lc1}, レベル2={lc2}");
        }

        return sb.ToString();
    }

    public string RunAnalysis(double? tolerance, int? maxIter, double? relaxation)
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。先に load_model を実行してください。");

        var runner = new HeadlessAnalysisRunner(_model)
        {
            Verbose = false,
            ConvergenceTolerance = tolerance ?? 1e-3,
            MaxIterations = maxIter ?? 50,
            RelaxationFactor = relaxation ?? 1.0
        };

        _lastResult = runner.Run();

        var sb = new StringBuilder();
        sb.AppendLine($"解析完了: {_lastResult.ConvergedCases}/{_lastResult.TotalCases} ケース収束");
        sb.AppendLine($"節点数: {_lastResult.NodeCount}, 要素数: {_lastResult.BeamCount}");

        // 各ケースのサマリ
        foreach (var lc in _lastResult.LoadCaseResults)
        {
            var ap = lc.ActionPointDisplacement;
            string disp = ap != null ? $"Ux={ap.Ux_mm:F2}mm, Uy={ap.Uy_mm:F2}mm" : "N/A";
            sb.AppendLine($"  L{lc.Level}-{lc.LoadCaseNo} Comb[{lc.CombinationNo}] " +
                          $"{(lc.IsLiquefaction ? "液状化" : "非液状化")}: {disp}");
        }

        return sb.ToString();
    }

    public string GetModelInfo()
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。");

        var info = new JsonObject
        {
            ["杭本数"] = _model.PileLayoutItems?.Count ?? 0,
            ["杭体定義数"] = _model.PileBodies?.Count ?? 0,
            ["地盤定義数"] = _model.GroundsInput?.Count ?? 0,
        };

        var lc = _model.LoadCasesInput;
        if (lc != null)
        {
            info["荷重ケースLevel1"] = lc.LoadCasesLevel1?.Count ?? 0;
            info["荷重ケースLevel2"] = lc.LoadCasesLevel2?.Count ?? 0;
            info["荷重組合せ数"] = lc.AllLoadCombinations?.Count ?? 0;
        }

        if (_lastResult != null)
        {
            info["最終解析_収束ケース"] = _lastResult.ConvergedCases;
            info["最終解析_全ケース"] = _lastResult.TotalCases;
        }

        return info.ToJsonString(_jsonPretty);
    }

    public string ListPiles()
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。");

        var piles = _model.PileLayoutItems;
        if (piles == null || piles.Count == 0)
            return "杭が定義されていません。";

        var sb = new StringBuilder();
        sb.AppendLine($"杭配置一覧 ({piles.Count}本):");
        sb.AppendLine($"{"No",4} {"X(m)",8} {"Y(m)",8} {"Z(m)",8} {"杭体No",6} {"地盤No",6} {"ξ",6}");
        sb.AppendLine(new string('-', 50));

        foreach (var p in piles)
        {
            sb.AppendLine($"{p.No,4} {p.X,8:F3} {p.Y,8:F3} {p.Z,8:F3} " +
                          $"{p.PileBodyNo,6} {p.GroundNo,6} {p.GroupPileFactor,6:F3}");
        }

        return sb.ToString();
    }

    public string ListLoadCases()
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。");

        var lc = _model.LoadCasesInput;
        if (lc == null)
            return "荷重ケースが定義されていません。";

        var sb = new StringBuilder();

        if (lc.LoadCasesLevel1 != null)
        {
            sb.AppendLine($"レベル1荷重ケース ({lc.LoadCasesLevel1.Count}):");
            foreach (var l in lc.LoadCasesLevel1)
            {
                sb.AppendLine($"  {$"L1-{l.No}"}: 上部={l.UpperMassForce:F1}kN, " +
                              $"基礎={l.FoundationMassForce:F1}kN, 解析対象={l.IsAnalysisTarget}");
            }
        }

        if (lc.LoadCasesLevel2 != null)
        {
            sb.AppendLine($"レベル2荷重ケース ({lc.LoadCasesLevel2.Count}):");
            foreach (var l in lc.LoadCasesLevel2)
            {
                sb.AppendLine($"  {$"L2-{l.No}"}: 上部={l.UpperMassForce:F1}kN, " +
                              $"基礎={l.FoundationMassForce:F1}kN, 解析対象={l.IsAnalysisTarget}");
            }
        }

        if (lc.AllLoadCombinations != null)
        {
            sb.AppendLine($"荷重組合せ ({lc.AllLoadCombinations.Count}):");
            foreach (var c in lc.AllLoadCombinations)
            {
                sb.AppendLine($"  Comb[{c.No}]: αL={c.Alpha1:F2}, βU={c.Beta1:F2}, βL={c.Beta2:F2}");
            }
        }

        return sb.ToString();
    }

    public string SetPileProperty(JsonNode? arguments)
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。");

        var pileNo = arguments?["pile_no"]?.GetValue<int>()
            ?? throw new ArgumentException("pile_no が必要です。");

        var pile = _model.PileLayoutItems?.FirstOrDefault(p => p.No == pileNo)
            ?? throw new ArgumentException($"杭 No.{pileNo} が見つかりません。");

        var changes = new List<string>();

        if (arguments?["x"] is JsonNode xNode)
        {
            pile.X = xNode.GetValue<double>();
            changes.Add($"X={pile.X:F3}m");
        }
        if (arguments?["y"] is JsonNode yNode)
        {
            pile.Y = yNode.GetValue<double>();
            changes.Add($"Y={pile.Y:F3}m");
        }
        if (arguments?["z"] is JsonNode zNode)
        {
            pile.Z = zNode.GetValue<double>();
            changes.Add($"Z={pile.Z:F3}m");
        }
        if (arguments?["axial_force_vl0"] is JsonNode vlNode)
        {
            pile.AxialForceVL0 = vlNode.GetValue<double>();
            changes.Add($"VL0={pile.AxialForceVL0:F1}kN");
        }

        if (changes.Count == 0)
            return $"杭 No.{pileNo}: 変更項目が指定されていません。";

        _lastResult = null; // 解析結果をクリア
        return $"杭 No.{pileNo} を変更しました: {string.Join(", ", changes)}";
    }

    public string SaveModel(string? path)
    {
        if (_model == null)
            throw new InvalidOperationException("モデルが読み込まれていません。");

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("path が指定されていません。");

        _model.SaveToFile(path);
        return $"モデルを保存しました: {path}";
    }
}
