using System.IO;
using System.Text.Json;
using PileDesign.Cli;
using PileDesign.Models.InputData;

// ─── コマンドライン引数の解析 ───

string? inputPath = null;
string? outputPath = null;
bool verbose = true;
bool showHelp = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input" or "-i":
            if (i + 1 < args.Length) inputPath = args[++i];
            break;
        case "--output" or "-o":
            if (i + 1 < args.Length) outputPath = args[++i];
            break;
        case "--quiet" or "-q":
            verbose = false;
            break;
        case "--help" or "-h":
            showHelp = true;
            break;
    }
}

if (showHelp || inputPath == null)
{
    Console.WriteLine("PileDesign CLI - 杭基礎水平解析コマンドラインツール");
    Console.WriteLine();
    Console.WriteLine("使い方:");
    Console.WriteLine("  PileDesign.Cli --input <入力JSON> [--output <出力JSON>] [--quiet]");
    Console.WriteLine();
    Console.WriteLine("オプション:");
    Console.WriteLine("  -i, --input   入力JSONファイルパス（必須）");
    Console.WriteLine("  -o, --output  結果出力JSONファイルパス（省略時はコンソール出力）");
    Console.WriteLine("  -q, --quiet   ログ出力を抑制");
    Console.WriteLine("  -h, --help    このヘルプを表示");
    Console.WriteLine();
    Console.WriteLine("入力ファイル:");
    Console.WriteLine("  GUIアプリで「ファイル→保存」した .json ファイルを指定してください。");
    Console.WriteLine("  計算例JSONファイル（Examples/Example*.json）は地盤データのみのため使用できません。");
    Console.WriteLine();
    Console.WriteLine("例:");
    Console.WriteLine("  PileDesign.Cli --input my_model.json --output results.json");
    Console.WriteLine("  PileDesign.Cli -i saved_model.json -q");
    return showHelp ? 0 : 1;
}

// ─── 実行 ───

try
{
    if (verbose) Console.WriteLine($"入力ファイル: {inputPath}");

    // 1. JSON読み込み
    var inputModel = InputModel.LoadHeadless(inputPath);
    if (verbose) Console.WriteLine($"モデル読み込み完了: 杭{inputModel.PileLayoutItems?.Count ?? 0}本");

    // 2. 解析実行
    var runner = new HeadlessAnalysisRunner(inputModel)
    {
        Verbose = verbose
    };

    var result = runner.Run();

    // 3. 結果出力
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    string resultJson = JsonSerializer.Serialize(result, jsonOptions);

    if (outputPath != null)
    {
        File.WriteAllText(outputPath, resultJson);
        if (verbose) Console.WriteLine($"結果を {outputPath} に保存しました。");
    }
    else
    {
        Console.WriteLine(resultJson);
    }

    return 0;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"エラー: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"エラー: {ex.Message}");
    if (verbose) Console.Error.WriteLine(ex.StackTrace);
    return 2;
}
