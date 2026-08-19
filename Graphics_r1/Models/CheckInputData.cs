using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using PileDesign.Services;


namespace PileDesign.Models
{
    class CheckInputData
    {
        /// <summary>
        /// 解析実行は止めないがユーザーに確認させたい「注意レベル」の入力警告を収集する。
        /// プリフライトダイアログから呼び出され、サマリと並んで表示される。
        /// ・ΔZc &lt;= 0 (接合点が杭頭より下または同位置 = ジオメトリ異常)
        /// ・地盤側の既存 ValidateForAnalysis 由来の注意 (Es=0 / 粘性土で Cu=0 / 深度順序逆 / 土質点 N=0 等)
        /// </summary>
        public static List<string> CollectInputWarnings(InputModel inputModel)
        {
            var warnings = new List<string>();
            if (inputModel == null) return warnings;

            // 各杭の ΔZc (接合点 − 杭頭オフセット)
            if (inputModel.PileLayoutItems != null)
            {
                for (int i = 0; i < inputModel.PileLayoutItems.Count; i++)
                {
                    var p = inputModel.PileLayoutItems[i];
                    if (p == null) continue;
                    if (p.FoundationBeamDeltaZc <= 0)
                        warnings.Add($"杭 No.{p.No}: 接合-杭頭 ΔZc = {p.FoundationBeamDeltaZc:N3} (>0 で接合点が杭頭の上に来るのが正常)。");
                }
            }

            // 各地盤の既存検証 (Es=0 / 粘性土 Cu=0 / 層・土質点深度順序逆 / N=0)
            if (inputModel.GroundsInput != null)
            {
                for (int i = 0; i < inputModel.GroundsInput.Count; i++)
                {
                    var gi = inputModel.GroundsInput[i];
                    if (gi == null) continue;
                    if (!gi.ValidateForAnalysis(out string msg))
                    {
                        foreach (var raw in (msg ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = raw.TrimStart('-', ' ', '\t');
                            if (!string.IsNullOrWhiteSpace(t))
                                warnings.Add($"地盤 {i + 1}: {t.Trim()}");
                        }
                    }
                }
            }

            // メーカー別の高支持力杭工法の適用範囲。
            // 範囲外でもクランプ後の値で計算は続けるため、エラーではなく警告として出す。
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles != null)
            {
                foreach (var soilPile in soilPiles)
                {
                    if (soilPile == null) continue;
                    foreach (var w in soilPile.ValidateSmartMagnumRange())
                        warnings.Add($"Smart-MAGNUM {w}");
                    foreach (var w in soilPile.ValidateHybridKneadingRange())
                        warnings.Add($"Hybridニーディング {w}");
                }
            }

            return warnings;
        }

