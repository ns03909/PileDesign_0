# -*- coding: utf-8 -*-
"""三谷セキサン MS-hi105 パイル (cat_pmc_pile.pdf) から製品ライブラリ CSV を生成する。

MS-hi105 は Fc=105N/mm² のストレート PHC 杭。断面は既存の PHC杭 と同一の中空円形なので、
既存の既製杭ライブラリ (pile_library_PHC.csv) と<b>同じ 28 列スキーマ</b>で出力し、
断面タイプ「PHC杭」の製品一覧にそのまま並べる。

カタログの本体部標準性能表 (p5) は MS-hi105 / MS-TS105 / MS-ST105 / BF105 / BF-TS105 で
共通なので、ここで抽出した値は将来これらの製品にも使える。

この表だけ罫線ベース (extract.py) が使えない
--------------------------------------------
行の区切りが<b>線ではなく背景の塗り矩形の縁</b>で表現されていて、`get_drawings` から
水平線として取れない。列の垂直線は取れるので、
  - 列境界 … 垂直罫線から
  - 行境界 … 行が等ピッチであることを使って算術的に作る (基準は必ず 1 行 1 値になる Ae 列)
  - 縦結合 … 結合セルの文字が<b>結合範囲の中央</b>に置かれることから span を厳密に解く
とする。ただし<b>結合行数が偶数のときは中央が行と行の境目になり</b>、文字がどちらの行に
置かれるかで半行ぶんの曖昧さが出る。本体部径はこれに当たる (φ1100 は 8 行結合) ので、
径だけは Ao = πT(D−T) から解き、印字されている径の一覧と一致することで照合する。中点で分ける近似だと、隣り合う結合の行数が違うとき境界行を取り違える
(この表は φ300 が 3 行結合・φ400 が 9 行など不揃い)。
span の解が行数とぴったり合うことが、そのまま抽出の検算になっている。
"""
import csv
import io
import math
import re
import sys

import fitz

PDF = "cat_pmc_pile.pdf"
PERF_PAGE = 5

# 表は φ300〜700 と φ800〜1200 の 2 ブロックが横並び。(列の x 範囲, 本体部径列の x 範囲)
BLOCKS = [(59.0, 572.0, 20.0, 59.0), (649.0, 1162.0, 610.0, 649.0)]
Y_BOTTOM = 780.0

# ── 設計に用いる材料強度・許容応力度 (p2「1.材料強度」) ─────────────
FC = 105.0
EC = 40000.0
FTP = 1275.0          # PC鋼棒 SBPDL1275/1420 の耐力
SIGMA_PU = 1420.0
EP = 200000.0
FC_ALLOW_COMP_LONG = 30.0
FC_ALLOW_COMP_SHORT = 60.0
ALLOW_BEND_TENS_SHORT_FACTOR = 0.5   # 短期許容曲げ引張 = σce/2

TOLERANCE = 0.0015    # カタログの丸め (有効数字 3〜4 桁) を許容する

# 列の並び。「JIS A 5373 ひびわれ曲げモーメント」列は取り込まない
# (曲げ耐力の Mcr が別列にあり、そちらで足りるため)。
COLUMNS = ["Kind", "ThickType", "T", "PcDesignation", "PcCount", "Ap",
           "Ao", "Ae", "Ie", "SigmaCe", "_JisMcr",
           "Mal", "Mas", "Mcr", "Mu", "Qal", "Qas", "Qcr", "Nal"]
ROW_ANCHOR = "Ae"     # 必ず 1 行 1 値になる列。行グリッドの基準に使う


