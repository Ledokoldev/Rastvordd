# Гармонизация цвета старой растительности и природных объектов под новую землю и низкую траву.
# Новые тайлы земли (game/art/tiles/ground_grass|forest|dry|dirt) и процедурная трава (game/art/grass/tuft_*,
# grass_*_1/2 — scripts/gen-grass-tufts.py) задали палитру; старые деревья, кусты, камни, пни и мелочи
# остались серыми и холодными (после cool() «B»/«V» в restyle-assets.py) и выпадают из неё.
#
# Что делает: в пространстве Lab сдвигает среднее каналов a/b (оттенок) картинки на долю K к эталону своей
# группы и частично подтягивает их разброс (насыщенность, K_STD); светлоту L сдвигает лишь чуть (K_L), не меняя
# её рисунка (тени, блики, контуры остаются). Альфу не трогает. Карты нормалей (*_n.png), пилотные *_v,
# снег/тундру, рукотворное (металл, сера, шины, знаки…), персонажей, интерфейс, иконки, эффекты и оружие не трогает.
#
# Оригинал каждого изменённого файла сначала копируется в D:\Archive\art_before_harmonize\<папка>\ (если копии
# ещё нет — повторный запуск не затирает исходник). Считается всегда от архивной копии, так что повтор не копит сдвиг.
#
# Запуск: python scripts/harmonize-colors.py [--dry] [--stats] [--k=0.7] [--kstd=0.6] [--kl=0.25] [--only=trees/tree_oak_1,...]
# После — импорт: Godot_..._console.exe --headless --path game --import
# Важно: process-assets.py пересобирает game/art из assets/gen — после него этот скрипт нужно запустить снова
# (для файлов, которые он перезаписал, удалить их архивную копию, иначе возьмётся старый исходник).
import os, sys, glob, shutil
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'game', 'art')
BACKUP = r'D:\Archive\art_before_harmonize'
K_STD, K_L = 0.6, 0.25

# ---------------- Lab (sRGB, D65) ----------------
def _lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def _gam(c): return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055)
M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
Mi = np.linalg.inv(M)
WP = np.array([0.95047, 1.0, 1.08883])


def rgb2lab(rgb):
    xyz = _lin(rgb) @ M.T / WP
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lab2rgb(lab):
    fy = (lab[..., 0] + 16) / 116
    fx, fz = fy + lab[..., 1] / 500, fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f ** 3 > 216 / 24389, f ** 3, (116 * f - 16) / (24389 / 27)) * WP
    return np.clip(_gam(xyz @ Mi.T), 0, 1)


def load(path):
    a = np.asarray(Image.open(path).convert('RGBA')).astype(np.float64) / 255
    return a[..., :3], a[..., 3]


# ---------------- эталоны ----------------
REFS = {
    # живая зелень: луг и лес + их трава
    'veg':   (['ground_grass', 'ground_forest'], ['tuft_meadow_*', 'tuft_forest_*', 'tuft_swamp_*', 'grass_meadow_[12].png', 'grass_forest_[12].png', 'grass_swamp_[12].png']),
    # сухое и мёртвое: пустошь, грунт + сухая трава
    'dry':   (['ground_dry', 'ground_dirt'], ['tuft_dry_*', 'grass_arid_[12].png']),
    # камни, пни, брёвна: тёплая земля (грунт, лесная подстилка, сухая земля), без травы
    'earth': (['ground_dirt', 'ground_forest', 'ground_dry'], []),
}

# группа -> множители (сдвиг среднего a/b, подстройка разброса a/b, сдвиг L): камни мягче — им не нужно зеленеть
STRENGTH = {'veg': (1.0, 1.0, 1.0), 'dry': (1.0, 1.0, 1.0), 'earth': (0.75, 0.5, 0.6),
            'accent': (0.35, 0.3, 0.5)}   # цветы и грибы: среднее держат лепестки и шляпки — сильный сдвиг убил бы их цвет


def ref_stats(tiles, tufts):
    px = []
    for t in tiles:
        rgb, _ = load(os.path.join(ART, 'tiles', t + '.png'))
        px.append(rgb2lab(rgb.reshape(-1, 3)))
    for pat in tufts:
        for f in sorted(glob.glob(os.path.join(ART, 'grass', pat if pat.endswith('.png') else pat + '.png'))):
            if f.endswith(('_n.png', '_v.png')): continue
            rgb, al = load(f)
            if (al > 0.5).any(): px.append(rgb2lab(rgb[al > 0.5]))
    v = np.concatenate(px, 0)
    return v.mean(0), v.std(0)


SKIP_WORDS = ('snow', 'frozen', 'tundra', 'glowing', 'fly_agaric')
DRY_WORDS = ('dead', 'dry', 'burnt', 'arid', 'hay')
FOLDERS = ('trees', 'plants', 'nodes', 'clutter', 'outdoor')


