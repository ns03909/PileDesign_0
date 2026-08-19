# -*- coding: utf-8 -*-
"""三谷セキサン DAM105 パイル (cat_dam105.pdf) から製品ライブラリ CSV を生成する。

DAM105 は Fc=105N/mm² のストレート PRC 杭。断面の扱いは既存の PRC杭 と同じなので、
既存の既製杭ライブラリ (pile_library_PRC.csv) と<b>同じ 28 列スキーマ</b>で出力し、
断面タイプ「PRC杭」の製品一覧にそのまま並べる。

カタログの本体部標準性能表 (p5〜p7) は DAM105 / TS-DAM105 / BF-DAM105 / BF-TS-DAM105 で
共通なので、ここで抽出した値は将来これらの製品にも使える。

抽出の要点:
  - 1 ページに「標準仕様」と「標準性能表」の 2 表が横並びで、<b>行が完全に対応</b>する。
    行数の一致を確認してから index で join し、種類と肉厚が両表で一致することも検算する
  - 本体部径 D の列は左端の罫線が無いので Ao = πT(D−T) から解き、印字値と照合する
  - 肉厚仕様は縦書きで、結合セルの中で 1 文字ずつ別の行帯に入ってしまう。
    径ごとに肉厚を並べた順位 (標準型 < 厚型 < 特厚型) から決め、
    抽出できた文字と矛盾しないことを確かめる
  - PCD はカタログに印字が無いので Ie から逆算する。PC鋼棒と異形鉄筋は<b>別の円周</b>にあり、
    さらに主筋の円は<b>鉄筋径が太いほど内側</b>へ寄る (かぶりが一定なら dr = D − 2c − db)。
    同じ (D, 肉厚) の種類違い (= 主筋径違い) が複数あるので、
    PC鋼棒の配筋径と主筋のかぶりを未知数として最小二乗で解ける。
    残差が小さく収まることがモデルの裏付けになる
  - <b>見出しとデータ 1 行目の間の罫線が引かれていない</b>ため、そのままだと見出し文字が
    1 行目のセルへ流れ込む。行は等ピッチなので抜けた境界を算出して extra_hlines で補う
  - <b>せん断耐力はカタログ自身が「シアスパン比 a=1.0 の参考値」と注記している</b>ので、
    設計値としては使わず参照列に置く
"""
import csv
import io
import math
import re
import sys

import fitz

import extract

PDF = "cat_dam105.pdf"
PAGES = [5, 6, 7]
LEFT_X = (60.0, 560.0)
RIGHT_X = (650.0, 1160.0)
Y_RANGE = (150.0, 800.0)

# ── 材料強度 (p2「1.材料強度」「2.許容応力度」) ─────────────────────
FC = 105.0
EC = 40000.0
EP = 200000.0          # PC鋼棒 SBPDL1275/1420
ER = 200000.0          # 異形鉄筋 SD345
FTP = 1275.0
SIGMA_PU = 1420.0
FTR = 345.0            # 異形鉄筋 短期許容応力度 (= SD345 の降伏点)
FC_ALLOW_COMP_LONG = 30.0
FC_ALLOW_COMP_SHORT = 60.0

TOLERANCE_UNIT = 0.5   # カタログは cm²/cm⁴ 単位で丸めて印字する

LEFT_COLUMNS = ["D", "ThickType", "Kind", "T", "L",
                "PcDia", "PcCount", "Ap", "BarDia", "BarCount", "As",
                "Sh50Dia", "Sh50Pitch", "Sh80Dia", "Sh80Pitch"]
RIGHT_COLUMNS = ["D", "ThickType", "Kind", "T", "Ao", "Ae", "Ie", "SigmaCe",
                 "Mcr", "Mal", "Mas", "Mu", "Qal",
                 "Q50Short", "Q50Ultimate", "Q80Short", "Q80Ultimate", "Nal"]

THICK_NAMES = ["標準型", "厚型", "特厚型"]


def num(s):
    m = re.fullmatch(r"[\d.]+", (s or "").strip())
    return float(m.group()) if m else None