        /// <summary>
        /// 解析実行ゲート: 入力データの整合性を検証する。
        ///   - エラー検出時: エラー内容とともに警告ダイアログを表示し false を返す (解析中止)
        ///   - 問題なし: ダイアログを出さずに true を返す (解析続行)
        /// 水平解析・単杭沈下・群杭沈下・基礎梁考慮鉛直 各解析の開始直前に呼ぶこと。
        /// </summary>
        public static bool ValidateForAnalysis(InputModel inputModel, string analysisName = "解析")
        {
            string message = "";
            message = CheckSoilPile(inputModel, message);
            message = CheckSoilEmbedment(inputModel, message);
            message = CheckPileBodyGeometry(inputModel, message);
            message = CheckGroundLayerGeometry(inputModel, message);

            if (message.Length == 0) return true; // OK: ダイアログなしで続行

            MessageService.Show(
                $"入力データに以下の問題があります。{analysisName}を中止します。\n\n{message}",
                $"{analysisName} 入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        // データのチェック
        public static bool CheckData(InputModel inputModel)

        {
            string message = "";
            message = CheckSoilPile(inputModel, message);
            message = CheckSoilEmbedment(inputModel, message);
            message = CheckPileBodyGeometry(inputModel, message);
            message = CheckGroundLayerGeometry(inputModel, message);

            if (message != "")
            {
                MessageService.Show(message);
                return false;
            }
            else
            {
                MessageService.Show("問題は検出されませんでした。\n" +
                    "\n杭体の存在高さ範囲すべてに選定された土層の定義が存在します。" +
                    "\n根入部の存在高さ範囲すべてに選定された土層の定義が存在します。" +
                    "\n杭断面寸法・主筋ピッチ円直径・コンクリート設計基準強度の整合性に問題はありません。" +
                    "\n土層厚さに問題はありません。");
                return true;
            }
        }

        /// <summary>
        /// 杭体ジオメトリ・断面整合性のチェック (MED #5, #6, #7)。
        ///   - 杭セグメント長 ≤ 0
        ///   - ConcreteOutDia ≤ 0 (場所打ち系)
        ///   - MainBarDr が 0 < MainBarDr < ConcreteOutDia を満たすか (場所打ち系)
        ///   - ConcreteFc ≤ 0
        /// 不整合があれば message に追記して返す。
        /// </summary>
        public static string CheckPileBodyGeometry(InputModel inputModel, string message)
        {
            if (inputModel?.PileBodies == null) return message;

            for (int i = 0; i < inputModel.PileBodies.Count; i++)
            {
                var pb = inputModel.PileBodies[i];
                if (pb?.PileBodySegments == null) continue;
                int pbNo = i + 1;

                for (int j = 0; j < pb.PileBodySegments.Count; j++)
                {
                    var seg = pb.PileBodySegments[j];
                    if (seg == null) continue;
                    int segNo = j + 1;
                    var sec = seg.PileSection;

                    if (seg.SegmentLength <= 0)
                        message += $"杭体{pbNo} 区間{segNo}: 区間長が 0 以下です ({seg.SegmentLength}).\n";

                    if (sec == null) continue;

                    // 節杭 は上杭に継手で接合される下杭として使うのが一般的なので、
                    // 最上段区間に来ている場合は知らせる。
                    // ただし Smart-MAGNUM / Hybrid ニーディングのように先端が節杭である前提の工法を
                    // 1 区間でモデル化することはありうるため、禁止ではなく注意にとどめる。
                    if (segNo == 1 && sec.IsNodularPile)
                    {
                        message += $"杭体{pbNo} 区間{segNo}: {sec.PileSectionType} が最上段の区間にあります " +
                                   $"(節杭は上杭に継手で接合する下杭として使うのが一般的です).\n";
                    }

                    // 節杭 の拡頭径が直上区間の径と合っていない場合も知らせる
                    if (sec.IsNodularPile && !string.IsNullOrEmpty(sec.NodularHeadNote)
                        && sec.NodularHeadNote.Contains("一致する拡頭径がありません"))
                    {
                        message += $"杭体{pbNo} 区間{segNo}: {sec.NodularHeadNote}.\n";
                    }

                    // 場所打ち系 (PileBodyType=場所打ち鉄筋コンクリート杭 / 場所打ち鋼管コンクリート杭+鉄筋コンクリート部)
                    bool isInsituRC =
                        sec.PileBodyType == PileTypeNames.InsituRc ||
                        (sec.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && sec.PileSectionType == PileTypeNames.RcSection);

                    if (isInsituRC)
                    {
                        if (sec.ConcreteOutDia <= 0)
                            message += $"杭体{pbNo} 区間{segNo}: コンクリート外径が 0 以下です ({sec.ConcreteOutDia}).\n";
                        if (sec.MainBarNum > 0 && sec.ConcreteOutDia > 0
                            && (sec.MainBarDr <= 0 || sec.MainBarDr >= sec.ConcreteOutDia))
                        {
                            message += $"杭体{pbNo} 区間{segNo}: 主筋配置直径 (MainBarDr={sec.MainBarDr}) が外径 ({sec.ConcreteOutDia}) との関係で不正です " +
                                        $"(0 < MainBarDr < 外径 を満たすこと).\n";
                        }
                    }

                    if (sec.ConcreteFc <= 0
                        && sec.PileBodyType != PileTypeNames.SteelPipe  // 純鋼管杭は Fc 不要
                        && !(sec.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && sec.PileSectionType == PileTypeNames.SteelPipeSection))
                    {
                        message += $"杭体{pbNo} 区間{segNo}: コンクリート設計基準強度 Fc が 0 以下です ({sec.ConcreteFc}).\n";
                    }
                }
            }
            return message;
        }

        /// <summary>
        /// 地盤層厚の整合性チェック (MED #6)。
        ///   - 各 GroundLayer の LayerThickness が 0 以下なら指摘
        /// </summary>
        public static string CheckGroundLayerGeometry(InputModel inputModel, string message)
        {
            if (inputModel?.GroundsInput == null) return message;
            for (int g = 0; g < inputModel.GroundsInput.Count; g++)
            {
                var gi = inputModel.GroundsInput[g];
                if (gi?.GroundLayers == null) continue;
                int gNo = g + 1;
                for (int li = 0; li < gi.GroundLayers.Count; li++)
                {
                    var layer = gi.GroundLayers[li];
                    if (layer == null) continue;
                    if (layer.LayerThickness <= 0)
                        message += $"地盤{gNo} 層{li + 1}: 層厚が 0 以下です ({layer.LayerThickness}).\n";
                }
            }
            return message;
        }

        // 杭のすべての高さ内で土質が定義されているかをチェック
        public static string CheckSoilPile(InputModel inputModel, string message)
        {
            if (inputModel.PileLayoutItems.Count == 0)
            {
                message += $"杭配置にデータがありません。\n";
            }


            ObservableCollection<(int, int, double)> UsedGroundNosPileBodyNosPileTopAltitudes = [];

            foreach (PileLayoutDataItem pileLayoutDataItem in inputModel.PileLayoutItems)
            {
                int pileBodyNo = pileLayoutDataItem.PileBodyNo;
                int groundNo = pileLayoutDataItem.GroundNo;
                // pileTopAltitude は杭頭高さ。v2 セマンティクスでは pile.Z は接合節点 Z なので PileHeadZ を使う。
                // SoilPile キャッシュ (杭頭基準) との整合のためにも PileHeadZ で揃える。
                double pileTopAltitude = pileLayoutDataItem.PileHeadZ;

                (int, int, double) groundNoPileBodyNoPileTopAltitude = (groundNo, pileBodyNo, pileTopAltitude);

                // UsedPileBodyNos内にpileBodyNoが含まれているかチェック
                if (!UsedGroundNosPileBodyNosPileTopAltitudes.Contains(groundNoPileBodyNoPileTopAltitude))
                {
                    // pileBodyNoがUsedPileBodyNosに含まれていない場合の処理
                    UsedGroundNosPileBodyNosPileTopAltitudes.Add(groundNoPileBodyNoPileTopAltitude);

                    if (inputModel.PileBodies[pileBodyNo - 1].PileBodySegments.Count == 0)
                    {
                        // 杭体データが空の場合のメッセージ
                        message += $"杭体番号{pileBodyNo}に杭区間データがありません。\n";
                        continue; // 次の杭体へスキップ
                    }

                    if (inputModel.GroundsInput[groundNo - 1].GroundLayers.Count == 0)
                    {
                        // 杭体データが空の場合のメッセージ
                        message += $"地盤番号{groundNo}に土層データがありません。\n";
                        continue; // 次の杭体へスキップ
                    }

                    double pileBottomAltitude = pileTopAltitude - inputModel.PileBodies[pileBodyNo - 1].PileBodySegments[^1].SegmentDepth;

                    ObservableCollection<GroundLayerInput> groundLayerDataItems = inputModel.GroundsInput[groundNo - 1].GroundLayers;
                    double groundTopAltitude = inputModel.GroundsInput[groundNo - 1].GroundLayers[0].BottomAltitude
                            + inputModel.GroundsInput[groundNo - 1].GroundLayers[0].LayerThickness;
                    double groundBottomAltitude = inputModel.GroundsInput[groundNo - 1].GroundLayers[^1].BottomAltitude;

                    if (groundTopAltitude < pileTopAltitude)
                    {
                        message += $"杭体番号{pileBodyNo}の最上部が地盤番号{groundNo}の最上部よりも浅いです。\n";
                    }

                    if (pileBottomAltitude < groundBottomAltitude)
                    {
                        message += $"杭体番号{pileBodyNo}の最下部が地盤番号{groundNo}の最下部よりも深いです。\n";
                    }
                }
            }
            return message;
        }

        //根入れのすべての高さ内で土質が定義されているかをチェック
        public static string CheckSoilEmbedment(InputModel inputModel, string message)
        {
            // 根入なし (EmbedmentInput 未初期化) なら検証スキップ
            if (inputModel.EmbedmentInput == null) return message;
            if (inputModel.EmbedmentInput.EmbedmentLayersCount != 0)
            {
                int groundNo = inputModel.EmbedmentInput.GroundNo;

                if (inputModel.GroundsInput[groundNo - 1].GroundLayers.Count == 0)
                {
                    // 杭体データが空の場合のメッセージ
                    message += $"根入部で選択された地盤番号{groundNo}に土層データがありません。\n";
                }
                else
                {
                    double groundTopAltitude = inputModel.GroundsInput[groundNo - 1].GroundLayers[0].BottomAltitude
                            + inputModel.GroundsInput[groundNo - 1].GroundLayers[0].LayerThickness;
                    double groundBottomAltitude = inputModel.GroundsInput[groundNo - 1].GroundLayers[^1].BottomAltitude;

                    double embedmentTopAltitude = inputModel.EmbedmentInput.EmbedmentLayers[0].TopAltitude;
                    double embedmentBottomAltitude = inputModel.EmbedmentInput.EmbedmentLayers[^1].BottomAltitude;
                    if (groundTopAltitude < embedmentTopAltitude)
                    {
                        message += "根入部の最上部が地盤番号{" + groundNo + "}の最上部よりも浅いです。\n";
                    }

                    if (embedmentBottomAltitude < groundBottomAltitude)
                    {
                        message += "根入部の最下部が地盤番号{" + groundNo + "}の最下部よりも深いです。\n";
                    }
                }
            }
            return message;
        }
    }
}
