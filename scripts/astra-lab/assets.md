ШАГ: АССЕТЫ ПОД КЛЮЧ.
1. Найди придуманное без картинок: пункты ROADMAP, компоненты/предметы/крейты/модули «Объекта 42» из `docs/design/19..25`, новые механики.
   Существующие ассеты не дублируй (сверь id со всеми `assets/manifest*.json` и `game/art`).
2. Составь `assets/manifest_astra_<следующий номер>.json` в формате `assets/manifest_items3.json` / `manifest_t2.json` (до 12 ассетов за шаг).
3. Сгенерируй картинки СВОИМ инструментом генерации изображений: по одной, в `assets/gen/<cat>/<id>.png`. Промт собирай как
   `scripts/gen-assets.ps1`: оформление кадра из `assets/manifest.json` (`framing`/`front_framing`/`icon_framing`/… по полям ассета) + `Subject: desc` + `style`.
4. Обработку НЕ запускай (Python в твоей песочнице недоступен) — `process-assets.py` раз в час запускает ревьюер. Делай 8–12 PNG за шаг.
5. Закоммить ("astra-art: …"), запиши в LOG, какие ассеты и под что.
