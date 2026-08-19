# -*- coding: utf-8 -*-
"""ジャパンパイル JP-NPRC105 カタログ (nprc.pdf) から製品ライブラリ CSV を生成する。

NPH との違い:
  - 異形棒鋼 (主筋) を持つ = PRC 断面
  - 1 製品が「PRC部」と「PHC部」の 2 断面を持つ
  - せん断耐力が 標準型/高せん断型 × Qas/Qu × せん断スパン比 1.0/1.5/2.0
  - 種類がローマ数字 Ⅰ〜Ⅷ (径によっては ⅠA/ⅠB のように A/B の枝番が付く)

抽出方針は extract.py と同じ罫線ベース。ただし
  - 罫線が無い列 (せん断補強筋) はヘッダーまで結合されるので、ヘッダー行を除いた
    y 範囲で再抽出する
  - 縦結合のある列 (呼び名/Do/D/PC鋼棒/Ao/So/Io/せん断補強筋/PHC部) と
    無い列 (種類/異形棒鋼/Ae/Ie/Ze/σce/PRC部の耐力) を 2 パスで取り分ける
"""
import bisect
import csv
import io
import math
import re
import sys

import fitz

import extract

PDF = "nprc.pdf"
SPEC_PAGES = [2, 4, 6]   # PRC部 断面諸数値一覧
PERF_PAGES = [3, 5, 7]   # PRC部 断面性能表 + PHC部 断面諸数値/性能

# ── 設計に用いる諸定数 (p1「■設計に用いる諸定数」) ─────────────────────
FC = 105.0
EC = 40000.0
# PC 鋼棒
FTP = 1275.0          # 耐力
SIGMA_PU = 1420.0     # 引張強さ
EP = 200000.0
# 異形棒鋼 SD345
BAR_FTU = 490.0       # 引張強さ
BAR_FY = 345.0        # 降伏点応力度
BAR_ALLOW_LONG = 215.0        # 長期許容応力度 (D25 以下)
BAR_ALLOW_LONG_D29UP = 195.0  # 長期許容応力度 (D29 以上)
BAR_ALLOW_SHORT = 345.0       # 短期許容応力度
ER = 205000.0         # ← Ae/Ie の換算比 nr = Er/Ec = 5.125 はこの値で検算済み
# コンクリート許容応力度。圧縮は PRC部/PHC部 共通だが、
# 曲げ引張 (σce 比例) は PHC部 にのみ規定があり、斜引張の短期も PHC部 にしかない。
FC_ALLOW_COMP_LONG = 30.0
FC_ALLOW_COMP_SHORT = 60.0
PRC_ALLOW_DIAG_LONG = 1.2
PHC_ALLOW_DIAG_LONG = 1.2
PHC_ALLOW_DIAG_SHORT = 1.8
PHC_ALLOW_BEND_TENS_LONG_FACTOR = 0.25   # σce/4
PHC_ALLOW_BEND_TENS_SHORT_FACTOR = 0.5   # σce/2

NODE_PITCH = 1000.0
HEAD_OFFSET = 600.0
TOE_OFFSET = 400.0

REGION = (40.0, 900.0)   # xmin, xmax


def num(s):
    s = re.sub(r"[^0-9.]", "", (s or ""))
    if not s or s == ".":
        return None
    try:
        return float(s)
    except ValueError:
        return None


ROMAN = re.compile(r"^[ⅠⅡⅢⅣⅤⅥⅦⅧ]")


# 性能ページは PRC部 の表と PHC部 の表が横に並ぶが、その間に垂直罫線が無いため
# 1 つの列に融合してしまう (PRC の長期許容軸力 N は x≈362..376、PHC の Ae は x≈388..398)。
# 表ごとに領域を分けて抽出する。
PERF_SPLIT_X = 382.0


def grids(doc, pageno, ymin, ymax, expand, xmin=None, xmax=None):
    return extract.extract_table(doc[pageno - 1], ymin, ymax,
                                 xmin if xmin is not None else REGION[0],
                                 xmax if xmax is not None else REGION[1],
                                 expand_merged=expand)


