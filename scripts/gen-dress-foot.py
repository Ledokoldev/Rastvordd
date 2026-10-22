# Хитбоксы мебели для обстановки РТ (game/src/Sim/DressFoot.g.cs) из assets/manifest*.json: поле foot = [ширина, глубина, высота] (м).
# Запуск: python scripts/gen-dress-foot.py
import json, os
R = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
foot = {}
for mf in ('manifest.json', 'manifest_t2.json', 'manifest_interior.json'):
    p = os.path.join(R, 'assets', mf)
    if not os.path.exists(p):
        continue
    for a in json.load(open(p, encoding='utf-8'))['assets']:
        f = a.get('foot')
        if a['cat'] not in ('furniture', 'props', 'outdoor', 'interior') or not f:
            continue
        legs = isinstance(f, dict)
        w, d, h = f['legs'] if legs else f
        foot[a['id']] = (w, d, h, legs)
out = ['// Сгенерировано scripts/gen-dress-foot.py — не править руками.', 'namespace Rastvordd.Sim', '{', '    public static class DressFoot', '    {',
       '        public static readonly System.Collections.Generic.Dictionary<string, (float w, float d, float h, bool legs)> Foot = new()', '        {']
for k in sorted(foot):
    w, d, h, legs = foot[k]
    out.append(f'            ["{k}"] = ({w}f, {d}f, {h}f, {"true" if legs else "false"}),')
out += ['        };', '    }', '}', '']
open(os.path.join(R, 'game', 'src', 'Sim', 'DressFoot.g.cs'), 'w', encoding='utf-8').write('\n'.join(out))
print(len(foot))
