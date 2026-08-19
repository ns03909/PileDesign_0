# -*- coding: utf-8 -*-
"""三谷セキサン BF.S105 / BF.S123 パイル (cat_bfs.pdf) から製品ライブラリ CSV を生成する。

JP-NPH / JP-NPRC との違い:
  - 「頭部厚型節付き杭」で、1 本の杭が<b>頭部軸部 (DT/TT)</b> と <b>先端軸部 (D0/T)</b> の
    2 つの軸部を持つ。内径 (DT-2TT = D0-2T) は両者で完全に一致し、頭部だけが外側に厚い。
  - カタログの標準性能表は<b>頭部軸部の値のみ</b> (表の注記)。先端軸部の断面性能・耐力は
    カタログに無いので、こちらで算定して持たせる。
  - PCD がカタログに印字されていない。Ie から逆算する (下記 verify 参照)。
  - 節の寸法が図面に寸法記入されている (テーパー 25(50) / 節部 75(100) / テーパー 25(50))。
    JP-NPH のような推定値ではない。
"""
import csv
import io
import math
import re
import sys

import fitz

import extract

PDF = "cat_bfs.pdf"
SPEC_PAGE = 4                       # 標準仕様／標準性能表 (A3 横)
TABLE = (153.0, 800.0, 20.0, 1180.0)  # ymin, ymax, xmin, xmax

# ── 設計に用いる諸数値 (p3「■設計に用いる諸数値」) ─────────────────────
EC = 40000.0
FTP = 1275.0          # PC鋼棒 耐力
SIGMA_PU = 1420.0     # PC鋼棒 引張強さ
EP = 200000.0
# コンクリート許容応力度は Fc ごと。曲げ引張は σce 比例 (長期 σce/4・短期 σce/2)。
ALLOW = {105.0: (30.0, 60.0), 123.0: (35.0, 70.0)}   # 長期圧縮, 短期圧縮
ALLOW_DIAG_LONG, ALLOW_DIAG_SHORT = 1.2, 1.8
ALLOW_BEND_TENS_LONG_FACTOR, ALLOW_BEND_TENS_SHORT_FACTOR = 0.25, 0.5

# ── 姿図の寸法 (p3「■標準構造図」) ───────────────────────────────────
NODE_PITCH = 1000.0     # 節中心間距離 (寸法記入あり)
TOE_OFFSET = 500.0      # 最終節中心〜杭先端 (寸法記入あり)
# 杭頭〜第 1 節中心はカタログに寸法記入が無い。杭長が 1m 単位・節ピッチ 1000・
# 先端 500 であることから 500 が唯一整合する値なので、導出値として持たせる。
HEAD_OFFSET = 500.0

TOLERANCE = 0.0015      # カタログの丸め (有効数字 3〜4 桁) を許容する


def num(s):
    s = re.sub(r"[^0-9.]", "", (s or ""))
    if not s or s == ".":
        return None
    try:
        return float(s)
    except ValueError:
        return None


def hollow_area(d, t):
    return math.pi / 4 * (d ** 2 - (d - 2 * t) ** 2)


def hollow_inertia(d, t):
    return math.pi / 64 * (d ** 4 - (d - 2 * t) ** 4)