def missing_top_boundary(page, xrange_):
    """見出しとデータ 1 行目の間に抜けている行境界を返す。

    この表は見出し下端の罫線が引かれておらず、放置すると見出し文字が 1 行目のセルへ
    流れ込む (杭断面積や肉厚が「（㎝2）」付きになる)。行は等ピッチなので、
    最初の正規の境界からピッチぶん遡った位置を補えばよい。
    左右の表は行位置が微妙に違うので、<b>表ごとに</b>求めること。
    """
    ys, _, _ = extract.extract_table(page, Y_RANGE[0], Y_RANGE[1], *xrange_,
                                     expand_merged=False)
    gaps = [ys[i + 1] - ys[i] for i in range(len(ys) - 1)]
    pitch = sorted(gaps)[len(gaps) // 2]
    out, y = [], ys[1] - pitch
    while y > ys[0] + 0.5 * pitch:
        out.append(y)
        y -= pitch
    return out


def parse_page(doc, pageno):
    page = doc[pageno - 1]
    extraL = missing_top_boundary(page, LEFT_X)
    extraR = missing_top_boundary(page, RIGHT_X)
    _, _, left = extract.extract_table(page, Y_RANGE[0], Y_RANGE[1], *LEFT_X,
                                       expand_merged=True, split_unruled=True,
                                       extra_hlines=extraL)
    _, _, right = extract.extract_table(page, Y_RANGE[0], Y_RANGE[1], *RIGHT_X,
                                        expand_merged=True, split_unruled=True,
                                        extra_hlines=extraR)
    assert len(left[0]) == len(LEFT_COLUMNS), f"p{pageno} 左表の列数 {len(left[0])}"
    assert len(right[0]) == len(RIGHT_COLUMNS), f"p{pageno} 右表の列数 {len(right[0])}"
    # 見出しの行数が左右で違いうるので、行数ではなく<b>データ行</b>で突き合わせる
    isData = lambda row, cols: bool(re.fullmatch(r"A-D\d{2}", dict(zip(cols, row))["Kind"].strip()))
    left = [r for r in left if isData(r, LEFT_COLUMNS)]
    right = [r for r in right if isData(r, RIGHT_COLUMNS)]
    assert len(left) == len(right), f"p{pageno} 左右でデータ行数が違う: {len(left)} / {len(right)}"

    # 本体部径の印字 (導出値の照合用)。左右どちらの表の左端にも出る。
    printed = {float(w[4]) for w in page.get_text("words")
               if 140.0 <= w[1] <= Y_RANGE[1]
               and (LEFT_X[0] - 25 <= w[0] <= LEFT_X[0] + 25
                    or RIGHT_X[0] - 25 <= w[0] <= RIGHT_X[0] + 25)
               and re.fullmatch(r"\d{3,4}", w[4]) and float(w[4]) % 50 == 0}
    assert printed, f"p{pageno}: 本体部径の印字が見つからない"

    rows = []
    for lrow, rrow in zip(left, right):
        lc = dict(zip(LEFT_COLUMNS, lrow))
        rc = dict(zip(RIGHT_COLUMNS, rrow))
        # 左右の表が同じ行を指していることを確認する
        assert lc["Kind"].strip() == rc["Kind"].strip(), \
            f"p{pageno}: 左右で種類が違う {lc['Kind']} / {rc['Kind']}"

        T = num(lc["T"]) or num(rc["T"])
        Ao = num(rc["Ao"])
        assert T and Ao, f"p{pageno} {lc['Kind']}: 肉厚か杭断面積が読めない"
        Ao *= 100.0                                  # cm2 -> mm2
        raw = Ao / (math.pi * T) + T                 # Ao = πT(D−T)
        D = round(raw / 50.0) * 50.0                 # 杭径は 50mm 刻み
        assert abs(D - raw) < 5.0, f"p{pageno}: 導出した径 {raw:.1f} が 50mm 刻みに乗らない"
        assert D in printed, f"p{pageno}: 導出した径 {D:.0f} が印字 {sorted(printed)} に無い"

        rows.append(dict(
            D=D, Kind=lc["Kind"].strip(), T=T,
            ThickChars=(lc["ThickType"] + rc["ThickType"]).strip(),
            Length=lc["L"].strip(),
            PcDia=lc["PcDia"].strip(), PcCount=int(num(lc["PcCount"])),
            Ap=num(lc["Ap"]) * 100.0,
            BarDia=lc["BarDia"].strip(), BarCount=int(num(lc["BarCount"])),
            As=num(lc["As"]) * 100.0,
            Sh50Dia=lc["Sh50Dia"].strip(), Sh50Pitch=lc["Sh50Pitch"].strip(),
            Sh80Dia=lc["Sh80Dia"].strip(), Sh80Pitch=lc["Sh80Pitch"].strip(),
            Ao=Ao, Ae=num(rc["Ae"]) * 100.0, Ie=num(rc["Ie"]) * 1e4,
            SigmaCe=num(rc["SigmaCe"]),
            Mcr=num(rc["Mcr"]), Mal=num(rc["Mal"]), Mas=num(rc["Mas"]), Mu=num(rc["Mu"]),
            Qal=num(rc["Qal"]),
            Q50Short=num(rc["Q50Short"]), Q50Ultimate=num(rc["Q50Ultimate"]),
            Q80Short=num(rc["Q80Short"]), Q80Ultimate=num(rc["Q80Ultimate"]),
            Nal=num(rc["Nal"]),
        ))
    return rows


def assign_thickness(rows):
    """肉厚仕様を径ごとの肉厚の順位から決める。

    縦書きの結合セルなので文字が 1 文字ずつ別の行帯に散り、そのままでは読めない。
    径ごとに肉厚を小さい順に並べれば 標準型 < 厚型 < 特厚型 なので順位で決まる。
    抽出できた文字断片と矛盾しないことを確認して裏を取る。
    """
    by_dia = {}
    for r in rows:
        by_dia.setdefault(r["D"], set()).add(r["T"])
    for d, ts in by_dia.items():
        assert len(ts) <= 3, f"φ{d:.0f}: 肉厚が {len(ts)} 種類ある {sorted(ts)}"

    for r in rows:
        rank = sorted(by_dia[r["D"]]).index(r["T"])
        r["ThickType"] = THICK_NAMES[rank]
        chars = set(r.pop("ThickChars"))
        # 「標」が出ていれば標準型、「特」が出ていれば特厚型。断片が矛盾していないか見る
        if "標" in chars:
            assert r["ThickType"] == "標準型", f"φ{r['D']:.0f} T{r['T']:.0f}: 標準型のはず"
        if "特" in chars:
            assert r["ThickType"] == "特厚型", f"φ{r['D']:.0f} T{r['T']:.0f}: 特厚型のはず"
    return rows


NOMINAL_BAR_AREA = {"D13": 126.7, "D16": 198.6, "D19": 286.5, "D22": 387.1,
                    "D25": 506.7, "D29": 642.4, "D32": 794.2, "D35": 956.6}


def fix_rebar_area(rows):
    """主筋断面積の印字誤りを検出して訂正する。

    As は 本数 × JIS G 3112 の公称断面積で決まる。合わない行はカタログの誤植で、
    同じ行の換算断面積 Ae は正しい値で計算されているのでそれで裏が取れる。
    """
    fixed = []
    for r in rows:
        key = f"D{r['BarDia']}"
        assert key in NOMINAL_BAR_AREA, f"未知の主筋径 {key}"
        exact = r["BarCount"] * NOMINAL_BAR_AREA[key]
        if abs(exact - r["As"]) > exact * 0.01:
            r["AsNote"] = (f"誤植: カタログ印字 As={r['As'] / 100:.2f}cm2 "
                           f"(正 {r['BarCount']}-{key}={exact / 100:.2f}cm2)")
            r["As"] = exact
            fixed.append(r)
    if fixed:
        for r in fixed:
            print(f"  誤植 (主筋断面積): φ{r['D']:.0f} {r['ThickType']} {r['Kind']} → {r['AsNote']}")
    return fixed


def solve_pcd(rows):
    """配筋径を Ie から逆算する。

    Ie = Io + (n−1)/2 · [Ap·rp² + As·rr²]
    PC鋼棒の半径 rp は (D, 肉厚) ごとに一定、主筋の半径は<b>かぶりが一定</b>と考えて
    rr = R0 − db/2 (db は鉄筋径) と置く。未知数は rp と R0 の 2 つで、
    同じ断面に種類 (主筋径) 違いが複数あるので最小二乗で解ける。
    R0 を 1 次元探索し、各 R0 で rp² を線形最小二乗で求める。
    """
    n = EP / EC
    by_section = {}
    for r in rows:
        by_section.setdefault((r["D"], r["T"]), []).append(r)

    out, worst = {}, 0.0
    for (D, T), g in by_section.items():
        di = D - 2 * T
        io_ = math.pi / 64 * (D ** 4 - di ** 4)
        target = [(r["Ie"] - io_) * 2 / (n - 1) for r in g]
        db = [float(r["BarDia"]) for r in g]

        def residual(R0):
            rr2 = [(R0 - x / 2) ** 2 for x in db]
            num_ = sum(r["Ap"] * (t - a * r2) for r, t, a, r2
                       in zip(g, target, (x["As"] for x in g), rr2))
            den = sum(r["Ap"] ** 2 for r in g)
            rp2 = num_ / den
            err = [(r["Ap"] * rp2 + r["As"] * r2 - t) / t
                   for r, r2, t in zip(g, rr2, target)]
            return math.sqrt(sum(e * e for e in err) / len(err)), rp2

        lo, hi = di / 2, D / 2 + 20.0
        for _ in range(40):                       # 3 分割法で R0 を絞る
            a, b = lo + (hi - lo) / 3, hi - (hi - lo) / 3
            if residual(a)[0] < residual(b)[0]:
                hi = b
            else:
                lo = a
        R0 = (lo + hi) / 2
        rms, rp2 = residual(R0)
        assert rp2 > 0, f"φ{D:.0f} T{T:.0f}: PC鋼棒の配筋径が解けない"
        assert rms < 0.005, f"φ{D:.0f} T{T:.0f}: 配筋径モデルの残差が大きい ({rms * 100:.2f}%)"
        worst = max(worst, rms)

        dp = round(2 * math.sqrt(rp2))
        for r in g:
            dr = round(2 * (R0 - float(r["BarDia"]) / 2))
            assert di < dp < D and di < dr < D,                 f"φ{D:.0f} T{T:.0f} {r['Kind']}: 配筋径が肉厚の外 (dp={dp}, dr={dr})"
            out[(D, T, r["Kind"])] = (dp, dr)
    print(f"  配筋径を Ie から逆算: {len(by_section)} 断面、残差 RMS 最大 {worst * 100:.3f}%")
    return out


def verify(rows):
    n = EP / EC
    worst = {"Ao": 0.0, "Ae": 0.0}
    for r in rows:
        di = r["D"] - 2 * r["T"]
        ao = math.pi / 4 * (r["D"] ** 2 - di ** 2)
        ae = ao + (n - 1) * (r["Ap"] + r["As"])
        # Ao は印字の丸め (0.5cm²) 以内で厳密に一致する。
        # Ae はメーカー側の中間値の丸めが乗るため 218 行中 8 行が 0.5cm² をわずかに超える
        # (最大 1.8cm² = 0.14%、符号は両方向)。桁の誤りは 10cm² 以上ずれるので取りこぼさない。
        limit = {"Ao": TOLERANCE_UNIT * 100.0, "Ae": 200.0}
        for k, v in (("Ao", ao), ("Ae", ae)):
            diff = abs(v - r[k])
            worst[k] = max(worst[k], diff / 100.0)
            assert diff <= limit[k] + r[k] * 1e-4,                 f"φ{r['D']:.0f} T{r['T']:.0f} {r['Kind']} の {k}: 印字 {r[k]:,.0f} / 計算 {v:,.0f}"
    for k in ("Ao", "Ae"):
        print(f"  {k:3} 最大ずれ {worst[k]:.2f} cm² (許容 {TOLERANCE_UNIT})")

    # 種類は主筋径そのもの (主筋断面積は fix_rebar_area で公称値と突合済み)
    for r in rows:
        assert r["Kind"] == f"A-D{r['BarDia']}",             f"種類 {r['Kind']} と主筋径 D{r['BarDia']} が食い違う"

    for r in rows:
        tag = f"φ{r['D']:.0f} T{r['T']:.0f} {r['Kind']}"
        assert r["Mal"] < r["Mcr"] < r["Mas"] < r["Mu"], f"{tag}: 曲げの大小関係"
        # せん断補強筋 80K は小径では設定が無く「－」になる。値がある組だけ大小を見る
        for lo, hi in (("Qal", "Q50Short"), ("Q50Short", "Q50Ultimate"),
                       ("Q80Short", "Q80Ultimate")):
            if r[lo] is not None and r[hi] is not None:
                assert r[lo] < r[hi], f"{tag}: {lo} < {hi} が成り立たない"


def to_record(no, r, pcds):
    dp, dr = pcds
    name = f"DAM105-{r['D']:.0f}-{r['ThickType']}-{r['Kind']}"
    return {
        "No.": no, "標準特厚": r["ThickType"], "種": r["Kind"],
        "typ": name, "杭種": "PRC",
        "D": f"{r['D']:.0f}", "t": f"{r['T']:.0f}", "Fc": f"{FC:.0f}",
        "fc_": f"{FC_ALLOW_COMP_SHORT:.0f}",
        "fbc": f"{r['SigmaCe'] / 2.0:.2f}",
        "sigma_e": f"{r['SigmaCe']:.1f}",
        "Ec": f"{EC:.0f}",
        "ap": f"{r['Ap']:.0f}", "dp": f"{dp:.0f}",
        "ftp": f"{FTP:.0f}", "sigma_pu": f"{SIGMA_PU:.0f}", "Ep": f"{EP:.0f}",
        "has_reinf": "true", "nr": r["BarCount"], "r_designation": f"D{r['BarDia']}",
        "ag": f"{r['As']:.0f}", "dr": f"{dr:.0f}",
        "ftr": f"{FTR:.0f}", "Er": f"{ER:.0f}",
        "ts": 0, "fts": 0, "Es": 0, "ps_sigma_y": 0,
        # ここから先は既存ローダー (列番号で読む) が触らない参照列
        "PcDesignation": r["PcDia"], "PcCount": r["PcCount"],
        "LengthRange": r["Length"],
        "CatalogAo": f"{r['Ao']:.0f}", "CatalogAe": f"{r['Ae']:.0f}",
        "CatalogIe": f"{r['Ie']:.0f}",
        "CatalogMcr": r["Mcr"], "CatalogMal": r["Mal"],
        "CatalogMas": r["Mas"], "CatalogMu": r["Mu"], "CatalogNal": r["Nal"],
        # せん断耐力はカタログ自身が「シアスパン比 a=1.0 の参考値」と注記している。
        # 設計値ではないので参照列に置く。
        "RefQal": r["Qal"] if r["Qal"] is not None else "",
        "RefQ50Short": r["Q50Short"] if r["Q50Short"] is not None else "",
        "RefQ50Ultimate": r["Q50Ultimate"] if r["Q50Ultimate"] is not None else "",
        "RefQ80Short": r["Q80Short"] if r["Q80Short"] is not None else "",
        "RefQ80Ultimate": r["Q80Ultimate"] if r["Q80Ultimate"] is not None else "",
        "RefShearNote": "シアスパン比a=1.0の参考値。設計値は別途計算式による",
        "Sh50": f"{r['Sh50Dia']}@{r['Sh50Pitch']}",
        "Sh80": f"{r['Sh80Dia']}@{r['Sh80Pitch']}",
        "AsNote": r.get("AsNote", ""),
    }


def main():
    doc = fitz.open(PDF)
    rows = []
    for pageno in PAGES:
        rows += parse_page(doc, pageno)
    assign_thickness(rows)

    print(f"データ行 {len(rows)}  径 {sorted({int(r['D']) for r in rows})}")
    print(f"  肉厚仕様 {sorted({r['ThickType'] for r in rows})}  "
          f"種類 {sorted({r['Kind'] for r in rows})}")

    fix_rebar_area(rows)
    pcd = solve_pcd(rows)
    verify(rows)

    records = [to_record(3000 + i, r, pcd[(r["D"], r["T"], r["Kind"])])
               for i, r in enumerate(rows, start=1)]
    names = [r["typ"] for r in records]
    assert len(set(names)) == len(names), \
        f"製品名が重複: {sorted({n for n in names if names.count(n) > 1})}"

    with io.open("pile_library_PRC_DAM105.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(records[0].keys()))
        w.writeheader()
        w.writerows(records)
    print(f"{len(records)} 行 -> pile_library_PRC_DAM105.csv")


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
