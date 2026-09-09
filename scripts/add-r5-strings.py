"""Insert R5 feature strings (frame-rate option + record-colors settings
entry) into all twelve localization files, keyed insertion after
"Settings.Animation.Speed.Title" style anchors — done as a simple append
via JSON-aware check plus textual insertion at file end is NOT valid
(JSON requires comma handling), so we insert before the final closing
brace with a comma fix instead.
"""
from __future__ import annotations

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1] / "src" / "DeskBox" / "Strings"

TRANSLATIONS: dict[str, dict[str, str]] = {
    "en-US": {
        "Settings.Animation.FrameRate.Title": "Animation frame rate",
        "Settings.Animation.FrameRate.Description": "Capsule expand/collapse cadence. Delivered rate is the display refresh divided by the cap and never exceeds it.",
        "Settings.QuickCapture.RecordColors.Title": "Record colors",
        "Settings.QuickCapture.RecordColors.Description": "Text and background colors for clipboard records; low-contrast pairs are rejected",
        "Settings.QuickCapture.RecordColors.Background.Title": "Record background color",
        "Settings.QuickCapture.RecordColors.Background.Description": "Opens the color picker; HEX input is included",
    },
    "zh-CN": {
        "Settings.Animation.FrameRate.Title": "动画帧率",
        "Settings.Animation.FrameRate.Description": "胶囊展开/收起节奏，实际帧率为刷新率除以档位且不超过所选值",
        "Settings.QuickCapture.RecordColors.Title": "记录配色",
        "Settings.QuickCapture.RecordColors.Description": "剪贴板记录的文字与背景颜色；低对比度组合会被拒绝",
        "Settings.QuickCapture.RecordColors.Background.Title": "记录背景颜色",
        "Settings.QuickCapture.RecordColors.Background.Description": "打开取色器，支持直接输入 HEX 色值",
    },
    "zh-TW": {
        "Settings.Animation.FrameRate.Title": "動畫影格率",
        "Settings.Animation.FrameRate.Description": "膠囊展開/收起節奏，實際影格率為重新整理率除以檔位且不超過所選值",
        "Settings.QuickCapture.RecordColors.Title": "記錄配色",
        "Settings.QuickCapture.RecordColors.Description": "剪貼簿記錄的文字與背景顏色；低對比組合會被拒絕",
        "Settings.QuickCapture.RecordColors.Background.Title": "記錄背景顏色",
        "Settings.QuickCapture.RecordColors.Background.Description": "開啟檢色器，支援直接輸入 HEX 色值",
    },
    "ja-JP": {
        "Settings.Animation.FrameRate.Title": "アニメーションフレームレート",
        "Settings.Animation.FrameRate.Description": "カプセル開閉の間隔。実効フレームレートはリフレッシュレートを段で割った値で、選択値を超えません",
        "Settings.QuickCapture.RecordColors.Title": "レコードの色",
        "Settings.QuickCapture.RecordColors.Description": "クリップボード記録の文字色と背景色。低コントラストの組み合わせは拒否されます",
        "Settings.QuickCapture.RecordColors.Background.Title": "レコードの背景色",
        "Settings.QuickCapture.RecordColors.Background.Description": "カラーピッカーを開きます。HEX 入力に対応",
    },
    "de-DE": {
        "Settings.Animation.FrameRate.Title": "Animationsbildrate",
        "Settings.Animation.FrameRate.Description": "Kadenz für Ein-/Ausklappen; die gelieferte Rate ist Bildwiederholrate geteilt durch die Stufe und überschreitet sie nie",
        "Settings.QuickCapture.RecordColors.Title": "Eintragsfarben",
        "Settings.QuickCapture.RecordColors.Description": "Text- und Hintergrundfarben der Zwischenablage-Einträge; kontrastarme Paare werden abgelehnt",
        "Settings.QuickCapture.RecordColors.Background.Title": "Hintergrundfarbe der Einträge",
        "Settings.QuickCapture.RecordColors.Background.Description": "Öffnet den Farbwähler; HEX-Eingabe enthalten",
    },
    "fr-FR": {
        "Settings.Animation.FrameRate.Title": "Fréquence d'animation",
        "Settings.Animation.FrameRate.Description": "Cadence d'ouverture/fermeture des capsules ; la fréquence effective est le taux de rafraîchissement divisé par le palier, sans le dépasser",
        "Settings.QuickCapture.RecordColors.Title": "Couleurs des entrées",
        "Settings.QuickCapture.RecordColors.Description": "Couleurs de texte et d'arrière-plan des enregistrements du presse-papiers ; les paires à faible contraste sont refusées",
        "Settings.QuickCapture.RecordColors.Background.Title": "Couleur d'arrière-plan des entrées",
        "Settings.QuickCapture.RecordColors.Background.Description": "Ouvre la palette de couleurs ; saisie HEX incluse",
    },
    "es-ES": {
        "Settings.Animation.FrameRate.Title": "Velocidad de fotogramas de la animación",
        "Settings.Animation.FrameRate.Description": "Cadencia de expansión/contracción de cápsulas; la velocidad efectiva es la de refresco dividida por el nivel y nunca la supera",
        "Settings.QuickCapture.RecordColors.Title": "Colores de registros",
        "Settings.QuickCapture.RecordColors.Description": "Colores de texto y fondo de los registros del portapapeles; se rechazan las combinaciones de bajo contraste",
        "Settings.QuickCapture.RecordColors.Background.Title": "Color de fondo de los registros",
        "Settings.QuickCapture.RecordColors.Background.Description": "Abre el selector de color; incluye entrada HEX",
    },
    "pt-BR": {
        "Settings.Animation.FrameRate.Title": "Taxa de quadros da animação",
        "Settings.Animation.FrameRate.Description": "Cadência de expansão/recolhimento das cápsulas; a taxa efetiva é a de atualização dividida pelo nível e nunca a excede",
        "Settings.QuickCapture.RecordColors.Title": "Cores dos registros",
        "Settings.QuickCapture.RecordColors.Description": "Cores de texto e de fundo dos registros da área de transferência; pares de baixo contraste são rejeitados",
        "Settings.QuickCapture.RecordColors.Background.Title": "Cor de fundo dos registros",
        "Settings.QuickCapture.RecordColors.Background.Description": "Abre o seletor de cores; entrada HEX incluída",
    },
    "ru-RU": {
        "Settings.Animation.FrameRate.Title": "Частота кадров анимации",
        "Settings.Animation.FrameRate.Description": "Ритм разворачивания/сворачивания капсул; фактическая частота равна частоте обновления, делённой на уровень, и не превышает выбранную",
        "Settings.QuickCapture.RecordColors.Title": "Цвета записей",
        "Settings.QuickCapture.RecordColors.Description": "Цвет текста и фона записей буфера обмена; малоконтрастные сочетания отклоняются",
        "Settings.QuickCapture.RecordColors.Background.Title": "Цвет фона записей",
        "Settings.QuickCapture.RecordColors.Background.Description": "Открывает палитру; поддерживается ввод HEX",
    },
    "ar-SA": {
        "Settings.Animation.FrameRate.Title": "معدل إطارات الحركة",
        "Settings.Animation.FrameRate.Description": "إيقاع فتح/طي الكبسولات؛ معدل الإطارات الفعلي هو معدل التحديث مقسومًا على المستوى ولا يتجاوزه",
        "Settings.QuickCapture.RecordColors.Title": "ألوان السجلات",
        "Settings.QuickCapture.RecordColors.Description": "لون نص السجلات وخلفيتها؛ تُرفض المجموعات منخفضة التباين",
        "Settings.QuickCapture.RecordColors.Background.Title": "لون خلفية السجلات",
        "Settings.QuickCapture.RecordColors.Background.Description": "يفتح منتقي الألوان مع دعم إدخال HEX",
    },
    "hi-IN": {
        "Settings.Animation.FrameRate.Title": "एनीमेशन फ़्रेम दर",
        "Settings.Animation.FrameRate.Description": "कैप्सूल विस्तार/संकुचन की गति; वास्तविक फ़्रेम दर रिफ्रेश दर को स्तर से विभाजित करके मिलती है और चयनित मान से अधिक नहीं होती",
        "Settings.QuickCapture.RecordColors.Title": "रिकॉर्ड रंग",
        "Settings.QuickCapture.RecordColors.Description": "क्लिपबोर्ड रिकॉर्ड का टेक्स्ट और पृष्ठभूमि रंग; कम कंट्रास्ट जोड़े अस्वीकृत होते हैं",
        "Settings.QuickCapture.RecordColors.Background.Title": "रिकॉर्ड पृष्ठभूमि रंग",
        "Settings.QuickCapture.RecordColors.Background.Description": "कलर पिकर खोलता है; HEX इनपुट शामिल है",
    },
    "bn-BD": {
        "Settings.Animation.FrameRate.Title": "অ্যানিমেশন ফ্রেম হার",
        "Settings.Animation.FrameRate.Description": "ক্যাপসুল প্রসারণ/সংকোচনের ছন্দ; প্রকৃত ফ্রেম হার রিফ্রেশ হারকে স্তর দিয়ে ভাগ করা মান এবং নির্বাচিত মান অতিক্রম করে না",
        "Settings.QuickCapture.RecordColors.Title": "রেকর্ডের রং",
        "Settings.QuickCapture.RecordColors.Description": "ক্লিপবোর্ড রেকর্ডের লেখা ও পটভূমির রং; কম কনট্রাস্টের জোড়া প্রত্যাখ্যাত হয়",
        "Settings.QuickCapture.RecordColors.Background.Title": "রেকর্ডের পটভূমির রং",
        "Settings.QuickCapture.RecordColors.Background.Description": "কালার পিকার খোলে; HEX ইনপুট সমর্থিত",
    },
}


