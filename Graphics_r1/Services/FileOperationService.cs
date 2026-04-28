using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public FileOperationService(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// ProjectData を JSON ファイルに保存
        /// </summary>
        public void SaveProjectData(string filePath, InputModel inputModel, AnaModel anaModel,
            IList<FEM.VerticalBeamCaseResult> verticalBeamCaseResults = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            ValidateFinite(inputModel);

            var projectData = new ProjectData
            {
                InputModel = inputModel,
                AnaModel = anaModel,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null
            };

            string json = JsonSerializer.Serialize(projectData, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// ProjectData を JSON ファイルに非同期保存（UIスレッドをブロックしない）
        /// </summary>
        public async Task SaveProjectDataAsync(string filePath, InputModel inputModel, AnaModel anaModel,
            IList<FEM.VerticalBeamCaseResult> verticalBeamCaseResults = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            ValidateFinite(inputModel);

            var projectData = new ProjectData
            {
                InputModel = inputModel,
                AnaModel = anaModel,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null
            };

            await Task.Run(() =>
            {
                string json = JsonSerializer.Serialize(projectData, _jsonOptions);
                File.WriteAllText(filePath, json);
            });
        }

        /// <summary>
        /// InputModel の中に NaN や ±∞ が含まれていれば、そのフィールドパスを示す
        /// InvalidOperationException を投げる。
        /// JSON シリアライズ前にプリチェックすることで、System.Text.Json のデフォルト
        /// 例外メッセージ (どの値が問題かを示さない) を、利用者が値を特定できる形に置換する。
        /// </summary>
        internal static void ValidateFinite(InputModel inputModel)
        {
            if (inputModel == null) return;
            var path = FindNonFiniteDouble(inputModel, "InputModel");
            if (path != null)
            {
                throw new InvalidOperationException(
                    $"NaN または無限大の値が含まれているため保存できません。\n該当箇所: {path}\n値を確認してください。");
            }
        }

        private static string FindNonFiniteDouble(object root, string rootName)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return Recurse(root, rootName);

            string Recurse(object obj, string path)
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

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                    var childPath = $"{path}.{p.Name}";
                    var pt = p.PropertyType;

                    if (pt == typeof(double))
                    {
                        double v;
                        try { v = (double)p.GetValue(obj); } catch { continue; }
                        if (!double.IsFinite(v))
                            return $"{childPath} = {FormatNonFinite(v)}";
                    }
                    else if (pt == typeof(double?))
                    {
                        double? v;
                        try { v = (double?)p.GetValue(obj); } catch { continue; }
                        if (v.HasValue && !double.IsFinite(v.Value))
                            return $"{childPath} = {FormatNonFinite(v.Value)}";
                    }
                    else if (!pt.IsValueType && p.CanRead)
                    {
                        object child;
                        try { child = p.GetValue(obj); } catch { continue; }
                        var r = Recurse(child, childPath);
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

            ProjectData projectData;
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
            const int currentVersion = 1;
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
                inputModel.PileGroupSettlement.SettlementGridData =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridData);
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
        private static ObservableCollection<T> EnsureObservableCollection<T>(IEnumerable<T> source)
        {
            if (source is ObservableCollection<T> observableCollection)
                return observableCollection;

            return source != null ? new ObservableCollection<T>(source) : new ObservableCollection<T>();
        }
    }
}
