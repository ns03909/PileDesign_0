# -*- coding: utf-8 -*-

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8-sig') as f:
    content = f.read()

# 具体的な文字化けパターンを修正
content = content.replace('LoadingType == "CӋ`"', 'LoadingType == "任意矩形"')
content = content.replace('LoadingType == "ʏ\\"', 'LoadingType == "通常荷重"')

with open('Graphics_r1/ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8-sig') as f:
    f.write(content)

print('Fixed LoadingType strings')