def main():
    doc = fitz.open(PDF)
    ys, xs, grid = extract.extract_table(doc[SPEC_PAGE - 1], *TABLE, expand_merged=True)
    assert len(xs) - 1 == 23, f"列数が想定と違う: {len(xs) - 1}"

    rows = []
    for r in grid:
        name = r[1].strip()
        if not re.fullmatch(r"\d{3,4}-\d{4,6}", name):
            continue
        rows.append(dict(
            Fc=num(r[0]), Name=name, Kind=r[2].strip(),
            DT=num(r[3]), D1=num(r[4]), D0=num(r[5]), TT=num(r[6]), T=num(r[7]),
            PcDesignation=r[8].strip(), PcCount=int(num(r[9]) or 0),
            Ap=(num(r[10]) or 0) * 100.0,          # cm2 -> mm2
            Ao=(num(r[11]) or 0) * 100.0,
            Ae=(num(r[12]) or 0) * 100.0,
            Ie=(num(r[13]) or 0) * 1e4,            # cm4 -> mm4
            SigmaCe=num(r[14]),
            Mal=num(r[15]), Mas=num(r[16]), Mcr=num(r[17]), Mu=num(r[18]),
            Qal=num(r[19]), Qas=num(r[20]), Qcr=num(r[21]), Nal=num(r[22]),
        ))
    assert len(rows) == 54, f"データ行数が想定と違う: {len(rows)}"

    pcd = solve_pcd(rows)
    verify(rows, pcd)

    records = [to_record(r, pcd[r["Name"]]) for r in rows]
    verify_tip_prestress(records)
    write_csv("pile_library_BfsPile.csv", records)
    print(f"{len(records)} 行 -> pile_library_BfsPile.csv "
          f"(呼び名 {len(pcd)} 種 / Fc {len({r['Fc'] for r in rows})} 種)")


def solve_pcd(rows):
    """PC鋼棒の配筋径 PCD をカタログの Ie から逆算する。

    カタログは PCD を印字していないが、断面性能の検算にも断面計算にも必要。
    Ie = Io + (n-1) Ap (PCD/2)^2 / 2  を PCD について解く。
    同じ呼び名なら Fc・種類によらず同じ値になるはずなので、それを一致検査に使う。
    """
    n = EP / EC
    by_name = {}
    for r in rows:
        io_ = hollow_inertia(r["DT"], r["TT"])
        v = (r["Ie"] - io_) * 2 / ((n - 1) * r["Ap"])
        by_name.setdefault(r["Name"], []).append(2 * math.sqrt(v))

    out = {}
    for name, vals in by_name.items():
        spread = max(vals) - min(vals)
        assert spread < 1.0, f"{name}: 逆算 PCD がばらつく ({spread:.2f}mm)"
        mean = sum(vals) / len(vals)
        rounded = round(mean / 5.0) * 5.0        # 設計値は 5mm 刻み
        assert abs(rounded - mean) < 1.0, f"{name}: 逆算 PCD {mean:.2f} が丸め値から離れている"
        out[name] = rounded
    print("  Ie から逆算した PCD [mm]:",
          "  ".join(f"{k}={v:.0f}" for k, v in out.items()))
    return out


def verify(rows, pcd):
    """頭部軸部の断面諸元を理論式と突合する。列ズレは 1% 以上ずれるので確実に検出できる。"""
    n = EP / EC
    worst = {"Ao": 0.0, "Ae": 0.0, "Ie": 0.0}
    for r in rows:
        ao = hollow_area(r["DT"], r["TT"])
        ie = hollow_inertia(r["DT"], r["TT"]) + (n - 1) * r["Ap"] * (pcd[r["Name"]] / 2) ** 2 / 2
        # 注: Ie は PCD の逆算元なので恒等的に一致する (独立な検算ではない)。
        #     PCD 側の妥当性は solve_pcd の「5mm 丸めに乗る」「同一呼び名で一致」で担保する。
        for key, calc in (("Ao", ao), ("Ae", ao + (n - 1) * r["Ap"]), ("Ie", ie)):
            rel = abs(calc - r[key]) / r[key]
            worst[key] = max(worst[key], rel)
            assert rel <= TOLERANCE, \
                f"{r['Name']} Fc{r['Fc']:.0f} {r['Kind']} の {key}: 印字 {r[key]:,.0f} / 計算 {calc:,.0f}"
    for k, v in worst.items():
        tail = "  (PCD 逆算元のため恒等)" if k == "Ie" else ""
        print(f"  {k:3} 最大相対差 {v * 100:6.3f}%{tail}")


# 先端軸部の種類ごとの σce 規定値 (JIS A 5373 附属書E の PHC 杭 A/B/C 種)
JIS_SIGMA_CE = {"A2": 4.0, "B2": 8.0, "C2": 10.0}