def data_range(doc, pageno, is_spec):
    """データ行だけを含む (ymin, ymax) を返す。

    ヘッダー行数はページごとに違い (2 行 / 3 行)、末尾に別表の行が続くページもあるため、
    行数を決め打ちせず内容で判定する。
      - 諸数値ページ: 種類の列がローマ数字
      - 性能ページ  : 先頭列が数値
    ヘッダーを含めたまま結合展開すると、罫線の無い列 (せん断補強筋) が
    ヘッダー文字ごと 1 セルに結合されてしまうため、範囲を絞ることが必須。
    """
    ys, _, g = grids(doc, pageno, 130.0, 820.0, False)
    hit = [i for i, r in enumerate(g)
           if (ROMAN.match(r[3] or "") if is_spec else re.fullmatch(r"[0-9.]+", r[0] or ""))]
    if not hit:
        raise AssertionError(f"p{pageno}: データ行を検出できません")
    return ys[hit[0]] + 0.5, ys[hit[-1] + 1] - 0.5


def normalize_thickness(cell):
    """厚さ仕様。縦書きセルは単語が y 方向に並ぶため x 順連結で字順が崩れる。"""
    s = (cell or "").strip()
    return {"厚特": "特厚", "厚特超": "超特厚", "特厚超": "超特厚"}.get(s, s)


def split_names(cell):
    return re.findall(r"(\d{3,4}-\d{3,4})", cell or "")


# ── p8 の付表 (標準質量表 / 拡頭形状一覧) ─────────────────────────────
# どちらも 2〜3 個の小表が横に並ぶ。小表の境界に垂直罫線が無いので x 範囲で切り分ける。
MASS_Y = (58.0, 277.0)
MASS_X = [(42.4, 199.4), (199.4, 371.2), (371.2, 538.4)]
HEAD_Y = (336.0, 552.0)
HEAD_X = [(42.4, 275.0), (300.0, 537.8)]

NAME_RE = re.compile(r"φ(\d{3,4}-\d{3,4})")


def column_by_y(page, x0, x1, ys, pattern):
    """列 x0..x1 の単語を y 位置で各行に割り当てる。

    p8 の付表は列によって行境界の罫線が引かれていない
    (呼び名・拡頭部長さ) ため、extract_table では全行が 1 セルに結合され
    値が数珠つなぎになる。座標から直接拾って行に配り直す。

    縦結合セルの文字は<b>結合範囲の中央</b>に置かれるので、単語の y が入る行帯を
    そのまま採ると 1 行ずれる。値どうしの中点を境界にして最寄りの値を割り当てる。
    """
    hits = sorted((0.5 * (w[1] + w[3]), w[4]) for w in page.get_text("words")
                  if x0 - 1 <= w[0] and w[2] <= x1 + 1 and pattern.fullmatch(w[4]))
    if not hits:
        return [""] * (len(ys) - 1)
    bounds = [0.5 * (hits[i][0] + hits[i + 1][0]) for i in range(len(hits) - 1)]
    return [hits[bisect.bisect_left(bounds, 0.5 * (ys[i] + ys[i + 1]))][1]
            for i in range(len(ys) - 1)]


def parse_mass_table(doc):
    """標準質量表 (p8) を {(呼び名, 肉厚): 単位長さ質量 [t/m]} で返す。

    カタログは「0.154×L」(L = 杭長 m) の形で書かれているので係数がそのまま t/m。
    """
    page = doc[7]
    mass = {}
    for x0, x1 in MASS_X:
        ys, xs, g = extract.extract_table(page, MASS_Y[0], MASS_Y[1], x0, x1, expand_merged=True)
        names = column_by_y(page, xs[0], xs[1], ys, NAME_RE)
        for row, name in zip(g, names):
            m = re.fullmatch(r"([\d.]+)×L", (row[4] or "").strip())
            t = num(row[3])
            if not m or not t or not name:
                continue
            mass[(NAME_RE.fullmatch(name).group(1), t)] = float(m.group(1))
    return mass


