# -*- coding: utf-8 -*-
"""JP-NPH カタログ (nph.pdf) から製品ライブラリ CSV を生成する。

出力:
  pile_library_NodularPile.csv       断面諸元 + 断面性能 (呼び名 x Fc x 種類)
  pile_library_NodularPile_head.csv  拡頭中間径/拡頭タイプの形状一覧
"""
import csv
import io
import math
import re
import sys

import fitz

import extract

PDF = "nph.pdf"

# 諸数値一覧表 (p2/p4/p6) と 断面性能表 (p3/p5/p7) の対応
PAGE_PAIRS = [(2, 3), (4, 5), (6, 7)]

# p1「設計に用いる諸定数」 (Fc -> 各種定数)
CONSTANTS = {
    85: dict(ec=40000, comp_l=24, comp_s=48, diag_l=1.2, diag_s=1.8),
    105: dict(ec=40000, comp_l=30, comp_s=60, diag_l=1.2, diag_s=1.8),
    123: dict(ec=42000, comp_l=35, comp_s=70, diag_l=1.2, diag_s=1.8),
}
FTP = 1275.0      # PC鋼棒 耐力 N/mm2
SIGMA_PU = 1420.0  # PC鋼棒 引張強さ N/mm2
EP = 200000.0     # PC鋼棒 ヤング係数 N/mm2

# 姿図 (p1) から確定できる寸法 [mm]
NODE_PITCH = 1000.0   # 節ピッチ (寸法記入あり)
HEAD_OFFSET = 600.0   # 杭頭から第1節中心 (寸法記入あり)
TOE_OFFSET = 400.0    # 杭先端から最終節中心 (寸法記入あり)
HEAD_LENGTH_LT = 600.0  # 拡頭部長さ Lt (p8 表)


def num(s):
    s = (s or "").replace(",", "").replace("　", "").strip()
    if not s:
        return None
    m = re.fullmatch(r"[0-9]+(?:\.[0-9]+)?", s)
    return float(s) if m else None


def split_names(cell):
    """'φ440-300φ450-300' -> ['φ440-300', 'φ450-300']"""
    return re.findall(r"[φΦ]?\s*(\d{3,4}-\d{3,4})", cell or "")


def split_diams(cell, n):
    """'440450' -> [440, 450] （n 個に等分割）"""
    s = re.sub(r"[^0-9]", "", cell or "")
    if not s:
        return []
    if n <= 1:
        return [float(s)]
    w = len(s) // n
    return [float(s[i * w:(i + 1) * w]) for i in range(n)]


def merge_number_tokens(triples, gap=2.5):
    """千位が半角スペースで別単語になった数値を連結する ('1','000' -> '1000')。

    引数は (x0, x1, text) のリスト。
    """
    out = []
    for x0, x1, s in triples:
        if out and re.fullmatch(r"[0-9]+", s) and re.fullmatch(r"[0-9]+", out[-1][2]) \
                and x0 - out[-1][1] <= gap:
            out[-1] = (out[-1][0], x1, out[-1][2] + s)
        else:
            out.append((x0, x1, s))
    return out


def read_mass_table(doc):
    """p8 標準質量表 -> {(呼び名, 厚さ仕様): 単位長さ質量 t/m}"""
    page = doc[7]
    out = {}
    for x0, x1 in ((43, 206), (213, 376), (383, 546)):
        ws = [w for w in page.get_text("words")
              if 96 <= (w[1] + w[3]) / 2 <= 258 and x0 <= (w[0] + w[2]) / 2 <= x1]
        ws.sort(key=lambda w: ((w[1] + w[3]) / 2, w[0]))
        rows, cur, cy = [], [], None
        for w in ws:
            yc = (w[1] + w[3]) / 2
            if cy is None or abs(yc - cy) <= 3.0:
                cur.append(w)
                cy = yc if cy is None else cy
            else:
                rows.append(cur)
                cur, cy = [w], yc
        if cur:
            rows.append(cur)

        # 呼び名は結合セルの中央に置かれるため、行ごとに「直近の呼び名」を
        # y 距離で選ぶ (直前の行から引き継ぐと結合セルの先頭行を取り違える)。
        names = []   # (y, 呼び名)
        datas = []   # (y, 厚さ仕様, 質量)
        for r in rows:
            y = sum((w[1] + w[3]) / 2 for w in r) / len(r)
            toks = [w[4] for w in r]
            for t in toks:
                if re.match(r"[φΦ]\d", t):
                    names.append((y, t.lstrip("φΦ")))
            # 厚さ仕様は「超特厚130」のように厚さと連結して 1 単語になることがある
            spec = next((m.group(1) for t in toks
                         if (m := re.match(r"(超特厚|特厚|標準)", t))), None)
            # 質量は必ず単独トークン '0.148×L' として現れる
            massv = next((float(m.group(1)) for t in toks
                          if (m := re.fullmatch(r"([0-9.]+)×L", t))), None)
            if spec and massv is not None:
                datas.append((y, spec, massv))
        for y, spec, massv in datas:
            if not names:
                continue
            name = min(names, key=lambda n: abs(n[0] - y))[1]
            out[(name, spec)] = massv
    return out


