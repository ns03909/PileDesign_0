using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Media3D;



namespace PileDesign.Services
{
    /// <summary>
    /// 杭例題データのJSONローダー
    /// </summary>
    public static class PileExampleLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// JSONファイルから例題データを読み込む
        /// </summary>
        /// <param name="fileName">JSONファイル名（拡張子なし）</param>
        /// <returns>例題データ</returns>
        public static PileExampleData LoadFromFile(string fileName)
        {
            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"例題ファイルが見つかりません: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PileExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");
        }

        /// <summary>
        /// 例題データをInputModelに適用する
        /// </summary>
        public static void ApplyToInputModel(
            InputModel inputModel,
            PileExampleData data,
            MainWindowViewModel viewModel)
        {
            // LoadCasesLevel1 設定
            for (int i = 0; i < inputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                var loadCase = inputModel.LoadCasesInput.LoadCasesLevel1[i];
                loadCase.ForceActionPoint = new Point3D(
                    data.LoadCaseLevel1.ForceActionPointX,
                    data.LoadCaseLevel1.ForceActionPointY,
                    data.LoadCaseLevel1.ForceActionPointZ);
                loadCase.IsApplicable = true;
                loadCase.UpperMassForce = data.LoadCaseLevel1.UpperMassForce;
                loadCase.FoundationMassForce = data.LoadCaseLevel1.FoundationMassForce;
            }

            // LoadCasesLevel2 設定
            for (int i = 0; i < inputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                var loadCase = inputModel.LoadCasesInput.LoadCasesLevel2[i];
                loadCase.ForceActionPoint = new Point3D(
                    data.LoadCaseLevel2.ForceActionPointX,
                    data.LoadCaseLevel2.ForceActionPointY,
                    data.LoadCaseLevel2.ForceActionPointZ);
                loadCase.IsApplicable = true;

                // Level2Alt がある場合は交互に適用（例3.1の特殊ケース）
                if (data.LoadCaseLevel2Alt != null && (i == 1 || i == 3))
                {
                    loadCase.UpperMassForce = data.LoadCaseLevel2Alt.UpperMassForce;
                    loadCase.FoundationMassForce = data.LoadCaseLevel2Alt.FoundationMassForce;
                }
                else
                {
                    loadCase.UpperMassForce = data.LoadCaseLevel2.UpperMassForce;
                    loadCase.FoundationMassForce = data.LoadCaseLevel2.FoundationMassForce;
                }
            }

            // PileBodies 設定
            inputModel.PileBodies.Clear();
            foreach (var pileBodyDto in data.PileBodies)
            {
                var pileBody = new PileBodyInput
                {
                    PileBodyRef = pileBodyDto.PileBodyRef,
                    PileBodyType = pileBodyDto.PileBodyType,
                    PileTopType = pileBodyDto.PileTopType,
                    PileConstructionType = pileBodyDto.PileConstructionType,
                    PileToeDia = pileBodyDto.PileToeDia,
                    TipNonPermability = pileBodyDto.TipNonPermability,
                    EmbedmentIntoBearingSoil = pileBodyDto.EmbedmentIntoBearingSoil,
                    PileInnerDia = pileBodyDto.PileInnerDia,
                    PileTipStyle = pileBodyDto.PileTipStyle,
                    SettlePileToeDia = pileBodyDto.SettlePileToeDia,
                    SettleAlpha = pileBodyDto.SettleAlpha,
                    SettleN = pileBodyDto.SettleN
                };

                // セグメント設定
                pileBody.PileBodySegments.Clear();
                foreach (var segDto in pileBodyDto.Segments)
                {
                    var segment = new PileBodySegment
                    {
                        No = segDto.No,
                        SegmentLength = segDto.SegmentLength,
                        SegmentDepth = segDto.SegmentDepth
                    };
                    segment.PileSection.PileSectionType = segDto.PileSectionType;

                    // 既製杭の場合
                    if (!string.IsNullOrEmpty(segDto.PrecastPileName))
                    {
                        segment.PileSection.SetSelectedPrecastPileByName(segDto.PrecastPileName);
                    }

                    // 場所打ち杭（鉄筋コンクリート部）の場合
                    if (segDto.ConcreteOutDia.HasValue)
                        segment.PileSection.ConcreteOutDia = segDto.ConcreteOutDia.Value;
                    if (segDto.ConcreteThickness.HasValue)
                        segment.PileSection.ConcreteThickness = segDto.ConcreteThickness.Value;
                    if (segDto.MainBarDr.HasValue)
                        segment.PileSection.MainBarDr = segDto.MainBarDr.Value;
                    if (segDto.PipeTs.HasValue)
                        segment.PileSection.PipeTs = segDto.PipeTs.Value;
                    if (segDto.PipeDia.HasValue)
                        segment.PileSection.PipeDia = segDto.PipeDia.Value;
                    if (segDto.MainBarNum.HasValue)
                        segment.PileSection.MainBarNum = segDto.MainBarNum.Value;
                    if (!string.IsNullOrEmpty(segDto.MainBarSize))
                        segment.PileSection.MainBarSize = segDto.MainBarSize;
                    if (segDto.ConcreteFc.HasValue)
                        segment.PileSection.ConcreteFc = segDto.ConcreteFc.Value;
                    if (segDto.ConcreteGsi.HasValue)
                        segment.PileSection.ConcreteGsi = segDto.ConcreteGsi.Value;
                    if (segDto.ConcreteGamma.HasValue)
                        segment.PileSection.ConcreteGamma = segDto.ConcreteGamma.Value;

                    pileBody.PileBodySegments.Add(segment);
                }

                inputModel.PileBodies.Add(pileBody);
            }

            // PileLayoutItems 設定
            inputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>();
            foreach (var layoutDto in data.PileLayoutItems)
            {
                var item = new PileLayoutDataItem
                {
                    PileNo = layoutDto.PileNo,
                    PileBodyNo = layoutDto.PileBodyNo,
                    GroundNo = layoutDto.GroundNo,
                    SoilPileAltNo = layoutDto.SoilPileAltNo,
                    GroupPileFactor = layoutDto.GroupPileFactor,
                    PileSpacingFactor = layoutDto.PileSpacingFactor,
                    X = layoutDto.X,
                    Y = layoutDto.Y,
                    Z = layoutDto.Z,
                    AxialForceVL0 = layoutDto.AxialForceVL0,
                    // 配列 -> ObservableCollection に変換して代入する
                    AxialForceLevel1s = layoutDto.AxialForceLevel1s != null
                        ? new ObservableCollection<double>(layoutDto.AxialForceLevel1s)
                        : new ObservableCollection<double>(),
                    AxialForceLevel2s = layoutDto.AxialForceLevel2s != null
                        ? new ObservableCollection<double>(layoutDto.AxialForceLevel2s)
                        : new ObservableCollection<double>()
                };
                item.SetMainWindowViewModel(viewModel);
                inputModel.PileLayoutItems.Add(item);
            }

            // GridXItems 設定
            inputModel.GridXItems.Clear();
            foreach (var gridDto in data.GridXItems)
            {
                inputModel.GridXItems.Add(new GridDataItem
                {
                    Name = gridDto.Name,
                    Coord = gridDto.Coord,
                    Spacing = gridDto.Spacing
                });
            }

            // GridYItems 設定
            inputModel.GridYItems.Clear();
            foreach (var gridDto in data.GridYItems)
            {
                inputModel.GridYItems.Add(new GridDataItem
                {
                    Name = gridDto.Name,
                    Coord = gridDto.Coord,
                    Spacing = gridDto.Spacing
                });
            }

            // Elements クリア
            if (inputModel.Elements != null)
            {
                inputModel.Elements.Clear();
            }
            else
            {
                inputModel.Elements = new ObservableCollection<Element>();
            }

            // PileGroupSettlement クリア
            if (inputModel.PileGroupSettlement != null)
            {
                inputModel.PileGroupSettlement.RectLoads?.Clear();
                inputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                inputModel.PileGroupSettlement.LoadingPlaneAltitude = 0.0;
            }

            // 根入れクリア
            inputModel.EmbedmentInput.EmbedmentLayers.Clear();
        }

        /// <summary>
        /// Examplesフォルダのパスを取得
        /// </summary>
        private static string GetExamplesPath()
        {
            // 実行ファイルのディレクトリからExamplesフォルダを探す
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? ".";
            var examplesPath = Path.Combine(assemblyDir, "Examples");

            if (Directory.Exists(examplesPath))
            {
                return examplesPath;
            }

            // 開発時: プロジェクトディレクトリのExamplesフォルダ
            var projectDir = FindProjectDirectory(assemblyDir);
            if (projectDir != null)
            {
                var devExamplesPath = Path.Combine(projectDir, "Examples");
                if (Directory.Exists(devExamplesPath))
                {
                    return devExamplesPath;
                }
            }

            throw new DirectoryNotFoundException($"Examplesフォルダが見つかりません: {examplesPath}");
        }

        /// <summary>
        /// プロジェクトディレクトリを探す
        /// </summary>
        private static string? FindProjectDirectory(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                // .csprojファイルがあればプロジェクトディレクトリ
                if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0)
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
