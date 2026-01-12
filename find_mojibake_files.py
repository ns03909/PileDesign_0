# -*- coding: utf-8 -*-
import os
import glob

def find_mojibake_files(root_dir):
    """文字化けを含むファイルを検索"""
    mojibake_files = []

    # .csと.xamlファイルを検索
    patterns = ['**/*.cs', '**/*.xaml']

    for pattern in patterns:
        for file_path in glob.glob(os.path.join(root_dir, pattern), recursive=True):
            # objとbinディレクトリは除外
            if '\\obj\\' in file_path or '\\bin\\' in file_path:
                continue

            try:
                with open(file_path, 'r', encoding='utf-8-sig', errors='ignore') as f:
                    content = f.read()
                    # 文字化けパターンを検出
                    if '・ｽ' in content or '�' in content:
                        mojibake_files.append(file_path)
            except Exception as e:
                print(f"Error reading {file_path}: {e}")

    return mojibake_files

# Graphics_r1ディレクトリを検索
root = 'Graphics_r1'
files = find_mojibake_files(root)

print(f"Found {len(files)} files with mojibake:")
for f in files[:30]:  # 最初の30ファイルを表示
    print(f"  {f}")

if len(files) > 30:
    print(f"  ... and {len(files) - 30} more files")