def read_head_table(doc):
    """p8 拡頭タイプ形状一覧 -> list of dict"""
    page = doc[7]
    out = []
    for x0, x1 in ((43, 290), (300, 570)):
        ws = [w for w in page.get_text("words")
              if 360 <= (w[1] + w[3]) / 2 <= 560 and x0 <= (w[0] + w[2]) / 2 <= x1]
        ws.sort(key=lambda w: ((w[1] + w[3]) / 2, w[0]))
        rows, cur, cy = [], [], None
        for w in ws:
            yc = (w[1] + w[3]) / 2
            if cy is None or abs(yc - cy) <= 3.0:
                cur.append(w)
                cy = yc if cy is None else cy
            else:
                rows.append(cur)
                cur, cy = [w], yc
        if cur:
            rows.append(cur)
        for r in rows:
            toks = [w[4] for w in r]
            # 呼び名 'φ1200-1000（1100）' が Do-D と Dt を全て含むため、
            # 数値列の分割ゆらぎ (千位スペース) に依存せずここから寸法を決める。
            m = re.match(r"[φΦ](\d{3,4})-(\d{3,4})（(\d{3,4})）", toks[0])
            if not m:
                continue
            do, dd, dt = (float(m.group(i)) for i in (1, 2, 3))
            lm = next((t for t in toks if t.startswith("m+")), "")
            # Lt は「拡頭部長さ」列 (Dt 列と Lm 列の間) の値。千位分割のゆらぎを避けるため
            # x 座標帯で拾う。
            lt_band = (x0 + 0.62 * (x1 - x0), x0 + 0.80 * (x1 - x0))
            lt_toks = [w[4] for w in r if lt_band[0] <= (w[0] + w[2]) / 2 <= lt_band[1]]
            lt = num("".join(lt_toks)) or HEAD_LENGTH_LT
            out.append(dict(name=f"{m.group(1)}-{m.group(2)}", do=do, d=dd, dt=dt, lt=lt,
                            mass_add=float(lm[2:]) if lm else None))
    return out