def classify(folder, name):
    """(эталон, сила) для файла или None — не трогать."""
    g = _group(folder, name)
    if g is None: return None
    accent = name.startswith('flowers_') or 'mushroom' in name or name == 'sunflower'
    return g, 'accent' if accent else g


def _group(folder, name):
    if name.endswith(('_n', '_v')) or any(w in name for w in SKIP_WORDS):
        return None
    dry = any(w in name for w in DRY_WORDS)
    if folder == 'trees':
        return 'dry' if dry else 'veg'
    if folder == 'plants':
        if 'mushroom' in name: return 'earth'
        return 'dry' if dry else 'veg'
    if folder == 'nodes':
        if name.startswith(('node_stone', 'rock_', 'coast_rock')):
            return 'dry' if 'arid' in name else 'earth'
        return None   # металл, сера, металлолом — рукотворное и «сигнальное», цвет свой
    if folder == 'clutter':
        if name in ('clutter_log', 'clutter_stump', 'clutter_mushrooms'): return 'earth'
        if name.startswith('flowers_'): return 'veg'
        return None   # шины, канистры, знаки и т. п. — рукотворное
    if folder == 'outdoor':
        if name in ('stump', 'log_pile_side', 'firewood_pile', 'firewood_stack_side'): return 'earth'
        if name in ('haystack', 'hay_bales_angled'): return 'dry'
        return None
    return None


def grade(rgb, al, ref_mean, ref_std, k, ks, kl):
    m = al > 0.5
    if m.sum() < 8:
        return None
    lab = rgb2lab(rgb)
    mu, sd = lab[m].mean(0), lab[m].std(0)
    out = lab.copy()
    # a/b: разброс — к эталонному частично (множитель в пределах ×0,7…×1,8), среднее — на долю k
    for c in (1, 2):
        ratio = np.clip(ref_std[c] / max(sd[c], 0.5), 0.7, 1.8) ** ks
        out[..., c] = (lab[..., c] - mu[c]) * ratio + mu[c] + k * (ref_mean[c] - mu[c])
    # L: рисунок светотени тот же, среднее — лишь немного к эталону
    out[..., 0] = lab[..., 0] + kl * (ref_mean[0] - mu[0])
    return lab2rgb(out)


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    args = {kk: v for kk, _, v in (s.lstrip('-').partition('=') for s in sys.argv[1:])}
    k, kstd, kl = float(args.get('k', 0.7)), float(args.get('kstd', K_STD)), float(args.get('kl', K_L))
    dry_run = 'dry' in args
    only = set(args['only'].split(',')) if args.get('only') else None
    refs = {g: ref_stats(*v) for g, v in REFS.items()}
    for g, (mu, sd) in refs.items():
        print(f'эталон {g:<6} L {mu[0]:5.1f}±{sd[0]:4.1f}  a {mu[1]:5.1f}±{sd[1]:4.1f}  b {mu[2]:5.1f}±{sd[2]:4.1f}')
    changed = 0
    for folder in FOLDERS:
        for path in sorted(glob.glob(os.path.join(ART, folder, '*.png'))):
            name = os.path.basename(path)[:-4]
            if only and f'{folder}/{name}' not in only: continue
            cl = classify(folder, name)
            if cl is None: continue
            g, sk = cl
            bak = os.path.join(BACKUP, folder, name + '.png')
            rgb, al = load(bak if os.path.exists(bak) else path)   # всегда от исходника
            sa, ss, sl = STRENGTH[sk]
            res = grade(rgb, al, refs[g][0], refs[g][1], k * sa, kstd * ss, kl * sl)
            if res is None: continue
            if 'stats' in args:
                l0, l1 = rgb2lab(rgb[al > 0.5]), rgb2lab(res[al > 0.5])
                print(f'{folder}/{name:<28} {g:<5} a {l0[:, 1].mean():5.1f}->{l1[:, 1].mean():5.1f}  b {l0[:, 2].mean():5.1f}->{l1[:, 2].mean():5.1f}  L {l0[:, 0].mean():5.1f}->{l1[:, 0].mean():5.1f}')
            if dry_run: continue
            if not os.path.exists(bak):
                os.makedirs(os.path.dirname(bak), exist_ok=True)
                shutil.copy2(path, bak)
            out = np.concatenate([res, al[..., None]], -1)
            Image.fromarray((out * 255 + 0.5).astype(np.uint8), 'RGBA').save(path)
            changed += 1
    print(f'изменено файлов: {changed}' + (' (пробный прогон, ничего не записано)' if dry_run else ''))


if __name__ == '__main__':
    main()
