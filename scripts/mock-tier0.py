# Макеты РТ тира 0 (Пост ГАИ, Сельмаг «Продукты», Заправка «Последняя капля») из игровых пиксельных спрайтов.
# Основа — scripts/mock-substation.py: 32 px/м, высоты стен ×0.75, тени от силуэта по свету (без круглых пятен).
# Запуск: python scripts/mock-tier0.py [gai|shop|fuel]  → docs/design/img/t0-<rt>-mock.png, -half.png, -plan.png
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops
import json, os, random, sys

ROOT = 'C:/Users/Xbaza/Desktop/Rastvordd/'
A = ROOT + 'game/art/'
OUT = ROOT + 'docs/design/img/'
PPM, HS = 32, 0.75
MAN = {a['id']: a for a in json.load(open(ROOT + 'assets/manifest.json', encoding='utf-8'))['assets']}
_cache = {}


def L(n):
    if n not in _cache:
        p = A + n + '.png'
        _cache[n] = Image.open(p).convert('RGBA') if os.path.exists(p) else None
        if _cache[n] is None:
            print('нет спрайта:', n)
    return _cache[n]


def font(sz, bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/' + ('segoeuib.ttf' if bold else 'segoeui.ttf'), sz)


class Scene:
    def __init__(self, title, W, H, M=9, seed=1):
        self.title, self.W, self.H, self.M = title, W, H, M
        self.img = Image.new('RGBA', ((W + 2 * M) * PPM, (H + 2 * M) * PPM), (0, 0, 0, 255))
        self.shadow = Image.new('L', self.img.size, 0)
        self.objs, self.zones, self.marks, self.plan_walls, self.plan_props, self.notes = [], [], [], [], [], []
        self.keepout = []          # прямоугольники, где не сажать лес и траву
        self.roofs = []            # крыши: (x0, y0, x1, y1, высота стен, текстура) — рисуются поверх всего
        # планировка для игры (scripts/mock-tier0.py → game/src/Sim/Tier0Layouts.g.cs): то же, что на макете
        self.ex = {'grounds': [], 'walls': [], 'props': [], 'decals': []}
        self._skip = False
        random.seed(seed)

    def px(self, x, y):
        return int((x + self.M) * PPM), int((y + self.M) * PPM)

    # ---------- земля ----------
    def mask(self, poly, blur=6):
        m = Image.new('L', self.img.size, 0)
        if blur >= 4:   # неровный край у земли: дробим рёбра и чуть сдвигаем точки
            pts = []
            for (p, q) in zip(poly, poly[1:] + poly[:1]):
                n = max(1, int(((q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2) ** .5 / 1.5))
                for i in range(n):
                    j = 0 if i == 0 else 0.55
                    pts.append((p[0] + (q[0] - p[0]) * i / n + random.uniform(-j, j), p[1] + (q[1] - p[1]) * i / n + random.uniform(-j, j)))
            poly = pts
        ImageDraw.Draw(m).polygon([self.px(*p) for p in poly], fill=255)
        return m.filter(ImageFilter.GaussianBlur(blur)) if blur else m

    def ground(self, name, poly=None, blur=6, zone=None):
        t = L('tiles/' + name)
        reg = Image.new('RGBA', self.img.size)
        for i in range(0, reg.width, t.width):
            for j in range(0, reg.height, t.height):
                reg.paste(t, (i, j))
        if not self._skip:
            self.ex['grounds'].append((name, poly if poly is not None else self.rect(-self.M, -self.M, self.W + self.M, self.H + self.M)))
        if poly is None:
            self.img.paste(reg, (0, 0))
        else:
            self.img.paste(reg, (0, 0), self.mask(poly, blur))
        if zone and poly:
            self.zones.append((poly, zone[0], zone[1]))

    def rect(self, x0, y0, x1, y1):
        return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]

    def dec(self, n, x, y, s=1.0, rot=0):
        d = L('decals/' + n) if '/' not in n else L(n)
        if d is None:
            return
        if not self._skip:
            self.ex['decals'].append(('decals/' + n if '/' not in n else n, x, y, s, rot))
        if s != 1:
            d = d.resize((max(1, int(d.width * s)), max(1, int(d.height * s))), Image.NEAREST)
        if rot:
            d = d.rotate(rot, expand=True, resample=Image.NEAREST)
        a, b = self.px(x, y)
        self.img.alpha_composite(d, (a - d.width // 2, b - d.height // 2))

    # ---------- тени: одна маска на всё, чтобы перекрытия не темнели вдвое ----------
    def add_shadow(self, spr, a, b):
        al = spr.getchannel('A').point(lambda v: 255 if v > 100 else 0)
        w, h = al.size
        sh_h = max(2, int(h * 0.42))
        al = al.resize((w, sh_h), Image.NEAREST)
        shear = int(sh_h * 1.1)
        out = Image.new('L', (w + shear, sh_h), 0)
        for row in range(sh_h):
            off = int(shear * (1 - row / sh_h))
            out.paste(al.crop((0, row, w, row + 1)), (off, row))
        layer = Image.new('L', self.img.size, 0)
        layer.paste(out, (a - w // 2, b - sh_h))
        self.shadow = ImageChops.lighter(self.shadow, layer)

    def wall_shadow(self, x0, y0, x1, y1, h):
        H = h * HS * PPM
        dx, dy = 0.46 * H, -0.42 * H
        a, b = self.px(x0, y0)
        c, d = self.px(x1, y1)
        poly = [(a, d), (a, b), (a + dx, b + dy), (c + dx, b + dy), (c + dx, d + dy), (c, d)]
        layer = Image.new('L', self.img.size, 0)
        ImageDraw.Draw(layer).polygon(poly, fill=255)
        self.shadow = ImageChops.lighter(self.shadow, layer)

    # ---------- стены ----------
    def wall(self, x0, y0, x1, y1, h, tex, t=0.3):
        if x0 == x1:
            x0, x1 = x0 - t / 2, x1 + t / 2
        if y0 == y1:
            y0, y1 = y0 - t / 2, y1 + t / 2
        self.plan_walls.append((x0, y0, x1, y1, tex))
        self.ex['walls'].append((x0, y0, x1, y1, h, tex))
        self.keepout.append((x0 - 1, y0 - 1, x1 + 1, y1 + 1))
        self.wall_shadow(x0, y0, x1, y1, h)

        def draw():
            H = int(h * PPM * HS)
            a, b = self.px(x0, y0)
            c, d = self.px(x1, y1)
            w = max(1, c - a)
            face = L('walls/' + tex)
            f = Image.new('RGBA', (w, H))
            for i in range(0, w, face.width):
                for j in range(0, H, face.height):
                    f.paste(face, (i, j))
            # низ стены чуть темнее (контакт с землёй), верх светлее
            grad = Image.new('L', (1, H))
            for j in range(H):
                grad.putpixel((0, j), int(255 * (0.86 + 0.14 * (1 - j / max(1, H - 1)))))
            grad = grad.resize((w, H))
            dark = Image.new('RGBA', (w, H), (0, 0, 0, 255))
            f = Image.composite(f, Image.blend(f, dark, 0.35), grad)
            self.img.alpha_composite(f, (a, d - H))
            avg = face.resize((1, 1), Image.BOX).getpixel((0, 0))
            top = Image.new('RGBA', (w, max(1, d - b)), tuple(int(v * 0.62) for v in avg[:3]) + (255,))
            self.img.alpha_composite(top, (a, b - H))
            dr = ImageDraw.Draw(self.img)
            dr.rectangle([a, b - H, a + w - 1, b - H + max(1, d - b) - 1], outline=(18, 18, 16, 255))
            dr.line([(a, d - 1), (a + w - 1, d - 1)], fill=(20, 20, 18, 255))
        self.objs.append((y1, draw))

    def walls(self, pts, h, tex, t=0.3, gaps=()):
        """Ломаная стена; gaps — пары точек (x0,y0,x1,y1) проёмов на прямых участках."""
        for (p, q) in zip(pts, pts[1:]):
            segs = [(p, q)]
            for g in gaps:
                out = []
                for (s, e) in segs:
                    if s[0] == e[0] == g[0] == g[2]:  # вертикальный
                        lo, hi = sorted((s[1], e[1]))
                        if lo <= g[1] < g[3] <= hi:
                            out += [((s[0], lo), (s[0], g[1])), ((s[0], g[3]), (s[0], hi))]
                            continue
                    if s[1] == e[1] == g[1] == g[3]:
                        lo, hi = sorted((s[0], e[0]))
                        if lo <= g[0] < g[2] <= hi:
                            out += [((lo, s[1]), (g[0], s[1])), ((g[2], s[1]), (hi, s[1]))]
                            continue
                    out.append((s, e))
                segs = out
            for (s, e) in segs:
                if s != e:
                    (ax, ay), (bx, by) = s, e
                    self.wall(min(ax, bx), min(ay, by), max(ax, bx), max(ay, by), h, tex, t)

    # ---------- объекты ----------
    def prop(self, n, x, y, flip=False, shadow=True, scale=1.0, plan=None):
        s = L(n)
        if s is None:
            return
        if not self._skip:
            self.ex['props'].append((n, x, y, bool(flip), plan[0] if plan else None))
        if flip:
            s = s.transpose(Image.FLIP_LEFT_RIGHT)
        if scale != 1:
            s = s.resize((int(s.width * scale), int(s.height * scale)), Image.NEAREST)
        a, b = self.px(x, y)
        if shadow:
            self.add_shadow(s, a, b)
        wm = s.width / PPM
        self.keepout.append((x - wm / 2 - .5, y - 1.5, x + wm / 2 + .5, y + .5))
        f = MAN.get(n.split('/')[-1], {}).get('foot')
        if plan:
            if isinstance(f, dict):
                w, d, _ = f['legs']
            elif f:
                w, d, _ = f
            else:
                w, d = wm, 0.8
            self.plan_props.append((x - w / 2, y - d, x + w / 2, y, plan[0], plan[1]))
        self.objs.append((y, lambda s=s, a=a, b=b: self.img.alpha_composite(s, (a - s.width // 2, b - s.height))))

    def mark(self, x, y, color, label):
        self.marks.append((x, y, color, label))

    def scatter(self, names, n, area, avoid=(), flip=True, shadow=True):
        x0, y0, x1, y1 = area
        k = 0
        while k < n:
            x, y = random.uniform(x0, x1), random.uniform(y0, y1)
            if any(a <= x <= c and b <= y <= d for (a, b, c, d) in avoid):
                continue
            self.prop(random.choice(names), x, y, flip and random.random() < .5, shadow)
            k += 1

    def weeds(self, area, n, avoid=(), bush=0.12):
        """Бурьян и кусты на пустырях внутри РТ (без теней, кусты с тенью)."""
        GR = ['grass/grass_meadow_1', 'grass/grass_meadow_2', 'grass/grass_arid_1', 'grass/grass_arid_2', 'grass/grass_forest_2']
        x0, y0, x1, y1 = area
        for _ in range(n):
            x, y = random.uniform(x0, x1), random.uniform(y0, y1)
            if any(a <= x <= c and b <= y <= d for (a, b, c, d) in list(avoid) + self.keepout):
                continue
            if random.random() < bush:
                self.prop('plants/bush_1', x, y, random.random() < .5)
            else:
                self.prop(random.choice(GR), x, y, random.random() < .5, shadow=False)
        self._skip = False

    def forest(self, keep, density=0.5, grass=900):
        """Лес и трава вокруг: keep — прямоугольники территории/дороги, где не сажать."""
        W, H, M = self.W, self.H, self.M
        TR = ['trees/tree_pine_1', 'trees/tree_pine_2', 'trees/tree_birch', 'trees/tree_leafy', 'trees/tree_oak_dead', 'plants/bush_1']
        GR = ['grass/grass_meadow_1', 'grass/grass_meadow_2', 'grass/grass_forest_1', 'grass/grass_forest_2']
        free = lambda x, y, pad: not any(a - pad <= x <= c + pad and b - pad <= y <= d + pad for (a, b, c, d) in keep + self.keepout)
        self._skip = True   # лес вокруг — в игре свой, из мира
        for _ in range(int((W + 2 * M) * (H + 2 * M) * density / 4)):
            x, y = random.uniform(-M, W + M), random.uniform(-M + 2, H + M + 4)
            if free(x, y, 1.5):
                self.prop(random.choice(TR), x, y, random.random() < .5)
        for _ in range(grass):
            x, y = random.uniform(-M, W + M), random.uniform(-M, H + M)
            if free(x, y, 0.2):
                self.prop(random.choice(GR), x, y, random.random() < .5, shadow=False)

    # ---------- крыши ----------
    def roof(self, x0, y0, x1, y1, h, tile='roof_slate'):
        self.roofs.append((x0, y0, x1, y1, h, tile))

    def draw_roofs(self):
        for (x0, y0, x1, y1, h, tile) in self.roofs:
            H = int(h * PPM * HS)
            a, b = self.px(x0 - 0.25, y0 - 0.25); c, d = self.px(x1 + 0.25, y1 + 0.25)
            b, d = b - H, d - H
            t = L('tiles/' + tile)
            reg = Image.new('RGBA', (c - a, d - b))
            for i in range(0, reg.width, t.width):
                for j in range(0, reg.height, t.height):
                    reg.paste(t, (i, j))
            self.img.alpha_composite(reg, (a, b))
            dr = ImageDraw.Draw(self.img, 'RGBA')
            fascia = int(0.35 * PPM * HS) + 2
            dr.rectangle([a, d - 1, c - 1, d - 1 + fascia], fill=(38, 34, 30, 255))     # торец крыши (свес) над лицом стены
            dr.rectangle([a, b, c - 1, b + 2], fill=(255, 248, 230, 50))                  # кромка на свету
            dr.rectangle([a, b, c - 1, d - 1 + fascia], outline=(18, 16, 14, 200), width=2)
            # начинка крыши: вентиляция, трубы, антенны — как в игре (1 предмет на ~25 м²)
            rng = random.Random(int(x0 * 131 + y0 * 17))
            n = max(1, min(9, int((x1 - x0) * (y1 - y0) / 25)))
            items = []
            for _ in range(n * 12):
                if len(items) >= n: break
                q = (x0 + 1.2 + rng.random() * (x1 - x0 - 2.4), y0 + 1.6 + rng.random() * (y1 - y0 - 2.4))
                if any((q[0] - o[0]) ** 2 + (q[1] - o[1]) ** 2 < 2.6 ** 2 for o in items): continue
                items.append(q)
            for q in sorted(items, key=lambda v: v[1]):
                spr = L('props/' + rng.choice(ROOF_ITEMS))
                if spr is None: continue
                qa, qb = self.px(*q); qb -= H
                al = spr.getchannel('A').point(lambda v: 255 if v > 100 else 0)
                sh_h = max(2, int(spr.height * 0.42)); al = al.resize((spr.width, sh_h), Image.NEAREST)
                shear = int(sh_h * 1.1); sm = Image.new('L', (spr.width + shear, sh_h), 0)
                for row in range(sh_h): sm.paste(al.crop((0, row, spr.width, row + 1)), (int(shear * (1 - row / sh_h)), row))
                shd = Image.new('RGBA', sm.size, (12, 14, 10, 255)); shd.putalpha(sm.point(lambda v: int(v * 0.35)))
                self.img.alpha_composite(shd, (qa - spr.width // 2, qb - sh_h))
                self.img.alpha_composite(spr, (qa - spr.width // 2, qb - spr.height + 2))

    # ---------- вывод ----------
    def render(self, key):
        sh = self.shadow.filter(ImageFilter.GaussianBlur(1.2)).point(lambda v: int(v * 0.42))
        dark = Image.new('RGBA', self.img.size, (12, 14, 10, 255))
        dark.putalpha(sh)
        self.img.alpha_composite(dark)
        for _, f in sorted(self.objs, key=lambda o: o[0]):
            f()
        self.draw_roofs()
        import json
        lay = {'W': self.W, 'H': self.H, 'M': self.M, 'grounds': self.ex['grounds'], 'walls': self.ex['walls'], 'props': self.ex['props'],
               'decals': self.ex['decals'], 'roofs': self.roofs, 'marks': [(x, y, lab) for (x, y, col, lab) in self.marks]}
        os.makedirs(ROOT_LAYOUT, exist_ok=True)
        with open(os.path.join(ROOT_LAYOUT, f't0-{key}.json'), 'w', encoding='utf-8') as fh:
            json.dump(lay, fh, ensure_ascii=False)
        im = self.img.convert('RGB')
        im.save(OUT + f't0-{key}-mock.png')
        im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(OUT + f't0-{key}-mock-half.png')
        self.plan(key)
        print(key, im.size)

    def plan(self, key, S=14):
        M = 3
        W, H = self.W + 2 * M, self.H + 2 * M
        side = 440
        im = Image.new('RGB', (W * S + side, max(H * S + 40, 620)), (36, 40, 36))
        d = ImageDraw.Draw(im)
        P = lambda x, y: (int((x + M) * S), int((y + M) * S) + 40)
        d.rectangle([P(-M, -M), P(self.W + M, self.H + M)], fill=(52, 60, 46))
        f, fb, ft = font(13), font(14, True), font(18, True)
        for poly, col, lab in self.zones:
            d.polygon([P(*p) for p in poly], fill=col)
        for poly, col, lab in self.zones:
            if lab:
                x = min(p[0] for p in poly); y = min(p[1] for p in poly)
                d.text((P(x, y)[0] + 4, P(x, y)[1] + 2), lab, fill=(225, 225, 210), font=f)
        for (x0, y0, x1, y1, col, lab) in self.plan_props:
            d.rectangle([P(x0, y0), P(x1, y1)], fill=col, outline=(20, 20, 18))
        for (x0, y0, x1, y1, tex) in self.plan_walls:
            col = {'wallface_storefront': (150, 200, 230), 'wallface_door_wood': (240, 235, 215), 'wallface_door_metal': (240, 235, 215),
                   'wallface_shutter': (230, 110, 70)}.get(tex, (190, 188, 176))
            a, b = P(x0, y0); c, e = P(x1, y1)
            d.rectangle([a, b, max(a + 2, c), max(b + 2, e)], fill=col)
        for (x, y, col, lab) in self.marks:
            a, b = P(x, y)
            d.ellipse([a - 8, b - 8, a + 8, b + 8], fill=col, outline=(20, 20, 18))
            if lab:
                d.text((a + 11, b - 9), lab, fill=(255, 226, 140), font=fb)
        for (x, y, lab) in getattr(self, 'labels', []):
            d.text(P(x, y), lab, fill=(255, 226, 140), font=f)
        d.text((14, 10), self.title, fill=(240, 240, 230), font=ft)
        y = 50
        x0 = W * S + 20
        for line in self.notes:
            if line.startswith('#'):
                d.rectangle([x0, y + 3, x0 + 14, y + 17], fill=self.legend[line.split('|')[0]])
                d.text((x0 + 22, y), line[1:].split('|')[1], fill=(225, 225, 215), font=f)
                y += 22
            else:
                bold = line.startswith('!')
                d.text((x0, y), line.lstrip('!'), fill=(240, 240, 230), font=fb if bold else f)
                y += 20
        im.save(OUT + f't0-{key}-plan.png')


ROOT_LAYOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets', 'layouts')

# ---------- общие куски ----------
ROOF_ITEMS = ['roof_vent_box', 'roof_vent_mushroom', 'roof_chimney_brick', 'roof_exhaust_pipe', 'roof_hatch', 'roof_ac_unit', 'roof_antenna', 'roof_panels', 'roof_junk', 'roof_water_tank']
CL = ['clutter/clutter_tire', 'clutter/clutter_pallet', 'clutter/clutter_planks', 'clutter/clutter_canister', 'clutter/clutter_bucket']
SCRAP = ['props/barrel_oil', 'props/barrel_oil', 'props/barrel_water']
C_ZONE = {'asph': (70, 72, 70), 'grav': (88, 86, 76), 'dirt': (86, 72, 56), 'conc': (110, 108, 100), 'floor': (122, 98, 80), 'yard': (78, 70, 54)}
C_CAR, C_LOOT, C_SCRAP, C_RECY, C_COVER = (150, 130, 60), (210, 170, 60), (170, 80, 60), (70, 140, 90), (120, 120, 130)
LEGEND = {'#car': C_CAR, '#loot': C_LOOT, '#scrap': C_SCRAP, '#recy': C_RECY, '#cover': C_COVER, '#punch': (90, 200, 250), '#door': (240, 235, 215),
          '#glass': (150, 200, 230), '#shut': (230, 110, 70)}


def road_v(s, x0, x1, dash=True):
    s.ground('ground_gravel', s.rect(x0 - 1.5, -s.M, x1 + 1.5, s.H + s.M), 5)
    s.ground('ground_asphalt', s.rect(x0, -s.M, x1, s.H + s.M), 2)
    if dash:
        for y in range(-s.M, s.H + s.M, 4):
            s.dec('decal_road_line', (x0 + x1) / 2, y)
    s.keepout.append((x0 - 1.5, -s.M - 5, x1 + 1.5, s.H + s.M + 5))


def road_h(s, y0, y1, dash=True):
    s.ground('ground_gravel', s.rect(-s.M, y0 - 1.5, s.W + s.M, y1 + 1.5), 5)
    s.ground('ground_asphalt', s.rect(-s.M, y0, s.W + s.M, y1), 2)
    if dash:
        for x in range(-s.M, s.W + s.M, 4):
            s.dec('decal_road_line', x, (y0 + y1) / 2, 1.0, 90)
    s.keepout.append((-s.M - 5, y0 - 1.5, s.W + s.M + 5, y1 + 1.5))


def cracks(s, area, n, avoid=()):
    x0, y0, x1, y1 = area
    for _ in range(n):
        x, y = random.uniform(x0, x1), random.uniform(y0, y1)
        if any(a <= x <= c and b <= y <= d for (a, b, c, d) in avoid):
            continue
        s.dec(random.choice(['decal_asphalt_crack', 'decal_asphalt_crack', 'decal_debris', 'decal_pebbles', 'decal_grass_tuft_2']), x, y, 1.0,
              random.choice([0, 90, 180, 270]))


def tufts(s, area, n, avoid=()):
    x0, y0, x1, y1 = area
    for _ in range(n):
        x, y = random.uniform(x0, x1), random.uniform(y0, y1)
        if any(a <= x <= c and b <= y <= d for (a, b, c, d) in avoid):
            continue
        s.dec(random.choice(['decal_grass_tuft_1', 'decal_grass_tuft_2', 'decal_pebbles', 'decal_debris', 'decal_leaves']), x, y)


# =====================================================================================
def gai():
    s = Scene('Пост ГАИ — РТ тира 0 (план, север сверху)', 72, 54, seed=11)
    s.legend = LEGEND
    s.ground('ground_grass')
    s.ground('ground_dirt', [(14, 3), (31, 2), (31, 52), (16, 52), (12, 30), (10, 22), (12, 12)], 8)
    s.ground('ground_dirt', [(-9, 17), (12, 16.5), (13, 21.5), (-9, 22)], 6)   # грунтовка в лес
    s.ground('ground_dirt', [(39, 10), (56, 2), (72, 2), (72, 48), (58, 52), (39, 46)], 6)
    road_v(s, 31, 39)
    s.ground('ground_asphalt', [(39, 15), (53, 15), (54, 38), (39, 40)], 3, zone=(C_ZONE['asph'], 'площадка поста'))
    s.ground('ground_asphalt', [(31, 30), (17, 33), (16, 49), (31, 51)], 3, zone=(C_ZONE['asph'], 'карман досмотра'))
    s.ground('ground_gravel', [(54, 4), (71, 4), (71, 46), (62, 46), (62, 30), (54, 30)], 4, zone=(C_ZONE['grav'], 'штрафстоянка'))
    s.zones.insert(0, ([(31, -3), (39, -3), (39, 57), (31, 57)], C_ZONE['asph'], 'трасса'))
    s.zones.append(([(14, 3), (31, 2), (31, 30), (17, 33), (12, 30), (10, 22), (12, 12)], C_ZONE['dirt'], 'обочина, свалка'))
    s.zones.insert(1, s.zones.pop())
    # декали
    cracks(s, (31, -9, 39, 63), 26)
    cracks(s, (39, 15, 54, 39), 12)
    cracks(s, (17, 33, 31, 50), 10)
    for (x, y, k) in [(47, 31.2, 1.4), (24, 45.5, 2.0), (59, 9, 1.2), (66, 21.6, 1.2), (58, 26, 1.3), (64, 12, 1.3), (35, 20, 1.2)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(36, 33), (44, 36), (21, 38), (60, 40), (33.5, 8)]:
        s.dec('decal_oil_puddle', x, y, 1.2)
    tufts(s, (54, 4, 71, 46), 40, [(62, 30, 72, 47)])
    tufts(s, (6, 2, 31, 52), 50)
    # штрафстоянка: забор из профлиста, ворота к посту, дыра на востоке
    s.walls([(54, 20), (54, 4), (71, 4), (71, 46), (62, 46), (62, 30), (54, 30), (54, 26)], 2.2, 'wallface_metal', gaps=[(71, 31, 71, 34.5), (60, 4, 63, 4)])
    s.prop('props/shed_canopy', 64.5, 13.5, plan=(C_COVER, ''))
    s.prop('props/recycler', 64, 12.2, plan=(C_RECY, ''))
    s.mark(64, 11, C_RECY, 'переработчик')
    for (x, y, fl) in [(58, 9, False), (58.5, 25.5, True), (66.5, 21.6, True), (66.8, 29, False), (66.5, 38, True), (66.8, 43.5, False)]:
        s.prop('props/car_zhiguli_wreck', x, y, fl, plan=(C_CAR, ''))
    s.prop('props/car_wreck', 58, 17, True)
    for (x, y) in [(55.2, 5.6), (56, 6), (55.4, 6.8), (70, 44.8), (69.2, 45.3), (63, 45.2), (70.2, 26), (55.5, 29)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(69, 8), (60, 36), (64.5, 33.5)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    s.prop('props/crate_wood', 63.5, 44.5, plan=(C_LOOT, ''))
    s.mark(63.5, 43.5, C_LOOT, 'ящик')
    for (x, y) in [(56, 12), (61, 19), (69.5, 15), (56.4, 21.3), (69, 33), (57, 33), (64, 40)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    # пост
    s.prop('props/gai_post', 45, 25.6, plan=(C_COVER, ''))
    s.labels = [(42, 18.5, 'будка ГАИ')]
    s.prop('props/gai_barrier', 35.2, 27.2, True, plan=(C_COVER, ''))
    s.labels.append((24.4, 26.2, 'шлагбаум'))
    s.prop('props/car_police_2101', 47.5, 31.8, plan=(C_CAR, ''))
    s.mark(47.5, 30.8, (90, 200, 250), 'ПЕРФОКАРТА: бардачок «копейки»')
    s.prop('props/phone_booth', 51.5, 20.5, plan=(C_COVER, ''))
    s.prop('props/subst_lamp', 40.6, 17.6)
    s.prop('props/subst_lamp', 29.6, 40.5, True)
    for (x, y) in [(41.2, 37.5), (42.4, 38)]:
        s.prop('props/barrel_oil', x, y, plan=(C_SCRAP, ''))
    s.prop('props/fuel_truck_wreck', 46.5, 38.2, True, plan=(C_CAR, ''))
    s.prop('props/int_table', 50.3, 26.2)
    s.prop('props/concrete_barrier', 51.5, 33.6, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 52.8, 33.8, plan=(C_COVER, ''))
    # дорога: шикана из блоков, конусы, знаки
    for (x, y) in [(32.2, 13.4), (33.5, 13.6), (34.8, 13.4), (35.4, 41.6), (36.7, 41.8), (38, 41.6)]:
        s.prop('props/concrete_barrier', x, y, plan=(C_COVER, ''))
    for (x, y) in [(32.2, 23.5), (33.6, 24.2), (37.6, 31.2), (38.2, 23.2), (32, 31.5)]:
        s.prop('clutter/clutter_cone', x, y)
    s.prop('clutter/road_sign_round', 30, 9)
    s.prop('clutter/road_sign_round', 40.2, 46, True)
    s.prop('clutter/clutter_sign', 40.4, 6)
    s.prop('clutter/clutter_sign', 29.8, 51, True)
    # карман досмотра: брошенный автобус
    s.prop('props/bus_wreck', 23.5, 46.6, plan=(C_CAR, ''))
    s.labels.append((18, 47.5, 'автобус ЛиАЗ'))
    for (x, y) in [(18, 35), (18.9, 35.4), (29.5, 49)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/car_zhiguli_wreck', 25, 37.5, True, plan=(C_CAR, ''))
    for (x, y) in [(20, 40), (28, 34), (18.5, 43)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    # обочина-свалка на северо-западе: разбитая машина у блоков, хлам, второй ящик
    s.prop('props/car_zhiguli_wreck', 23, 15.5, plan=(C_CAR, ''))
    s.prop('props/concrete_barrier', 26.8, 16.4, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 28, 18.8, plan=(C_COVER, ''))
    for (x, y) in [(15, 9), (12, 21), (25, 7.5)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(10, 13), (10.8, 13.4), (19, 26), (27, 27)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/crate_military', 13.6, 22.2, plan=(C_LOOT, ''))
    s.mark(13.6, 21.4, C_LOOT, 'ящик')
    for (x, y) in [(18, 11), (21, 22), (12.5, 16), (16, 27), (27, 4)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    for (x, y) in [(8, 26), (8.5, 6), (30, 1.5), (52, 44), (40, 44.5), (53, 14)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    s.prop('props/subst_ktp', 18.5, 5.5, plan=(C_COVER, ''))
    s.prop('props/car_zhiguli_wreck', 6, 20.6, True, plan=(C_CAR, ''))
    s.prop('props/car_wreck', 16, 31)
    for (x, y) in [(27.5, 11), (28.3, 11.4), (13.5, 27.5), (14.2, 28)]:
        s.prop('clutter/clutter_tire', x, y)
    s.prop('props/concrete_barrier', 17.2, 24.2, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 18.6, 24.5, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 17.8, 23.3, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 25.5, 26, plan=(C_COVER, ''))
    for (x, y) in [(43.5, 41.5), (52.5, 41), (57.5, 44), (59, 49), (46, 46)]:
        s.prop('props/junk_pile' if random.random() < .4 else random.choice(CL), x, y)
    s.weeds((10, 2, 31, 53), 260, [(31, 0, 39, 60)])
    s.weeds((39, 2, 72, 52), 260, [(39, 15, 54, 40), (54, 4, 71, 30), (62, 30, 71, 46)])
    s.weeds((54, 4, 71, 46), 60, [(62, 30, 72, 47)], bush=0.03)
    s.weeds((16, 33, 31, 50), 18, bush=0)
    s.weeds((39, 15, 54, 40), 22, bush=0)
    s.forest([(10, 1, 31, 53), (39, 1, 73, 48), (39, 44, 60, 53), (-9, 16.5, 13, 22)], 0.5)
    s.notes = ['#car|машины (укрытия, бардачки)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#recy|переработчик', '#cover|укрытия, постройки',
               '#punch|перфокарта I', '', '!Как проходить:', '1. Подойти к патрульной «копейке»', '   у поста (машина заперта).', '2. Вскрыть бардачок через',
               '   приоткрытую дверь: 4 с, лязг 25 м.', '3. Перфокарта I — в руки.', '', '!Рядом:', 'переработчик под навесом',
               'на штрафстоянке (вход — ворота', 'со стороны поста, дыра на востоке).', '2 ящика: штрафстоянка, свалка.', '',
               'Трасса насквозь, выходы на север', 'и юг по дороге + в лес отовсюду.', 'Размер ~72×54 м. Клетка = 1 м.']
    s.render('gai')


# =====================================================================================
def shop():
    s = Scene('Сельмаг «Продукты» — РТ тира 0 (план, север сверху)', 62, 52, seed=23)
    s.legend = LEGEND
    s.ground('ground_grass')
    road_h(s, 43, 50)
    s.ground('ground_dirt', [(5, 11), (15, 11), (15, 3), (55, 3), (55, 21), (51, 23), (51, 32), (44, 33), (5, 33)], 6, zone=(C_ZONE['yard'], 'хоздвор'))
    s.ground('ground_asphalt', [(4, 33.5), (46, 33.5), (50, 41.5), (2, 41.5)], 3, zone=(C_ZONE['asph'], 'площадка у входа'))
    s.ground('ground_gravel', [(34, 20), (46, 19), (47, 30), (34, 31)], 5)
    s.ground('floor_linoleum', s.rect(10, 20, 34, 33), 0, zone=(C_ZONE['floor'], 'торговый зал'))
    s.ground('floor_tile', s.rect(24, 8, 33, 20), 0, zone=((120, 104, 88), 'подсобка'))
    s.ground('ground_concrete', s.rect(33, 8, 42, 20), 0, zone=(C_ZONE['conc'], 'склад'))
    s.zones.insert(0, ([(-3, 43), (65, 43), (65, 50), (-3, 50)], C_ZONE['asph'], 'дорога'))
    cracks(s, (-9, 43, 71, 50), 22)
    cracks(s, (3, 34, 48, 41), 14)
    tufts(s, (5, 3, 56, 31), 70, [(9.5, 7.5, 42.5, 33.5)])
    for (x, y, k) in [(29, 39.6, 1.4), (46, 25, 1.2), (14, 12.6, 1.2), (52, 47, 1.6)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(8, 38), (40, 37), (20, 46), (48, 14)]:
        s.dec('decal_oil_puddle', x, y, 1.2)
    for (x, y) in [(15, 27), (21, 30), (27, 22.5), (30, 9), (37, 14)]:
        s.dec('decal_debris', x, y)
    # здание: Г — зал (юг) + крыло подсобка/склад (северо-восток)
    ST, SF = 'wallface_stucco', 'wallface_storefront'
    s.walls([(10, 33), (10, 20), (24, 20)], 3.2, ST)
    s.walls([(24, 20), (24, 8), (42, 8), (42, 20)], 3.2, ST, gaps=[(42, 12, 42, 15.5)])
    s.walls([(42, 20), (34, 20), (34, 33)], 3.2, ST)
    s.wall(24, 20, 28, 20, 3.2, ST); s.wall(29.4, 20, 34, 20, 3.2, ST)
    s.wall(28, 20, 29.4, 20, 3.2, 'wallface_door_wood')        # заклинившая дверь подсобки
    s.wall(33, 8, 33, 20, 3.2, ST)                                 # подсобка | склад — глухая
    s.wall(10, 33, 19, 33, 3.2, SF); s.wall(21.6, 33, 34, 33, 3.2, SF)
    s.labels = [(29.8, 18.6, 'дверь заклинило'), (19, 34.2, 'вход'), (42.6, 12.2, 'вход в склад')]
    s.roof(10, 20, 34, 33, 3.2, 'roof_slate'); s.roof(24, 8, 42, 20, 3.2, 'roof_slate')
    # вход в склад со двора (восточная стена) — крыльцо и фонарь, чтобы дверь читалась под крышей
    s.prop('props/porch_side', 43.5, 15.2, True); s.prop('props/door_lamp_post', 43.4, 11.6)
    # зал
    for x in (12, 14, 16, 18, 20.6):
        s.prop('props/shop_shelf', x, 20.7, plan=(C_COVER, ''))
    for x in (13, 15.3, 17.6):
        s.prop('props/shop_counter', x, 24.6, plan=(C_COVER, ''))
    s.prop('props/cash_register', 20.4, 24.8, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 32.6, 23.2, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 32.6, 26.2, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 31.4, 20.8, plan=(C_COVER, ''))
    s.prop('props/shop_shelf', 11, 29.5, True, plan=(C_COVER, ''))
    for x in (14.5, 16.3, 24.5, 26.3):          # островные стеллажи посреди зала
        s.prop('props/shop_shelf', x, 29.2, x > 20, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 11, 23.4, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 11, 26.4, plan=(C_COVER, ''))
    s.prop('props/bottle_crates', 29.5, 28.5, plan=(C_SCRAP, ''))
    s.prop('props/beer_kegs', 20.5, 31.6, plan=(C_SCRAP, ''))
    s.prop('props/crate_wood', 13.5, 27, plan=(C_LOOT, ''))
    for (x, y) in [(18.5, 27), (22, 22.4), (31.5, 30.8), (12.5, 32)]:
        s.prop(random.choice(['clutter/clutter_pallet', 'clutter/clutter_planks', 'clutter/clutter_bucket']), x, y)
    s.prop('props/bottle_crates', 30.5, 31.6, plan=(C_SCRAP, ''))
    s.prop('props/bottle_crates', 25, 28, plan=(C_SCRAP, ''))
    for (x, y) in [(23, 31), (14, 31.6), (27.5, 25)]:
        s.prop(random.choice(['clutter/clutter_bucket', 'clutter/clutter_planks', 'clutter/clutter_pallet']), x, y)
    # подсобка (перфокарта)
    s.prop('props/int_shelf', 25.4, 8.6, plan=(C_COVER, ''))
    s.prop('props/shop_shelf', 31.4, 8.7, plan=(C_COVER, ''))
    s.prop('props/int_desk', 27.4, 12.4, plan=(C_COVER, ''))
    s.mark(27.4, 11.4, (90, 200, 250), 'ПЕРФОКАРТА: стол завмага')
    s.prop('props/crate_wood', 31.6, 17.6, plan=(C_LOOT, ''))
    s.mark(31.6, 17, C_LOOT, 'еда')
    s.prop('props/int_burzhuika', 25, 14.5)
    s.prop('props/bottle_crates', 25.2, 19.2, plan=(C_SCRAP, ''))
    # склад
    s.prop('props/int_shelf', 34.6, 8.6, plan=(C_COVER, ''))
    s.prop('props/int_shelf', 36.4, 8.6, plan=(C_COVER, ''))
    s.prop('props/beer_kegs', 39.6, 10, plan=(C_SCRAP, ''))
    s.prop('props/bottle_crates', 35.5, 15, plan=(C_SCRAP, ''))
    s.prop('props/bottle_crates', 36.8, 15.4, plan=(C_SCRAP, ''))
    s.prop('props/crate_military', 39.5, 18.6, plan=(C_LOOT, ''))
    s.mark(39.5, 18, C_LOOT, 'ящик')
    # хоздвор: забор из досок, калитка на западе, пролом на севере, ворота к площадке
    s.walls([(10, 26), (6, 26), (6, 12), (16, 12), (16, 4), (54, 4), (54, 22), (50, 22), (50, 30), (34, 30)], 1.9, 'wallface_wood', gaps=[(6, 16, 6, 19.5), (28, 4, 31.5, 4), (42, 30, 47, 30)])
    s.labels += [(1, 14.6, 'калитка'), (27, 1.4, 'пролом'), (42, 30.6, 'ворота')]
    s.prop('props/beer_kegs', 46, 23, plan=(C_SCRAP, ''))
    for (x, y) in [(44.2, 26), (45.6, 26.4), (51, 10), (52.2, 10.5), (49, 18)]:
        s.prop('props/bottle_crates', x, y, plan=(C_SCRAP, ''))
    s.prop('props/car_zhiguli_wreck', 12, 18.4, True, plan=(C_CAR, ''))
    s.prop('props/fuel_truck_wreck', 48, 7.2, plan=(C_CAR, ''))
    for (x, y) in [(17.5, 5.6), (18.3, 6), (21.5, 5.5), (48.6, 28.8), (49.3, 28.2), (36, 28.8), (7.6, 24.6)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(19, 10), (9, 23.5), (47, 24), (37.5, 24)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(8, 14), (21, 16), (8.5, 21), (41, 27), (51, 19), (46, 12), (18.5, 6)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    s.prop('clutter/clutter_bicycle', 9, 31.5)
    # площадка у входа и дорога
    s.prop('props/car_zhiguli_wreck', 30, 40.2, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 11, 39.6, True, plan=(C_CAR, ''))
    s.prop('props/car_police_2101', 47.5, 39.2, True, plan=(C_CAR, ''))
    for (x, y) in [(43.5, 34.6), (44.3, 35), (4.5, 34.6)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/bottle_crates', 36, 34.4, plan=(C_SCRAP, ''))
    s.prop('props/bottle_crates', 8.5, 34.5, plan=(C_SCRAP, ''))
    s.prop('props/phone_booth', 40, 35.6, plan=(C_COVER, ''))
    s.prop('props/subst_lamp', 45, 41.2)
    s.prop('props/bus_wreck', 56, 48.6, True, plan=(C_CAR, ''))
    s.prop('props/concrete_barrier', 6, 36, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 7.4, 36.2, plan=(C_COVER, ''))
    s.prop('clutter/road_sign_round', 2, 42.2)
    s.prop('clutter/clutter_sign', 59, 42.2)
    for (x, y) in [(13, 37), (23, 36), (36, 39.5)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    for (x, y) in [(3, 30), (57, 6), (57, 30), (3, 6), (47, 33)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    s.weeds((5, 3, 56, 31), 900, [(9.5, 7.5, 42.5, 33.5), (0, 0, 15.5, 11.5), (50.5, 22.5, 60, 40)])
    s.weeds((2, 33, 50, 42), 30, bush=0)
    s.forest([(1, 11, 57, 42), (14, 2, 57, 12), (1, 22, 50, 42)], 0.5)
    s.notes = ['#car|машины (укрытия)', '#scrap|бочки, хлам, тара с ломом', '#loot|ящики / еда', '#cover|прилавки, стеллажи, укрытия',
               '#punch|перфокарта I', '#glass|витрины (битое стекло)', '#door|заклинившая дверь', '', '!Как проходить:', '1. Зал → дверь подсобки за прилавком.',
               '2. Её заклинило: выбить — 4 удара', '   (любым оружием/инструментом),', '   грохот слышно на 40 м.', '3. В подсобке на столе завмага —',
               '   перфокарта I, рядом ящик с едой.', '', '!Рядом:', 'склад с отдельным входом со двора', '(бочки, кеги, ящик). Переработчика нет.',
               '', 'Выходы: витрины и дверь (юг), ворота,', 'калитка (запад), пролом (север).', 'Размер ~58×47 м. Клетка = 1 м.']
    s.render('shop')


# =====================================================================================
def fuel():
    s = Scene('Заправка «Последняя капля» — РТ тира 0 (план, север сверху)', 76, 52, seed=37)
    s.legend = LEGEND
    s.ground('ground_grass')
    road_h(s, 44, 51)
    s.ground('ground_dirt', [(30, 0), (60, 0), (60, 8), (30, 8)], 5, zone=(C_ZONE['yard'], 'задний двор'))
    s.ground('ground_concrete', [(4, 17), (58, 17), (62, 26), (74, 26), (74, 43), (0, 43)], 3, zone=(C_ZONE['conc'], 'площадка АЗС'))
    s.ground('ground_asphalt', [(10, 30), (44, 30), (46, 43), (8, 43)], 3)
    s.ground('ground_gravel', s.rect(0, 5, 14, 17), 3, zone=(C_ZONE['grav'], 'мойка'))
    s.ground('ground_concrete', s.rect(60, 4, 74, 24), 2, zone=(C_ZONE['conc'], 'ёмкости ГСМ'))
    s.ground('ground_grass', s.rect(20, 41.5, 38, 44), 1)
    s.ground('floor_linoleum', s.rect(34, 8, 50, 18), 0, zone=(C_ZONE['floor'], 'павильон'))
    s.ground('floor_tile', s.rect(50, 8, 56, 18), 0, zone=((120, 104, 88), 'подс.'))
    s.zones.insert(0, ([(-3, 44), (79, 44), (79, 51), (-3, 51)], C_ZONE['asph'], 'трасса'))
    cracks(s, (-9, 44, 85, 51), 26)
    cracks(s, (0, 17, 74, 43), 40, [(20, 41, 38, 44)])
    tufts(s, (30, 0, 60, 8), 30)
    tufts(s, (0, 5, 14, 17), 20)
    for (x, y, k) in [(19, 30.4, 1.4), (25, 30.6, 1.2), (31, 30.2, 1.3), (63, 34.6, 1.8), (8, 13, 1.6), (44, 5, 1.2), (66, 14, 1.4), (66, 21, 1.4)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(14, 38), (36, 35), (52, 40), (6, 22), (70, 30), (48, 24)]:
        s.dec('decal_oil_puddle', x, y, 1.3)
    # навес с колонками
    s.ground('ground_concrete', s.rect(11, 29.6, 38.5, 31), 0)
    for x in (12.5, 17, 21.5, 28, 32.5, 37):
        s.prop('props/fuel_pump', x, 30.6, plan=(C_COVER, ''))
    s.prop('props/fuel_canopy', 17.2, 31.4, plan=(C_COVER, ''))
    s.prop('props/fuel_canopy', 32.2, 31.4, plan=(C_COVER, ''))
    s.labels = [(16, 22, '2 навеса, 6 колонок')]
    s.prop('props/car_zhiguli_wreck', 15, 34.8, True, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 34.5, 35.2, plan=(C_CAR, ''))
    s.prop('props/car_wreck', 25, 36.5, True)
    for (x, y) in [(10.8, 31.6), (24, 31.2), (39, 31.5), (30.2, 32.6), (19.4, 33)]:
        s.prop('clutter/clutter_canister', x, y)
    # шиномонтаж (будка) и стоянка
    s.prop('props/subst_kpp', 6, 37, plan=(C_COVER, ''))
    s.labels.append((2, 38, 'шиномонтаж'))
    for (x, y) in [(9.2, 36.4), (9.8, 37), (9.0, 37.6), (3, 39.8), (10.4, 38.2)]:
        s.prop('clutter/clutter_tire', x, y)
    s.prop('props/car_zhiguli_wreck', 49, 23, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 44.5, 28.4, True, plan=(C_CAR, ''))
    s.prop('props/phone_booth', 57, 19.5, plan=(C_COVER, ''))
    for (x, y) in [(41, 21.2), (42.4, 21.4), (43.8, 21.2)]:
        s.prop('props/concrete_barrier', x, y, plan=(C_COVER, ''))
    s.prop('props/bottle_crates', 51.5, 20, plan=(C_SCRAP, ''))
    # павильон оператора: витрина на юг, касса, подсобка
    ST, SF = 'wallface_stucco', 'wallface_storefront'
    s.walls([(34, 18), (34, 8), (56, 8), (56, 18), (50, 18)], 3.2, ST, gaps=[(52, 8, 53.5, 8)])
    s.wall(52, 8, 53.5, 8, 3.2, "wallface_door_metal")
    s.wall(34, 18, 39.5, 18, 3.2, SF); s.wall(41.8, 18, 50, 18, 3.2, SF)
    s.roof(34, 8, 56, 18, 3.2, 'roof_metal')
    # задняя дверь павильона (северная стена) — крыльцо и фонарь
    s.prop('props/porch_front', 52.75, 7.7); s.prop('props/door_lamp_post', 54.6, 7.6)
    s.walls([(50, 8), (50, 18)], 3.2, ST, gaps=[(50, 14, 50, 15.6)])
    s.prop('props/shop_counter', 39.2, 12.4, plan=(C_COVER, ''))
    s.prop('props/shop_counter', 41.6, 12.4, plan=(C_COVER, ''))
    s.prop('props/cash_register', 44.2, 12.6, plan=(C_COVER, ''))
    s.mark(44.2, 11.6, (90, 200, 250), 'ПЕРФОКАРТА: касса под стеклом')
    for x in (35.6, 37.6):
        s.prop('props/shop_shelf', x, 8.7, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 48.6, 9, plan=(C_COVER, ''))
    s.prop('props/fridge_display', 47.4, 9, plan=(C_COVER, ''))
    s.prop('props/int_shelf', 52, 8.6, plan=(C_COVER, ''))
    s.prop('props/int_lockers', 54.4, 8.6, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 53.6, 16.6, plan=(C_LOOT, ''))
    s.mark(53.6, 16, C_LOOT, 'ящик')
    s.prop('props/bottle_crates', 36, 16, plan=(C_SCRAP, ''))
    s.labels += [(38.8, 18.6, 'вход')]
    # задний двор: переработчик за павильоном
    s.prop('props/recycler', 41, 5.4, plan=(C_RECY, ''))
    s.mark(41, 4.4, C_RECY, 'переработчик')
    for (x, y) in [(36.2, 3), (37, 3.4), (47.6, 6), (48.4, 5.6), (57.2, 6.4), (31.6, 6.4)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 52.5, 4.5, plan=(C_SCRAP, ''))
    for (x, y) in [(33.5, 2.2), (45, 2.4), (55.5, 2.2)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    # мойка
    s.prop('props/car_wash_frame', 7, 12.2, plan=(C_COVER, ''))
    s.prop('props/car_zhiguli_wreck', 7.2, 21.5, plan=(C_CAR, ''))
    for (x, y) in [(1.6, 7), (12.5, 6.4), (2.2, 15.8)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    s.prop('props/barrel_water', 12.6, 15.6, plan=(C_SCRAP, ''))
    # ёмкости ГСМ за низкой обваловкой
    s.walls([(60, 24), (60, 4), (74, 4), (74, 24), (68, 24)], 1.0, 'wallface_concrete', t=0.5, gaps=[])
    s.prop('props/subst_oil_tank', 67, 13, plan=(C_COVER, ''))
    s.prop('props/subst_oil_tank', 67, 20.5, plan=(C_COVER, ''))
    s.prop('props/subst_lamp', 61.5, 25)
    for (x, y) in [(71.5, 6), (72.3, 6.5), (62.4, 6.2)]:
        s.prop('props/barrel_oil', x, y, plan=(C_SCRAP, ''))
    # бензовоз, хлам на площадке
    s.prop('props/fuel_truck_wreck', 64, 35.5, plan=(C_CAR, ''))
    s.prop('props/crate_military', 71.5, 40.5, plan=(C_LOOT, ''))
    s.mark(71.5, 39.8, C_LOOT, 'ящик')
    for (x, y) in [(53, 28), (54, 28.4), (3, 26), (3.8, 26.5), (47, 41)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 57, 21, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 4, 31, plan=(C_SCRAP, ''))
    for (x, y) in [(10, 27), (38, 24), (50, 33), (56, 38), (13, 40), (42, 38), (3, 38)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    for (x, y) in [(12, 25.5), (13.4, 25.7)]:
        s.prop('props/concrete_barrier', x, y, plan=(C_COVER, ''))
    s.prop('props/price_pylon', 29, 43, plan=(C_COVER, ''))
    s.prop('props/subst_lamp', 2, 42.5)
    s.prop('props/subst_lamp', 44, 42.5, True)
    s.prop('clutter/road_sign_round', 75, 43)
    s.prop('props/car_police_2101', 54, 48.4, True)
    for (x, y) in [(22, 42.8), (35, 42.6), (28, 1.5), (59, 1.5), (75, 3)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    s.ground('ground_dirt', [(15, 6), (31, 5), (33, 17), (16, 17.5)], 8)
    s.prop('props/bus_wreck', 24, 15.6, True, plan=(C_CAR, ''))
    s.labels.append((18, 16.4, 'автобус на приколе'))
    for (x, y) in [(18, 8), (30.5, 9), (17, 16.8)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(28.4, 7), (29.1, 7.4), (58, 15), (58.7, 15.5)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.weeds((15, 5, 33, 17.5), 160, bush=0.1)
    s.weeds((56, 8, 60, 26), 40, bush=0.2)
    s.weeds((29, -1, 61, 8), 140, bush=0.15)
    s.weeds((0, 5, 14, 17), 50, bush=0.05)
    s.weeds((60, 4, 74, 24), 50, bush=0.05)
    s.weeds((0, 17, 74, 43), 90, [(14, 22, 34, 32), (34, 7.5, 56.5, 18.5)], bush=0)
    s.forest([(-1, 4, 76, 44), (29, -1, 61, 9)], 0.5)
    s.notes = ['#car|машины, бензовоз (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#recy|переработчик', '#cover|навес, колонки, укрытия',
               '#punch|перфокарта I', '#glass|витрины павильона', '', '!Как проходить:', '1. Павильон оператора → прилавок.',
               '2. Касса под толстым стеклом:', '   разбить — 3 с, звон на 30 м.', '3. Перфокарта I — в руки.', '', '!Рядом:',
               'переработчик за павильоном', '(задний двор, дверь из подсобки).', 'Ящики: подсобка павильона,', 'у бензовоза.', '',
               'Въезд/выезд с трассы, мойка (запад),', 'ёмкости ГСМ (восток), в лес — отовсюду.', 'Размер ~74×46 м. Клетка = 1 м.']
    s.render('fuel')


if __name__ == '__main__':
    which = sys.argv[1:] or ['gai', 'shop', 'fuel']
    for w in which:
        {'gai': gai, 'shop': shop, 'fuel': fuel}[w]()
    # планировки → C# (строка JSON на РТ; игра строит РТ по ним — макет и игра совпадают)
    out = ['// Сгенерировано scripts/mock-tier0.py — не править руками.', 'namespace Rastvordd.Sim', '{', '    public static class Tier0Layouts', '    {']
    for k in ('gai', 'shop', 'fuel'):
        fp = os.path.join(ROOT_LAYOUT, f't0-{k}.json')
        if os.path.exists(fp):
            js = open(fp, encoding='utf-8').read().replace('"', '""')
            out.append(f'        public const string {k.capitalize()} = @"{js}";')
    # хитбоксы всех пропсов из манифеста — общая таблица для готовых РТ
    out.append('        public static readonly System.Collections.Generic.Dictionary<string, (float w, float d, float h, bool legs)> Foot = new()')
    out.append('        {')
    for a in MAN.values():
        f = a.get('foot')
        if not f: continue
        legs = isinstance(f, dict)
        w, d, h = f['legs'] if legs else f
        out.append(f'            ["{a["id"]}"] = ({w}f, {d}f, {h}f, {"true" if legs else "false"}),')
    out.append('        };')
    out += ['    }', '}', '']
    open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'game', 'src', 'Sim', 'Tier0Layouts.g.cs'), 'w', encoding='utf-8').write(chr(10).join(out))
