"""ジャパンパイル JP-NPH カタログ (nph.pdf) の表を罫線ベースで厳密抽出する。

方針:
  1. ページの描画要素から水平/垂直の罫線を集める
  2. 罫線交点で決まるセル格子を作る
  3. 各セルについて「その列でそのセル境界に罫線があるか」を調べ、
     縦結合 (merged) セルの範囲を確定する
  4. 単語をセル中心判定で割り当て、結合セルの値は span 内の全行へ複製する

日本語は正しく抽出できる (端末表示のみ化ける)。数値は千位が半角スペース区切りで
別単語になるため、同一セル内の単語は x 順に連結してからスペースを除去する。
"""
import fitz
import sys
import json


def collect_lines(page):
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


def cluster(vals, tol=1.2):
    """近接値をまとめて代表値のリストにする。"""
    out = []
    for v in sorted(vals):
        if out and v - out[-1][-1] <= tol:
            out[-1].append(v)
        else:
            out.append([v])
    return [sum(g) / len(g) for g in out]


def has_hline(hl, y, x0, x1, ytol=1.5):
    """[x0,x1] を覆う水平罫線が y にあるか。"""
    need0, need1 = x0 + 1.0, x1 - 1.0
    if need1 <= need0:
        return True
    for ly, lx0, lx1 in hl:
        if abs(ly - y) <= ytol and lx0 <= need0 and lx1 >= need1:
            return True
    return False


def has_vline(vl, x, y0, y1, xtol=1.5):
    need0, need1 = y0 + 1.0, y1 - 1.0
    if need1 <= need0:
        return True
    for lx, ly0, ly1 in vl:
        if abs(lx - x) <= xtol and ly0 <= need0 and ly1 >= need1:
            return True
    return False


def extract_table(page, ymin, ymax, xmin, xmax, expand_merged=True, split_unruled=False,
                  extra_hlines=()):
    """表を抽出する。

    expand_merged=True  : 縦結合セルの値を span 内の全行へ複製する (諸数値一覧表向け)
    expand_merged=False : 各セルの値をそのまま返す (縦結合が無い断面性能表向け)。
                          罫線が一部欠けている列で誤って結合と判定され、上の行の値が
                          下の行へ複製される事故を避ける。

    split_unruled=True  : 「行境界の罫線が無いだけで、実際には結合していない」列を救う。
                          span 内の各行にそれぞれ値があれば結合ではないと判断して
                          行ごとに切り分ける (1 つしか値が無ければ従来どおり複製)。
                          同じ値が連続する列 (肉厚仕様が全行「標準型」など) で
                          罫線が省略されるカタログ向け。既定 OFF なので既存の出力は変わらない。

    extra_hlines        : 「行境界ではあるが線が描かれていない」位置を補う。
                          表の全幅に渡る水平罫線として扱うので、行の切り出しにも
                          縦結合の範囲判定にも効く。見出しとデータ 1 行目の間の線が
                          省略されているカタログ (DAM105) 向け。既定は空。
    """
    hl, vl = collect_lines(page)
    # 罫線は「線の位置」だけでなく「線の伸びている範囲」でも絞る。
    # 垂直線を x だけで絞ると、同じページの別の表 (下方に置かれた表など) の
    # 垂直線が列境界として混入し、列がずれる。
    hl = [h for h in hl
          if ymin - 2 <= h[0] <= ymax + 2 and h[2] >= xmin - 2 and h[1] <= xmax + 2]

    vl = [v for v in vl
          if xmin - 2 <= v[0] <= xmax + 2 and v[2] >= ymin - 2 and v[1] <= ymax + 2]

    # 表の左右端は「実際に描かれている」水平罫線の到達範囲から決めるので、
    # 補った罫線を加える前に確定させておく。
    tableLeft = min((h[1] for h in hl), default=xmin)
    tableRight = max((h[2] for h in hl), default=xmax)
    hl += [(y, tableLeft, tableRight) for y in extra_hlines if ymin - 2 <= y <= ymax + 2]

    ys = [y for y in cluster([h[0] for h in hl]) if ymin - 2 <= y <= ymax + 2]
    xs = [x for x in cluster([v[0] for v in vl]) if xmin - 2 <= x <= xmax + 2]

    # 表の左右端は「水平罫線が実際に届いている範囲」から決める。
    # 指定した領域 (xmin/xmax) をそのまま端にすると、表の外側に余白がある場合に
    # 左端列が罫線に覆われていないと判定され、その列の行境界が検出できなくなる
    # (= 縦結合と誤認して製品グループを越えて値が連結する)。
    if not xs or xs[0] > tableLeft + 3:
        xs = [max(tableLeft, xmin)] + xs
    if xs[-1] < tableRight - 3:
        xs = xs + [min(tableRight, xmax)]

    nrow, ncol = len(ys) - 1, len(xs) - 1

    # 単語をセルへ
    cells = [[[] for _ in range(ncol)] for _ in range(nrow)]
    for w in page.get_text("words"):
        cx, cy = (w[0] + w[2]) / 2, (w[1] + w[3]) / 2
        if not (ymin <= cy <= ymax and xmin <= cx <= xmax):
            continue
        r = c = None
        for i in range(nrow):
            if ys[i] <= cy < ys[i + 1]:
                r = i
                break
        for j in range(ncol):
            if xs[j] <= cx < xs[j + 1]:
                c = j
                break
        if r is not None and c is not None:
            cells[r][c].append((w[0], w[4]))

    def cell_text(r, c):
        toks = sorted(cells[r][c])
        s = "".join(t for _, t in toks)
        return s.replace(" ", "").replace("　", "")

    if not expand_merged:
        return ys, xs, [[cell_text(r, c) for c in range(ncol)] for r in range(nrow)]

    # 縦結合の展開: 列 c で行境界 ys[i] に罫線が無ければ上の行と同一セル
    grid = [[None] * ncol for _ in range(nrow)]
    for c in range(ncol):
        x0, x1 = xs[c], xs[c + 1]
        r = 0
        while r < nrow:
            span = [r]
            rr = r + 1
            while rr < nrow and not has_hline(hl, ys[rr], x0, x1):
                span.append(rr)
                rr += 1
            texts = [cell_text(k, c) for k in span]
            if split_unruled and len(span) > 1 and all(texts):
                # 全行に値がある = 罫線が省略されているだけで結合ではない
                for k, t in zip(span, texts):
                    grid[k][c] = t
            else:
                val = "".join(texts)
                for k in span:
                    grid[k][c] = val
            r = rr
    return ys, xs, grid


if __name__ == "__main__":
    path = sys.argv[1]
    pageno = int(sys.argv[2])
    ymin, ymax, xmin, xmax = (float(v) for v in sys.argv[3:7])
    doc = fitz.open(path)
    ys, xs, grid = extract_table(doc[pageno - 1], ymin, ymax, xmin, xmax)
    print(json.dumps({"ys": ys, "xs": xs, "grid": grid}, ensure_ascii=False))
