# -*- coding: utf-8 -*-
import re

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8-sig', errors='ignore') as f:
    content = f.read()

# MessageBox内の文字化けを修正
content = content.replace('vZo̓EBhE̕\ɃG[܂', '計算結果ウィンドウの表示中にエラーが発生しました')
content = content.replace('ǂݍݒfށH', '読み込み済みデータを削除しますか？')
content = content.replace('mF', '確認')
content = content.replace('ۑ܂H', '保存しますか？')
content = content.replace('ۑɎs܂', '保存に失敗しました')
content = content.replace('ۑ܂', '保存しました')
content = content.replace('YzuSf[^폜܂', '杭配置全データを削除します')
content = content.replace('I肳ꂽf[^폜܂', '選択されたデータを削除します')
content = content.replace('IڂI肵Ă', '選択節点を選択してください')
content = content.replace('YQQڂȏKv', '群杭2節点以上必要です')
content = content.replace('QYYnw1wȏKv', '群杭沈下地盤層1層以上必要です')
content = content.replace('ǂݍݓe폜܂B悵H', '読み込み済み内容を削除します。よろしいですか？')

# その他の一般的な文字化け
content = re.sub(r'G\[', 'エラー', content)
content = re.sub(r'x', '警告', content)
content = re.sub(r'', '情報', content)

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8-sig') as f:
    f.write(content)

print('Fixed all mojibake')
