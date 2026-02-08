using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace PileDesign.Services
{
    /// <summary>
    /// 群杭沈下解析例題データのJSONローダー
    /// </summary>
    public static class GroupSettlementExampleLoader
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
        public static GroupSettlementExampleData LoadFromFile(string fileName)
        {

            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"例題ファイルが見つかりません: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<GroupSettlementExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");
        }

        /// <summary>
        /// 例題データをInputModelに適用する
        /// </summary>
        public static void ApplyToInputModel(
            InputModel inputModel,
            GroupSettlementExampleData data,
            MainWindowViewModel viewModel)
        {
            // 群杭沈下解析結果をクリア
            inputModel.PileGroupSettlement.SettlementGridData?.Clear();
            inputModel.PileGroupSettlement.SettlementGridX?.Clear();
            inputModel.PileGroupSettlement.SettlementGridY?.Clear();

            // 慣性力中心点を設定
            if (data.InertiaPoint != null)
            {
                var inertiaPoint = new Point3D(
                    data.InertiaPoint.X,
                    data.InertiaPoint.Y,
                    data.InertiaPoint.Z);

                foreach (var loadCase in inputModel.LoadCasesInput.LoadCasesLevel1)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
                foreach (var loadCase in inputModel.LoadCasesInput.LoadCasesLevel2)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
            }

            // 載荷面標高を設定
            inputModel.PileGroupSettlement.LoadingPlaneAltitude = data.LoadingPlaneAltitude;

            // 矩形荷重を設定
            inputModel.PileGroupSettlement.RectLoads = [];
            foreach (var rectLoadDto in data.RectLoads)
            {
                inputModel.PileGroupSettlement.RectLoads.Add(new RectLoad
                {
                    X1 = rectLoadDto.X1,
                    X2 = rectLoadDto.X2,
                    Y1 = rectLoadDto.Y1,
                    Y2 = rectLoadDto.Y2,
                    QA = rectLoadDto.QA
                });
            }

            // 沈下計算用地層を設定
            inputModel.PileGroupSettlement.SettlementSoilLayers = [];
            foreach (var layerDto in data.SettlementSoilLayers)
            {
                inputModel.PileGroupSettlement.SettlementSoilLayers.Add(new SettlementSoilLayer
                {
                    BottomAltitude = layerDto.BottomAltitude,
                    Ek = layerDto.Ek,
                    PoissonsRatio = layerDto.PoissonsRatio,
                    Thickness = layerDto.Thickness
                });
            }

            // 杭体情報（pileBodies）の反映
            if (data.PileBodies != null && data.PileBodies.Count > 0)
            {
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
                    pileBody.PileBodySegments.Clear();
                    foreach (var segDto in pileBodyDto.Segments)
                    {
                        var segment = new PileBodySegment
                        {
                            No = segDto.No,
                            SegmentLength = segDto.SegmentLength,
                            SegmentDepth = segDto.SegmentDepth
                        };

                        // 先にコレクションに追加
                        pileBody.PileBodySegments.Add(segment);

                        // 追加後にPileSectionTypeとPrecastPileNameを設定
                        segment.PileSection.PileSectionType = segDto.PileSectionType;
                        if (!string.IsNullOrEmpty(segDto.PrecastPileName))
                            segment.PileSection.SetSelectedPrecastPileByName(segDto.PrecastPileName);

                        // 場所打ち杭用プロパティ
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
                    }
                    inputModel.PileBodies.Add(pileBody);
                }
            }

            // 杭位置を設定（ある場合）
            if (data.PileLayoutPositions != null && data.PileLayoutPositions.Count > 0)
            {
                inputModel.PileLayoutItems = [];
                foreach (var pos in data.PileLayoutPositions)
                {
                    var item = new PileLayoutDataItem
                    {
                        X = pos.X,
                        Y = pos.Y
                    };
                    item.SetMainWindowViewModel(viewModel);
                    inputModel.PileLayoutItems.Add(item);
                }
            }

            // 要素接続を設定（ある場合）
            if (data.ElementConnections != null && data.ElementConnections.Count > 0)
            {
                inputModel.Elements = [];
                foreach (var conn in data.ElementConnections)
                {
                    if (conn.Length >= 2 && conn[0] < inputModel.PileLayoutItems.Count && conn[1] < inputModel.PileLayoutItems.Count)
                    {
                        inputModel.Elements.Add(new Element(
                            "ダミー",
                            inputModel.PileLayoutItems[conn[0]],
                            inputModel.PileLayoutItems[conn[1]]));
                    }
                }
            }

            // グリッドを設定（ある場合）
            if (data.GridXItems != null)
            {
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
            }

            if (data.GridYItems != null)
            {
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
            }

            // 沈下解析オフセットを設定（ある場合）
            if (data.SettlementOffsets != null)
            {
                viewModel.GroupPileSettlementXOffset = data.SettlementOffsets.XOffset;
                viewModel.GroupPileSettlementYOffset = data.SettlementOffsets.YOffset;
                viewModel.GroupPileSettlementXSpacing = data.SettlementOffsets.XSpacing;
                viewModel.GroupPileSettlementYSpacing = data.SettlementOffsets.YSpacing;
            }

            // 根入れ部を無効化
            inputModel.EmbedmentInput.EmbedmentLayers.Clear();
            inputModel.ElementDivision.SoilEmbedment = null;
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
