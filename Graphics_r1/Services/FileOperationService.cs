using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PileDesign.Services
{
    /// <summary>
    /// ファイル操作に関するサービスクラス
    /// JSON シリアライズ、デシリアライズ、コレクション型変換を担当
    /// </summary>
    public class FileOperationService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// 保存前に ValidateFinite (NaN/∞ 検出) を実行するかどうか。
        /// 既定 false: 6 秒以上のリフレクションコストが惜しく、また AllowNamedFloatingPointLiterals
        /// により NaN/∞ もそのまま保存できるため事前チェック不要。
        /// デバッグ目的で値を確認したい場合のみ true にする。
        /// </summary>
        public bool ValidateFiniteBeforeSave { get; set; } = false;

        public FileOperationService(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// ProjectData を JSON ファイルに保存
        /// </summary>
        public void SaveProjectData(string filePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null,
            InputModel? resultInputSnapshot = null, DateTime? resultCapturedAt = null,
            PileFemLinkTable? pileFemLinks = null, bool? isElementSplit = null,
            bool inputChangedSinceAnalysis = false)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            ValidateFinite(inputModel);

            var projectData = new ProjectData
            {
                FormatVersion = 2,  // v2: PileLayoutItems[*].Z = 接合節点 Z (旧 v1 = 杭頭 Z)
                InputModel = inputModel,
                AnaModel = anaModel!,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null!,
                // 解析結果を保存しないときはスナップショットも不要
                ResultInputSnapshot = anaModel != null ? resultInputSnapshot : null,
                ResultCapturedAt = anaModel != null ? resultCapturedAt : null,
                InputChangedSinceAnalysis = anaModel != null ? inputChangedSinceAnalysis : null,
                PileFemLinks = anaModel != null ? pileFemLinks : null,
                IsElementSplit = isElementSplit,
            };

            // string 中間生成を避けて UTF-8 バイト直書き。
            // Stream オーバーロードは _jsonOptions の全設定 (WriteIndented / Encoder 等) を尊重し、
            // 内部で UTF-8 を直接書き出すため SaveProjectDataAsync と出力が一致する。
            // (旧実装は new Utf8JsonWriter(stream) を JsonWriterOptions 無しで生成しており
            //  WriteIndented が効かず常にコンパクト出力になっていた)
            WriteAtomically(filePath, stream => JsonSerializer.Serialize(stream, projectData, _jsonOptions));
        }

        /// <summary>
        /// 一時ファイルへ書いてから差し替える。
        ///
        /// 保存先へ直接書くと、途中で落ちた場合に<b>切り株のファイルが残る</b>。
        /// 自動保存では、その切り株が復元候補として拾われてしまう。
        /// 同じフォルダの一時ファイルに書き切ってから <see cref="File.Move(string, string, bool)"/> で
        /// 置き換えれば、保存先は「前の内容」か「新しい内容」のどちらかになる。
        /// </summary>
        private static void WriteAtomically(string filePath, Action<Stream> write)
        {
            string tempPath = filePath + ".saving";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                }
                File.Move(tempPath, filePath, overwrite: true);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        /// <summary>一時ファイルの後始末。消せなくても保存の失敗として扱わない。</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// ProjectData を JSON ファイルに非同期保存（UIスレッドをブロックしない）
        /// </summary>
        public async Task SaveProjectDataAsync(string filePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null,
            InputModel? resultInputSnapshot = null, DateTime? resultCapturedAt = null,
            PileFemLinkTable? pileFemLinks = null, bool? isElementSplit = null,
            bool inputChangedSinceAnalysis = false)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            var swTotal = Stopwatch.StartNew();

            var projectData = new ProjectData
            {
                FormatVersion = 2,  // v2: PileLayoutItems[*].Z = 接合節点 Z (旧 v1 = 杭頭 Z)
                InputModel = inputModel,
                AnaModel = anaModel!,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null!,
                // 解析結果を保存しないときはスナップショットも不要
                ResultInputSnapshot = anaModel != null ? resultInputSnapshot : null,
                ResultCapturedAt = anaModel != null ? resultCapturedAt : null,
                InputChangedSinceAnalysis = anaModel != null ? inputChangedSinceAnalysis : null,
                PileFemLinks = anaModel != null ? pileFemLinks : null,
                IsElementSplit = isElementSplit,
            };

            long tValidate = 0;
            long tSerialize = 0;
            long fileSize = 0;

            // ValidateFinite はリフレクション全走査で 6 秒以上かかるため既定では実行しない。
            // NumberHandling=AllowNamedFloatingPointLiterals が設定済みなので NaN/Infinity は
            // "NaN" 等の文字列としてそのまま保存される (シリアライズが失敗しない)。
            // 必要なときだけ ValidateFiniteBeforeSave=true で有効化する。
            await Task.Run(async () =>
            {
                if (ValidateFiniteBeforeSave)
                {
                    var swVal = Stopwatch.StartNew();
                    ValidateFinite(inputModel);
                    swVal.Stop();
                    tValidate = swVal.ElapsedMilliseconds;
                }

                var swSer = Stopwatch.StartNew();
                const int bufferSize = 1024 * 1024;  // 1 MB バッファ (旧 80KB → I/O 回数削減)

                // 一時ファイルへ書き切ってから差し替える (WriteAtomically と同じ理由)
                string tempPath = filePath + ".saving";
                try
                {
                    await using (var stream = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await JsonSerializer.SerializeAsync(stream, projectData, _jsonOptions);
                        await stream.FlushAsync();
                        fileSize = stream.Length;
                    }
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    TryDelete(tempPath);
                    throw;
                }

                swSer.Stop();
                tSerialize = swSer.ElapsedMilliseconds;
            });

            swTotal.Stop();
            Log.Information(
                "[Save] total={Total}ms validate={Validate}ms serialize+write={Serialize}ms size={SizeKB:N0}KB path={Path}",
                swTotal.ElapsedMilliseconds, tValidate, tSerialize, fileSize / 1024, System.IO.Path.GetFileName(filePath));
        }

        /// <summary>
        /// InputModel の中に NaN や ±∞ が含まれていれば、そのフィールドパスを示す
        /// InvalidOperationException を投げる。
        /// JSON シリアライズ前にプリチェックすることで、System.Text.Json のデフォルト
        /// 例外メッセージ (どの値が問題かを示さない) を、利用者が値を特定できる形に置換する。
        /// </summary>
        internal static void ValidateFinite(InputModel? inputModel)
        {
            if (inputModel == null) return;
            var path = FindNonFiniteDouble(inputModel, "InputModel");
            if (path != null)
            {
                throw new InvalidOperationException(
                    $"NaN または無限大の値が含まれているため保存できません。\n該当箇所: {path}\n値を確認してください。");
            }
        }

        // Type → 走査対象 PropertyInfo 配列のキャッシュ。
        // 旧版は毎オブジェクトで GetProperties + GetCustomAttribute を呼び、
        // 数万オブジェクト × 数十プロパティで数百万回のリフレクションが発生していた。
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _validateCache = new();

        private static PropertyInfo[] GetValidateProperties(Type t)
        {
            return _validateCache.GetOrAdd(t, type =>
            {
                var list = new List<PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    var pt = p.PropertyType;
                    // double / double? / 参照型 (PileDesign 名前空間に再帰可能) のみ対象
                    bool keep =
                        pt == typeof(double) ||
                        pt == typeof(double?) ||
                        (!pt.IsValueType && p.CanRead);
                    if (keep) list.Add(p);
                }
                return list.ToArray();
            });
        }

        private static string? FindNonFiniteDouble(object root, string rootName)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return Recurse(root, rootName);

            string? Recurse(object? obj, string path)
            {
                if (obj == null) return null;
                if (obj is string) return null;
                if (!visited.Add(obj)) return null;

                var t = obj.GetType();
                if (t.IsPrimitive || t.IsEnum || t == typeof(decimal) ||
                    t == typeof(DateTime) || t == typeof(Guid)) return null;

                if (obj is System.Collections.IEnumerable e && obj is not System.Collections.IDictionary)
                {
                    int i = 0;
                    foreach (var item in e)
                    {
                        var r = Recurse(item, $"{path}[{i}]");
                        if (r != null) return r;
                        i++;
                    }
                    return null;
                }

                // 自社型 (PileDesign.* 名前空間) のみ深く再帰する。
                // System.* やフレームワーク型に踏み込んでも誤検知や循環を招くだけ。
                var ns = t.Namespace ?? "";
                if (!ns.StartsWith("PileDesign", StringComparison.Ordinal)) return null;

                foreach (var p in GetValidateProperties(t))
                {
                    var pt = p.PropertyType;

                    if (pt == typeof(double))
                    {
                        double v;
                        try
                        {
                            var raw = p.GetValue(obj);
                            if (raw == null) continue;
                            v = (double)raw;
                        }
                        catch { continue; }
                        if (!double.IsFinite(v))
                            return $"{path}.{p.Name} = {FormatNonFinite(v)}";
                    }
                    else if (pt == typeof(double?))
                    {
                        double? v;
                        try { v = (double?)p.GetValue(obj); } catch { continue; }
                        if (v.HasValue && !double.IsFinite(v.Value))
                            return $"{path}.{p.Name} = {FormatNonFinite(v.Value)}";
                    }
                    else
                    {
                        // 参照型 → 再帰
                        object? child;
                        try { child = p.GetValue(obj); } catch { continue; }
                        var r = Recurse(child, $"{path}.{p.Name}");
                        if (r != null) return r;
                    }
                }
                return null;
            }
        }

        private static string FormatNonFinite(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "+∞";
            if (double.IsNegativeInfinity(v)) return "-∞";
            return v.ToString();
        }

        /// <summary>
        /// JSON ファイルから ProjectData を読み込み
        /// </summary>
        public ProjectData LoadProjectData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません。", filePath);

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (IOException ex)
            {
                throw new IOException($"ファイルの読込に失敗しました。\n別のプロセスで使用中の可能性があります。\n{filePath}", ex);
            }

            ProjectData? projectData;
            try
            {
                projectData = JsonSerializer.Deserialize<ProjectData>(json, _jsonOptions);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException(
                    $"ファイルのJSON形式が不正です。ファイルが破損しているか、対応していない形式です。\n{filePath}", ex);
            }

            if (projectData == null)
                throw new InvalidOperationException("ファイル形式が不正です。");

            // バージョン0はFormatVersionプロパティ追加前の旧ファイル → 互換あり
            // v1: 初期形式
            // v2: PileLayoutItems[*].Z のセマンティクスを「杭頭節点」→「接合節点」に変更 (2026-05 改修)
            //     v1 ファイルはロード時に MigratePileZSemantics_v1_to_v2 で内部的に v2 化される
            const int currentVersion = 2;
            if (projectData.FormatVersion > currentVersion)
            {
                throw new InvalidOperationException(
                    $"このファイルは新しいバージョン（v{projectData.FormatVersion}）で保存されています。\n" +
                    $"現在のプログラム（v{currentVersion}）では読み込めません。プログラムを更新してください。");
            }

            return projectData;
        }

        /// <summary>
        /// JSON ファイルから ProjectData を非同期読み込み（UIスレッドをブロックしない）
        /// </summary>
        public async Task<ProjectData> LoadProjectDataAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません。", filePath);

            // I/O + デシリアライズをバックグラウンドスレッドで実行
            return await Task.Run(() => LoadProjectData(filePath));
        }

        /// <summary>
        /// InputModel の全コレクションを ObservableCollection に変換
        /// </summary>
        public void ConvertToObservableCollections(InputModel inputModel)
        {
            if (inputModel == null)
                throw new ArgumentNullException(nameof(inputModel));

            // PileGroupSettlement のコレクション変換
            if (inputModel.PileGroupSettlement != null)
            {
                inputModel.PileGroupSettlement.SettlementSoilLayers =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementSoilLayers);
                inputModel.PileGroupSettlement.RectLoads =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.RectLoads);
                inputModel.PileGroupSettlement.SettlementGridX =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridX);
                inputModel.PileGroupSettlement.SettlementGridY =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridY);
                // 読み込み専用の複製。旧ファイルを開くためだけに残してある
                inputModel.PileGroupSettlement.SettlementGridData =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.LegacySettlementGridData);
            }

            // トップレベルのコレクション変換
            inputModel.PileLayoutItems = EnsureObservableCollection(inputModel.PileLayoutItems);
            inputModel.InputNodes = EnsureObservableCollection(inputModel.InputNodes);

            inputModel.GridXItems = EnsureObservableCollection(inputModel.GridXItems);
            inputModel.GridYItems = EnsureObservableCollection(inputModel.GridYItems);
            inputModel.PileBodies = EnsureObservableCollection(inputModel.PileBodies);
            inputModel.GroundsInput = EnsureObservableCollection(inputModel.GroundsInput);

            // EmbedmentInput のコレクション変換
            if (inputModel.EmbedmentInput != null)
            {
                inputModel.EmbedmentInput.EmbedmentLayers =
                    EnsureObservableCollection(inputModel.EmbedmentInput.EmbedmentLayers);
            }

            // LoadCasesInput のコレクション変換
            if (inputModel.LoadCasesInput != null)
            {
                inputModel.LoadCasesInput.LoadCasesLevel1 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel1);
                inputModel.LoadCasesInput.LoadCasesLevel2 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel2);
                inputModel.LoadCasesInput.LoadCombinations =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinations);
                inputModel.LoadCasesInput.LoadCombinationsPlus =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinationsPlus);
            }

            // ネストされたコレクションの変換
            if (inputModel.GroundsInput != null)
            {
                foreach (var ground in inputModel.GroundsInput)
                {
                    ground.GroundLayers = EnsureObservableCollection(ground.GroundLayers);
                    ground.GroundMassesData = EnsureObservableCollection(ground.GroundMassesData);
                }
            }

            if (inputModel.PileBodies != null)
            {
                foreach (var pileBody in inputModel.PileBodies)
                {
                    pileBody.PileBodySegments = EnsureObservableCollection(pileBody.PileBodySegments);
                }
            }

            // FoundationBeamInput のコレクション変換
            if (inputModel.FoundationBeamInput != null)
            {
                inputModel.FoundationBeamInput.Materials =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Materials);
                inputModel.FoundationBeamInput.Sections =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Sections);
                inputModel.FoundationBeamInput.Nodes =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Nodes);
                inputModel.FoundationBeamInput.Beams =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Beams);

                // 梁要素があるが材料・断面が空のとき、参照先を保証するためデフォルトエントリを追加
                if (inputModel.FoundationBeamInput.Beams.Count > 0)
                {
                    inputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();
                }
            }

            // Null チェック・空コレクション初期化
            inputModel.GridXItems ??= new ObservableCollection<GridDataItem>();
            inputModel.GridYItems ??= new ObservableCollection<GridDataItem>();
        }

        /// <summary>
        /// IEnumerable を ObservableCollection に変換（既に ObservableCollection の場合はそのまま返す）
        /// </summary>
        private static ObservableCollection<T> EnsureObservableCollection<T>(IEnumerable<T>? source)
        {
            if (source is ObservableCollection<T> observableCollection)
                return observableCollection;

            return source != null ? new ObservableCollection<T>(source) : new ObservableCollection<T>();
        }
    }
}
