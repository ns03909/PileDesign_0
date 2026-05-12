using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Windows;
using PileDesign.Services;


namespace PileDesign.Models
{
    class CheckInputData
    {
        // データのチェック
        public static bool CheckData(InputModel inputModel)

        {
            string message = "";
            message = CheckSoilPile(inputModel, message);
            message = CheckSoilEmbedment(inputModel, message);

            if (message != "")
            {
                MessageService.Show(message);
                return false;
            }
            else
            {
                MessageService.Show("問題は検出されませんでした。\n" +
                    "\n杭体の存在高さ範囲すべてに選定された土層の定義が存在します。" +
                    "\n根入部の存在高さ範囲すべてに選定された土層の定義が存在します。");
                return true;
            }
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
