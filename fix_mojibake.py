# -*- coding: utf-8 -*-
import re

# 現在のファイルを読み込む
with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8-sig', errors='ignore') as f:
    current = f.read()

# 以前のファイルを読み込む
with open('old_vm.cs', 'r', encoding='utf-8') as f:
    old = f.read()

# MessageBox.Showの文字化け修正パターン
message_box_patterns = [
    ('グラフウィンドウの表示中にエラーが発生しました', r'・ｽO・ｽ・ｽ・ｽt・ｽE・ｽB・ｽ・ｽ・ｽh・ｽE・ｽﾌ表・ｽ・ｽ・ｽ・ｽ・ｽﾉエ・ｽ・ｽ・ｽ\[・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽﾜゑｿｽ・ｽ・ｽ'),
    ('エラー', r'・ｽG・ｽ・ｽ・ｽ\['),
    ('確認', r'・ｽm・ｽF'),
    ('保存', r'・ｽﾛ存'),
    ('読込', r'・ｽﾇみ込'),
    ('削除', r'・ｽ・ｽ・ｽ・ｽ'),
    ('選択', r'・ｽI・ｽ・ｽ'),
    ('解析', r'・ｽ・ｽﾍ'),
    ('情報', r'・ｽ・ｽ・ｽ・ｽ'),
    ('警告', r'・ｽx・ｽ・ｽ'),
]

# パターン置換
for correct, garbled in message_box_patterns:
    current = re.sub(garbled, correct, current)

# コメント部分の修正
comment_patterns = [
    ('フィールド宣言', r'・ｽt・ｽB・ｽ\[・ｽ・ｽ・ｽh・ｽﾝ宣'),
    ('コマンド状態一括更新ヘルパ', r'・ｽR・ｽ}・ｽ・ｽ・ｽh・ｽ・ｽﾔ一括・ｽX・ｽV・ｽw・ｽ・ｽ・ｽp'),
    ('解析完了後', r'・ｽ・ｽﾍ奇ｿｽ・ｽ・ｽ・ｽ・ｽ'),
    ('解析結果', r'・ｽ・ｽﾍ鯉ｿｽ'),
    ('テーブル', r'・ｽe・ｽ\[・ｽu・ｽ・ｽ'),
    ('群杭', r'・ｽQ・ｽY'),
    ('沈下', r'・ｽ・ｽ・ｽ・ｽ'),
    ('根入部', r'・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ'),
    ('地盤', r'・ｽn・ｽﾔ'),
    ('杭配置', r'・ｽY・ｽz・ｽu'),
]

for correct, garbled in comment_patterns:
    current = re.sub(garbled, correct, current)

# ファイルに書き込む
with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8-sig') as f:
    f.write(current)

print('Fixed mojibake patterns')
