# -*- coding: utf-8 -*-
import codecs

file_path = r"c:\Users\keisu\source\repos\PileDesign_0\Graphics_r1\ViewModels\MainWindowViewModel.cs"

# Read file with UTF-8 BOM
with codecs.open(file_path, 'r', encoding='utf-8-sig') as f:
    content = f.read()

# Fix the remaining corrupted patterns that have "情報" and "根入部" mixed in
specific_fixes = [
    ('・ｽZ情報ﾔに包ｿｽ根入部ﾄばゑｿｽﾄゑｿｽ・ｽA・ｽﾅ鯉ｿｽﾌ呼び出根入部情報闔橸ｿｽﾔ鯉ｿｽ・ｽ1・ｽｾゑｿｽ情報s根入部ﾜゑｿｽ・ｽB', '短時間に複数回呼ばれても、最後の呼び出しだけが最終的に1回だけ実行されます。'),
    ('SoilPiles ・ｽﾌ撰ｿｽ情報ｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs情報ﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE情報X情報X・ｽL・ｽb・ｽv・ｽj・ｽB', 'SoilPiles の生成を即座に実行します（デバウンススキップ）。'),
    ('・ｽE・ｽB情報h・ｽE・ｽX・ｽV・ｽｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs情報ﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE情報X情報X・ｽL・ｽb・ｽv・ｽj・ｽB', 'ウィンドウ更新を即座に実行します（デバウンススキップ）。'),
]

for old, new in specific_fixes:
    if old in content:
        print(f"Fixing: {old[:40]}...")
        content = content.replace(old, new)

# Write back
with codecs.open(file_path, 'w', encoding='utf-8-sig') as f:
    f.write(content)

print("Final cleanup completed!")

# Check for remaining garbled text
import re
garbled_pattern = r'・ｽ'
remaining = len(re.findall(garbled_pattern, content))
print(f"Remaining garbled characters: {remaining}")