def main():
    doc = fitz.open(PDF)
    mass = read_mass_table(doc)
    heads = read_head_table(doc)

    records = []
    for pg_spec, pg_perf in PAGE_PAIRS:
        _, _, gs = extract.extract_table(doc[pg_spec - 1], 89.0, 800.0, 42.0, 596.0)
        # 断面性能表は縦結合が無い。結合展開を有効にすると罫線欠けの列で
        # 上の行の値が下の行へ複製される (Ze 列で実際に発生) ため無効化する。
        _, _, gp = extract.extract_table(doc[pg_perf - 1], 89.0, 800.0, 42.0, 596.0,
                                         expand_merged=False)
        rows_s = gs[1:]
        rows_p = gp[2:]
        assert len(rows_s) == len(rows_p), f"行数不一致 p{pg_spec}/{pg_perf}: {len(rows_s)} vs {len(rows_p)}"

        for rs, rp in zip(rows_s, rows_p):
            names = split_names(rs[0])
            if not names:
                continue
            # 呼び名 'Do-D' が節部径と軸部径そのものなので、ここから導出する。
            # 節部径列は「1 200」の千位スペースで呼び名セルへ食い込むことがあり、
            # 列から読むと欠測する (φ1200-1100 で実際に発生)。
            dos = [float(n.split("-")[0]) for n in names]
            d = float(names[0].split("-")[1])
            # 列側が読めている場合は照合する (取り違えの検出)。
            # 結合セル内は '1000' + '900' が連結して現れるため、桁列の一致で判定する
            # (各値の桁数が異なるので等分割では判定できない)。
            col_digits = re.sub(r"[^0-9]", "", rs[1] or "")
            if col_digits and col_digits != "".join(str(int(x)) for x in dos):
                raise AssertionError(
                    f"節部径不一致 {names}: 呼び名から {dos} / 列の桁列 {col_digits!r}")
            col_d = num(rs[2])
            if col_d is not None and abs(col_d - d) > 1e-6:
                raise AssertionError(f"軸部径不一致 {names}: 呼び名 {d} vs 列 {col_d}")
            fc = num(rs[3])
            ptype = (rs[4] or "").strip()
            tspec = (rs[5] or "").strip()
            t = num(rs[6])
            pc_desig = (rs[7] or "").strip()
            pc_n = num(rs[8])
            ap = num(rs[9])
            pcd = num(rs[10])
            ao = num(rs[11])
            ae = num(rs[12])
            io_ = num(rs[13])
            ie = num(rs[14])

            ze = num(rp[1])
            sce_spec = num(rp[2])
            sce_calc = num(rp[3])
            mal, mas, mc, mu = (num(rp[i]) for i in (4, 5, 6, 7))
            qal, qas, qu = (num(rp[i]) for i in (8, 9, 10))
            nal = num(rp[11])

            for name, do in zip(names, dos):
                c = CONSTANTS[int(fc)]
                records.append(dict(
                    Maker="ジャパンパイル", Series=f"JP-NPH{int(fc)}",
                    Shape="節杭", Name=name, Do=do, D=d, t=t,
                    ThicknessType=tspec, Fc=fc, PrestressType=ptype,
                    PcDesignation=pc_desig, PcCount=int(pc_n) if pc_n else 0,
                    Ap=ap, PCD=pcd,
                    Ao=ao * 100.0, Ae=ae * 100.0,
                    Io=io_ * 1e4, Ie=ie * 1e4, Ze=ze * 1e3,
                    SigmaCeSpec=sce_spec, SigmaCeCalc=sce_calc,
                    Nal=nal, Mal=mal, Mas=mas, Mc=mc, Mu=mu,
                    Qal=qal, Qas=qas, Qu=qu,
                    Ec=c["ec"], Ftp=FTP, SigmaPu=SIGMA_PU, Ep=EP,
                    FcAllowCompLong=c["comp_l"], FcAllowCompShort=c["comp_s"],
                    FcAllowDiagLong=c["diag_l"], FcAllowDiagShort=c["diag_s"],
                    MassPerM=mass.get((name, tspec)),
                    NodePitch=NODE_PITCH, HeadOffset=HEAD_OFFSET, ToeOffset=TOE_OFFSET,
                ))

    # カタログ自身の内部矛盾を検出して注記する。
    # Ze は Ie/(D/2) で一意に決まるので、印字値がこれと大きくずれる行は誤植。
    # (実際 φ700-600/φ800-600 の Fc123 AH 種で、直上行と同じ値が印字されている)
    for r in records:
        ze_from_ie = r["Ie"] / (r["D"] / 2.0)
        if r["Ze"] and abs(r["Ze"] - ze_from_ie) / ze_from_ie > 0.0015:
            r["Note"] = (f"カタログ Ze 誤植の疑い: 印字 {r['Ze']/1e3:,.0f}×10^3 に対し "
                         f"Ie/(D/2) = {ze_from_ie/1e3:,.0f}×10^3。ZeFromIe を使用のこと")
        else:
            r["Note"] = ""
        r["ZeFromIe"] = round(ze_from_ie, 1)

    cols = list(records[0].keys())
    with io.open("pile_library_NodularPile.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        for r in records:
            w.writerows([r])
    print(f"本体: {len(records)} 行 -> pile_library_NodularPile.csv")

    hcols = ["Maker", "Shape", "Name", "Do", "D", "Dt", "Lt", "MassAdd"]
    with io.open("pile_library_NodularPile_head.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=hcols)
        w.writeheader()
        for h in heads:
            w.writerow(dict(Maker="ジャパンパイル", Shape="節杭", Name=h["name"],
                            Do=h["do"], D=h["d"], Dt=h["dt"], Lt=h["lt"],
                            MassAdd=h["mass_add"]))
    print(f"拡頭: {len(heads)} 行 -> pile_library_NodularPile_head.csv")


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