def main() -> None:
    inserted_total = 0
    for locale, strings in TRANSLATIONS.items():
        path = ROOT / f"{locale}.json"
        raw = path.read_text(encoding="utf-8")
        existing = set(json.loads(raw).keys())
        new_pairs = [
            f'    "{key}": {json.dumps(value, ensure_ascii=False)}'
            for key, value in strings.items()
            if key not in existing
        ]
        if not new_pairs:
            print(f"{locale}: nothing to insert")
            continue

        stripped = raw.rstrip()
        assert stripped.endswith("}")
        head = stripped[:-1].rstrip()
        if not head.endswith(","):
            head += ","
        block = ",\n" + ",\n".join(new_pairs) + "\n"
        path.write_text(head + block + "}", encoding="utf-8", newline="\n")
        inserted_total += len(new_pairs)
        print(f"{locale}: inserted {len(new_pairs)} keys")

    reference = set(json.loads((ROOT / "en-US.json").read_text(encoding="utf-8")).keys())
    for path in sorted(ROOT.glob("*.json")):
        keys = set(json.loads(path.read_text(encoding="utf-8")).keys())
        missing = reference - keys
        if missing:
            raise SystemExit(f"{path.name} missing keys: {sorted(missing)}")
    print(f"done; inserted {inserted_total} total; key coverage verified")


if __name__ == "__main__":
    main()
