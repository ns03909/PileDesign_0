# -*- coding: utf-8 -*-
import codecs
import re

file_path = r"c:\Users\keisu\source\repos\PileDesign_0\Graphics_r1\ViewModels\MainWindowViewModel.cs"

# Read the file with UTF-8 encoding
with codecs.open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace remaining garbled patterns using regex
# Pattern 1: Lines that start with /// and contain garbled text about debounce
pattern1 = r'/// ・ｽZ情報ﾔに包ｿｽ根入部ﾄばゑｿｽﾄゑｿｽ・ｽA・ｽﾅ鯉ｿｽﾌ呼び出根入部情報闔橸ｿｽﾔ鯉ｿｽ・ｽ1・ｽｾゑｿｽ情報s根入部ﾜゑｿｽ・ｽB'
replacement1 = '/// 短時間に複数回呼ばれても、最後の呼び出しだけが最終的に1回だけ実行されます。'
content = content.replace(pattern1, replacement1)

# Pattern 2: SoilPiles generation immediate
pattern2 = r'SoilPiles ・ｽﾌ撰ｿｽ情報ｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs情報ﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE情報X情報X・ｽL・ｽb・ｽv・ｽj・ｽB'
replacement2 = 'SoilPiles の生成を即座に実行します（デバウンススキップ）。'
content = content.replace(pattern2, replacement2)

# Pattern 3: Window update immediate
pattern3 = r'・ｽE・ｽB情報h・ｽE・ｽX・ｽV・ｽｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs情報ﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE情報X情報X・ｽL・ｽb・ｽv・ｽj・ｽB'
replacement3 = 'ウィンドウ更新を即座に実行します（デバウンススキップ）。'
content = content.replace(pattern3, replacement3)

# Write back to file with UTF-8 encoding
with codecs.open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)

print("Final garbled text fixed successfully!")
print(f"Pattern 1 replaced: {pattern1 in content}")
print(f"Pattern 2 replaced: {pattern2 in content}")
print(f"Pattern 3 replaced: {pattern3 in content}")
