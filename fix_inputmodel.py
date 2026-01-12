# -*- coding: utf-8 -*-

# 現在のファイルを読み込む
with open('Graphics_r1/Models/InputData/InputModel.cs', 'r', encoding='utf-8-sig') as f:
    current = f.read()

# 古いファイルを読み込む（使用しない、参照のみ）
# with open('InputModel_old.cs', 'r', encoding='shift-jis') as f:
#     old = f.read()

# コメントの文字化けを修正
comment_fixes = {
    '// �ǉ�': '// 追加',
    '// �� PropertyChanged �����΂���': '// ※ PropertyChanged が呼ばれる',
    '// �f�[�^�̕ۑ�': '// データの保存',
    '// �f�[�^�̓ǂݍ���': '// データの読み込み',
    '// �w�肳�ꂽ�t�@�C�������݂��܂���B': '// 指定されたファイルが見つかりません。',
    '// �t�@�C���̓��e���f�V���A���C�Y�ł��܂���ł����B': '// ファイルの内容をデシリアライズできませんでした。',
    '// MainWindowViewModel���Z�b�g': '// MainWindowViewModelを設定',
    '// �܂� System.Text.Json �Ŏ��s�i����ݒ�j': '// まず System.Text.Json で試行（現在設定）',
    '// �t�H�[���o�b�N: Newtonsoft.Json �ŎQ�ƃ��^�f�[�^�𕜌����ēǂݍ���': '// フォールバック: Newtonsoft.Json で参照メタデータを復元して読み込み',
    '// Newtonsoft �ɂ��f�V���A���C�Y�Ŏ��s���܂����B': '// Newtonsoft によるデシリアライズに失敗しました。',
    '// �ŏI�I�Ɏ��s�����猳�̗�O������œ�����': '// 最終的に失敗したら元の例外情報で投げる',
    '// �t�@�C���ǂݍ��݂Ɏ��s���܂����iSystem.Text.Json + Newtonsoft.Json �����Ŏ��s�j�B': '// ファイル読み込みに失敗しました（System.Text.Json + Newtonsoft.Json 両方で失敗）。',
    '// ��{�ݒ�': '// 基本設定',
    '// �׏d�P�[�X': '// 荷重ケース',
    '// �n�Տ���': '// 地盤情報',
    '// �Y�z�u': '// 杭配置',
    '// �Q�Y�̒������ݒ�': '// 群杭の沈下設定',
    '// �n���h���Ĕz��': '// ハンドラー配線',
    '// �񂲂Ƃ̃C�x���g�n���h��': '// 項目ごとのイベントハンドラ',
    '// �R���N�V�����̕ύX���Ǐo': '// コレクションの変更を監視',
}

for old_text, new_text in comment_fixes.items():
    current = current.replace(old_text, new_text)

# ファイルに書き込む
with open('Graphics_r1/Models/InputData/InputModel.cs', 'w', encoding='utf-8-sig') as f:
    f.write(current)

print('Fixed InputModel.cs comments')
