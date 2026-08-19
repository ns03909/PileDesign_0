# -*- coding: utf-8 -*-
"""三谷セキサン Hi-SC105 パイル (cat_hi_sc105.pdf) から製品ライブラリ CSV を生成する。

Hi-SC105 は Fc=105N/mm² の外殻鋼管付コンクリート杭 (SC杭)。断面の扱いは既存の SC杭 と
同じなので、既存の既製杭ライブラリ (pile_library_SC.csv) と<b>同じ 28 列スキーマ</b>で
出力し、断面タイプ「SC杭」の製品一覧にそのまま並べる。

このカタログの特徴:
  - 鋼管厚 ts が<b>1mm 刻み</b>で表になっている (既存の JIS 汎用ライブラリは代表値のみ)
  - 肉厚 T は<b>鋼管とコンクリートを合わせた全肉厚</b> (Ao = πT(D−T) で確認できる)
  - <b>腐食代 0mm と 1mm の両方</b>の表がある。本プログラムは腐食を製品ではなく
    CorrosionDepth パラメータとして扱うので製品としては 0mm 側を取り込み、
    1mm 側は<b>腐食モデルの外部検証データ</b>として同じ行に併記する
    (TestProject1/HiSc105PileLibraryTests.cs が使う)

抽出の要点:
  - 罫線はきちんと引かれているので行・列とも罫線から取れる。ただし
    短い装飾の縦線が混じるので、同じ x の線分をまとめて「表本体を縦断するもの」だけ採る
  - 杭断面積 Ao は Ac + As と一致するはずなので、これを検算に使う
    (腐食代1mm 表の φ1200 特厚型で印字誤りを 1 件検出した)
  - 肉厚仕様は縦書き (標/準/型) なので連結して正規化する
"""
import csv
import io
import math
import re
import sys

import fitz

PDF = "cat_hi_sc105.pdf"

# 標準性能表のページと、そのページのどちらのブロックが腐食代 0mm / 1mm か。
# p3〜p6 は両ブロックとも 0mm、p7 は左が 0mm・右が 1mm、p8〜p11 は両方 1mm。
PAGES = [(3, "00"), (4, "00"), (5, "00"), (6, "00"), (7, "01"),
         (8, "11"), (9, "11"), (10, "11"), (11, "11")]
BLOCK_X = [(40.0, 560.0), (635.0, 1155.0)]   # 左ブロック / 右ブロック の x 範囲
Y_RANGE = (90.0, 765.0)

# ── 設計に用いる数値 (p2「■設計に用いる数値／Hi-SC105」) ───────────────
FC = 105.0
EC = 40000.0
ES = 205000.0          # 鋼管 SKK490 / STK490
FTS = 325.0            # 鋼管 降伏点
FC_ALLOW_COMP_LONG = 30.0
FC_ALLOW_COMP_SHORT = 60.0
PIPE_GRADE = "SKK490"

TOLERANCE = 0.0015     # カタログの丸め (有効数字 3〜4 桁) を許容する

# 腐食代 0mm の表は最後に「杭単位重量」がある。1mm の表にはこの列が無い。
COLUMNS = ["D", "ThickType", "T", "Ts", "Ao", "Ac", "As", "Ae", "Ie",
           "Ml", "Ma", "Mu", "Ql", "Qa", "Weight"]
COLUMNS_CORRODED = COLUMNS[:-1]


def cluster(vals, tol=1.2):
    out = []
    for v in sorted(vals):
        if out and v - out[-1][-1] <= tol:
            out[-1].append(v)
        else:
            out.append([v])
    return [sum(g) / len(g) for g in out]


