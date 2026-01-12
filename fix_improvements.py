# -*- coding: utf-8 -*-

# 現在のファイルを読み込む
with open('Graphics_r1/ViewModels/MainWindowViewModel.Improvements.cs', 'r', encoding='utf-8-sig') as f:
    current = f.read()

# コメントの文字化けを修正
comment_fixes = {
    '// ������ partial MainWindowViewModel �ɋ@�\�����S�ɒǉ�����g�� partial': '// 追加的な partial MainWindowViewModel に機能を完全に追加するための partial',
    '// �L���b�V��: �R�}���h��񋓂��� PropertyInfo �̃L���b�V���i���˃R�X�g�팸�j': '// キャッシュ: コマンド列挙に使う PropertyInfo のキャッシュ（反復コスト削減）',
    '// Undo �X�i�b�v�V���b�g�d���ۑ��h�~�̂��߂̃n�b�V��': '// Undo スナップショット重複保存防止のためのハッシュ',
    '// �����ԏ����̃L�����Z���p': '// 長時間処理のキャンセル用',
    '// �y�ʂȃV���A���C�Y�I�v�V�����iUndo ����p: �Q�ƕۑ��������j': '// 軽量なシリアライズオプション（Undo 専用: 参照保存しない）',
    '// --- Public helper: ����R�}���h�L���b�V���쐬�i�C�ӌĂяo���j ---': '// --- Public helper: 全コマンドキャッシュ作成（任意呼び出し） ---',
    '// --- Optimized version of RaiseAllCommandsCanExecute ---': '// --- Optimized version of RaiseAllCommandsCanExecute ---',
    '// �����̌Ăяo���ӏ������̃��\�b�h�ɍ����ւ��邱�ƂŔ��ˉ񐔂�}���܂��B': '// 一度だけ呼び出す想定ですが、このメソッドに置き換えることで反復回数を減らします。',
    '// ���񂾂��v���p�e�B���X�g�����W': '// 一度だけプロパティリストを列挙',
    '// �R�}���h�����݂��Ȃ��ꍇ�͌x��': '// コマンドが見つからない場合は警告',
    '// ���҃��\�b�h���擾': '// 通知メソッドを取得',
    '// --- �X�i�b�v�V���b�g�n�b�V���󂩂�d���`�F�b�N���� ---': '// --- スナップショットハッシュから重複チェック付き ---',
    '// �V���A���C�Y�����X�i�b�v�V���b�g����SHA256�n�b�V���𐶐�': '// シリアライズしたスナップショットからSHA256ハッシュを生成',
    '// ���O�Ɏ擾�����n�b�V���Ɨ����Ȃ�d���ۑ����X�L�b�v': '// 以前に取得したハッシュと同一なら重複保存をスキップ',
    '// ���グ': '// 簡略',
    '// �Ō�̃n�b�V���𕫑�': '// 最後のハッシュを保存',
    '// --- �����Ԓ�~�̃L�����Z�� ---': '// --- 長時間中断のキャンセル ---',
    '// �L�����Z�� CTS �̃N���A': '// キャンセル CTS のクリア',
    '// Dispose �p�^�[��': '// Dispose パターン',
    '// �����̃��\�[�X�𔼊s': '// 非管理リソースを破棄',
    '// �L�����Z�����Ăяo��': '// キャンセルを呼び出す',
    '// �����̃��\�[�X���s': '// 管理リソース破棄',
}

for old_text, new_text in comment_fixes.items():
    current = current.replace(old_text, new_text)

# ファイルに書き込む
with open('Graphics_r1/ViewModels/MainWindowViewModel.Improvements.cs', 'w', encoding='utf-8-sig') as f:
    f.write(current)

print('Fixed MainWindowViewModel.Improvements.cs comments')
