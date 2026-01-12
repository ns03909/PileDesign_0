# -*- coding: utf-8 -*-
import re

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8-sig') as f:
    content = f.read()

# MessageBox.Show内の文字化けを広範囲に修正
replacements = [
    # 具体的なパターン
    (r'LoadingType == "任意矩形"', r'LoadingType == "任意矩形"'),  # すでに正しい
    (r'LoadingType == "[^"]*通常[^"]*"', r'LoadingType == "通常荷重"'),  # 通常を含むパターン
    (r'MessageBox\.Show\(\$?"[^"]*計算結果[^"]*', lambda m: m.group(0).split('"')[0] + '"計算結果ウィンドウの表示中にエラーが発生しました'),
    (r'MessageBox\.Show\(\$?"[^"]*テーブル[^"]*', lambda m: m.group(0).split('"')[0] + '"テーブルウィンドウの表示中にエラーが発生しました'),
]

# 基本的な置換
basic_replacements = {
    '読み込み済みデータを削除しますか？': '読み込み済みデータを削除しますか？',
    '杭配置前の全データを削除': '杭配置前の全データを削除',
    '選択された節点を削除': '選択された節点を削除',
    '選択された杭が見つかりません。': '選択された杭が見つかりません。',
    '群杭2節点以上必要です': '群杭2節点以上必要です',
}

# まず文字化け文字を含むMessageBoxを全て見つけて修正
# 文字化けパターン: \x80以上の文字を含む文字列
content = re.sub(
    r'(LoadingType == ")[^"]*\\[^"]*(")',
    r'\1通常荷重\2',
    content
)

# MessageBox内の文字化けを修正（エスケープシーケンスエラーを引き起こす\を含む）
content = re.sub(
    r'MessageBox\.Show\([^)]*\\[^)]*\)',
    lambda m: m.group(0).replace('\\', ''),
    content
)

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8-sig') as f:
    f.write(content)

print('Fixed remaining errors')
