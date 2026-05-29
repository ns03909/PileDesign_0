using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Windows;
using PileDesign.Services;


namespace PileDesign.Models
{
    class CheckInputData
    {
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

                    // 場所打ち系 (PileBodyType=場所打ち鉄筋コンクリート杭 / 場所打ち鋼管コンクリート杭+鉄筋コンクリート部)
                    bool isInsituRC =
                        sec.PileBodyType == "場所打ち鉄筋コンクリート杭" ||
                        (sec.PileBodyType == "場所打ち鋼管コンクリート杭" && sec.PileSectionType == "鉄筋コンクリート部");

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
                        && sec.PileBodyType != "鋼管杭"  // 純鋼管杭は Fc 不要
                        && !(sec.PileBodyType == "場所打ち鋼管コンクリート杭" && sec.PileSectionType == "鋼管部"))
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
