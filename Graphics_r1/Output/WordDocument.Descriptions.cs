using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Models.InputData;
using System.Collections.Generic;
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

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{p}"), ": 支圧面積（杭頭面積）[m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\phi_{c}"), ": 支圧係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": パイルキャップコンクリートの設計基準強度[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ[m]"]);

            AddHeader1(body, "短期許容せん断力の算定", 2);

            AddText(body, "設計用地震力によるせん断力が，杭頭接合部の短期許容せん断力以内であることを確認する。" +
                "杭頭接合部の短期許容せん断力は，" +
                "杭頭の摩擦抵抗とパイルキャップ（へりあき）のせん断抵抗を考慮した（4.4）式により算定する。", "left");

            AddInlineMathParagraph(body, [Tex(@"Q_{a} = \mu_{a}\cdot N_{c} + Q_{ha}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{a}"), ": 杭頭接合部の短期許容せん断力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\mu_{a}"), ": 許容摩擦係数(=0.2)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[m]"]);

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
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{f} = \frac{M_{max} - M_{c}}{K_{0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\\theta_{f}"), ": 基準回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大曲げモーメント(kN・m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{c}"), ": 浮き上がりモーメント[kNm]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kN・m/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{\pi E}{32(1-\nu^{2})}\cdot\frac{10^{3}}{D_{1}^{3}-D_{2}^{3}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E"), ": 材料の弾性係数[N/mm<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu"), ": ポアソン比"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径[m]"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{M_{a}}{\theta_{a}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{a}"), ": 杭頭接合部の許容モーメント[kNm]"]);

            AddInlineMathParagraph(body, [Tex(@"M_{a} = 3.75\times 10^{-4}a_{g}\cdot f_{t}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{a} = \frac{\delta_{a}}{D_{s}}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角[rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び[m]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[m]"]);

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
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径[m]"]);

            AddInlineMathParagraph(body, [Tex(@"M_{e} = 5\times 10^{-4}a_{g}\cdot f_{u}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"a_{g}"), ": 鋼棒の全断面積(mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{u}"), ": 鋼棒の短期許容応力度[N/mm<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離[m]"]);
        }

        // キャプテンパイル工法説明
        private void AddDescriptionCaptainPile(Body body)
        {
            AddHeader1(body, "キャプテンパイル工法", 1);
            //AddText(body, "キャプテンパイル工法", "left");
            AddText(body, "(BCJ評定-FD0230-01)", "left");
            //AddText(body, "杭頭接合部の曲げモーメント-回転角関係の評価", "left");
            AddHeader1(body, "杭頭接合部の曲げモーメント-回転角関係の評価", 2);
            //AddText(body, "（1）杭頭接合部構造性能モデル化", "left");
            AddHeader1(body, "杭頭接合部構造性能モデル化", 3);

            AddInlineMathParagraph(body, ["杭頭回転特性は、杭頭曲げモーメント", Tex(@"M_{p}"), "と回転角", Tex(@"\theta_{p}"), "の関係により評価する。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{1}"), ": 杭頭接合部の等価初期回転剛性"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"K_{2}"), ": 杭頭接合部の圧縮軸力時における2次回転剛性", Tex(@"K_{2} = \frac{M_{y} - M_{1}}{\theta_{y} - \theta_{1}}")]);

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

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{p}"), ": 杭体のヤング係数[kN/m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{p}"), ": 杭体の断面二次モーメント、絞り部ありの場合は絞り部径を杭径とみなす[m<^4>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{p}"), ": 杭体とPCリングの重なり長さ[m]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{c}"), ": PCリング内コンクリートの回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{c} = \frac{E_{c}\cdot I_{c}}{H_{c}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{c}"), ": 杭体のヤング係数[kN/m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{c}"), ": PCリング内側コンクリートの断面二次モーメント、絞り部有無によらない[m<^4>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{c}"), ": 杭頭接合面からPCリング上端までの長さ[m]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{b}"), ": パイルキャップ部分の回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{b} = \frac{E_{b}\cdot I_{b}}{H_{b}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{b}"), ": パイルキャップコンクリートのヤング係数[kN/m<^2>]"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{b}"), ": 仮想円柱のコンクリートの断面二次モーメント、絞り部有無によらない[m<^4>]"]);

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
                "については、以下の仮定に基づいて算定する。"]);

            AddInlineMathParagraph(body, ["① 断面力の釣合いとひずみ度の適合条件を考慮した塑性曲げ理論に基づいて算定する。"]);

            AddInlineMathParagraph(body, ["② 解析における断面形状は、杭径を直径とする円形断面とする。" +
                "ただし、絞り部がある場合は絞り部径を直径とする。"]);

            AddInlineMathParagraph(body, ["③ 引張定着筋降伏時の曲げモーメントは、最外縁の引張定着筋が降伏するときの値とする。"]);

            AddInlineMathParagraph(body, ["④ 圧縮降伏時の曲げモーメントは、圧縮縁コンクリートが0.85", Tex(@"\sigma_{max}"),
                "に達したときの値とする。"]);

            AddInlineMathParagraph(body, ["⑤ 降伏時の曲げモーメント", Tex(@"M_{y}"),
                "は、引張定着筋降伏時と圧縮降伏時の曲げモーメントのうち小さい方の値とする。" +
                "ただし、引張定着筋を配置しない場合の降伏時曲げモーメントは、上記④に達したときの値とする。"]);

            AddInlineMathParagraph(body, ["⑥ 終局時の曲げモーメント", Tex(@"M_{u}"),
                "は、最大曲げモーメントとする。"]);

            AddInlineMathParagraph(body, [ "⑦ コンクリートの応力度", Tex(@"\sigma"),
                "とひずみ度", Tex(@"\varepsilon"), "の関係は下式とする。" ]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon \le 0"), ": ", Tex(@"\sigma = 0")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"0 < \varepsilon \le \varepsilon_{B}"), ": ",
                Tex(@"\sigma = 6.75\left{ e^{-0.812\frac{\varepsilon}{\varepsilon_{0}}} - e^{-1.218\frac{\varepsilon}{\varepsilon_{0}}} \right}, \sigma_{max}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon_{B} < \varepsilon"), ": ", Tex(@"\sigma = \sigma_{max}")]);

            AddInlineMathParagraph(body, ["ここに、"]);

            AddInlineMathParagraph(body, [Tex(@"\varepsilon_{B}"), ": コンクリート最大強度時のひずみ度で", Tex(@"\varepsilon_{B}"), " = 0.003 とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{max}"), ": コンクリートの最大強度で", Tex(@"\sigma_{max} = \frac{F_{c}}{\nu^{2}}"), "とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": コンクリートの設計基準強度で杭体、パイルキャップの設計基準強度のうち、最小の値とする。"]);

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

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [mathPhiy(), ": 前項c.で算定される", Tex(@"M_{y}"), "時曲率"]);

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
            AddEq(body, @"M_{0}(\theta_{0}) = \begin{cases} K_{i}\cdot\theta_{0} & (0 \le \theta_{0} \le \theta_{y}) \\ M_{u} & (\theta_{y} < \theta_{0} \le \theta_{u}) \end{cases}, \quad \theta_{y} = M_{u}/K_{i}");
            AddInlineMathParagraph(body, ["ここに、"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{i}"), ": 初期回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{u}"), ": 最大抵抗モーメント[kN·m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}"), ": 降伏時回転角 (=", Tex(@"M_{u}/K_{i}"), ") [rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{u}"), ": 限界回転角 (=0.03rad)"]);
            AddInlineMathParagraph(body,
                [Tex(@"K_{i}"), " および ", Tex(@"M_{u}"),
                " は、引張定着筋の有無、軸力の方向 (圧縮 / 引張) に応じて以下の 3 ケースに場合分けされる。",
                "なお、軸力 ", Tex(@"N"), " は圧縮を正とする。"]);

            AddHeader1(body, "ケース① 引張定着筋なし × 軸力圧縮", 2);
            AddText(body, "引張定着筋を配置しない場合は、軸力 N による圧着のみで回転を抵抗する。", "left");
            AddEq(body, @"K_{i} = K_{e} = \dfrac{1}{\dfrac{1}{K_{p}} + \dfrac{1}{K_{c}} + \dfrac{1}{K_{b}}}, \quad M_{u} = \dfrac{D}{2}\cdot N");

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
            AddEq(body, @"M_{u} = M_{r}\left(1 - \dfrac{|N|}{N_{y}}\right)");

            AddHeader1(body, "直列ばね Kp / Kc / Kb の定義", 2);
            AddInlineMathParagraph(body,
                ["初期回転剛性 ", Tex(@"K_{e}"),
                " は、杭体部分・PC リング内コンクリート部分・パイルキャップ仮想円柱の 3 ばねの直列合成で評価する。"]);
            AddEq(body, @"K_{p} = \dfrac{E_{p}\cdot I_{p}}{H_{p}}, \quad K_{c} = \dfrac{E_{c}\cdot I_{c}}{H_{c}}, \quad K_{b} = \dfrac{E_{b}\cdot I_{b}}{H_{b}}");
            AddInlineMathParagraph(body, ["ここに、"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{p}"), ": 杭体部分の回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{p}"), ": 杭体のヤング係数[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{p}"), ": 杭体の断面二次モーメント[m<^4>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{p}"), ": 杭体と PC リングとの重なり長さ[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{c}"), ": PC リング内コンクリート部分の回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{c}"), ": PC リング内コンクリートのヤング係数[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{c}"), ": PC リング内側コンクリートの断面二次モーメント[m<^4>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{c}"), ": 杭頭接合面から PC リング上端までの長さ[m]"]);
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
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 引張定着筋のヤング係数[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{y}"), ": 引張定着筋の総降伏軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{ty}"), ": 引張軸力時の遷移境界軸力[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{ty}"), ": 引張軸力 ", Tex(@"|N|\ge N_{ty}"), " における初期回転剛性[kN·m/rad]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{r}"), ": 引張定着筋による最大抵抗モーメント寄与[kN·m]、応力中心距離係数 ", Tex(@"\jmath = 7/8"), "、内部偶力アーム ", Tex(@"D/2")]);

            AddText(body,
                "出典: 一般社団法人キャプリングパイル工法協会 (CAPIA) 「キャプリングパイル工法 設計マニュアル」 (2023年4月版)",
                "left");
        }
    }
}
