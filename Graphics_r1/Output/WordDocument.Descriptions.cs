using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 杭種別（F.T.Pile/キャプテンパイル）の構法説明を提供する partial。
    internal partial class WordDocument
    {
        // FT-Pile構法説明
        private void AddDescriptionFTPile(Body body)
        {
            //AddText(body, "F.T.Pile構法", "left");
            AddHeader1(body, "F.T.Pile構法", 1);
            AddText(body, "(BCJ評定-FD0141)", "left");

            AddHeader1(body, "概要", 2);

            AddText(body, "設計用地震力に相当する水平力と鉛直力が作用したとき，" +
                "杭頭接合部および杭体の応力\r\nを4.4 に示す応力解析により算定し，" +
                "以下の項目を満足していることを確認する。" +
                "ただし，\r\nパイルキャップにFc36を超える高強度コンクリートを使用する場合は，" +
                "杭頭の損傷限界に\r\nついて別途検討を行うものとする。" +
                "表4-3-1に設計クライテリアを示す。", "left");
            AddText(body, "（1） 標準タイプにおいては，杭頭接合部の回転角がパイルキャップのひび割れ発生限界によって決まる$\\theta_{ac}$を超えないこと。");

            AddText(body, "（2） 引抜き対応タイプにおいては，" +
                "杭頭接合部の回転角がパイルキャップのひび割れ発生限界によって" +
                "求まる回転角$\\theta_{ac}$を超えないこと，" +
                "かつ引抜き抵抗用鋼棒の短期許容応力度から求まる回転角$\\theta_{as}$を超えないこと。");
            AddText(body, "（3） 杭頭接合部のせん断力が短期許容せん断力を超えないこと。");
            AddText(body, "（4） 杭体の軸力・曲げモーメント・せん断力が短期許容耐力を超えないこと。");

            AddHeader1(body, "許容回転角の設定", 2);
            //AddText(body, "許容回転角の設定");
            AddText(body, "杭頭接合部の許容回転角$\\theta_{a}$は，パイルキャップにひび割れが発生する回転角$\\theta_{ac}$以下かつ，" +
                "引抜き対応タイプにおいては引抜き抵抗用鋼棒が短期許容応力に達する回転角$\\theta_{as}$以下とし，次式によって定める。 ");

            AddInlineMathParagraph(body, [Tex(@"\theta_{a} = \min\left(\theta_{ac},\theta_{as}\right)")]);
            AddInlineMathParagraph(body, [Tex(@"\theta_{ac} = 0.03 - 0.05\frac{\sigma_{nc}}{\phi_{c}\cdot F_{c}}")]);
            AddInlineMathParagraph(body, [Tex(@"\theta_{as} = \frac{\delta_{a}}{D_{s}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{ac}"), ": パイルキャップのひび割れで決まる許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{as}"), ": 引抜き抵抗用鋼棒の短期許容応力で決まる許容回転角[rad]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{nc}"), ": 圧縮合力による軸応力度[N/mm<^2>]"]);

            AddInlineMathParagraph(body, [Tex(@"\sigma_{nc} = \frac{N_{c}}{A_{p}} \times 10^{-3}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{c}"), ": コンクリートの圧縮合力"]);

            AddText(body, "標準タイプ", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = N")]);

            AddInlineMathParagraph(body, [Tex(@"N \le 0")]);
            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);

            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| < \left|\frac{M}{D_{s}}\right|")]);

            AddInlineMathParagraph(body, [Tex(@"N > 0")]);
            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);

            AddText(body, "引抜きタイプ", "left");
            AddText(body, "ⅰ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = 0")]);
            AddText(body, "ⅱ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = \left|\frac{M}{D_{s}}\right| + \frac{N}{2}")]);
            AddText(body, "ⅲ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = N")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{p}"), ": 支圧面積（杭頭面積）[mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\phi_{c}"), ": 支圧係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": パイルキャップコンクリートの設計基準強度[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び[mm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ[mm]"]);

            AddHeader1(body, "短期許容せん断力の算定", 2);

            AddText(body, "設計用地震力によるせん断力が，杭頭接合部の短期許容せん断力以内であることを確認する。" +
                "杭頭接合部の短期許容せん断力は，" +
                "杭頭の摩擦抵抗とパイルキャップ（へりあき）のせん断抵抗を考慮した（4.4）式により算定する。", "left");

            AddInlineMathParagraph(body, [Tex(@"Q_{a} = \mu_{a}\cdot N_{c} + Q_{ha}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{a}"), ": 杭頭接合部の短期許容せん断力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\mu_{a}"), ": 許容摩擦係数(=0.2)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{ha}"), ": パイルキャップの短期許容せん断抵抗[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{ct}"), ": コンクリートの引張抵抗[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{ct}"), ": 破壊面の水平投影面積(mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"B"), ": パイルキャップの短辺[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"h"), ": へりあき[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"d_{e}"), ": 杭頭の有効埋込み深さ[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"d"), ": 杭頭の埋込み深さ[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D"), ": 杭径[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{hu}"), ": パイルキャップの終局せん断抵抗[kN]"]);

            AddHeader1(body, "杭頭接合部の回転剛性の設定", 2);

            AddText(body, "$M$-$\\theta$関係の基本式", "left");
            AddText(body, "杭頭接合部の回転剛性は，杭頭の曲げモーメント$M$と回転角$\\theta$の関係（以後，$M$-$\\theta$関係と略す）を" +
                "（4.8）式と（4.9）式でモデル化して用いる。図4-4-2に$M$-$\\theta$関係のモデル化の概要を示す。", "left");

            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);
            AddInlineMathParagraph(body, [Tex(@"M = \frac{\theta}{\theta + \theta_{f}} \cdot M_{max}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M"), ": 杭頭接合部の曲げモーメント[kNm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta"), ": 杭頭接合部の回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{f}"), ": 基準回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kN・m/rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大曲げモーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{c} = \frac{M_{c}}{K_{0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{c}"), ": 浮き上がりモーメント[kNm]"]);

            AddInlineMathParagraph(body, [Tex(@"M_{c} = \frac{D_{1}^{2} + D_{2}^{2}}{8 D_{1}} \cdot N")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[mm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径[mm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{f} = \frac{M_{max} - M_{c}}{K_{0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{f}"), ": 基準回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大曲げモーメント(kN・m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{c}"), ": 浮き上がりモーメント[kNm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kN・m/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{\pi E}{32(1-\nu^{2})}\cdot\frac{10^{3}}{D_{1}^{3}-D_{2}^{3}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E"), ": 材料の弾性係数[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu"), ": ポアソン比"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[mm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径[mm]"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{M_{a}}{\theta_{a}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{a}"), ": 杭頭接合部の許容モーメント[kNm]"]);

            AddInlineMathParagraph(body, [Tex(@"M_{a} = 3.75\times 10^{-4}a_{g}\cdot f_{t}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{a} = \frac{\delta_{a}}{D_{s}}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び[mm]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ[mm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[mm]"]);

            AddHeader1(body, "最大抵抗モーメント$M_{max}$の設定方法", 2);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大抵抗モーメント(kN・m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{e}"), ": 基準抵抗抵抗モーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{max} = \eta \cdot M_{e}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\eta"), ": 補正係数"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{n}"), ":  パイルキャップの軸応力度[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{p}"), ": 杭頭面積[m<^2>]"]);

            AddInlineMathParagraph(body, [Tex(@"\eta = -0.16 \frac{\sigma_{n}}{F_{c}} + 1.0")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{e}"), ": 基準抵抗モーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{e} = 0.5N \cdot D_{1}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[mm]"]);

            AddInlineMathParagraph(body, [Tex(@"M_{e} = 5\times 10^{-4}a_{g}\cdot f_{u}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"a_{g}"), ": 鋼棒の全断面積(mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{u}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[mm]"]);
        }

        // キャプテンパイル工法説明
        private void AddDescriptionCaptainPile(Body body)
        {
            AddHeader1(body, "キャプテンパイル工法", 1);
            //AddText(body, "キャプテンパイル工法", "left");
            AddText(body, "(BCJ評定-FD0230-05、「キャプテンパイル工法 設計・施工マニュアル」第 6 版)", "left");
            //AddText(body, "杭頭接合部の曲げモーメント-回転角関係の評価", "left");
            AddHeader1(body, "杭頭接合部の曲げモーメント-回転角関係の評価", 2);
            //AddText(body, "（1）杭頭接合部構造性能モデル化", "left");
            AddHeader1(body, "杭頭接合部構造性能モデル化", 3);

            AddInlineMathParagraph(body, ["杭頭回転特性は、杭頭曲げモーメント", Tex(@"M_{p}"), "と回転角", Tex(@"\theta_{p}"), "の関係により評価する。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{1}"), ": 杭頭接合部の等価初期回転剛性"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"K_{2}"), ": 杭頭接合部の圧縮軸力時における2次回転剛性", Tex(@"K_{2} = \min\left(\left|\frac{M_{y} - M_{1}}{\theta_{y} - \theta_{1}}\right|,\ K_{e}\right)")]);

            // 同書の式は (My − M1)/(θy − θ1)。実装 (CaptainPile.GetThetaYDonCompressionSide) は絶対値を取り、初期剛性 Ke を上限にする
            AddInlineMathParagraph(body, ["(同書の式は", Tex(@"K_{2} = \frac{M_{y} - M_{1}}{\theta_{y} - \theta_{1}}"),
                "。本計算では、2 次剛性が初期剛性を超えないよう絶対値を取って", Tex(@"K_{e}"), "を上限とする。通常の入力では補正は働かない)"]);
            // 実装は θy > θ1 のときだけ K2 を求め、それ以外は中間点を置かない (GetMThetaRelationship・GetThetaYDonCompressionSide)
            AddInlineMathParagraph(body, ["上式は", Tex(@"\theta_{y} > \theta_{1}"), "のときに用いる。",
                Tex(@"\theta_{y} \le \theta_{1}"), "のときは", Tex(@"K_{2}"), "を求めず、圧縮軸力時の M-θ 関係を (0, 0)・(",
                Tex(@"\theta_{1}"), ", ", Tex(@"\min(M_{1},\ M_{u})"), ")・(", Tex(@"\theta_{u}"), ", ", Tex(@"M_{u}"),
                ") の 3 点を結ぶ折れ線とする。終局時回転角", Tex(@"\theta_{y}' = \theta_{1} + (M_{u} - M_{1})/K_{2}"),
                "は", Tex(@"\theta_{u}"), "で頭打ちとする。"]);

            AddInlineMathParagraph(body, ["あるいは、引張軸力時における初期回転剛性", Tex(@"K_{2} = \frac{M_{y}}{\theta_{y}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{1}"), ": 離間時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{y}"), ": 降伏時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{u}"), ": 終局時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{1}"), ": 離間時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}"), ": 降伏時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}'"), ": 終局時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{u}"), ": 限界回転角(=0.04rad)"]);

            AddHeader1(body, "設定値の算出法", 3);
            //AddText(body, "（2）設定値の算出法");
            AddHeader1(body, "杭頭接合部の等価初期回転剛性", 4);

            AddInlineMathParagraph(body, ["杭頭接合部の圧縮軸力時における等価初期回転剛性", Tex(@"K_{1}"), "は下式により算定する。"]);

            AddInlineMathParagraph(body, [Tex(@"K_{1} = K_{e} = \frac{1}{\dfrac{1}{K_{p}} + \dfrac{1}{K_{c}} + \dfrac{1}{K_{b}}}")]);

            AddText(body, "ここに", "left");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{e}"), ": 初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{p}"), ": 杭体部分の回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{p} = \frac{E_{p}\cdot I_{p}}{H_{p}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{p}"), ": 杭体のヤング係数[kN/m<^2>]、杭体の最上段の断面のコンクリートの値とする"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{p}"), ": 杭体の断面二次モーメント、絞り部ありの場合は絞り部径を杭径とみなす[m<^4>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{p}"), ": 杭体とPCリングの重なり長さ[m]、杭径 2,100 mm 未満は 0.09、以上は 0.11 とする (同書 2.4(3) 図 2.4.3)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{c}"), ": PCリング内コンクリートの回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{c} = \frac{E_{c}\cdot I_{c}}{H_{c}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{c}"), ": PCリング内のパイルキャップコンクリートのヤング係数[kN/m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{c}"), ": PCリング内側コンクリートの断面二次モーメント、絞り部有無にかかわらず杭径", Tex(@"D"), "の断面とする[m<^4>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{c}"), ": 杭頭接合面からPCリング上端までの長さ[m]、", Tex(@"H_{c} = h_{r} - H_{p}"), " (", Tex(@"h_{r}"), ": PCリング高さ)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{b}"), ": パイルキャップ部分の回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{b} = \frac{E_{b}\cdot I_{b}}{H_{b}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{b}"), ": パイルキャップコンクリートのヤング係数[kN/m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{b}"), ": 仮想円柱のコンクリートの断面二次モーメント、絞り部有無にかかわらずPCリング内径", Tex(@"r_{D1}"), "の断面とする[m<^4>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{b}"), ": 仮想円柱の高さ(=D/2)[m]"]);

            AddHeader1(body, "離間時曲げモーメント", 4);

            AddInlineMathParagraph(body, ["引張応力が発生しない離間時曲げモーメント", Tex(@"M_{1}"), "は下式により算定する。"]);

            AddInlineMathParagraph(body, [Tex(@"M_{1} = \sigma_{0} \cdot Z")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["ここに、", Tex(@"\sigma_{0}"), ": 杭頭接合部の軸方向応力度[kN/m<^2>]"]);

            AddInlineMathParagraph(body, [Tex(@"\sigma_{0} = \frac{N}{A_{e}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{e}"), ": 杭頭接合部の断面積[m<^2>]"]);

            AddInlineMathParagraph(body, [Tex(@"A_{e} = \frac{\pi}{4} D^{2}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Z"), ": 杭頭接合部の断面係数(m<^3>)"]);

            AddInlineMathParagraph(body, [Tex(@"Z = \frac{\pi}{32} D^{3}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D"), ": 杭径(m)ただし絞り部ありの場合は絞り部径を杭径とみなす。"]);

            AddHeader1(body, "降伏曲げモーメントおよび終局曲げモーメント", 4);

            AddInlineMathParagraph(body, ["降伏曲げモーメント", Tex(@"M_{y}"), "および終局曲げモーメント", Tex(@"M_{u}"),
                "については、同書 3.4 の以下の仮定に基づいて算定する。ただし、", Tex(@"M_{u}"), "の取り方 (⑥) は本計算の設定による。"]);

            AddInlineMathParagraph(body, ["① 断面力の釣合いとひずみ度の適合条件を考慮した塑性曲げ理論に基づいて算定する。"]);

            AddInlineMathParagraph(body, ["② 解析における断面形状は、杭径を直径とする円形断面とする。" +
                "ただし、絞り部がある場合は絞り部径を直径とする。"]);

            AddInlineMathParagraph(body, ["③ 引張定着筋降伏時の曲げモーメントは、最外縁の引張定着筋が降伏するときの値とする。"]);

            AddInlineMathParagraph(body, ["④ 圧縮降伏時の曲げモーメントは、圧縮縁コンクリートが0.85", Tex(@"\sigma_{max}"),
                "に達したときの値とする。"]);

            AddInlineMathParagraph(body, ["⑤ 降伏時の曲げモーメント", Tex(@"M_{y}"),
                "は、引張定着筋降伏時と圧縮降伏時の曲げモーメントのうち小さい方の値とする。" +
                "ただし、引張定着筋を配置しない場合の降伏時曲げモーメントは、上記④に達したときの値とする。"]);

            // ⑥ は同書の規定と本計算の取り方が違うことがあるので、分けて書く。本計算の取り方は既定ではなく
            // このモデルの設定から組む (以前は ⑥ で「最大曲げモーメントとする」と断定し、直後に既定 (εcu 0.005) を
            // 書いていたので、計算書だけを読むとどちらで求めたか決められなかった。2026-11-28 の指摘)
            AddInlineMathParagraph(body, ["⑥ 終局時の曲げモーメント", Tex(@"M_{u}"),
                "は、同書では最大曲げモーメントとしている。同書には圧縮縁の限界ひずみの規定が無く、",
                "コンクリートの応力度は", Tex(@"\varepsilon_{B}"), "を超えても", Tex(@"\sigma_{max}"),
                "のまま下がらない (⑦) ため、最大曲げモーメントは曲率を限りなく大きくした極限 (全塑性) になる。"]);
            var captainSettings = inputModel?.PileBodies?
                .Select((pb, i) => (No: i + 1, Captain: pb?.PileTopType?.Contains("キャプテンパイル工法") == true ? pb.PileTop?.CaptainPile : null))
                .Where(x => x.Captain != null)
                .Select(x => (x.No, x.Captain!))
                .ToList() ?? [];
            AddText(body, CaptainUltimateMomentText(captainSettings), "left");

            AddInlineMathParagraph(body, [ "⑦ コンクリートの応力度", Tex(@"\sigma"),
                "とひずみ度", Tex(@"\varepsilon"), "の関係は下式とする。" ]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon \le 0"), ": ", Tex(@"\sigma = 0")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"0 < \varepsilon \le \varepsilon_{B}"), ": ",
                Tex(@"\sigma = 6.75\left\{ e^{-0.812\frac{\varepsilon}{\varepsilon_{B}}} - e^{-1.218\frac{\varepsilon}{\varepsilon_{B}}} \right\}\,\sigma_{max}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon_{B} < \varepsilon"), ": ", Tex(@"\sigma = \sigma_{max}")]);

            AddInlineMathParagraph(body, ["ここに、"]);

            AddInlineMathParagraph(body, [Tex(@"\varepsilon_{B}"), ": コンクリート最大強度時のひずみ度で", Tex(@"\varepsilon_{B}"), " = 0.003 とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{max}"), ": コンクリートの最大強度で", Tex(@"\sigma_{max} = \min\left(\frac{F_{c}}{\nu^{2}},\ 2F_{c}\right)"),
                "とする (同書の式は ", Tex(@"F_{c}/\nu^{2}"), "。", Tex(@"\nu"), " が小さいとき過大にならないよう ", Tex(@"2F_{c}"),
                " を上限とする。", Tex(@"\nu = 0.70"), " では ", Tex(@"F_{c}/\nu^{2} = 2.04F_{c}"), " に対し ", Tex(@"2F_{c}"), ")。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": コンクリートの設計基準強度で杭体、パイルキャップの設計基準強度のうち、最小の値とする。杭体は最上段の断面の値とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu"), ": 絞り係数"]);


            AddInlineMathParagraph(body, ["⑧ 引張定着筋の応力度", Tex(@"\sigma"), "とひずみ度", Tex(@"\varepsilon"), "の関係は下式とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon \le -\varepsilon_{y}"), ": ", Tex(@"\sigma = -\sigma_{y}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"- \varepsilon_{y} < \varepsilon \le \varepsilon_{y}"), ": ", Tex(@"\sigma = E_{s}\varepsilon")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon_{y} < \varepsilon"), ": ", Tex(@"\sigma = \sigma_{y}")]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathEpsilonYEq() =>
                Tex(@"\varepsilon_{y} = \frac{\sigma_{y}}{E_{s}}");

            AddInlineMathParagraph(body, ["ここに、", Tex(@"\sigma_{y}"), "：引張定着筋降伏点強度"]);
            AddInlineMathParagraph(body, [Tex(@"\varepsilon_{y}"), "：引張定着筋降伏時ひずみ度（", mathEpsilonYEq(), "）"]);

            AddHeader1(body, "降伏時回転角", 4);
            //AddInlineMathParagraph(body, ["d. 降伏時回転角", mathThetau()]);

            AddInlineMathParagraph(body, ["断面解析における曲率から降伏時回転角", Tex(@"\theta_{y}"), "への換算は、杭体のヒンジ領域を杭径部とし、ヒンジ領域では一定の曲率であると仮定した下式で評価する。"]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathPhiy() =>
                Tex(@"\phi_{y}");

            AddEq(body, @"\theta_{y} = \int_{0}^{D} {\phi_{y}dx} = \phi_{y}D");

            AddInlineMathParagraph(body, ["ここに"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [mathPhiy(), ": 前項で算定される", Tex(@"M_{y}"), "時曲率"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [ Tex(@"D"), ": 杭径 " +
                "ただし、杭頭接合部に絞り部がある場合には杭径を絞り部径とみなす。" ]);

            AddHeader1(body, "杭頭接合部の曲げ耐力の算定法", 3);

            AddInlineMathParagraph(body, [
                "短期許容曲げモーメント", Tex(@"M_{a}"), "は、",
                "降伏時曲げモーメント", Tex(@"M_{y}"), "算定に用いたモーメント-曲率関係において、",
                "以下の条件に達した時点の曲げモーメント①、②のうち、小さいほうの値とする。",
                "ただし、引張鉄筋を配置しない場合は、以下の②の条件に達したときの値とする。"
            ]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathShortTermAllowableStressEq() =>
                Tex(@"\frac{2}{3}\cdot\frac{F_{c}}{\nu^{2}}");

            AddInlineMathParagraph(body, ["①最外縁の引張定着筋が降伏するとき"]);
            AddInlineMathParagraph(body, ["②圧縮縁コンクリートが短期許容圧縮応力度(",
                mathShortTermAllowableStressEq(), "(", Tex(@"\nu"), ": 絞り係数))に達するとき"]);

            AddHeader1(body, "杭頭接合部の検定 (キャプテンパイル工法)", 3);
            AddInlineMathParagraph(body, [
                "レベル1 の荷重ケースでは、杭頭の要素の i 端の曲げモーメント", Tex(@"|M| = \sqrt{M_{y}^{2} + M_{z}^{2}}"),
                "を、そのケースの軸力", Tex(@"N"), "(杭体の曲げ・せん断の検定と同じ軸力) に対する短期許容曲げモーメント", Tex(@"M_{a}"),
                "と比べる (同書 3.2(3))。軸力だけで短期許容を超えるときは", Tex(@"M_{a} = 0"), "(NG) とする。",
                "レベル2 の荷重ケースでは、杭頭回転角を限界回転角", Tex(@"\theta_{u} = 0.04\,\mathrm{rad}"), "と比べる。",
                "どちらも耐震グレードによらない (同書の短期・終局の照査の規定による)。"]);
            AddPCRingShearDescription(body, "キャプテンパイル工法", "表 2.2.4 (長期・短期)・表 2.2.5 (終局)", "同書 3.2(5) 式 3.2.10・式 3.2.11");
            AddText(body,
                "同書 3.2(3) が求める、終局時に杭頭が PC リングと杭体の重なりを超えて浮き上がらないことの確認 (浮き上がり量は算定しない) と、" +
                "設計フロー (図 3.3.1 の 9) のパイルキャップのパンチングの検討 (同書に算定式は無い) は行わない。別途確認を要する。",
                "left");

            AddText(body,
                "出典: キャプテンパイル協会「キャプテンパイル工法 (場所打ちコンクリート杭用杭頭半固定工法) 設計・施工マニュアル」 (第 6 版、2026 年 4 月、BCJ 評定-FD0230-05)",
                "left");
        }

        /// <summary>
        /// キャプテンパイル工法の、本計算での終局時曲げモーメント Mu の取り方の文。杭体ごとの設定
        /// (<see cref="Models.CaptainPile.UseMaximumUltimateMoment"/>・εcu・強度倍率) から組む。
        /// 杭体で設定が違えば、杭体番号を添えて設定ごとに書く。
        /// </summary>
        internal static string CaptainUltimateMomentText(IReadOnlyList<(int No, Models.CaptainPile Captain)> bodies)
        {
            static string Describe(Models.CaptainPile c)
            {
                string how = c.UseMaximumUltimateMoment
                    ? "最大曲げモーメント (全塑性: 圧縮側のコンクリートは一様に σmax、引張定着筋は ±σy) とし"
                    : $"圧縮縁のひずみが εcu = {c.UltimateConcreteStrain:0.####} に達したときの曲げモーメントとし";
                string consistent = c.IsUltimateSettingConsistentWithDesignExample
                    ? "同書 3.5 節の設計例の Q-α 図から求めた Mu と整合する設定"
                    : $"同書 3.5 節の設計例と整合する設定 (εcu = {Models.CaptainPile.DefaultUltimateConcreteStrain:0.###}・強度倍率 {Models.CaptainPile.DefaultUltimateRebarStrengthFactor:0.#}) とは異なる";
                return $"{how}、引張定着筋の降伏強度 σy に強度倍率 {c.UltimateRebarStrengthFactor:0.##} を乗じる (降伏曲げモーメント My には乗じない。{consistent})";
            }
            if (bodies.Count == 0) return "本計算の Mu の取り方は、杭頭諸元の表の「終局時曲げモーメントの取り方」による。";
            var groups = bodies.GroupBy(b => Describe(b.Captain)).ToList();
            if (groups.Count == 1)
                return $"本計算の Mu は、{groups[0].Key}。";
            return "本計算の Mu は、" + string.Join("。", groups.Select(g =>
                $"杭体 {string.Join("・", g.Select(b => b.No))} では{g.Key}")) + "。";
        }

        /// <summary>
        /// PC リングのせん断の検定 (キャプテンパイル工法・キャプリングパイル工法で共通)。耐力は同書の表の値で、
        /// 実際に使った値は杭頭諸元の表に出る。<paramref name="tables"/> は耐力の表、<paramref name="checks"/> は照査式の所在。
        /// </summary>
        private void AddPCRingShearDescription(Body body, string method, string tables, string? checks)
        {
            AddHeader1(body, $"PC リングのせん断の検定 ({method})", 3);
            AddInlineMathParagraph(body, [
                "杭頭の要素の i 端のせん断力", Tex(@"|Q| = \sqrt{F_{y}^{2} + F_{z}^{2}}"),
                "を、選んだ PC リングの許容せん断力・終局せん断耐力と比べる。設計用せん断力は割増ししない。",
                "耐力は同書 " + tables + " の値とし" + (PileTopSpecsTableIsShown ? " (杭頭諸元の表に示す)" : "") + "、式からは求めない。",
                "使用限界は長期許容せん断力", Tex(@"{}_{L}Q_{a}"), "、損傷限界は短期許容せん断力", Tex(@"{}_{s}Q_{a}"),
                "、安全限界は終局せん断耐力", Tex(@"{}_{u}Q_{r}"), "(摩擦無視) と比べる。",
                JointLimitStateAssignmentText(inputModel?.FundamentalInput?.SeismicGrade),
                "終局で圧縮側の杭頭の摩擦を見込む設定のときは、", Tex(@"{}_{u}Q_{r} = \min(\text{摩擦無視} + 0.1N,\ \text{摩擦考慮上限})"), "とする。",
                checks == null ? "" : "照査式は " + checks + " による。"]);

            // 杭頭諸元の表は出力設定で省略できる。省略したときは、検定に使う耐力をここに書く
            // (以前は省略しても「杭頭諸元の表に示す」と書き、無い表を参照していた。2026-11-28 の指摘)
            if (!PileTopSpecsTableIsShown)
            {
                foreach (string line in PCRingCapacityLines(inputModel?.PileBodies, method))
                    AddText(body, line, "left");
            }
        }

        /// <summary>計算書に杭頭諸元の表を出すか (出力設定「杭頭諸元テーブル」)。</summary>
        private bool PileTopSpecsTableIsShown => mainWindowViewModel?.DocxOutput?.IncludePileTopSpecs == true;

        /// <summary>
        /// PC リングの耐力 (杭頭諸元の表と同じ行) を杭体ごとに 1 行にした文。杭頭諸元の表を出さないときの本文用。
        /// </summary>
        internal static List<string> PCRingCapacityLines(IList<PileBodyInput>? bodies, string method)
        {
            var lines = new List<string>();
            if (bodies == null) return lines;
            for (int i = 0; i < bodies.Count; i++)
            {
                var pb = bodies[i];
                if (pb?.PileTopType?.Contains(method) != true || pb.PileTop == null) continue;
                (Models.PileLibrary.PCRingShearCapacity? capacity, bool friction, string? ring) = method switch
                {
                    "キャプテンパイル工法" when pb.PileTop.CaptainPile is { } cp => (cp.ShearCapacity, cp.UltimateShearConsidersFriction, cp.PCRing?.Name),
                    "キャプリングパイル工法" when pb.PileTop.CapringPile is { } cr => (cr.ShearCapacity, cr.UltimateShearConsidersFriction, cr.PCRing?.Name),
                    _ => (null, false, null),
                };
                var specs = new System.Collections.ObjectModel.ObservableCollection<Models.PileLibrary.Spec>();
                Models.PileLibrary.PCRingShearTable.AddSpecs(specs, capacity, friction);
                string values = string.Join("、", specs.Select(s => string.IsNullOrEmpty(s.Mark)
                    ? $"{s.Item}: {s.Value}" : $"{s.Item} {s.Mark} = {s.Value} {s.Unit}".TrimEnd()));
                lines.Add($"・杭体 {i + 1}{(string.IsNullOrEmpty(ring) ? "" : $" (PC リング {ring})")}: {values}");
            }
            return lines;
        }

        /// <summary>
        /// 杭頭接合部の検定 (NewJ-BAR・PC リングのせん断) の、荷重ケースのレベルと限界状態の対応の文。
        /// 杭体の曲げ・せん断と同じ規則 (<see cref="PileDesign.Services.LimitStateRule"/>) から組む。
        /// 以前は「安全限界 (レベル2)」と書いていて、耐震グレード S のレベル2 (損傷限界で検定する) と食い違った (2026-11-28 の指摘)。
        /// </summary>
        internal static string JointLimitStateAssignmentText(string? grade)
        {
            static string Of(string? g, int level) =>
                PileDesign.Services.LimitStateRule.Name(PileDesign.Services.LimitStateRule.For(g, level).Limit);
            string used = string.IsNullOrEmpty(grade) ? "A" : grade;
            return $"荷重ケースとの対応は杭体の曲げ・せん断と同じで、長期は{Of(used, 0)}、レベル1 は{Of(used, 1)}、" +
                   $"レベル2 は耐震グレード A のとき{Of("A", 2)}、S のとき{Of("S", 2)}とする" +
                   $" (本計算は耐震グレード {used} であるため、レベル2 は{Of(used, 2)}で検定する)。";
        }

        // キャプリングパイル工法説明
        private void AddDescriptionCapringPile(Body body)
        {
            AddHeader1(body, "キャプリングパイル工法", 1);
            AddText(body,
                "既製コンクリート杭 (PHC / PRC / SC) および鋼管杭の杭頭半剛接合工法。" +
                "PC リングおよびパイルキャップにより杭頭部の回転を拘束する。",
                "left");

            AddHeader1(body, "適用範囲", 2);
            AddText(body, "・適用杭種: PHC 杭、PRC 杭、SC 杭、鋼管杭", "left");
            AddInlineMathParagraph(body, ["・杭径: ", Tex(@"D = 300\text{〜}1200\,\mathrm{mm}")]);
            AddInlineMathParagraph(body, ["・杭体コンクリート設計基準強度: ", Tex(@"\sigma_{ck} \ge 80\,\mathrm{N/mm^{2}}")]);
            AddInlineMathParagraph(body, ["・限界回転角: ", Tex(@"\theta_{u} = 0.03\,\mathrm{rad}"),
                " (実験結果及び設計許容範囲を考慮)"]);

            AddHeader1(body, "力学モデル (M-θ 完全バイリニア)", 2);
            AddInlineMathParagraph(body,
                ["杭頭接合部の回転ばねは、杭頭曲げモーメント ", Tex(@"M_{0}"),
                " と杭頭回転角 ", Tex(@"\theta_{0}"), " の関係で表す。",
                "初期回転剛性 ", Tex(@"K_{i}"), "、最大抵抗モーメント ", Tex(@"M_{u}"),
                "、限界回転角 ", Tex(@"\theta_{u}"), " で定義される完全バイリニアでモデル化する。"]);
            AddEq(body, @"M_{0}(\theta_{0}) = \begin{cases} \dfrac{M_{u}}{\theta_{y}}\cdot\theta_{0} & (0 \le \theta_{0} \le \theta_{y}) \\ M_{u} & (\theta_{y} < \theta_{0} \le \theta_{u}) \end{cases}, \quad \theta_{y} = \min\left(\dfrac{M_{u}}{K_{i}},\ \theta_{u}\right)");
            AddInlineMathParagraph(body, ["ここに、"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{i}"), ": 初期回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{u}"), ": 最大抵抗モーメント[kN·m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}"), ": 降伏時回転角 [rad]。", Tex(@"M_{u}/K_{i}"),
                " が ", Tex(@"\theta_{u}"), " を超えるときは ", Tex(@"\theta_{u}"), " で頭打ちとし、その点の曲げモーメントを ", Tex(@"M_{u}"),
                " とする (このとき ", Tex(@"M_{u} > K_{i}\theta_{u}"), " であるため、第 1 勾配は ", Tex(@"K_{i}"), " より大きい ",
                Tex(@"M_{u}/\theta_{u}"), " になる)"]);
            // 実装 (CapringPile.GetMThetaRelationship) は Mu = 0 なら θy = 0 とし、曲げモーメント 0 の曲線を返す。式の第 1 区間は 0/0 になるので分けて書く
            AddInlineMathParagraph(body, [Tex(@"M_{u} = 0"),
                " のとき (引張定着筋なしで引張、引張軸力が定着筋の降伏軸力以上など) は、", Tex(@"\theta_{0}"), " によらず ",
                Tex(@"M_{0} = 0"), " とする (上式は用いない)。"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{u}"), ": 限界回転角 (=0.03rad)"]);
            AddInlineMathParagraph(body,
                [Tex(@"K_{i}"), " および ", Tex(@"M_{u}"),
                " は、引張定着筋の有無、軸力の方向 (圧縮 / 引張) に応じて以下の 3 ケースに場合分けされる。",
                "なお、軸力 ", Tex(@"N"), " は圧縮を正とする。"]);

            AddHeader1(body, "ケース① 引張定着筋なし × 軸力圧縮", 2);
            AddText(body, "引張定着筋を配置しない場合は、軸力 N による圧着のみで回転を抵抗する。", "left");
            AddEq(body, @"K_{i} = K_{e} = \dfrac{1}{\dfrac{1}{K_{p}} + \dfrac{1}{K_{c}} + \dfrac{1}{K_{b}}}, \quad M_{u} = \dfrac{D}{2}\cdot N");

            AddHeader1(body, "ケース①′ 引張定着筋なし × 軸力引張", 2);
            AddInlineMathParagraph(body, [
                "引張定着筋を配置せず、軸力", Tex(@"N"), "が引張側 (", Tex(@"N < 0"), ") の場合は、杭頭は引張力に抵抗できないとして",
                Tex(@"M_{u} = 0"), " (曲げ剛性 0) とし、解析では当該杭頭の軸方向の拘束も解放する。"]);

            AddHeader1(body, "ケース② 引張定着筋あり × 軸力圧縮", 2);
            AddInlineMathParagraph(body, ["軸力 N による圧着に加え、引張定着筋による曲げ抵抗 ", Tex(@"M_{r}"), " が加算される。"]);
            AddEq(body, @"K_{i} = K_{e} = \dfrac{1}{\dfrac{1}{K_{p}} + \dfrac{1}{K_{c}} + \dfrac{1}{K_{b}}}, \quad M_{u} = \dfrac{D}{2}\cdot N + M_{r}");

            AddHeader1(body, "ケース③ 引張定着筋あり × 軸力引張", 2);
            AddInlineMathParagraph(body,
                ["軸力 ", Tex(@"N"), " が引張側 (", Tex(@"N < 0"),
                ") の場合、引張軸力レベルに応じて初期回転剛性 ", Tex(@"K_{i}"), " が低減される。",
                "引張軸力が定着筋降伏軸力相当 ", Tex(@"N_{ty}"), " 以下の領域では、",
                Tex(@"K_{e}"), " から ", Tex(@"K_{ty}"), " への遷移を楕円相互作用式で評価する。"]);
            AddEq(body, @"\begin{cases} \left(\dfrac{|N| - N_{ty}}{N_{ty}}\right)^{2} + \left(\dfrac{K_{e} - K_{i}}{K_{e} - K_{ty}}\right)^{2} = 1 & (0 \le |N| \le N_{ty}) \\ K_{i} = K_{ty} & (|N| > N_{ty}) \end{cases}");
            AddInlineMathParagraph(body,
                ["最大抵抗モーメントは、定着筋の引張降伏軸力 ", Tex(@"N_{y}"), " に対する余裕に応じて低減される。"]);
            AddEq(body, @"M_{u} = \max\left(M_{r}\left(1 - \dfrac{|N|}{N_{y}}\right),\ 0\right)");
            AddInlineMathParagraph(body, ["引張軸力が定着筋の降伏軸力を超える (", Tex(@"|N| > N_{y}"), ") ときは ", Tex(@"M_{u} = 0"), " とする。"]);

            AddHeader1(body, "直列ばね Kp / Kc / Kb の定義", 2);
            AddInlineMathParagraph(body,
                ["初期回転剛性 ", Tex(@"K_{e}"),
                " は、杭体部分・PC リング内コンクリート部分・パイルキャップ仮想円柱の 3 ばねの直列合成で評価する。"]);
            AddEq(body, @"K_{p} = \dfrac{E_{p}\cdot I_{p}}{H_{p}}, \quad K_{c} = \dfrac{E_{c}\cdot I_{c}}{H_{c}}, \quad K_{b} = \dfrac{E_{b}\cdot I_{b}}{H_{b}}");
            AddInlineMathParagraph(body, ["ここに、"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{p}"), ": 杭体部分の回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{p}"), ": 杭体のヤング係数[kN/m<^2>] (最上段の杭断面の値)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{p}"), ": 杭体の断面二次モーメント[m<^4>] (最上段の杭断面の ", Tex(@"EI/E_{p}"),
                "。鋼管杭は ", Tex(@"E_{p}I_{p} = E_{s}I_{pipe} + E_{c}I_{fill}"), " とする。", Tex(@"E_{c}"), " はパイルキャップの値)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{p}"), ": 杭体と PC リングとの重なり長さ[m]、", Tex(@"H_{p} = 0.07"), " とする (同書 3.7.1 設計例 (5))"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{c}"), ": PC リング内コンクリート部分の回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{c}"), ": PC リングに囲まれたパイルキャップコンクリートのヤング係数[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{c}"), ": PC リング内側コンクリートの断面二次モーメント[m<^4>]、PC リング内径 ", Tex(@"r_{D1}"), " の断面とする"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{c}"), ": 杭頭接合面から PC リング上端までの長さ[m]、", Tex(@"H_{c} = h_{r} - H_{p}"), " (", Tex(@"h_{r}"), ": PC リング高さ)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{b}"), ": パイルキャップ部分仮想円柱の回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{b}"), ": パイルキャップコンクリートのヤング係数[kN/m<^2>]、", Tex(@"E_{b} = E_{c}"), " とする"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{b}"), ": 仮想円柱の断面二次モーメント[m<^4>]、", Tex(@"I_{b} = I_{c}"), " とする"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{b}"), ": 仮想円柱の高さ[m]、", Tex(@"H_{b} = D/2"), " とする"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D"), ": 杭径[m]"]);

            AddHeader1(body, "引張定着筋関連量", 2);
            AddInlineMathParagraph(body,
                ["引張定着筋を等価な円環配置と仮定し、配置径 ", Tex(@"D_{c}"),
                "、本数 ", Tex(@"n_{s}"), "、1 本あたり断面積 ", Tex(@"a_{s}"),
                " により以下の量を算定する。鋼種は SD345 または SD390 とする。"]);
            AddEq(body, @"Z = \dfrac{\pi}{32}\cdot\dfrac{D_{c}^{4} - \left(D_{c}^{2} - \dfrac{4 n_{s} a_{s}}{\pi}\right)^{2}}{D_{c}}");
            AddEq(body, @"N_{y} = n_{s}\cdot a_{s}\cdot\sigma_{y}, \quad N_{ty} = N_{y}\cdot\dfrac{D}{D + D_{c}}");
            AddEq(body, @"K_{ty} = \dfrac{D_{c}\cdot Z\cdot E_{s}}{2D}");
            AddEq(body, @"\phi_{ty} = \dfrac{2\sigma_{y}}{E_{s}(D + D_{c})}, \quad \theta_{ty} = \phi_{ty}\cdot D, \quad M_{ty} = \dfrac{\sigma_{y}\cdot D_{c}\cdot Z}{D + D_{c}}");
            AddEq(body, @"M_{r} = n_{s}\cdot a_{s}\cdot\sigma_{y}\cdot\dfrac{7}{8}\cdot\dfrac{D}{2}");

            AddInlineMathParagraph(body, ["ここに、"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Z"), ": 引張定着筋を等価な円環配置と仮定した場合の断面係数[m<^3>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{c}"), ": 引張定着筋の配置径[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"n_{s}"), ": 引張定着筋の本数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"a_{s}"), ": 引張定着筋 1 本あたりの断面積[m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{y}"), ": 引張定着筋の規格降伏強度[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 引張定着筋のヤング係数[kN/m<^2>] (", Tex(@"2.05 \times 10^{8}"), ")"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{y}"), ": 引張定着筋の総降伏軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{ty}"), ": 引張軸力時の遷移境界軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{ty}"), ": 引張軸力 ", Tex(@"|N|\ge N_{ty}"), " における初期回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{r}"), ": 引張定着筋による最大抵抗モーメント寄与[kN·m]、応力中心距離係数 ", Tex(@"\jmath = 7/8"), "、内部偶力アーム ", Tex(@"D/2")]);

            AddHeader1(body, "杭頭接合部の検定 (キャプリングパイル工法)", 2);
            // レベル2 で照査するかは基本設定 (ApplyLevel1RotationLimitToLevel2、既定は照査する)。耐震グレードによらない
            // (EvaluationService.ResolveRotationCriterion)
            bool rotationAtLevel2 = inputModel?.FundamentalInput?.ApplyLevel1RotationLimitToLevel2 ?? true;
            AddInlineMathParagraph(body, ["レベル1 の荷重ケースで、杭頭回転角を限界回転角 ", Tex(@"\theta_{u} = 0.03\,\mathrm{rad}"), " と比べる。",
                "同書の規定は短期の照査であり、レベル2 の荷重ケースでも比べるかは基本設定で選ぶ",
                rotationAtLevel2 ? " (本計算は比べる)。" : " (本計算は比べない)。",
                "耐震グレードによらない。"]);
            AddPCRingShearDescription(body, "キャプリングパイル工法", "2.2(4)「PC リング許容せん断力と終局せん断耐力」", null);
            AddText(body,
                "同書の設計フローにあるパイルキャップのパンチングの検討 (3.6(1) に算定式は無い) は行わない。別途確認を要する。",
                "left");

            AddText(body,
                "出典: キャプリングパイル協会 (CAPIA) 「キャプリングパイル工法 (既製杭用杭頭半固定工法) 設計・施工マニュアル」 (第 6 版、2023 年 4 月、BCJ 評定-FD0060-05)",
                "left");
        }

        /// <summary>
        /// NewJ-BAR (鋼管に溶接する杭頭補強筋) の計算法。ヘルプの「NewJ-BAR」の節と同じ内容を計算書の文体で書く。
        /// 値 (仮想円形断面の外径・配置半径・Ma・Mu・Qa・Qu・定着長さ) は杭頭諸元の表に出る (NewJBar.GetSpecs)。
        /// </summary>
        private void AddDescriptionNewJBar(Body body, bool hasInfill)
        {
            AddHeader1(body, "NewJ-BAR (鋼管に溶接する杭頭補強筋)", 1);
            AddText(body,
                "杭頭の鋼管の外面に開先付き異形棒鋼 NewJ-BAR (WSD390・WSD490) を補強筋として溶接し、フーチングに定着する。" +
                "杭頭部の設計は「NewJ-BAR 設計マニュアル (杭頭補強編)」(2026 年 9 月改訂) に従う。" +
                "杭体のいちばん上の区間の鋼管の公称外径・板厚・規格 (腐食代を見込まない) に溶接するものとする。",
                "left");

            AddHeader1(body, "杭頭部の断面", 2);
            AddInlineMathParagraph(body, [
                "杭頭部は、NewJ-BAR を主筋とする鉄筋コンクリートの仮想円形断面とみなす (同書 4.3.1)。",
                "仮想円形断面の外径は、杭径 ", Tex(@"D"), " が 250 mm 以上 500 mm 未満なら ", Tex(@"D + 200"),
                "、500 mm 以上なら ", Tex(@"D + 2d + 200"), " (", Tex(@"d"), " は補強筋の呼び名の数値) とし、",
                "コンクリートはフーチング (パイルキャップ) の ", Tex(@"F_{c}"), " とする。",
                "補強筋の中心の半径は ", Tex(@"r' = D/2 + (\text{公称直径})/2"), " とする。",
                "主筋 8 本以上は円環に置き換え、8 本未満は配置の向きを ", Tex(@"\pi/m"), "〜", Tex(@"2\pi/m"),
                " の範囲で 11 通り変え、最も小さい値を採る (弱軸方向)。"]);

            AddHeader1(body, "許容曲げモーメント", 2);
            AddInlineMathParagraph(body, [
                "引張側のコンクリートを無視し、圧縮側は補強筋をヤング係数比 ", Tex(@"n"),
                " 倍した等価断面とする円形断面の式による (同書 4.4.3)。コンクリートの圧縮縁・引張側の補強筋・圧縮側の補強筋のいずれかが",
                "許容応力度に達するときの曲げモーメントを、軸力ごとに求める。",
                "短期はコンクリート ", Tex(@"\frac{2}{3}F_{c}"), "・補強筋 ", Tex(@"F"),
                " (WSD390 は 390、WSD490 は 490 N/mm²)、長期は ", Tex(@"\frac{1}{3}F_{c}"), "・195 N/mm² とする。"]);

            AddHeader1(body, "終局曲げモーメント", 2);
            AddInlineMathParagraph(body, [
                "層分割法 (仮想径を 400 層に分割) により、圧縮縁のひずみ 0.003 (または引張側の補強筋のひずみ 0.2) を終局とする (同書 4.4.4)。",
                "コンクリートは ", Tex(@"E_{c} = 3.35 \times 10^{4} (\gamma/24)^{2} (F_{c}/60)^{1/3}"), " の直線のあと ", Tex(@"F_{c}"),
                " で頭打ち、補強筋は ", Tex(@"E_{r} = 2.05 \times 10^{5}"), " の直線のあと終局強度 (WSD490 は 490、WSD390 は ",
                Tex(@"1.1 \times 390"), ") で頭打ちとする。補強筋の位置のコンクリートは差し引く。"]);

            if (hasInfill)
            {
                AddHeader1(body, "中詰補強筋", 2);
                AddInlineMathParagraph(body, [
                    "中詰めコンクリートの中に置く補強筋 (中詰補強筋) は、同書に計算法の本文が無いため、",
                    "NewJ-BAR と中詰補強筋を半径の違う 2 段の補強筋とし、それぞれの材料で上の許容曲げモーメント・終局曲げモーメントの計算",
                    " (平面保持の仮定) を行う。中詰補強筋は本数によらず円環に置き換える。",
                    "NewJ-BAR は中詰補強筋が無いときと同じく、8 本以上は円環に置き換え、8 本未満は個々の位置で配置の向きを変えて弱軸方向を求める",
                    " (8 本未満の NewJ-BAR と円環の中詰補強筋を組み合わせた断面の最小値)。",
                    "配置半径 ", Tex(@"r_{n}"), " は中詰めの外径の半分から重心かぶりを引いた値",
                    " (中詰めの外径は、鋼管杭・外殻鋼管は鋼管の内径、SC 杭は中空部の径) とする。",
                    "許容応力度は短期 ", Tex(@"F"), "、長期 ", Tex(@"\min(F/1.5,\ 215)"), " (D29 以上は 195)、終局強度は ",
                    Tex(@"1.1F"), " (SD490 は ", Tex(@"F"), ") とする。",
                    "中詰めコンクリートの深さは、中詰補強筋の定着長さ (SD490 は ", Tex(@"42d"), "、それ以外は ", Tex(@"35d"),
                    ") + 200 mm 以上とする (同書 4.2.1 1))。"]);
            }

            AddHeader1(body, "せん断", 2);
            AddInlineMathParagraph(body, [
                "杭頭の埋込み部の側面のフーチングコンクリートの支圧により、", Tex(@"Q_{a} = D \cdot L \cdot f_{cc}"),
                " (", Tex(@"f_{cc} = 2 \times \frac{2}{3}F_{c}"), "、", Tex(@"L"), " は埋込み深さ)、終局は ",
                Tex(@"Q_{u} = 1.5\,Q_{a}"), " とする (同書 4.3.1 2)・(4.7) 式)。"]);

            AddHeader1(body, "杭頭接合部の検定 (NewJ-BAR)", 2);
            AddInlineMathParagraph(body, [
                "曲げは、杭頭の要素の i 端の曲げモーメント ", Tex(@"|M| = \sqrt{M_{y}^{2} + M_{z}^{2}}"),
                " を、杭体の曲げ・せん断の検定と同じ軸力に対する仮想円形断面の曲げモーメントと比べる。",
                "使用限界は長期許容曲げモーメント、損傷限界は短期許容曲げモーメント、安全限界は終局曲げモーメントとする。",
                "軸力が仮想円形断面の軸力の範囲を超えるときは、曲げに耐えられないとして曲げの限界値を 0 (NG) とする。"]);
            AddInlineMathParagraph(body, [
                "せん断は、杭頭の要素の i 端のせん断力 ", Tex(@"|Q| = \sqrt{F_{y}^{2} + F_{z}^{2}}"),
                " を、埋込み部の支圧による耐力と比べる。この耐力は軸力によらず、損傷限界は ", Tex(@"Q_{a}"),
                "、安全限界は ", Tex(@"Q_{u}"), " とする。長期のせん断は同書に値が無いため検定しない。",
                "埋込み深さが決まらないときは検定不能とする。"]);
            AddInlineMathParagraph(body, [JointLimitStateAssignmentText(inputModel?.FundamentalInput?.SeismicGrade)]);
            AddText(body,
                "水平解析では杭頭を剛接合として解く。" +
                "溶接は標準溶接長で施工するものとし、溶接部の計算は行わない (同書 4.1.4)。",
                "left");

            AddText(body,
                "出典: 株式会社ブレイブ・株式会社クラウン「開先付き異形棒鋼 NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)」 (2026 年 9 月改訂)",
                "left");
        }
    }
}