def parse_head_table(doc):
    """拡頭中間径タイプ / 拡頭タイプの形状一覧 (p8) を返す。"""
    page = doc[7]
    rows = []
    for x0, x1 in HEAD_X:
        ys, xs, g = extract.extract_table(page, HEAD_Y[0], HEAD_Y[1], x0, x1, expand_merged=True)
        lts = column_by_y(page, xs[4], xs[5], ys, re.compile(r"\d{3,4}"))
        for row, lt in zip(g, lts):
            m = NAME_RE.match((row[0] or "").strip())
            if not m:
                continue
            rows.append(dict(Maker="ジャパンパイル", Series="JP-NPRC105", Shape="節杭",
                             Name=m.group(1), Do=num(row[1]), D=num(row[2]),
                             Dt=num(row[3]), Lt=num(lt)))
    return rows


TOLERANCE = 0.0015   # カタログは有効数字 3〜4 桁の丸めなので 0.15% を許容する


def verify(records):
    """抽出値を理論式で検算する。列ズレは 1% 以上ずれるので確実に検出できる。

    節杭の断面性能は全て<b>軸部</b>の中空円形断面から一意に決まる。
    換算比は np = Ep/Ec = 5.0、nr = Er/Ec = 5.125。
    """
    np_, nr = EP / EC, ER / EC
    keys = ("Ao", "Io", "So", "Ae", "Ie", "Ze", "PhcAe", "PhcIe")
    worst = {k: (0.0, "", "") for k in keys}
    failed, noted = [], []
    for r in records:
        d, di = r["D"], r["D"] - 2 * r["t"]
        ao = math.pi / 4 * (d ** 2 - di ** 2)
        io_ = math.pi / 64 * (d ** 4 - di ** 4)
        calc = {
            "Ao": ao,
            "Io": io_,
            "So": (d ** 3 - di ** 3) / 12,
            "Ae": ao + (np_ - 1) * r["Ap"] + (nr - 1) * r["Ag"],
            "Ie": (io_ + (np_ - 1) * r["Ap"] * (r["PCD"] / 2) ** 2 / 2
                   + (nr - 1) * r["Ag"] * (r["BarPCD"] / 2) ** 2 / 2),
            "PhcAe": ao + (np_ - 1) * r["Ap"],
            "PhcIe": io_ + (np_ - 1) * r["Ap"] * (r["PCD"] / 2) ** 2 / 2,
        }
        calc["Ze"] = calc["Ie"] / (d / 2)
        for k, v in calc.items():
            printed = r[k]
            if not printed:
                continue
            rel = abs(v - printed) / printed
            row = (k, r["Name"], r["PrestressType"], printed, v, rel)
            if rel > TOLERANCE:
                # Note 付き = 誤植として既に検出・記録済みの行
                (noted if r["Note"] else failed).append(row)
                continue
            if rel > worst[k][0]:
                worst[k] = (rel, r["Name"], r["PrestressType"])

    for k in keys:
        rel, name, kind = worst[k]
        print(f"  {k:6} 最大相対差 {rel * 100:6.3f}%  ({name} {kind})")
    for f in noted:
        print(f"  誤植 {f[0]} {f[1]} {f[2]}: 印字 {f[3]:,.0f} / 正 {f[4]:,.0f} ({f[5] * 100:.2f}%)")
    if failed:
        for f in failed:
            print(f"  NG {f[0]} {f[1]} {f[2]}: 印字 {f[3]:,.0f} / 計算 {f[4]:,.0f} ({f[5] * 100:.2f}%)")
        raise AssertionError(f"検算不一致 {len(failed)} 件")