def column_x(page, xmin, xmax):
    """ブロックの列境界 (垂直罫線の x)。

    x だけで絞ると同じページの N-M 図などの縦線が混ざる。列の罫線は<b>複数の線分に
    分かれて</b>いるので、同じ x のものをまとめてから「表本体を縦断している」もの
    だけを列境界として採る。
    """
    segs = []
    for g in page.get_drawings():
        for it in g["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.x - b.x) >= 0.6 or abs(a.y - b.y) <= 1:
                    continue
                segs.append((a.x, min(a.y, b.y), max(a.y, b.y)))
            elif it[0] == "re" and it[1].width < 1.5 and it[1].height > 1:
                r = it[1]
                segs.append(((r.x0 + r.x1) / 2, r.y0, r.y1))

    groups = []
    for x, y0, y1 in sorted(segs):
        if xmin - 1 > x or x > xmax + 1:
            continue
        if groups and x - groups[-1][0][-1] <= 1.2:
            groups[-1][0].append(x)
            groups[-1][1].append((y0, y1))
        else:
            groups.append(([x], [(y0, y1)]))

    out = []
    for exes, spans in groups:
        total = sum(b - a for a, b in spans)
        if total > 300 and min(a for a, _ in spans) < 250 and max(b for _, b in spans) > 600:
            out.append(sum(exes) / len(exes))
    return out


def words_in(page, x0, x1, y0, y1):
    """列 x0..x1 の単語を y でまとめて (y中心, 文字列) の昇順リストにする。

    数値は千位が半角スペースで別単語になるため、同じ y のものは x 順に連結する。
    """
    groups = {}
    for w in page.get_text("words"):
        cx, cy = (w[0] + w[2]) / 2, (w[1] + w[3]) / 2
        if not (x0 - 0.5 <= cx <= x1 + 0.5 and y0 <= cy <= y1):
            continue
        groups.setdefault(round(cy, 0), []).append((w[0], cy, w[4]))
    # キーは丸め値だが、返す y は<b>丸めない平均</b>にする。
    # 丸めた y をそのまま行位置に使うと、ピッチ 11.7 が 12 に化けて行数分ずれる。
    return sorted((sum(c for _, c, _ in v) / len(v),
                   "".join(t for _, _, t in sorted(v)).replace(" ", ""))
                  for v in groups.values())


def row_grid(page, xs, y0, y1):
    """行の中心 y の一覧とピッチ。1 行 1 値が保証される列 (Ae) の単語位置がそのまま行になる。"""
    i = COLUMNS.index(ROW_ANCHOR)
    # 見出しの「（cm2）」の上付き 2 などが 1 桁の単語として拾えるので桁数で弾く
    centres = [y for y, t in words_in(page, xs[i], xs[i + 1], y0, y1) if re.fullmatch(r"\d{3,5}", t)]
    assert len(centres) >= 10, f"行グリッドの基準列から {len(centres)} 行しか取れない"
    gaps = [centres[k + 1] - centres[k] for k in range(len(centres) - 1)]
    pitch = sorted(gaps)[len(gaps) // 2]
    assert max(abs(g - pitch) for g in gaps) < 1.5, \
        f"行ピッチが一定でない: {sorted({round(g, 1) for g in gaps})}"
    return centres, pitch


def assign_spans(values, centres, pitch, label):
    """縦結合セルを含む列の値を各行へ割り当てる。

    結合セルの文字は結合範囲の中央に置かれる。行が等ピッチなので、
    ある結合の開始行 a が分かれば終了行は b = 2·p − a (p は文字中心の行番号) で決まる。
    先頭は必ず a=0 なので、そこから順に鎖のように解ける。
    """
    n = len(centres)
    out = [None] * n
    a = 0
    for i, (y, text) in enumerate(values):
        p = (y - centres[0]) / pitch
        b = int(round(2 * p - a))
        assert a <= b < n, f"{label}: {i} 番目 '{text}' の結合範囲が壊れている (a={a}, b={b}, n={n})"
        for k in range(a, b + 1):
            out[k] = text
        a = b + 1
    assert a == n, f"{label}: 結合範囲の合計 {a} が行数 {n} と合わない"
    return out


def parse(doc):
    page = doc[PERF_PAGE - 1]
    rows = []
    for xmin, xmax, dx0, dx1 in BLOCKS:
        xs = column_x(page, xmin, xmax)
        assert len(xs) - 1 == len(COLUMNS), f"列数が想定と違う: {len(xs) - 1}"

        centres, pitch = row_grid(page, xs, 150.0, Y_BOTTOM)
        top, bottom = centres[0] - pitch, centres[-1] + pitch

        cols = {}
        for i, name in enumerate(COLUMNS):
            vals = [(y, t) for y, t in words_in(page, xs[i], xs[i + 1], top, bottom) if t]
            cols[name] = assign_spans(vals, centres, pitch, name)
        block = []
        for k in range(len(centres)):
            r = {name: cols[name][k] for name in COLUMNS}
            T = float(r["T"])
            Ao = float(r["Ao"]) * 100.0
            # Ao = πT(D−T) を D について解く (本体部径の列は結合行数が偶数の径があり、
            # 文字位置から結合範囲を一意に決められないため)
            D = round((Ao / (math.pi * T) + T) / 10.0) * 10.0
            block.append(dict(
                D=D, Kind=r["Kind"], ThickType=r["ThickType"], T=T,
                PcDesignation=r["PcDesignation"], PcCount=int(r["PcCount"]),
                Ap=float(r["Ap"]) * 100.0,          # cm2 -> mm2
                Ao=Ao,
                Ae=float(r["Ae"]) * 100.0,
                Ie=float(r["Ie"]) * 1e4,            # cm4 -> mm4
                SigmaCe=float(r["SigmaCe"]),
                Mal=float(r["Mal"]), Mas=float(r["Mas"]),
                Mcr=float(r["Mcr"]), Mu=float(r["Mu"]),
                Qal=float(r["Qal"]), Qas=float(r["Qas"]), Qcr=float(r["Qcr"]),
                Nal=float(r["Nal"]),
            ))

        printed = sorted({float(t) for _, t in words_in(page, dx0, dx1, top, bottom)
                          if re.fullmatch(r"\d{3,4}", t)})
        derived = sorted({r["D"] for r in block})
        assert derived == printed, f"導出した径 {derived} が印字 {printed} と一致しない"
        rows += block
    return rows


def solve_pcd(rows):
    """PC鋼棒の配筋径 PCD を Ie から逆算する。

    カタログに印字が無いが断面計算に必要。同じ径なら種類・肉厚仕様によらず同じ値に
    なるはずなので、それを一致検査に使う (5mm 刻みに乗ることも確認する)。
    """
    n = EP / EC
    by_dia = {}
    for r in rows:
        di = r["D"] - 2 * r["T"]
        io_ = math.pi / 64 * (r["D"] ** 4 - di ** 4)
        v = (r["Ie"] - io_) * 2 / ((n - 1) * r["Ap"])
        assert v > 0, f"φ{r['D']:.0f} {r['ThickType']} {r['Kind']}: Ie が Io を下回る"
        by_dia.setdefault(r["D"], []).append(2 * math.sqrt(v))

    out = {}
    for d, vals in by_dia.items():
        spread = max(vals) - min(vals)
        assert spread < 1.0, f"φ{d:.0f}: 逆算 PCD がばらつく ({spread:.2f}mm)"
        mean = sum(vals) / len(vals)
        rounded = round(mean / 5.0) * 5.0
        assert abs(rounded - mean) < 1.0, f"φ{d:.0f}: 逆算 PCD {mean:.2f} が丸め値から離れている"
        out[d] = rounded
    print("  Ie から逆算した PCD [mm]:",
          "  ".join(f"φ{k:.0f}={v:.0f}" for k, v in sorted(out.items())))
    return out


def verify(rows):
    n = EP / EC
    worst = {"Ao": 0.0, "Ae": 0.0}
    for r in rows:
        ao = math.pi / 4 * (r["D"] ** 2 - (r["D"] - 2 * r["T"]) ** 2)
        for key, calc in (("Ao", ao), ("Ae", ao + (n - 1) * r["Ap"])):
            rel = abs(calc - r[key]) / r[key]
            worst[key] = max(worst[key], rel)
            assert rel <= TOLERANCE, \
                f"φ{r['D']:.0f} {r['ThickType']} {r['Kind']} の {key}: 印字 {r[key]:,.0f} / 計算 {calc:,.0f}"
    for k, v in worst.items():
        print(f"  {k:3} 最大相対差 {v * 100:6.3f}%")

    # 種類は JIS A 5373 の A/B/C。σce は種別の規定値 (4/8/10) を<b>超えない</b>。
    # 標準型・特厚型はちょうど規定値、厚型だけが下がる
    # (標準型と同じ PC 鋼棒のまま肉厚が増えるので換算断面積が大きくなるため。実測 0.91〜0.99 倍)。
    # 種類の割り当てや σce の行ズレがあればこの範囲から外れる。
    spec = {"A": 4.0, "B": 8.0, "B2": 8.0, "C": 10.0, "C2": 10.0}
    ratios = [(r["SigmaCe"] / spec[r["Kind"]], r) for r in rows]
    for v, r in ratios:
        tag = f"φ{r['D']:.0f} {r['ThickType']} {r['Kind']}"
        assert 0.90 <= v <= 1.005, f"{tag}: σce {r['SigmaCe']} が種別 {r['Kind']} の規定値と釣り合わない"
        if r["ThickType"] != "厚型":
            assert v >= 0.995, f"{tag}: 厚型以外なのに σce が規定値を下回る"
    lo = min(v for v, _ in ratios)
    print(f"  σce / JIS 規定値 (A=4/B=8/C=10): {lo:.2f}〜1.00  (下振れは厚型のみ)")

    for r in rows:
        tag = f"φ{r['D']:.0f} {r['ThickType']} {r['Kind']}"
        assert r["Mal"] < r["Mas"] <= r["Mcr"] < r["Mu"], f"{tag}: 曲げの大小関係"
        assert r["Qal"] < r["Qas"] < r["Qcr"], f"{tag}: せん断の大小関係"


def to_record(no, r, pcd):
    """既存 pile_library_PHC.csv と同じ 28 列 + 参照用のカタログ耐力列。"""
    return {
        "No.": no,
        "標準特厚": r["ThickType"],
        "種": r["Kind"],
        "typ": f"MS-hi105-{r['D']:.0f}-{r['ThickType']}-{r['Kind']}",
        "杭種": "PHC",
        "D": f"{r['D']:.0f}",
        "t": f"{r['T']:.0f}",
        "Fc": f"{FC:.0f}",
        "fc_": f"{FC_ALLOW_COMP_SHORT:.0f}",
        "fbc": f"{r['SigmaCe'] * ALLOW_BEND_TENS_SHORT_FACTOR:.2f}",
        "sigma_e": f"{r['SigmaCe']:.1f}",
        "Ec": f"{EC:.0f}",
        "ap": f"{r['Ap']:.0f}",
        "dp": f"{pcd:.0f}",
        "ftp": f"{FTP:.0f}",
        "sigma_pu": f"{SIGMA_PU:.0f}",
        "Ep": f"{EP:.0f}",
        "has_reinf": "false",
        "nr": 0, "r_designation": 0, "ag": 0, "dr": 0, "ftr": 0, "Er": 0,
        "ts": 0, "fts": 0, "Es": 0, "ps_sigma_y": 0,
        # ここから先は既存ローダー (列番号で読む) が触らない参照列。カタログ記載の耐力。
        "CatalogAo": f"{r['Ao']:.0f}",
        "CatalogAe": f"{r['Ae']:.0f}",
        "CatalogIe": f"{r['Ie']:.0f}",
        "CatalogMal": r["Mal"], "CatalogMas": r["Mas"],
        "CatalogMcr": r["Mcr"], "CatalogMu": r["Mu"],
        "CatalogQal": r["Qal"], "CatalogQas": r["Qas"], "CatalogQcr": r["Qcr"],
        "CatalogNal": r["Nal"],
        "PcDesignation": r["PcDesignation"], "PcCount": r["PcCount"],
    }


def main():
    doc = fitz.open(PDF)
    rows = parse(doc)
    print(f"データ行 {len(rows)}  径 {sorted({int(r['D']) for r in rows})}")
    print(f"  肉厚仕様 {sorted({r['ThickType'] for r in rows})}  種類 {sorted({r['Kind'] for r in rows})}")

    pcd = solve_pcd(rows)
    verify(rows)

    records = [to_record(1000 + i, r, pcd[r["D"]]) for i, r in enumerate(rows, start=1)]
    names = [r["typ"] for r in records]
    assert len(set(names)) == len(names), "製品名が重複している"

    with io.open("pile_library_PHC_MSHI105.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(records[0].keys()))
        w.writeheader()
        w.writerows(records)
    print(f"{len(records)} 行 -> pile_library_PHC_MSHI105.csv")


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