def lines(page):
    hl, vl = [], []
    for g in page.get_drawings():
        for it in g["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.y - b.y) < 0.6 and abs(a.x - b.x) > 1:
                    hl.append((a.y, min(a.x, b.x), max(a.x, b.x)))
                elif abs(a.x - b.x) < 0.6 and abs(a.y - b.y) > 1:
                    vl.append((a.x, min(a.y, b.y), max(a.y, b.y)))
            elif it[0] == "re":
                r = it[1]
                if r.height < 1.5 and r.width > 1:
                    hl.append(((r.y0 + r.y1) / 2, r.x0, r.x1))
                elif r.width < 1.5 and r.height > 1:
                    vl.append(((r.x0 + r.x1) / 2, r.y0, r.y1))
    return hl, vl


def grid(page, xmin, xmax):
    """ブロックの列境界と行境界。

    縦線は短い装飾が混じるので、同じ x の線分をまとめて<b>表本体を縦断するもの</b>だけ採る。
    杭径 D と杭単位重量は外枠側の罫線が取れないので、水平罫線の届く範囲を端にする。
    """
    hl, vl = lines(page)
    hl = [h for h in hl if Y_RANGE[0] <= h[0] <= Y_RANGE[1]
          and h[2] >= xmin and h[1] <= xmax]
    # 行境界は表を横断する長い線で決める。ただし縦結合の判定 (has_hline) には
    # 列ごとに分かれた短い線分も要るので、絞り込む前のものを別に持っておく。
    hlong = [h for h in hl if h[2] - h[1] > 150]
    assert hlong, "水平罫線が見つからない"

    groups = []
    for x, y0, y1 in sorted(vl):
        if not (xmin <= x <= xmax):
            continue
        if groups and x - groups[-1][0][-1] <= 1.2:
            groups[-1][0].append(x)
            groups[-1][1].append((y0, y1))
        else:
            groups.append(([x], [(y0, y1)]))
    inner = [sum(ex) / len(ex) for ex, sp in groups
             if sum(b - a for a, b in sp) > 300
             and min(a for a, _ in sp) < 200 and max(b for _, b in sp) > 700]

    left = min(h[1] for h in hlong)
    right = max(h[2] for h in hlong)
    xs = [left] + inner + [right]
    ys, virtual = fill_missing(cluster([h[0] for h in hlong]))
    return ys, xs, hl, virtual


def fill_missing(ys):
    """行境界の抜けを補う。

    表を横断する罫線が 1 本だけ描かれていないブロックがある (p4 右など)。
    行は等ピッチなので、間隔が基準ピッチの整数倍になっている箇所へ境界を補間する。
    放置すると見出しと 1 行目が同じ帯に入り、見出し文字が値へ流れ込む。
    """
    if len(ys) < 4:
        return ys, set()
    gaps = sorted(ys[i + 1] - ys[i] for i in range(len(ys) - 1))
    pitch = gaps[len(gaps) // 2]
    out, virtual = [ys[0]], set()
    for a, b in zip(ys, ys[1:]):
        k = int(round((b - a) / pitch))
        if k >= 2 and abs((b - a) / k - pitch) < 0.6:
            add = [a + (b - a) * j / k for j in range(1, k)]
            out += add
            virtual.update(add)
        out.append(b)
    return out, virtual


def has_hline(hl, virtual, y, x0, x1, ytol=1.5):
    """[x0,x1] を覆う水平罫線が y にあるか (縦結合セルの範囲判定に使う)。

    fill_missing が補間した境界は「線が引かれていないだけで行境界ではある」ので
    罫線があるものとして扱う。そうしないと見出しと 1 行目が同じ結合セルになる。
    """
    if y in virtual:
        return True
    need0, need1 = x0 + 1.0, x1 - 1.0
    if need1 <= need0:
        return True
    return any(abs(ly - y) <= ytol and lx0 <= need0 and lx1 >= need1 for ly, lx0, lx1 in hl)


def cells(page, ys, xs, hl, virtual):
    """単語をセルへ入れ、縦結合セルの値を span 内の全行へ複製する。

    杭径・肉厚仕様・肉厚・杭断面積は鋼管厚の行数ぶん縦結合されている。
    結合セルの文字は範囲の中央に置かれるので、罫線で span を確定してから複製する。"""
    grid_ = [[[] for _ in range(len(xs) - 1)] for _ in range(len(ys) - 1)]
    for w in page.get_text("words"):
        cx, cy = (w[0] + w[2]) / 2, (w[1] + w[3]) / 2
        if not (ys[0] <= cy <= ys[-1] and xs[0] <= cx <= xs[-1]):
            continue
        r = next((i for i in range(len(ys) - 1) if ys[i] <= cy < ys[i + 1]), None)
        c = next((j for j in range(len(xs) - 1) if xs[j] <= cx < xs[j + 1]), None)
        if r is not None and c is not None:
            grid_[r][c].append((w[1], w[0], w[4]))
    text = [["".join(t for _, _, t in sorted(cell)).replace(" ", "") for cell in row]
            for row in grid_]

    nrow, ncol = len(ys) - 1, len(xs) - 1
    out = [[""] * ncol for _ in range(nrow)]
    for c in range(ncol):
        r = 0
        while r < nrow:
            span = [r]
            rr = r + 1
            while rr < nrow and not has_hline(hl, virtual, ys[rr], xs[c], xs[c + 1]):
                span.append(rr)
                rr += 1
            val = "".join(text[k][c] for k in span)
            for k in span:
                out[k][c] = val
            r = rr
    return out


def normalize_thickness(s):
    """肉厚仕様。縦書きセルなので字が 1 文字ずつ別単語になり、
    同じ群がページをまたぐと末尾の「型」が落ちることがある (φ450 T=85 で実際に発生)。
    正しく直せているかは、径ごとに 標準型 < 厚型 < 特厚型 の順で肉厚が増えることで検算する。"""
    s = (s or "").strip()
    return {"標準": "標準型", "厚": "厚型", "特厚": "特厚型"}.get(s, s)


def parse_block(page, xmin, xmax, columns, corrosion=0.0):
    ys, xs, hl, virtual = grid(page, xmin, xmax)
    assert len(xs) - 1 == len(columns), f"列数が想定と違う: {len(xs) - 1} (期待 {len(columns)})"

    # 見出しは行境界の罫線が列によって欠けており、そのままだと見出し文字が
    # 1 行目のセルへ流れ込む (杭単位重量の列で実際に起きる)。
    # 1 行 1 値が保証される列 (コンクリート断面積) が最初に数値になる行を探し、
    # そこから下だけでグリッドを作り直す。
    first = cells(page, ys, xs, hl, virtual)
    ci = columns.index("Ac")
    top = next((i for i, row in enumerate(first) if re.fullmatch(r"\d+", row[ci])), None)
    assert top is not None, "データ行が見つからない"
    ys = ys[top:]

    # 杭径は列としては取り込めないが、印字はされているので候補一覧として使う
    printed = sorted({float(w[4]) for w in page.get_text("words")
                      if xs[0] <= w[0] <= xs[1] and ys[0] <= w[1] <= ys[-1]
                      and re.fullmatch(r"\d{3,4}", w[4])})
    assert printed, "杭径の印字が見つからない"

    rows = []
    for row in cells(page, ys, xs, hl, virtual):
        c = dict(zip(columns, row))
        # 見出し行は数値列が埋まらないので落ちる
        if not all(re.fullmatch(r"\d+", c[k]) for k in ("D", "Ts", "Ao", "Ac", "As", "Ae", "Ie")):
            continue
        thick = normalize_thickness(c["ThickType"])
        T = float(c["T"])
        Ao = float(c["Ao"]) * 100.0                 # cm2 -> mm2
        # 杭径の列は左端の罫線が無く取り込めないので Ao から解く。
        # 腐食代 c があると外径が 2c 縮むので Ao = π(T−c)(D−T−c)。
        D = float(c["D"])
        assert D in printed, f"杭径 {D} が印字一覧 {printed} に無い"
        rows.append(dict(
            D=D, ThickType=thick, T=T, Ts=float(c["Ts"]),
            Ao=Ao, Ac=float(c["Ac"]) * 100.0, As=float(c["As"]) * 100.0,
            Ae=float(c["Ae"]) * 100.0, Ie=float(c["Ie"]) * 1e4,
            Ml=float(c["Ml"]), Ma=float(c["Ma"]), Mu=float(c["Mu"]),
            Ql=float(c["Ql"]), Qa=float(c["Qa"]),
            Weight=float(c["Weight"]) if "Weight" in columns else 0.0,
        ))

    return rows


def parse(doc):
    plain, corroded = [], []
    for pageno, marks in PAGES:
        page = doc[pageno - 1]
        for (xmin, xmax), mark in zip(BLOCK_X, marks):
            cols = COLUMNS if mark == "0" else COLUMNS_CORRODED
            rows = parse_block(page, xmin, xmax, cols, 0.0 if mark == "0" else 1.0)
            (plain if mark == "0" else corroded).extend(rows)
    return plain, corroded


def key(r):
    return (r["D"], r["ThickType"], r["T"], r["Ts"])


def verify(plain):
    """腐食代 0mm の断面諸元を理論式と突合する。

    肉厚 T は鋼管 + コンクリートの全肉厚、鋼管厚が ts。
      Ao = π/4 (D² − (D−2T)²)
      As = π/4 (D² − (D−2ts)²)          鋼管は外側
      Ac = Ao − As
      Ae = Ac + (Es/Ec) As              換算断面積 (鋼管をコンクリートに換算)
    """
    n = ES / EC
    # カタログは cm² / cm⁴ 単位で丸めて印字しているので、許容は
    # 「印字単位の半分 + わずかな相対分」とする (74cm² のような小さい値では
    #  相対 0.15% は丸め幅より狭くなってしまう)。
    unit = {"Ao": 100.0, "Ac": 100.0, "As": 100.0, "Ae": 100.0, "Ie": 1e4}
    worst = {k: 0.0 for k in unit}
    for r in plain:
        D, T, ts = r["D"], r["T"], r["Ts"]
        ao = math.pi / 4 * (D ** 2 - (D - 2 * T) ** 2)
        as_ = math.pi / 4 * (D ** 2 - (D - 2 * ts) ** 2)
        ac = ao - as_
        # 換算断面 2 次モーメント: 鋼管は外殻、コンクリートはその内側の中空円
        is_ = math.pi / 64 * (D ** 4 - (D - 2 * ts) ** 4)
        ic = math.pi / 64 * ((D - 2 * ts) ** 4 - (D - 2 * T) ** 4)
        calc = {"Ao": ao, "As": as_, "Ac": ac, "Ae": ac + n * as_, "Ie": ic + n * is_}
        for k, v in calc.items():
            diff = abs(v - r[k])
            worst[k] = max(worst[k], diff / r[k])
            # Ie だけは印字の丸めを超える系統的なずれがある (下のコメント参照)
            limit = 0.5 * unit[k] + r[k] * (3e-3 if k == "Ie" else 1e-4)
            assert diff <= limit,                 f"φ{D:.0f} {r['ThickType']} T{T:.0f} ts{ts:.0f} の {k}: 印字 {r[k]:,.0f} / 計算 {v:,.0f}"
        # 杭断面積は内訳の和と一致する (誤植検出に効く)
        assert abs(r["Ao"] - (r["Ac"] + r["As"])) <= 1.0 * unit["Ao"],             f"φ{D:.0f} {r['ThickType']} ts{ts:.0f}: Ao {r['Ao']:,.0f} ≠ Ac+As {r['Ac'] + r['As']:,.0f}"
    for k in ("Ao", "As", "Ac", "Ae", "Ie"):
        print(f"  {k:3} 最大相対差 {worst[k] * 100:6.3f}%")
    # Ie は ts が厚いほど計算値が印字を上回る (φ400 ts=20 で +0.22%、Ao/Ac/As/Ae は丸め以内)。
    # 鋼管の断面二次モーメントを薄肉近似 (π·ts·Dm³/8) で出しているためと見られる。
    # 本プログラムは EI を自前で積算するので取り込みには影響しないが、
    # カタログ Ie をそのまま使う場合はこの差を承知しておくこと。

    for r in plain:
        tag = f"φ{r['D']:.0f} {r['ThickType']} ts{r['Ts']:.0f}"
        assert r["Ml"] < r["Ma"] < r["Mu"], f"{tag}: 曲げの大小関係"
        assert r["Ql"] < r["Qa"], f"{tag}: せん断の大小関係"
        assert r["Ts"] < r["T"], f"{tag}: 鋼管厚が全肉厚以上"

    # 肉厚仕様の正規化が正しいか: 径ごとに 標準型 < 厚型 < 特厚型 の順で肉厚が増える
    order = {"標準型": 0, "厚型": 1, "特厚型": 2}
    by_dia = {}
    for r in plain:
        by_dia.setdefault(r["D"], {}).setdefault(r["ThickType"], set()).add(r["T"])
    for d, spec in by_dia.items():
        for name, tset in spec.items():
            assert name in order, f"φ{d:.0f}: 未知の肉厚仕様 {name}"
            assert len(tset) == 1, f"φ{d:.0f} {name}: 肉厚が 1 つに定まらない {sorted(tset)}"
        seq = sorted((order[k], next(iter(v))) for k, v in spec.items())
        assert all(a[1] < b[1] for a, b in zip(seq, seq[1:])),             f"φ{d:.0f}: 肉厚仕様の順に肉厚が増えていない {seq}"

def verify_corroded(plain, corroded):
    """腐食代 1mm の表が「外径を 2mm 縮めた鋼管」になっていることを確かめる。

    ここで一致すれば、この表を本プログラムの腐食モデルの検証データとして使える。
    """
    n = ES / EC
    by_key = {key(r): r for r in plain}
    worst = 0.0
    matched = 0
    for c in corroded:
        p = by_key.get(key(c))
        if p is None:
            continue
        matched += 1
        D, T, ts = c["D"], c["T"], c["Ts"]
        dCor = D - 2.0                      # 腐食代 1mm → 外径が 2mm 縮む
        as_ = math.pi / 4 * (dCor ** 2 - (D - 2 * ts) ** 2)
        rel = abs(as_ - c["As"]) / c["As"]
        worst = max(worst, rel)
        assert rel <= 0.01, \
            f"φ{D:.0f} ts{ts:.0f}: 腐食後鋼管断面積 印字 {c['As']:,.0f} / 計算 {as_:,.0f}"
    print(f"  腐食代1mm 表と対応が付いた行 {matched} / {len(corroded)}、"
          f"腐食後 As の最大相対差 {worst * 100:.3f}%")

    # 杭断面積は内訳の和と一致するはず。合わない行はカタログの印字誤り。
    typo = [c for c in corroded if abs(c["Ao"] - (c["Ac"] + c["As"])) > 150.0]
    for c in typo:
        # CSV へ出すのでカンマ区切りは使わない
        c["AoNote"] = (f"誤植: カタログ印字 Ao={c['Ao'] / 100:.0f}cm2 "
                       f"(正 Ac+As={(c['Ac'] + c['As']) / 100:.0f}cm2)")
        c["Ao"] = c["Ac"] + c["As"]
    if typo:
        g = sorted({(t["D"], t["ThickType"]) for t in typo})
        print(f"  誤植 {len(typo)} 行 (腐食代1mm の Ao): {g} → Ac+As で置き換え")
    return by_key


def to_record(no, r, c):
    """既存 pile_library_SC.csv と同じ 28 列 + 参照用のカタログ値。"""
    name = f"Hi-SC105-{r['D']:.0f}-{r['ThickType']}-{r['T']:.0f}-{r['Ts']:.0f}"
    rec = {
        "No.": no, "標準特厚": r["ThickType"], "種": "",
        "typ": name, "杭種": "SC",
        "D": f"{r['D']:.0f}", "t": f"{r['T']:.0f}", "Fc": f"{FC:.0f}",
        "fc_": f"{FC_ALLOW_COMP_SHORT:.0f}", "fbc": "0", "sigma_e": "0",
        "Ec": f"{EC:.0f}",
        "ap": 0, "dp": 0, "ftp": 0, "sigma_pu": 0, "Ep": 0,
        "has_reinf": "false", "nr": 0, "r_designation": 0, "ag": 0, "dr": 0,
        "ftr": 0, "Er": 0,
        "ts": f"{r['Ts']:.0f}", "fts": f"{FTS:.0f}", "Es": f"{ES:.0f}", "ps_sigma_y": 0,
        # ここから先は既存ローダー (列番号で読む) が触らない参照列
        "PipeGrade": PIPE_GRADE,
        "CatalogAo": f"{r['Ao']:.0f}", "CatalogAc": f"{r['Ac']:.0f}",
        "CatalogAs": f"{r['As']:.0f}", "CatalogAe": f"{r['Ae']:.0f}",
        "CatalogIe": f"{r['Ie']:.0f}",
        "CatalogMl": r["Ml"], "CatalogMa": r["Ma"], "CatalogMu": r["Mu"],
        "CatalogQl": r["Ql"], "CatalogQa": r["Qa"], "CatalogWeight": r["Weight"],
    }
    # 腐食代 1mm (腐食モデルの検証用)
    rec.update({
        "Corr1Ao": f"{c['Ao']:.0f}" if c else "",
        "Corr1Ac": f"{c['Ac']:.0f}" if c else "",
        "Corr1As": f"{c['As']:.0f}" if c else "",
        "Corr1Ae": f"{c['Ae']:.0f}" if c else "",
        "Corr1Ie": f"{c['Ie']:.0f}" if c else "",
        "Corr1Mu": c["Mu"] if c else "",
        "Corr1Weight": c["Weight"] if c else "",
        "Corr1Note": (c or {}).get("AoNote", ""),
    })
    return rec


def main():
    doc = fitz.open(PDF)
    plain, corroded = parse(doc)
    print(f"腐食代0mm {len(plain)} 行 / 腐食代1mm {len(corroded)} 行")
    print(f"  径 {sorted({int(r['D']) for r in plain})}")
    print(f"  肉厚仕様 {sorted({r['ThickType'] for r in plain})}  "
          f"鋼管厚 {min(r['Ts'] for r in plain):.0f}〜{max(r['Ts'] for r in plain):.0f}mm")

    verify(plain)
    corr = {key(c): c for c in corroded}
    verify_corroded(plain, corroded)

    records = [to_record(2000 + i, r, corr.get(key(r)))
               for i, r in enumerate(plain, start=1)]
    names = [r["typ"] for r in records]
    assert len(set(names)) == len(names), \
        f"製品名が重複: {[n for n in names if names.count(n) > 1][:3]}"

    with io.open("pile_library_SC_HISC105.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(records[0].keys()))
        w.writeheader()
        w.writerows(records)
    print(f"{len(records)} 行 -> pile_library_SC_HISC105.csv")


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