def main():
    doc = fitz.open(PDF)
    mass = parse_mass_table(doc)
    records = []

    for pg_spec, pg_perf in zip(SPEC_PAGES, PERF_PAGES):
        # --- PRC部 諸数値 (p2/p4/p6) ---
        y0, y1 = data_range(doc, pg_spec, is_spec=True)
        _, _, sMerge = grids(doc, pg_spec, y0, y1, True)
        _, _, sFlat = grids(doc, pg_spec, y0, y1, False)

        # --- 性能表 (p3/p5/p7) ---
        # PRC部: 行ごとに値が入るので結合展開しない
        # PHC部: PRC の複数行にまたがって結合されているので展開して各行へ複製する
        # PHC部 は PRC部 と行数が違う (製品グループ単位で 1 行) ので、
        # 行番号ではなく y 位置で PRC の行に対応付ける。
        p0, p1 = data_range(doc, pg_perf, is_spec=False)
        ysPrc, _, pFlat = grids(doc, pg_perf, p0, p1, False, xmax=PERF_SPLIT_X)
        ysPhc, _, gPhc = grids(doc, pg_perf, p0, p1, True, xmin=PERF_SPLIT_X)

        def phc_row_for(i):
            yc = (ysPrc[i] + ysPrc[i + 1]) * 0.5
            for j in range(len(gPhc)):
                if ysPhc[j] <= yc < ysPhc[j + 1]:
                    return gPhc[j]
            return [""] * len(gPhc[0])

        n = len(sFlat)
        assert len(sMerge) == n, f"p{pg_spec} 行数不一致"
        assert len(pFlat) == n, \
            f"p{pg_spec}/{pg_perf} 行数不一致: {n} vs {len(pFlat)}"

        for i in range(n):
            sm, sf = sMerge[i], sFlat[i]
            pm, pf = phc_row_for(i), pFlat[i]

            names = split_names(sm[0])
            if not names:
                continue
            # 呼び名 'Do-D' が節部径・軸部径そのもの (列は千位分割で崩れるため呼び名から取る)
            dos = [float(x.split("-")[0]) for x in names]
            d = float(names[0].split("-")[1])

            kind = (sf[3] or "").strip()          # 種類 Ⅰ〜Ⅵ (行ごと)
            tspec = normalize_thickness(sm[4])     # 厚さ仕様 (結合・縦書き)
            t = num(sm[5])
            pc_desig, pc_n, pc_ap, pcd = (sm[6] or "").strip(), num(sm[7]), num(sm[8]), num(sm[9])
            bar_desig = (sf[10] or "").strip()     # 異形棒鋼 呼び名 (行ごと)
            bar_n, bar_ag, bar_pcd = num(sm[11]), num(sf[12]), num(sf[13])
            ao, ae = num(sm[14]), num(sf[15])
            so, io_ = num(sm[16]), num(sm[17])
            ie, ze, sce = num(sf[18]), num(sf[19]), num(sf[20])
            sh = [num(sm[j]) for j in range(21, 27)]  # せん断補強筋 (結合)

            # PRC部 耐力 (行ごと)
            msc, mal, mas, mu, qal = (num(pf[j]) for j in range(0, 5))
            q_std_as = [num(pf[j]) for j in (5, 6, 7)]
            q_std_u = [num(pf[j]) for j in (8, 9, 10)]
            q_hi_as = [num(pf[j]) for j in (11, 12, 13)]
            q_hi_u = [num(pf[j]) for j in (14, 15, 16)]
            nal = num(pf[17])

            # PHC部 (結合)
            phc = [num(pm[j]) for j in range(0, 8)]

            # 断面一次モーメント So は D=700/t=100 の行だけカタログ印字が
            # 18 617×10³ で、正しくは 18 167×10³ (桁の入れ替わり誤植)。
            # 印字値は So に忠実に残し、正値を SoFromSection に入れる。
            di = d - 2.0 * (t or 0.0)
            so_calc = (d ** 3 - di ** 3) / 12.0
            so_printed = (so or 0) * 1e3
            note = ""
            if so_printed and abs(so_calc - so_printed) / so_printed > 0.0015:
                note = (f"カタログ印字の So={so_printed/1e3:,.0f}×10³ は誤植 "
                        f"(正: {so_calc/1e3:,.0f}×10³)。SoFromSection を使うこと")

            for name, do in zip(names, dos):
                records.append(dict(
                    Maker="ジャパンパイル", Series="JP-NPRC105", Shape="節杭",
                    Name=name, Do=do, D=d, t=t, ThicknessType=tspec, Fc=FC,
                    PrestressType=kind,
                    PcDesignation=pc_desig, PcCount=int(pc_n or 0), Ap=pc_ap, PCD=pcd,
                    BarDesignation=bar_desig, BarCount=int(bar_n or 0), Ag=bar_ag, BarPCD=bar_pcd,
                    Ao=(ao or 0) * 100.0, Ae=(ae or 0) * 100.0,
                    So=so_printed, SoFromSection=so_calc, Io=(io_ or 0) * 1e4,
                    Ie=(ie or 0) * 1e4, Ze=(ze or 0) * 1e3, SigmaCe=sce,
                    ShearBarStdDia490=sh[0], ShearBarStdPitch490=sh[1],
                    ShearBarStdDia785=sh[2], ShearBarStdPitch785=sh[3],
                    ShearBarHighDia785=sh[4], ShearBarHighPitch785=sh[5],
                    Msc=msc, Mal=mal, Mas=mas, Mu=mu, Qal=qal,
                    QasStd10=q_std_as[0], QasStd15=q_std_as[1], QasStd20=q_std_as[2],
                    QuStd10=q_std_u[0], QuStd15=q_std_u[1], QuStd20=q_std_u[2],
                    QasHigh10=q_hi_as[0], QasHigh15=q_hi_as[1], QasHigh20=q_hi_as[2],
                    QuHigh10=q_hi_u[0], QuHigh15=q_hi_u[1], QuHigh20=q_hi_u[2],
                    Nal=nal,
                    PhcAe=(phc[0] or 0) * 100.0, PhcIe=(phc[1] or 0) * 1e4,
                    PhcSigmaCe=phc[2], PhcMc=phc[3], PhcMu=phc[4],
                    PhcQas=phc[5], PhcQu=phc[6], PhcNal=phc[7],
                    Ec=EC, Ftp=FTP, SigmaPu=SIGMA_PU, Ep=EP,
                    BarFtu=BAR_FTU, BarFy=BAR_FY, Er=ER,
                    BarAllowLong=BAR_ALLOW_LONG, BarAllowLongD29Up=BAR_ALLOW_LONG_D29UP,
                    BarAllowShort=BAR_ALLOW_SHORT,
                    FcAllowCompLong=FC_ALLOW_COMP_LONG, FcAllowCompShort=FC_ALLOW_COMP_SHORT,
                    PrcAllowDiagLong=PRC_ALLOW_DIAG_LONG,
                    PhcAllowDiagLong=PHC_ALLOW_DIAG_LONG, PhcAllowDiagShort=PHC_ALLOW_DIAG_SHORT,
                    PhcAllowBendTensLongFactor=PHC_ALLOW_BEND_TENS_LONG_FACTOR,
                    PhcAllowBendTensShortFactor=PHC_ALLOW_BEND_TENS_SHORT_FACTOR,
                    MassPerM=mass.get((name, t), 0.0),
                    NodePitch=NODE_PITCH, HeadOffset=HEAD_OFFSET, ToeOffset=TOE_OFFSET,
                    Note=note,
                ))

    missing = sorted({(r["Name"], r["t"]) for r in records if not r["MassPerM"]})
    assert not missing, f"標準質量表に無い断面: {missing}"

    verify(records)
    write_csv("pile_library_NodularPrcPile.csv", records)
    print(f"本体: {len(records)} 行 (呼び名 {len(set(r['Name'] for r in records))} 種)")

    heads = parse_head_table(doc)
    names = {r["Name"] for r in records}
    unknown = sorted({h["Name"] for h in heads} - names)
    assert not unknown, f"本体に無い呼び名の拡頭形状: {unknown}"
    write_csv("pile_library_NodularPrcPile_head.csv", heads)
    print(f"拡頭: {len(heads)} 行")


def write_csv(path, rows):
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
