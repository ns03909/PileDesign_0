# -*- coding: utf-8 -*-
import codecs

file_path = r"c:\Users\keisu\source\repos\PileDesign_0\Graphics_r1\ViewModels\MainWindowViewModel.cs"

# Try different encodings
encodings_to_try = ['utf-8-sig', 'utf-8', 'shift_jis', 'cp932', 'latin1']

content = None
used_encoding = None

for encoding in encodings_to_try:
    try:
        with codecs.open(file_path, 'r', encoding=encoding) as f:
            content = f.read()
        used_encoding = encoding
        print(f"Successfully read file with encoding: {encoding}")
        break
    except Exception as e:
        print(f"Failed with {encoding}: {str(e)[:50]}")
        continue

if content is None:
    print("Could not read file with any encoding!")
    exit(1)

# Define ALL replacements - specific patterns first, then general ones
replacements = [
    # Very specific patterns
    ('・ｽ・ｽ・ｽ[Z・ｽﾍ茨ｿｽﾂ擾ｿｽﾌセ・ｽ・ｽ・ｽﾌ値・ｽ・ｽ闖ｬ・ｽ・ｽ・ｽ・ｽ・ｽﾈゑｿｽ・ｽ・ｽﾎなゑｿｽﾜゑｿｽ・ｽ・ｽB', '下端Zは一つ上のセルの値より小さくなければなりません。'),
    ('SettlementSoilLayer ・ｽﾍ適・ｽﾘな・ｿｽ・ｽf・ｽ・ｽ・ｽN・ｽ・ｽ・ｽX・ｽﾉ置・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽﾄゑｿｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ', 'SettlementSoilLayer は適切なモデルクラスに置き換えてください'),

    # Documentation
    ('SoilPiles ・ｽﾌ撰ｿｽ・ｽ・ｽ・ｽ・ｽ・ｽf・ｽo・ｽE・ｽ・ｽ・ｽX・ｽt・ｽ・ｽ・ｽﾅ・ｿｽ・ｽN・ｽG・ｽX・ｽg・ｽ・ｽ・ｽﾜゑｿｽ・ｽB', 'SoilPiles の生成をデバウンスでリクエストします。'),
    ('・ｽZ・ｽ・ｽ・ｽﾔに包ｿｽ・ｽ・ｽ・ｽ・ｽﾄばゑｿｽﾄゑｿｽ・ｽA・ｽﾅ鯉ｿｽﾌ呼び出・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ・ｽ闔橸ｿｽﾔ鯉ｿｽ・ｽ1・ｽｾゑｿｽ・ｽ・ｽ・ｽs・ｽ・ｽ・ｽ・ｽﾜゑｿｽ・ｽB', '短時間に複数回呼ばれても、最後の呼び出しだけが最終的に1回だけ実行されます。'),
    ('SoilPiles ・ｽﾌ撰ｿｽ・ｽ・ｽ・ｽｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs・ｽ・ｽ・ｽﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE・ｽ・ｽ・ｽX・ｽ・ｽ・ｽX・ｽL・ｽb・ｽv・ｽj・ｽB', 'SoilPiles の生成を即座に実行します（デバウンススキップ）。'),
    ('・ｽ・ｽ・ｽ・ｽ・ｽI・ｽﾉ托ｿｽ・ｽ・ｽ・ｽ・ｽ・ｽs・ｽ・ｽ・ｽK・ｽv・ｽﾈ場合・ｽﾉ使・ｽp・ｽ・ｽ・ｽﾜゑｿｽ・ｽB', '即時に実行する必要がある場合に使用します。'),
    ('・ｽE・ｽB・ｽ・ｽ・ｽh・ｽE・ｽX・ｽV・ｽ・ｽ・ｽf・ｽo・ｽE・ｽ・ｽ・ｽX・ｽt・ｽ・ｽ・ｽﾅ・ｿｽ・ｽN・ｽG・ｽX・ｽg・ｽ・ｽ・ｽﾜゑｿｽ・ｽB', 'ウィンドウ更新をデバウンスでリクエストします。'),
    ('・ｽE・ｽB・ｽ・ｽ・ｽh・ｽE・ｽX・ｽV・ｽｦ搾ｿｽ・ｽﾉ趣ｿｽ・ｽs・ｽ・ｽ・ｽﾜゑｿｽ・ｽi・ｽf・ｽo・ｽE・ｽ・ｽ・ｽX・ｽ・ｽ・ｽX・ｽL・ｽb・ｽv・ｽj・ｽB', 'ウィンドウ更新を即座に実行します（デバウンススキップ）。'),
    ('・ｽ_・ｽC・ｽA・ｽ・ｽ・ｽO・ｽ・ｽﾂゑｿｽ・ｽ・ｽ・ｽ・ｽﾈど、・ｽ・ｽ・ｽ・ｽ・ｽX・ｽV・ｽ・ｽ・ｽK・ｽv・ｽﾈ場合・ｽﾉ使・ｽp・ｽ・ｽ・ｽﾜゑｿｽ・ｽB', 'ダイアログを閉じた時など、即座更新が必要な場合に使用します。'),

    # Comments
    ('// ・ｽ・ｽ: ・ｽt・ｽB・ｽ[・ｽ・ｽ・ｽh・ｽ骭ｾ・ｽ・ｽﾏ更', '// 追加: フィールド宣言の変更'),
    ('// ・ｽﾛ暦ｿｽ・ｽ・ｽ・ｽﾌデ・ｽo・ｽE・ｽ・ｽ・ｽX・ｽ・ｽ・ｽL・ｽ・ｽ・ｽ・ｽ・ｽZ・ｽ・ｽ', '// 保留中のデバウンスタイマーをキャンセル'),
    ('// ・ｽN・ｽ・ｽ・ｽX・ｽﾌ先頭・ｽt・ｽﾟのフ・ｽB・ｽ[・ｽ・ｽ・ｽh・ｽﾉ追会ｿｽ・ｽi・ｽ・ｽ・ｽ・ｽ・ｽﾌフ・ｽB・ｽ[・ｽ・ｽ・ｽh・ｽﾌ近ゑｿｽ・ｽﾉ）', '// クラスの先頭付近のフィールドに追加（既存のフィールドの近くに）'),
    ('// JsonSerializerOptions ・ｽ・ｽ・ｽL・ｽ・ｽ・ｽb・ｽV・ｽ・ｽ', '// JsonSerializerOptions をキャッシュ'),
    ('// ・ｽX・ｽ・ｽ・ｽC・ｽ_・ｽ[・ｽﾏ更・ｽ・ｽ・ｽﾉ再描・ｽ・ｽ', '// スライダー変更時に再描画'),
    ('// ・ｽﾇ会ｿｽ: ・ｽR・ｽ}・ｽ・ｽ・ｽh・ｽX・ｽV・ｽ鼕・ｿｽw・ｽ・ｽ・ｽp', '// 追加: コマンド更新最適化用'),

    # Keywords
    ('・ｽ・ｽ・ｽ[Z', '下端Z'),
    ('・ｽY・ｽz・ｽu', '杭配置'),
    ('・ｽY・ｽﾌ', '杭体'),
    ('・ｽ・ｽ・ｽ・ｽ', '根入部'),
    ('・ｽn・ｽﾕ', '地盤'),
    ('・ｽm・ｽF', '確認'),
    ('・ｽG・ｽ・ｽ・ｽ[', 'エラー'),
    ('・ｽﾛ托ｿｽ', '保存'),
    ('・ｽﾇ搾ｿｽ', '読込'),
    ('・ｽQ・ｽY', '群杭'),
]

# Apply replacements
for old, new in replacements:
    count = content.count(old)
    if count > 0:
        print(f"Replacing '{old[:20]}...' ({count} occurrences)")
        content = content.replace(old, new)

# Write back with UTF-8 BOM
with codecs.open(file_path, 'w', encoding='utf-8-sig') as f:
    f.write(content)

print("\nFile fixed and saved as UTF-8 with BOM!")
