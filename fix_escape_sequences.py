# -*- coding: utf-8 -*-
import re

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()

# エラー行を修正
errors_to_fix = {
    1332: ('LoadingType == "ʏ\\"', 'LoadingType == "通常荷重"'),
    1768: ('MessageBox.Show($"vZo̓EBhE̕\ɃG[܂', 'MessageBox.Show($"計算結果ウィンドウの表示中にエラーが発生しました'),
    1783: ('MessageBox.Show($"e[uEBhE̕\ɃG[', 'MessageBox.Show($"テーブルウィンドウの表示中にエラーが発生しました'),
    1819: ('MessageBox.Show("ǂݍݒfށH", "mF"', 'MessageBox.Show("読み込み済みデータを削除しますか？", "確認"'),
    1946: ('MessageBox.Show("YzuOSf[^폜"', 'MessageBox.Show("杭配置前の全データを削除"'),
    1994: ('MessageBox.Show("I肳ꂽڂ폜', 'MessageBox.Show("選択された節点を削除'),
    2015: ('MessageBox.Show("I肳ꂽY삪܂B"', 'MessageBox.Show("選択された杭が見つかりません。"'),
    2116: ('MessageBox.Show("X^Cu[̋ߎ"', 'MessageBox.Show("スタイラブーナーの近似"'),
    2659: ('MessageBox.Show("YQQڂȏKv', 'MessageBox.Show("群杭2節点以上必要です'),
}

for line_num, (old_text, new_text) in errors_to_fix.items():
    idx = line_num - 1  # 0-indexed
    if idx < len(lines):
        lines[idx] = lines[idx].replace(old_text, new_text)

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8-sig') as f:
    f.writelines(lines)

print('Fixed escape sequence errors')