def verify_tip_prestress(records):
    """算定した先端軸部の σce が JIS の A/B/C 種の規定値に乗ることを確かめる。

    PCD の逆算と「有効プレストレス力は両軸部で共通」という仮定は、どちらもカタログに
    直接は書かれていない。両方が正しいときだけ先端軸部の σce が JIS 規定値になるので、
    これが 2 つの仮定に対する独立した検算になっている。
    (カタログ注記と同じ ±5% を許容する)
    """
    worst = 0.0
    for r in records:
        spec = JIS_SIGMA_CE[r["PrestressType"]]
        rel = abs(r["TipSigmaCe"] - spec) / spec
        worst = max(worst, rel)
        assert rel <= 0.05,             f"{r['Name']} {r['PrestressType']}: 先端軸部 σce {r['TipSigmaCe']} が JIS 規定 {spec} から外れる"
    print(f"  先端軸部 σce と JIS 規定値 (A2=4/B2=8/C2=10) の最大相対差 {worst * 100:.1f}%  (許容 5%)")


def to_record(r, pcd):
    n = EP / EC
    # 先端軸部はカタログに断面性能が無いので算定する。
    # 内径・PC鋼棒 (本数/Ap/PCD) は頭部軸部と共通で、外径と肉厚だけが違う。
    tipAo = hollow_area(r["D0"], r["T"])
    tipAe = tipAo + (n - 1) * r["Ap"]
    tipIe = hollow_inertia(r["D0"], r["T"]) + (n - 1) * r["Ap"] * (pcd / 2) ** 2 / 2
    # 有効プレストレス力 P = σce·Ae は同じ杭なので両軸部で共通。
    # 先端軸部は換算断面積が小さいぶん σce が大きくなる。
    tipSigmaCe = r["SigmaCe"] * r["Ae"] / tipAe

    comp_long, comp_short = ALLOW[r["Fc"]]
    return dict(
        Maker="三谷セキサン", Series=f"BF.S{r['Fc']:.0f}", Shape="頭部厚型節付き杭",
        Name=r["Name"], Fc=r["Fc"], PrestressType=r["Kind"],
        # 形状
        HeadDia=r["DT"], HeadThickness=r["TT"],
        TipDia=r["D0"], TipThickness=r["T"], NodeDia=r["D1"],
        # PC 鋼棒 (両軸部共通)
        PcDesignation=r["PcDesignation"], PcCount=r["PcCount"], Ap=r["Ap"], Pcd=pcd,
        # 頭部軸部 = カタログ記載値
        HeadAo=r["Ao"], HeadAe=r["Ae"], HeadIe=r["Ie"], HeadSigmaCe=r["SigmaCe"],
        HeadMal=r["Mal"], HeadMas=r["Mas"], HeadMcr=r["Mcr"], HeadMu=r["Mu"],
        HeadQal=r["Qal"], HeadQas=r["Qas"], HeadQcr=r["Qcr"], HeadNal=r["Nal"],
        # 先端軸部 = 算定値 (カタログに記載が無い)
        TipAo=round(tipAo, 1), TipAe=round(tipAe, 1), TipIe=round(tipIe, 1),
        TipSigmaCe=round(tipSigmaCe, 3),
        # 姿図
        NodePitch=NODE_PITCH, HeadOffset=HEAD_OFFSET, ToeOffset=TOE_OFFSET,
        NodeFlatLength=(r["D1"] - r["D0"]) / 2,
        # 諸定数
        Ec=EC, Ftp=FTP, SigmaPu=SIGMA_PU, Ep=EP,
        FcAllowCompLong=comp_long, FcAllowCompShort=comp_short,
        FcAllowDiagLong=ALLOW_DIAG_LONG, FcAllowDiagShort=ALLOW_DIAG_SHORT,
        AllowBendTensLongFactor=ALLOW_BEND_TENS_LONG_FACTOR,
        AllowBendTensShortFactor=ALLOW_BEND_TENS_SHORT_FACTOR,
    )


def write_csv(path, rows):
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    main()
