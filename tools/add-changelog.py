#!/usr/bin/env python3
"""CHANGELOG.md の [Unreleased] に項目を足す。

    python tools/add-changelog.py <項目を書いたファイル>

なぜスクリプトにするか
----------------------
手で書き換えていて、CHANGELOG.md を **2 回空にした**。どちらも同じ形:

    open(p, 'wb').write(open(p, 'rb').read().replace(b'\\r\\n', b'\\n'))

Python は呼び出す側の ``open(p, 'wb')`` を先に評価する。読む前にファイルが
切り詰められ、空の内容を書き戻す。2 文に分ければ起きない。

そのうえ ``git commit`` は通ってしまう。CHANGELOG は他のどのテストも見ないので、
気づけるのは commit の "N deletions" を目で見たときだけ。道具にして塞ぐ。
"""
import io
import shutil
import sys
from pathlib import Path

MARKER = "\n## [1.0.33-beta] — 2026-09-12"   # [Unreleased] の直後にある節


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    entry_path = Path(sys.argv[1])
    changelog = Path(__file__).resolve().parent.parent / "CHANGELOG.md"

    # 1. 読む (書く前に、すべて読み終える)
    before = io.open(changelog, encoding="utf-8-sig").read()
    entry = io.open(entry_path, encoding="utf-8-sig").read()

    if before.count(MARKER) != 1:
        print(f"NG: 差し込む目印が {before.count(MARKER)} 個です: {MARKER.strip()}")
        return 1
    if not entry.strip():
        print("NG: 追記する内容が空です")
        return 1

    after = before.replace(MARKER, entry + MARKER)

    # 2. 減っていないことを確かめる
    if len(after) <= len(before):
        print(f"NG: 追記後のほうが短くなります ({len(before)} → {len(after)} 文字)")
        return 1

    # 3. 控えを取ってから書く
    shutil.copy2(changelog, str(changelog) + ".bak")
    io.open(changelog, "w", encoding="utf-8-sig", newline="").write(
        after.replace("\r\n", "\n"))

    added = after.count("\n") - before.count("\n")
    print(f"OK: {added} 行を追記しました (控え: CHANGELOG.md.bak)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
