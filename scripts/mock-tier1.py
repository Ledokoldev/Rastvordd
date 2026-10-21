# Макеты РТ тира 1 (Гаражи «Ракушки», АТС «Занято», Депо «Тупик», Карьер «Ковш») из игровых пиксельных спрайтов.
# Копия scripts/mock-tier0.py (32 px/м, высоты стен ×0.75, тени от силуэта) + метки механик MECH:* для игры.
# Запуск: python scripts/mock-tier1.py [garages|ats|depot|quarry]
#   → docs/design/img/t1-<rt>-mock.png, -mock-half.png, -inside-half.png (без крыш), -plan.png;
#   → assets/layouts/t1-<rt>.json и game/src/Sim/Tier1Layouts.g.cs
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops
import json, os, random, sys

ROOT = 'C:/Users/Xbaza/Desktop/Rastvordd/'
A = ROOT + 'game/art/'
OUT = ROOT + 'docs/design/img/'
PPM, HS = 32, 0.75
MAN = {a['id']: a for a in json.load(open(ROOT + 'assets/manifest.json', encoding='utf-8'))['assets']}
MAN.update({a['id']: a for a in json.load(open(ROOT + 'assets/manifest_t1.json', encoding='utf-8'))['assets']})
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
        self.lines = []            # линии на плане (трубы пневмопочты, пути): (pts, цвет, толщина)
        self.labels = []
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
        inside = self.img.convert('RGB')
        inside.resize((inside.width // 2, inside.height // 2), Image.LANCZOS).save(OUT + f't1-{key}-inside-half.png')
        self.draw_roofs()
        import json
        lay = {'W': self.W, 'H': self.H, 'M': self.M, 'grounds': self.ex['grounds'], 'walls': self.ex['walls'], 'props': self.ex['props'],
               'decals': self.ex['decals'], 'roofs': self.roofs, 'marks': [(x, y, lab) for (x, y, col, lab) in self.marks]}
        os.makedirs(ROOT_LAYOUT, exist_ok=True)
        with open(os.path.join(ROOT_LAYOUT, f't1-{key}.json'), 'w', encoding='utf-8') as fh:
            json.dump(lay, fh, ensure_ascii=False)
        im = self.img.convert('RGB')
        im.save(OUT + f't1-{key}-mock.png')
        im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(OUT + f't1-{key}-mock-half.png')
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
        for (pts, col, wd) in self.lines:
            d.line([P(*q) for q in pts], fill=col, width=wd)
        for (x, y, col, lab) in self.marks:
            a, b = P(x, y)
            d.ellipse([a - 8, b - 8, a + 8, b + 8], fill=col, outline=(20, 20, 18))
            if lab and not lab.startswith('MECH:'):
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
        im.save(OUT + f't1-{key}-plan.png')


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
# тир 1: общие куски
C_MECH, C_GUARD, C_PIPE, C_RAIL = (90, 200, 250), (225, 70, 60), (120, 210, 170), (150, 120, 100)
LEGEND.update({'#mech': C_MECH, '#guard': C_GUARD, '#pipe': C_PIPE, '#rail': C_RAIL})


def mech(s, x, y, tag, ru=None, col=C_MECH):
    """Метка механики для игры (MECH:…) + подпись по-русски на плане."""
    s.mark(x, y, col, 'MECH:' + tag)
    if ru:
        s.labels.append((x + 0.9, y - 0.7, ru))


def guard(s, x, y, weapon, ru=None, back=False):
    """Сторож: метка для игры + фигура на макете (только картинка, в планировку не идёт)."""
    mech(s, x, y, 'guard:' + weapon, ru or {'club': 'сторож (дубинка)', 'pipe': 'сторож (труба)', 'bow': 'сторож (лук)'}[weapon], C_GUARD)
    spr = L('chars/char_guard_' + ('b0' if back else 'f0'))
    a, b = s.px(x, y)
    s.add_shadow(spr.crop(spr.getchannel('A').getbbox()), a, b)
    s.objs.append((y, lambda spr=spr, a=a, b=b: s.img.alpha_composite(spr, (a - spr.width // 2, b - spr.height + 2))))


def lamp(s, x, y, flip=False):
    s.prop('props/subst_lamp', x, y, flip)
    s.mark(x, y - 0.3, (250, 230, 120), 'MECH:lamp')


def box_row(s, x0, y0, n, bw, d, h, tex, fronts, gate_tex='wallface_garage'):
    """Ряд гаражных боксов: задняя стена y0, ворота на южной стене y0+d (видны камере).
    fronts[i]: ('gate', k) — ворота k (метка MECH:gate:k в центре ворот), 'c' — чужие запертые ворота (без механики, просто стена),
    'weld' — заварены листом, 'open' — открыт, 'gone' — обрушен."""
    yf = y0 + d
    s.wall(x0, y0, x0 + n * bw, y0, h, tex)
    for i in range(n + 1):
        s.wall(x0 + i * bw, y0, x0 + i * bw, yf, h, tex)
    pil = (bw - 2.6) / 2
    for i, f in enumerate(fronts):
        x = x0 + i * bw
        if f == 'gone':
            continue
        s.wall(x, yf, x + pil, yf, h, tex)
        s.wall(x + bw - pil, yf, x + bw, yf, h, tex)
        if isinstance(f, tuple):
            s.wall(x + pil, yf, x + bw - pil, yf, h, gate_tex)
            mech(s, x + bw / 2, yf, 'gate:%d' % f[1])
            s.labels.append((x + bw / 2 - 0.3, yf + 0.7, '%d' % f[1]))
        elif f == 'weld':
            s.wall(x + pil, yf, x + bw - pil, yf, h, 'wallface_metal')
        elif f == 'c':                                   # чужой закрытый бокс: ворота как ворота, но без механики
            s.wall(x + pil, yf, x + bw - pil, yf, h, gate_tex)
    return yf


def track(s, pts, ballast=True):
    """Путь по ломаной: балласт (гравий) + звенья rail_track (6 м), повёрнутые вдоль участка (rot — градусы против часовой на экране)."""
    import math
    s.lines.append((pts, C_RAIL, 5))
    for (p, q) in zip(pts, pts[1:]):
        dx, dy = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(dx, dy)
        ux, uy = dx / ln, dy / ln
        nx, ny = -uy * 1.9, ux * 1.9
        if ballast:
            s.ground('ground_gravel', [(p[0] + nx - ux, p[1] + ny - uy), (q[0] + nx + ux, q[1] + ny + uy), (q[0] - nx + ux, q[1] - ny + uy), (p[0] - nx - ux, p[1] - ny - uy)], 2)
        ang = round(-math.degrees(math.atan2(dy, dx)), 1)
        k = max(1, int(math.ceil(ln / 9.2)))         # звено 6 м ×1,6: колея и шпалы в масштабе вагонов (ширина ~1,8 м)
        for i in range(k):
            t = (i + 0.5) / k
            s.dec('rail_track', p[0] + dx * t, p[1] + dy * t, 1.6, ang)
        s.keepout.append((min(p[0], q[0]) - 2, min(p[1], q[1]) - 2, max(p[0], q[0]) + 2, max(p[1], q[1]) + 2))


def pipe(s, pts):
    """Труба пневмопочты по полу: звенья ats_pipe_run (3 м) + точки MECH:pipe."""
    import math
    s.lines.append((pts, C_PIPE, 3))
    for (p, q) in zip(pts, pts[1:]):
        dx, dy = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(dx, dy)
        ang = round(-math.degrees(math.atan2(dy, dx)), 1)
        k = max(1, int(math.ceil(ln / 2.9)))
        for i in range(k):
            t = (i + 0.5) / k
            s.dec('ats_pipe_run', p[0] + dx * t, p[1] + dy * t, 1.0, ang)
        s.mark((p[0] + q[0]) / 2, (p[1] + q[1]) / 2, C_PIPE, 'MECH:pipe')


# =====================================================================================
def garages():
    s = Scene('Гаражи «Ракушки» — РТ тира 1, сложность 1/5 (план, север сверху)', 80, 62, seed=101)
    s.legend = LEGEND
    s.ground('ground_grass')
    road_h(s, 54, 61)
    s.ground('ground_dirt', [(4, 50), (4, 18), (10, 18), (10, 4), (64, 4), (64, 9), (76, 9), (76, 50)], 6, zone=(C_ZONE['yard'], 'территория ГСК'))
    s.ground('ground_asphalt', [(14, 50), (22, 50), (22, 54), (14, 54)], 2)
    s.ground('ground_asphalt', [(12, 14.3), (62, 14.3), (62, 25.3), (12, 25.3)], 3, zone=(C_ZONE['asph'], 'проезд 1'))
    s.ground('ground_asphalt', [(13, 25), (24, 25), (24, 50), (13, 50)], 3, zone=(C_ZONE['asph'], 'въезд'))
    s.ground('ground_gravel', [(24, 31.3), (60, 31.3), (61, 37.8), (24, 37.8)], 4, zone=(C_ZONE['grav'], 'проезд 2'))
    s.ground('ground_gravel', [(24, 44.3), (62, 44.3), (62, 49.6), (24, 49.6)], 4, zone=(C_ZONE['grav'], 'проезд 3'))
    s.ground('ground_gravel', [(62, 12), (75.5, 12), (75.5, 49.6), (62, 49.6)], 5, zone=(C_ZONE['grav'], 'свалка, мастерская'))
    cracks(s, (12, 15, 62, 25), 30)
    cracks(s, (13, 25, 24, 54), 14)
    tufts(s, (4, 4, 76, 50), 60, [(13, 7, 62, 14.5), (25, 25, 57, 31.5), (25, 37.5, 62, 44.5)])
    for (x, y, k) in [(38, 19, 1.6), (21, 20, 1.2), (52, 18, 1.3), (18, 38, 1.2), (66, 33, 1.5), (35, 34, 1.2), (66, 45, 1.4), (45, 47, 1.3)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(17, 30), (48, 22), (30, 35), (70, 28), (54, 47)]:
        s.dec('decal_oil_puddle', x, y, 1.2)
    # забор ГСК: бетонный, въезд со шлагбаумом (юг), пролом (восток), лаз (север)
    s.walls([(14, 50), (4, 50), (4, 18), (10, 18), (10, 4), (64, 4), (64, 9), (76, 9), (76, 50), (22, 50)], 2.2, 'wallface_concrete',
            gaps=[(76, 30, 76, 33.5), (40, 4, 42.5, 4)])
    s.labels += [(76.6, 31, 'пролом'), (39.6, 2.6, 'лаз'), (13.6, 52.6, 'въезд, шлагбаум')]
    # ряд А: кирпичные боксы 3,5×6 м; ворота 1–3, открытый бокс с «шестёркой», крайний обрушен
    A = ['c', ('gate', 1), 'c', 'weld', 'c', ('gate', 2), 'open', 'c', ('gate', 3), 'c', 'weld', 'c', 'gone']
    box_row(s, 14, 8, 13, 3.5, 6, 2.6, 'wallface_brick', A)
    s.roof(14, 8, 56, 14, 2.6, 'roof_tar')
    s.labels.append((14, 6.4, 'ряд А — кирпичные боксы'))
    s.prop('props/garage_ramp', 36.75, 15.7)                          # пандус у открытого бокса
    s.prop('props/gar_workbench_vise', 36.8, 9.0, plan=(C_COVER, ''))
    s.prop('props/gar_shelf_jars', 36.0, 13.6, plan=(C_COVER, ''))
    s.prop('props/gar_car_open', 37.0, 19.0, plan=(C_CAR, ''))
    mech(s, 37.8, 19.2, 'alarm_car', 'машина: сигналка, брелок в бардачке')
    for i in range(12):
        s.prop(random.choice(['props/gar_shelf_jars', 'props/gar_tire_stack', 'props/int_shelf', 'props/gar_workbench_vise']), 15.75 + i * 3.5, 9.0)
    mech(s, 43.75, 11.4, 'crate', 'КРЕЙТ (за воротами 3; игра может выбрать любые)')
    s.prop('props/gar_scrap_heap', 58.2, 13.4, plan=(C_SCRAP, ''))
    s.prop('props/crate_wood', 57.6, 10.4, plan=(C_LOOT, ''))
    s.labels.append((56.4, 15.0, 'обрушен'))
    # ряд Б: «ракушки» из профлиста (3 м), ворота 4 и 5
    B = ['c', ('gate', 4), 'c', 'weld', 'c', 'c', ('gate', 5), 'c', 'open', 'c']
    box_row(s, 26, 25.5, 10, 3.0, 5.5, 2.2, 'wallface_metal', B)
    s.roof(26, 25.5, 56, 31, 2.2, 'roof_metal')
    s.labels.append((26, 23.9, 'ряд Б — «ракушки»'))
    s.prop('props/gar_tire_stack', 50.6, 30.2)
    s.prop('props/garage_ramp', 50.5, 32.7)
    # ряд В: кирпичные боксы, ворота 6–8
    C = ['c', ('gate', 6), 'c', 'c', 'weld', ('gate', 7), 'c', 'c', ('gate', 8), 'c']
    box_row(s, 26, 38.3, 10, 3.5, 6, 2.6, 'wallface_brick', C)
    s.roof(26, 38.3, 61, 44.3, 2.6, 'roof_tar')
    s.labels.append((26, 36.9, 'ряд В'))
    # «ракушки» поодиночке у западного забора (под углом)
    for (x, y, fl) in [(8.0, 26.0, False), (8.0, 32.6, True), (8.4, 39.2, False)]:
        s.prop('props/gar_rakushka_angled', x, y, fl, plan=(C_COVER, ''))
    s.prop('props/gar_rakushka_angled', 60.5, 22.5, True, plan=(C_COVER, ''))
    # въезд: шлагбаум, сторожка, фонари
    s.prop('props/gai_barrier', 18.0, 51.4, plan=(C_COVER, ''))
    s.prop('props/gar_guard_hut', 8.8, 48.6, plan=(C_COVER, ''))
    s.labels.append((5.0, 44.4, 'сторожка'))
    s.prop('props/door_lamp_post', 11.2, 49.4)
    lamp(s, 24.6, 24.4); lamp(s, 50, 24.6, True); lamp(s, 24.4, 49.2); lamp(s, 61.6, 37.4, True)
    # свалка и общая мастерская под навесом на востоке
    s.prop('props/shed_canopy', 69, 49.0, plan=(C_COVER, ''))
    s.prop('props/gar_engine_stand', 66.8, 48.0, plan=(C_COVER, ''))
    s.prop('props/gar_workbench_vise', 70.8, 47.6, plan=(C_COVER, ''))
    s.prop('props/gar_car_jacks', 68, 30, plan=(C_CAR, ''))
    s.prop('props/gar_boat_trailer', 69.5, 17.5, plan=(C_COVER, ''))
    s.prop('props/car_wreck', 68.5, 39.5, True, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 18.5, 34, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 45, 36.4, True, plan=(C_CAR, ''))
    s.prop('props/gar_scrap_heap', 73, 25, True, plan=(C_SCRAP, ''))
    s.prop('props/gar_scrap_heap', 64.5, 43.6, plan=(C_SCRAP, ''))
    for (x, y) in [(73, 35), (12.6, 7.6), (60.5, 7.2), (7, 21.5), (32, 48.8)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(64, 22), (64.8, 22.4), (74.5, 44), (75.1, 44.5), (25.5, 48.5), (26.3, 49), (5.6, 46), (62.4, 11.3), (63.2, 11.6), (58.6, 48.8)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(66.5, 35.5), (63.2, 27), (74.4, 30.6), (7, 44.5), (22.4, 26.8), (59.6, 30.6)]:
        s.prop('props/gar_tire_stack', x, y, plan=(C_COVER, ''))
    s.prop('props/crate_military', 74.6, 13.6, plan=(C_LOOT, ''))
    for (x, y) in [(42, 22), (55, 36), (29, 34), (16, 28), (66, 20.5), (40, 48), (52, 48.6), (18.6, 44)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    for (x, y) in [(6, 49), (74.5, 10.6), (11.5, 20.5), (62.6, 15.6), (74.2, 22)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    # сторожа
    guard(s, 15.2, 47.0, 'club', 'сторож у шлагбаума (дубинка)')
    guard(s, 28.0, 20.5, 'club', 'обходчик проездов (дубинка)', back=True)
    s.weeds((4, 4, 76, 50), 420, [(12, 14.3, 62, 25.3), (13, 25, 24, 54), (13.5, 7.5, 62, 14.5), (25.5, 25, 56.5, 31.5), (25.5, 37.8, 61.5, 44.5)])
    s.weeds((24, 31.3, 61, 37.8), 30, bush=0)
    s.weeds((24, 44.3, 62, 49.6), 30, bush=0)
    s.forest([(3, 3, 77, 51), (13, 50, 23, 54)], 0.5)
    s.notes = ['#car|машины (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#cover|постройки, укрытия', '#mech|механика: ворота 1–8,',
               '   машина, крейт', '#guard|сторожа', '', '!Как проходить:', '1. Открытый бокс в ряду А, перед ним',
               '   «шестёрка». Открыть дверь — орёт', '   сигналка 15 с (слышно далеко).', '2. В бардачке — брелок (локальный).',
               '3. Жмёшь кнопку — одни из 8 ворот', '   дёргаются и пищат: ищешь по звуку.', '4. Эти ворота открываются — внутри',
               '   крейт. Перфокарта I → крейт.', '', '!Охрана: 2 сторожа с дубинками.', '!Награда: фрагменты ×1, перфокарта II.', '',
               'Выходы: въезд со шлагбаумом (юг),', 'пролом (восток), лаз (север).', 'Территория ~72×46 м за забором,', 'с подъездом 80×62. Клетка = 1 м.']
    s.render('garages')


# =====================================================================================
def ats():
    s = Scene('АТС «Занято» — РТ тира 1, сложность 3/5 (план, север сверху)', 84, 64, seed=202)
    s.legend = LEGEND
    s.ground('ground_grass')
    road_h(s, 56, 63)
    s.ground('ground_gravel', [(6, 52), (6, 3), (76, 3), (76, 14), (80, 14), (80, 52)], 5, zone=(C_ZONE['grav'], 'двор АТС'))
    s.ground('ground_asphalt', [(8, 34), (61, 34), (61, 46), (74, 46), (74, 52), (8, 52)], 3, zone=(C_ZONE['asph'], 'площадка, стоянка'))
    s.ground('ground_asphalt', [(34, 52), (44, 52), (44, 56), (34, 56)], 2)
    # полы
    s.ground('ground_concrete', s.rect(12, 12, 46, 32), 0, zone=(C_ZONE['conc'], 'машинный зал (реле)'))
    s.ground('floor_tile', s.rect(46, 12, 54, 32), 0, zone=((120, 104, 88), 'кросс'))
    s.ground('floor_tile', s.rect(24, 6, 36, 12), 0, zone=((120, 104, 88), 'аккумуляторная'))
    s.ground('floor_linoleum', s.rect(54, 18, 72, 44), 0, zone=(C_ZONE['floor'], ''))
    for (x, y, lab) in [(54.3, 18.3, 'выпрямители'), (54.3, 26.3, 'диспетчерская'), (54.3, 34.3, 'вестибюль'), (61.2, 18.3, 'кор.'),
                        (64.3, 18.3, 'коммутаторная'), (64.3, 28.3, 'контора'), (64.3, 36.3, 'экспедиция')]:
        s.labels.append((x, y + 0.6, lab))
    cracks(s, (8, 34, 61, 52), 30)
    tufts(s, (6, 3, 80, 52), 80, [(11, 5, 73, 45)])
    for (x, y, k) in [(24, 45.5, 1.6), (40, 48.5, 1.2), (70, 49, 1.2), (19, 36.5, 1.3)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(13, 41), (30, 47), (52, 38), (58, 49)]:
        s.dec('decal_oil_puddle', x, y, 1.2)
    for (x, y, r) in [(15, 36.5, 0), (9, 43, 90), (66, 8, 0), (28, 37, 0)]:
        s.dec('decal_cables', x, y, 1.0, r)
    # ---------- здание ----------
    BR, ST, DM = 'wallface_brick', 'wallface_stucco', 'wallface_door_metal'
    # машинный зал + кросс (один корпус), аккумуляторная — пристройка на севере
    s.walls([(46, 12), (36, 12)], 4.0, BR)
    s.walls([(24, 12), (12, 12), (12, 32), (16, 32)], 4.0, BR, gaps=[(12, 24, 12, 25.6)])
    s.wall(16, 32, 20, 32, 4.0, 'wallface_shutter')                    # роллета погрузки (видна с юга)
    s.walls([(20, 32), (46, 32)], 4.0, BR)
    s.wall(12, 24, 12, 25.6, 4.0, DM)                                    # боковая дверь (запад)
    s.walls([(46, 12), (54, 12), (54, 18)], 4.0, BR)
    s.walls([(46, 32), (49, 32)], 4.0, BR); s.wall(49, 32, 50.6, 32, 4.0, DM); s.walls([(50.6, 32), (54, 32)], 4.0, BR)
    s.walls([(46, 12), (46, 32)], 4.0, BR, gaps=[(46, 20, 46, 22)])      # зал | кросс — проём
    s.walls([(24, 12), (24, 6), (36, 6), (36, 12)], 3.2, BR)
    s.walls([(24, 12), (36, 12)], 4.0, BR, gaps=[(28.5, 12, 30.5, 12)])  # проём из зала в аккумуляторную
    # административное крыло (штукатурка): коридор, комнаты
    s.walls([(54, 18), (72, 18), (72, 44), (64, 44)], 4.0, ST, gaps=[(72, 40, 72, 41.6)])
    s.wall(72, 40, 72, 41.6, 4.0, DM)                                    # дверь экспедиции (восток)
    s.wall(61.5, 44, 63.5, 44, 4.0, DM)                                  # главный вход (юг)
    s.walls([(54, 44), (61.5, 44)], 4.0, ST); s.walls([(63.5, 44), (64, 44)], 4.0, ST)
    s.walls([(54, 18), (54, 44)], 4.0, ST, gaps=[(54, 28, 54, 29.6)])   # дверь кросс → диспетчерская
    s.walls([(61, 18), (61, 44)], 3.6, ST, gaps=[(61, 21, 61, 22.6), (61, 29, 61, 30.6), (61, 38, 61, 39.6)])
    s.walls([(64, 18), (64, 44)], 3.6, ST, gaps=[(64, 22, 64, 23.6), (64, 31, 64, 32.6), (64, 39, 64, 40.6)])
    s.wall(54, 26, 61, 26, 3.6, ST); s.wall(54, 34, 61, 34, 3.6, ST)
    s.wall(64, 28, 72, 28, 3.6, ST); s.wall(64, 36, 72, 36, 3.6, ST)
    s.roof(12, 12, 46, 32, 4.0, 'roof_tar'); s.roof(46, 12, 54, 32, 4.0, 'roof_tar')
    s.roof(24, 6, 36, 12, 3.2, 'roof_metal'); s.roof(54, 18, 72, 44, 4.0, 'roof_slate')
    # подсказки входов, которых не видно камере
    s.prop('props/garage_ramp', 18, 34.0)
    s.prop('props/porch_side', 10.8, 25.9); s.prop('props/door_lamp_post', 10.9, 23.4)
    s.prop('props/porch_side', 73.3, 41.9, True); s.prop('props/door_lamp_post', 73.2, 39.4)
    s.prop('props/porch_front', 62.5, 45.6); s.prop('props/door_lamp_post', 60.6, 45.0)
    s.labels += [(16, 34.8, 'роллета'), (5.6, 23.4, 'бок. дверь'), (73.6, 38.2, 'дверь эксп.'), (57.4, 46.4, 'главный вход'), (48.4, 33.0, 'дверь')]
    # ---------- машинный зал: ряды релейных стоек, проход посередине ----------
    for row, y in enumerate((16.0, 20.4, 24.8, 29.2)):
        name = 'props/ats_relay_rack' if row % 2 == 0 else 'props/ats_relay_rack_side'
        for x in (15.5, 18.5, 21.5, 24.5, 32.5, 35.5, 38.5, 41.5):
            if row == 3 and x in (15.5, 18.5):
                continue                                                 # место у роллеты
            s.prop(name, x, y, plan=(C_COVER, ''))
    s.labels.append((27.6, 14.4, 'ряды реле'))
    s.prop('props/ats_cable_tray_stack', 17, 30.6, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 44.2, 31.2, plan=(C_COVER, ''))
    s.prop('props/crate_military', 44.6, 27.3, plan=(C_LOOT, ''))
    s.prop('props/ats_rectifier', 13.4, 13.0, plan=(C_COVER, ''))
    s.prop('props/ats_rectifier', 14.8, 13.0, plan=(C_COVER, ''))
    # кросс: стойки кросса, патч-панель (сюда втыкают кабели), схема на стене
    s.prop('props/ats_cross_frame', 50, 13.0, plan=(C_COVER, ''))
    s.prop('props/ats_patch_panel', 50, 18.7, plan=(C_COVER, ''))
    mech(s, 50, 19.3, 'patch', 'ПАТЧ-ПАНЕЛЬ')
    s.prop('props/ats_cross_frame', 50, 24.6, plan=(C_COVER, ''))
    s.prop('props/rail_schema_board', 51.6, 31.4, plan=(C_COVER, ''))
    s.labels.append((46.6, 30.0, 'схема'))
    # аккумуляторная
    s.prop('props/ats_battery_rack', 26.4, 9.6, plan=(C_COVER, ''))
    s.prop('props/ats_battery_rack', 33.8, 9.6, plan=(C_COVER, ''))
    # админ. крыло
    for x in (55.4, 56.8, 58.2, 59.6):
        s.prop('props/ats_rectifier', x, 18.9, plan=(C_COVER, ''))
    s.prop('props/ats_desk_phones', 57, 28.6, plan=(C_COVER, ''))
    s.prop('props/int_lockers', 59.6, 26.8, plan=(C_COVER, ''))
    s.prop('props/ats_desk_phones', 57.6, 32.8, True, plan=(C_COVER, ''))
    s.prop('props/phone_booth', 55.2, 35.2, plan=(C_COVER, ''))
    s.prop('props/phone_booth', 56.4, 35.2, plan=(C_COVER, ''))
    s.prop('props/int_table', 58.5, 41.5)
    s.prop('props/ats_switchboard', 68, 19.6, plan=(C_COVER, ''))
    mech(s, 68, 20.6, 'switch', 'КОММУТАТОР')
    s.prop('props/ats_desk_phones', 70.4, 26.6, True, plan=(C_COVER, ''))
    s.prop('props/int_desk', 67, 31.2, plan=(C_COVER, ''))
    s.prop('props/int_shelf', 65.2, 28.7, plan=(C_COVER, ''))
    s.prop('props/int_lockers', 70.6, 28.7, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 66, 43.2, plan=(C_LOOT, ''))
    s.prop('props/int_shelf', 67.2, 36.7, plan=(C_COVER, ''))
    # пневмопочта: от коммутатора к трём приёмным станциям (часть трубы идёт снаружи по стене)
    pipe(s, [(68, 21.2), (71.3, 21.2), (71.3, 36.6), (70, 36.6)])
    pipe(s, [(66.4, 21.0), (66.4, 13.4), (56, 13.4), (56, 10.6), (43, 10.6), (43, 12.6)])
    pipe(s, [(43, 10.6), (30, 10.6), (30, 7.1)])
    s.prop('props/ats_pneumo_station', 70, 37.0, plan=(C_COVER, ''))
    mech(s, 70, 37.6, 'capsule:1', 'станция 1')
    s.prop('props/ats_pneumo_station', 43, 13.0, plan=(C_COVER, ''))
    mech(s, 43, 13.6, 'capsule:2', 'станция 2')
    s.prop('props/ats_pneumo_station', 30, 7.0, plan=(C_COVER, ''))
    mech(s, 30, 7.6, 'capsule:3', 'станция 3')
    s.labels.append((57, 12.6, 'трубы пневмопочты'))
    # кабели-перемычки в трёх разных комнатах
    s.prop('props/ats_cable_bundle', 27, 11.4)
    mech(s, 27, 11.4, 'cable:1', 'кабель 1')
    s.prop('props/ats_cable_bundle', 68.6, 34.6)
    mech(s, 68.6, 34.6, 'cable:2', 'кабель 2')
    s.prop('props/ats_cable_bundle', 22.6, 22.9)
    mech(s, 22.6, 22.9, 'cable:3', 'кабель 3')
    # ---------- двор ----------
    s.walls([(34, 52), (6, 52), (6, 3), (76, 3), (76, 14), (80, 14), (80, 52), (44, 52)], 2.2, 'wallface_fence_po2', gaps=[(80, 30, 80, 32)])
    s.prop('props/gate_leaf_open', 35.4, 53.2); s.prop('props/gate_leaf_open', 42.6, 53.2, True)
    s.labels += [(36, 54.4, 'ворота'), (80.6, 30.2, 'калитка')]
    s.prop('props/subst_truck', 26, 45, plan=(C_CAR, ''))
    s.prop('props/car_zhiguli_wreck', 47, 49, True, plan=(C_CAR, ''))
    s.prop('props/car_police_2101', 66, 50.5, plan=(C_CAR, ''))
    for (x, y) in [(10.5, 38.5), (13, 38.8), (9, 47)]:
        s.prop('props/subst_cable_drum', x, y, plan=(C_COVER, ''))
    s.prop('props/ats_cable_tray_stack', 31, 39.6, plan=(C_COVER, ''))
    s.prop('props/ats_diesel_gen', 70, 9, plan=(C_COVER, ''))
    s.prop('props/subst_oil_tank', 64, 12.4, plan=(C_COVER, ''))
    s.prop('props/subst_ktp', 9.6, 17, plan=(C_COVER, ''))
    s.prop('props/subst_relay_cabinet', 44, 40, plan=(C_COVER, ''))
    s.prop('props/fuel_truck_wreck', 43, 47.6, True, plan=(C_CAR, ''))
    s.prop('props/ats_cable_tray_stack', 36, 36.6, plan=(C_COVER, ''))
    s.prop('props/subst_reel_stack', 20, 39.4, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 33.5, 47.6, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 12, 44.6, plan=(C_COVER, '')); s.prop('props/concrete_barrier', 13.4, 44.8, plan=(C_COVER, ''))
    s.prop('props/shed_canopy', 16, 50.8, plan=(C_COVER, ''))
    s.prop('props/junk_pile', 40, 44, plan=(C_SCRAP, ''))
    s.prop('props/barrel_oil', 57, 37.4, plan=(C_SCRAP, '')); s.prop('props/barrel_water', 57.7, 37.8, plan=(C_SCRAP, ''))
    for (x, y) in [(8, 7), (50, 6), (77.5, 47), (18, 6.5)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(66.5, 9.6), (67.3, 10), (8.5, 30), (9.2, 30.5), (52, 41.5), (78, 18), (40, 6.4), (40.8, 6.8)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/crate_wood', 74.6, 6.6, plan=(C_LOOT, ''))
    for (x, y) in [(20, 40), (36, 45), (55, 49.5), (73, 22), (9, 26), (60, 8), (33, 4.5)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    lamp(s, 8.4, 34.2); lamp(s, 45, 51.2, True); lamp(s, 74.5, 33.2); lamp(s, 58.6, 11.6)
    for (x, y) in [(7.6, 50.6), (78.4, 4.6), (76.6, 26), (22, 4.4)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    # охрана — ближний бой в коридорах
    guard(s, 62.5, 30.5, 'pipe', 'сторож в коридоре (труба)', back=True)
    guard(s, 29.6, 22.8, 'club', 'сторож в зале (дубинка)')
    guard(s, 48.2, 28.4, 'pipe', 'сторож у кросса (труба)')
    s.weeds((6, 3, 80, 52), 360, [(8, 34, 61, 52), (11, 5, 73, 45.5)])
    s.forest([(5, 2, 81, 53), (34, 52, 44, 56)], 0.5)
    s.notes = ['#car|машины (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#cover|стойки, шкафы, укрытия', '#mech|механика',
               '#pipe|трубы пневмопочты', '#guard|сторожа', '#shut|роллета', '#door|двери', '', '!Как проходить:', '1. Собрать 3 кабеля-перемычки:',
               '   аккумуляторная, контора, зал.', '   Носятся в руках по одному.', '2. Кросс: воткнуть кабели в патч-',
               '   панель по схеме на стене.', '3. Коммутаторная: перфокарта I', '   в коммутатор.', '4. Крейт летит пневмопочтой: свист',
               '   и стук в трубах, капсула приходит', '   в 1 из 3 станций — ищешь по звуку,', '   перехватить могут другие.', '',
               '!Охрана: 2–3 сторожа (труба, дубинка),', '   бой в коридорах и между стойками.', '!Награда: фрагменты ×2, перфокарта II.', '',
               'Выходы: ворота (юг), калитка (восток);', 'в здание: главный вход, роллета,', 'боковая дверь (запад), экспедиция.',
               'Корпус ~60×38 м, двор 74×49 м.']
    s.render('ats')


# =====================================================================================
def depot():
    s = Scene('Депо «Тупик» — РТ тира 1, сложность 4/5 (план, север сверху)', 112, 64, seed=303)
    s.legend = LEGEND
    s.ground('ground_grass')
    s.ground('ground_dirt', [(0, 4), (112, 4), (112, 60), (0, 60)], 8)
    s.ground('ground_gravel', [(4, 14), (108, 14), (110, 58), (2, 58)], 6, zone=(C_ZONE['grav'], 'двор депо'))
    s.ground('ground_concrete', s.rect(52, 38, 96, 58.5), 2)
    s.ground('ground_concrete', [(88, 31.6), (104, 31.6), (104, 37), (88, 37)], 1)
    s.ground('ground_asphalt', [(4, 24), (24, 24), (24, 30), (4, 30)], 3)
    s.zones.insert(0, ([(-9, 7), (121, 7), (121, 13), (-9, 13)], C_RAIL, 'главный ход (насквозь)'))
    # пути: главный ход, отстой с вагоном (C), путь к рампе (B), тупик (E), путь в локомотивное депо (D)
    track(s, [(-9, 10), (121, 10)])
    # стрелочный перевод: rail_switch ×1,15, ответвление уходит вверх-вправо под ~35°; от его конца идёт отвод
    track(s, [(-9, 44), (90, 44)])                                  # C (отстой с вагоном) → прямо через стрелку 1 → D в депо
    track(s, [(41.8, 40.76), (57.2, 30), (106, 30)])                # стрелка 1 в отвод → B → рампа, упор
    track(s, [(71.8, 26.76), (95.7, 10)])                           # стрелка 2 в отвод → на главный ход (вагон уходит)
    track(s, [(85.8, 26.76), (92.6, 22), (105, 22)])                # стрелка 3 в отвод → тупик E без рампы
    for (jx, jy) in [(34, 44), (64, 30), (78, 30)]:                # после путей: балласт путей рисуется землёй поверх
        s.dec('rail_switch', jx + 3.45, jy - 1.2, 1.15)
    s.labels += [(2, 41.2, 'путь отстоя (вагон)'), (58, 27.6, 'путь к рампе'), (96, 19.6, 'тупик'), (26, 6.6, 'главный ход'), (2, 6.6, '')]
    for (x, y, k) in [(30, 44.5, 1.3), (60, 30.5, 1.2), (94, 44.4, 1.6), (12, 10.6, 1.2), (98, 30.4, 1.2)]:
        s.dec('decal_oil_stain', x, y, k)
    tufts(s, (2, 14, 110, 58), 120, [(56, 39.5, 92.5, 56.5)])
    for (x, y) in [(28, 50), (48, 20), (70, 52.5), (104, 50)]:
        s.dec('decal_puddle', x, y, 1.3)
    # ---------- локомотивное депо: ворота на западе (путь заходит внутрь), задние ворота на востоке ----------
    BR = 'wallface_brick'
    s.walls([(56, 42), (56, 40), (92, 40), (92, 42)], 5.0, BR)
    s.walls([(56, 46), (56, 56), (92, 56), (92, 46)], 5.0, BR, gaps=[(62, 56, 64, 56)])
    s.wall(62, 56, 64, 56, 5.0, 'wallface_door_metal')
    s.wall(56, 42, 56, 46, 5.0, 'wallface_rail_shed')                  # ворота депо (запад) — створки открыты
    s.wall(92, 42, 92, 46, 5.0, 'wallface_rail_shed')                  # задние ворота (восток), заперты
    s.roof(56, 40, 92, 56, 5.0, 'roof_metal')
    s.prop('props/gate_leaf_open', 54.6, 41.6, True); s.prop('props/gate_leaf_open', 54.6, 47.8, True)
    s.prop('props/door_lamp_post', 54.4, 40.4); s.prop('props/porch_front', 63, 57.6)
    s.labels += [(46.6, 38.6, 'ворота депо'), (60.4, 58.4, 'дверь')]
    s.prop('props/rail_locomotive', 76, 45.6, plan=(C_COVER, ''))
    s.labels.append((64, 39.0, 'локомотивное депо (тепловоз)'))
    s.prop('props/rail_wheelset', 62, 54.8, plan=(C_COVER, ''))
    s.prop('props/rail_wheelset', 65, 54.8, plan=(C_COVER, ''))
    s.prop('props/gar_workbench_vise', 86, 41.4, plan=(C_COVER, ''))
    s.prop('props/int_shelf', 89.6, 40.8, plan=(C_COVER, ''))
    s.prop('props/crate_military', 88.6, 54.8, plan=(C_LOOT, ''))
    for (x, y) in [(58.4, 54.6), (59.3, 55)]:
        s.prop('props/barrel_oil', x, y, plan=(C_SCRAP, ''))
    s.prop('props/rail_sleepers_stack', 74, 55.2, plan=(C_COVER, ''))
    # ---------- вагон с крейтом на пути отстоя ----------
    s.prop('props/rail_freight_wagon', 18, 45.45, plan=(C_CAR, ''))
    mech(s, 18, 44, 'wagon_start', 'ВАГОН (старт), внутри крейт')
    s.mark(18, 43.6, C_MECH, 'MECH:crate')
    mech(s, 97, 30, 'wagon_end', 'вагон у рампы (финиш)')
    s.prop('props/rail_loading_ramp', 96, 35.6, plan=(C_COVER, ''))
    s.labels.append((89, 36.6, 'погрузочная рампа'))
    s.prop('props/rail_buffer_stop', 107.4, 31.2, plan=(C_COVER, ''))
    s.prop('props/rail_buffer_stop', 106.4, 23.2, plan=(C_COVER, ''))
    s.prop('props/rail_handcar', 99, 23.4, plan=(C_COVER, ''))
    # ---------- стрелки и пост ЭЦ ----------
    s.prop('props/rail_switch_lever', 36.4, 47.4, plan=(C_COVER, ''))
    mech(s, 36.4, 47.0, 'lever:1', 'стрелка 1')
    s.prop('props/rail_switch_lever', 66.4, 33.2, plan=(C_COVER, ''))
    mech(s, 66.4, 32.8, 'lever:2', 'стрелка 2')
    s.prop('props/rail_switch_lever', 80.4, 33.2, plan=(C_COVER, ''))
    mech(s, 80.4, 32.8, 'lever:3', 'стрелка 3')
    s.prop('props/rail_signal_box', 37, 25.6, plan=(C_COVER, ''))
    s.labels.append((32.6, 19.0, 'пост ЭЦ'))
    s.prop('props/rail_schema_board', 44.6, 25.4, plan=(C_COVER, ''))
    mech(s, 44.6, 25.0, 'board', 'СХЕМА стрелок')
    for (x, y) in [(33, 13.4), (60, 27.4), (104, 13.4), (34, 41.2)]:
        s.prop('props/rail_signal', x, y, plan=(C_COVER, ''))
    # ---------- контора депо ----------
    ST = 'wallface_stucco'
    s.ground('floor_linoleum', s.rect(6, 15, 20, 23), 0)
    s.walls([(6, 23), (6, 15), (20, 15), (20, 23), (15, 23)], 3.2, ST)
    s.wall(6, 23, 12, 23, 3.2, ST); s.wall(12, 23, 13.6, 23, 3.2, 'wallface_door_wood'); s.wall(13.6, 23, 15, 23, 3.2, ST)
    s.wall(13, 15, 13, 23, 3.0, ST)
    s.roof(6, 15, 20, 23, 3.2, 'roof_slate')
    s.prop('props/porch_front', 12.8, 24.6)
    s.prop('props/int_desk', 9, 16.0, plan=(C_COVER, '')); s.prop('props/int_lockers', 18.4, 15.7, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 17.6, 21.6, plan=(C_LOOT, ''))
    s.labels.append((6.2, 13.6, 'контора'))
    s.prop('props/car_zhiguli_wreck', 10, 28.4, plan=(C_CAR, ''))
    # ---------- главный ход: цистерны-укрытия ----------
    s.prop('props/rail_cistern', 50, 11.45, plan=(C_CAR, ''))
    s.prop('props/rail_cistern', 63, 11.45, plan=(C_CAR, ''))
    s.prop('props/rail_cistern', 104, 11.45, True, plan=(C_CAR, ''))
    # ---------- хлам и лут по двору ----------
    for (x, y) in [(28, 36), (48, 50), (100, 50), (6, 54), (74, 20)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(26, 34), (26.8, 34.4), (48.6, 37), (49.3, 37.4), (102, 40.6), (102.7, 41), (9, 50), (84, 17)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(30, 52.6), (100, 56), (46, 33.6)]:
        s.prop('props/rail_sleepers_stack', x, y, plan=(C_COVER, ''))
    s.prop('props/rail_wheelset', 104, 44.6, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 22, 36, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 23.6, 52.4, plan=(C_LOOT, ''))
    for (x, y) in [(16, 36), (40, 54), (58, 22), (96, 52.5), (6, 33), (88, 26.8)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    s.prop('props/subst_oil_tank', 101, 53, plan=(C_COVER, ''))
    s.prop('props/subst_truck', 22, 55.4, plan=(C_CAR, ''))
    s.prop('props/shed_canopy', 12, 38.6, plan=(C_COVER, ''))
    s.prop('props/gar_workbench_vise', 10.6, 37.6, plan=(C_COVER, ''))
    s.prop('props/rail_wheelset', 14.6, 37.8, plan=(C_COVER, ''))
    s.prop('props/rail_sleepers_stack', 48, 22, plan=(C_COVER, ''))
    s.prop('props/rail_sleepers_stack', 22, 31, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 70, 18.6, plan=(C_COVER, ''))
    s.prop('props/gar_scrap_heap', 40, 52, plan=(C_SCRAP, ''))
    s.prop('props/concrete_barrier', 44, 56.6, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 46.2, 56.4, plan=(C_COVER, ''))
    lamp(s, 30, 38.6); lamp(s, 52, 35.8, True); lamp(s, 86, 35.6); lamp(s, 24, 24.4)
    # забор только с юга и востока (север — открытый главный ход)
    s.walls([(2, 58), (2, 60), (110, 60), (110, 38)], 2.2, 'wallface_concrete', gaps=[(30, 60, 36, 60)])
    s.labels.append((30.4, 60.6, 'проезд'))
    # охрана: один с луком на вагоне, двое внизу
    guard(s, 18, 43.2, 'bow', 'лучник на крыше вагона')
    guard(s, 50.5, 46.5, 'club', 'сторож у ворот депо (дубинка)')
    guard(s, 92, 34.2, 'pipe', 'сторож у рампы (труба)', back=True)
    for (x, y) in [(3, 34), (108, 34), (60, 58.4), (24, 16.8), (110, 56)]:
        s.prop('plants/bush_1', x, y, random.random() < .5)
    s.weeds((2, 14, 110, 58), 520, [(55.5, 39.5, 92.5, 58.5), (5.5, 14.5, 20.5, 23.5), (87, 31, 105, 37)])
    s.weeds((-9, 0, 121, 14), 140, bush=0.05)
    s.forest([(-9, 3, 121, 61)], 0.5)
    s.notes = ['#rail|пути', '#car|вагоны, цистерны (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#cover|постройки, укрытия',
               '#mech|механика', '#guard|сторожа', '', '!Как проходить:', '1. Пост ЭЦ: схема стрелок — какие', '   нужно перевести (каждый раз своя).',
               '2. Перевести 3 стрелки рычагами', '   (лязг, слышно ~40 м).', '3. Перфокарта I в вагон: вагон', '   трогается и катится на виду',
               '   (стрелка 1 → путь к рампе,', '   стрелка 2 — мимо главного хода,', '   стрелка 3 — мимо тупика).', '4. У рампы крейт открывается.',
               '   Ошибся стрелкой — вагон в депо', '   или в тупике, надо переводить', '   и толкать заново.', '',
               '!Охрана: 3 (лучник на вагоне,', '   дубинка у депо, труба у рампы).', '!Награда: фрагменты ×2,', '   шанс второй перфокарты II.', '',
               'Открытый двор: простреливается', 'с главного хода и с вагонов.', 'Выходы: по путям на запад и восток,', 'проезд в заборе (юг).',
               'Размер ~110×60 м. Клетка = 1 м.']
    s.render('depot')


# =====================================================================================
def quarry():
    import math
    s = Scene('Карьер «Ковш» — РТ тира 1, сложность 5/5 (план, север сверху)', 112, 92, seed=404)
    s.legend = LEGEND
    s.ground('ground_grass')
    # уступы: верх (север, промплощадка) → уступ 1 → уступ 2 → дно; карьер врезан в склон и открыт на юг к дороге
    s.ground('ground_dirt', [(2, 2), (106, 2), (110, 18), (110, 86), (2, 86)], 8)
    s.ground('ground_gravel', [(6, 3), (98, 3), (100, 17.6), (6, 17.6)], 4, zone=(C_ZONE['grav'], 'промплощадка (верх)'))
    s.ground('ground_gravel', [(8, 18), (104, 18), (104, 86), (92, 86), (92, 36), (20, 36), (20, 86), (8, 86)], 3, zone=(C_ZONE['grav'], 'уступ 1'))
    s.ground('ground_sand', [(20, 36), (92, 36), (92, 86), (78, 86), (78, 56), (34, 56), (34, 86), (20, 86)], 3, zone=((120, 108, 80), 'уступ 2'))
    s.ground('ground_rock', [(34, 56), (78, 56), (78, 86), (34, 86)], 3, zone=((96, 90, 84), 'дно карьера'))
    s.ground('ground_dirt', [(-9, 85), (121, 85), (121, 92), (-9, 92)], 4, zone=(C_ZONE['dirt'], 'карьерная дорога'))
    s.ground('ground_dirt', [(44, -9), (52, -9), (52, 4), (44, 4)], 4)
    s.ground('ground_concrete', s.rect(52, 5, 64, 14), 1, zone=(C_ZONE['conc'], 'бетонная площадка'))
    # съезды (проёмы в уступах) — грунтовые полосы
    for poly in ([(2, 40), (9, 40), (9, 47), (2, 47)], [(19, 62), (21, 62), (21, 69), (19, 69)], [(77, 64), (79, 64), (79, 71), (77, 71)], [(60, 17), (66, 17), (66, 19), (60, 19)]):
        s.ground('ground_dirt', poly, 2)
    for (x, y, k) in [(48, 70, 1.6), (64, 80, 1.3), (30, 10, 1.2), (86, 12, 1.4)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(40, 78), (60, 66), (70, 82), (26, 50), (86, 48)]:
        s.dec('decal_puddle', x, y, 1.5)
    tufts(s, (6, 3, 104, 86), 120)
    # ---------- уступы (скальные стенки 3 м, вид спереди — видно их южные лица) ----------
    RK = 'wallface_rock'
    s.walls([(8, 86), (8, 18), (104, 18), (104, 86)], 3.0, RK, t=0.8, gaps=[(8, 40, 8, 47), (60, 18, 66, 18)])
    s.walls([(20, 86), (20, 36), (92, 36), (92, 86)], 3.0, RK, t=0.8, gaps=[(20, 62, 20, 69)])
    s.walls([(34, 86), (34, 56), (78, 56), (78, 86)], 3.0, RK, t=0.8, gaps=[(78, 64, 78, 71)])
    s.labels += [(1, 38.6, 'съезд'), (60, 15.2, 'съезд'), (15, 60.6, 'съезд'), (79, 62.6, 'съезд')]
    # ---------- кран: база, кабина, стрела (отдельные спрайты; стрела крутится вокруг шарнира) ----------
    PX, PY = 50.0, 27.6                     # шарнир стрелы (центр поворотной платформы)
    CS, CE = (40.0, 44.0), (58.0, 9.6)      # контейнер: старт на завале, финиш на бетонной площадке
    s.prop('props/quarry_crane_base', PX, 30.6, plan=(C_COVER, ''))
    s.prop('props/quarry_crane_cabin', 55.6, 31.4, plan=(C_COVER, ''))
    mech(s, 55.6, 31.8, 'crane_cabin', 'КАБИНА КРАНА')
    mech(s, PX, PY, 'crane_pivot', 'шарнир стрелы (стрела 20 м)')
    s.prop('props/quarry_rubble_pile', CS[0], CS[1] + 2.4, plan=(C_COVER, ''))
    s.labels.append((CS[0] - 4, CS[1] + 3.2, 'завал'))
    mech(s, CS[0], CS[1], 'container_start', 'КОНТЕЙНЕР (старт)')
    mech(s, CE[0], CE[1], 'container_end', 'площадка (финиш)')
    mech(s, CS[0] + 0.6, CS[1], 'crate', None)
    # контейнер на завале (приподнят на кучу) и стрела над ним — рисуем сами, в планировку идут как обычные пропсы
    s.ex['props'].append(('props/quarry_container', CS[0], CS[1] + 1.2, False, list(C_CAR)))
    s.ex['props'].append(('props/quarry_crane_boom', PX, PY, False, None))
    cont, boom, mag = L('props/quarry_container'), L('props/quarry_crane_boom'), L('props/quarry_magnet')
    ca, cb = s.px(CS[0], CS[1] + 1.2)
    cb -= int(1.6 * PPM * HS)
    s.objs.append((CS[1] + 2.5, lambda: s.img.alpha_composite(cont, (ca - cont.width // 2, cb - cont.height))))
    s.plan_props.append((CS[0] - 3, CS[1] - 1.2, CS[0] + 3, CS[1] + 1.2, C_CAR, ''))
    ang = math.degrees(math.atan2(-(CS[1] - PY), CS[0] - PX))     # на экране против часовой
    L_b = math.hypot(CS[0] - PX, CS[1] - PY)

    def draw_boom():
        b = boom.resize((max(1, int(boom.width * L_b / 20.0)), boom.height), Image.NEAREST)
        pad = Image.new('RGBA', (b.width * 2, b.width * 2))
        pad.alpha_composite(b, (b.width, b.width - b.height // 2))  # шарнир (левый край) — в центре холста
        r = pad.rotate(ang, resample=Image.NEAREST)
        a, c = s.px(PX, PY)
        c -= int(3.2 * PPM * HS)
        sh = r.getchannel('A').point(lambda v: int(v * 0.3))
        shd = Image.new('RGBA', r.size, (12, 14, 10, 255)); shd.putalpha(sh)
        s.img.alpha_composite(shd, (a - r.width // 2 + 40, c - r.height // 2 + 70))   # тень стрелы на земле
        s.img.alpha_composite(r, (a - r.width // 2, c - r.height // 2))
        if mag:                                                      # магнит висит над контейнером (только картинка)
            s.img.alpha_composite(mag, (ca - mag.width // 2, cb - cont.height - mag.height + 6))
    s.objs.append((999, draw_boom))
    s.lines.append(([(PX, PY), CS], (230, 200, 90), 3))
    s.lines.append(([(PX, PY), CE], (230, 200, 90), 1))
    # ---------- промплощадка (верх) ----------
    BR = 'wallface_brick'
    s.ground('floor_linoleum', s.rect(70, 4, 82, 11), 0)
    s.walls([(70, 11), (70, 4), (82, 4), (82, 11), (77.6, 11)], 3.2, BR)
    s.wall(70, 11, 76, 11, 3.2, BR); s.wall(76, 11, 77.6, 11, 3.2, 'wallface_door_wood')
    s.roof(70, 4, 82, 11, 3.2, 'roof_slate')
    s.prop('props/porch_front', 76.8, 12.6)
    s.prop('props/int_desk', 72.6, 5.0, plan=(C_COVER, '')); s.prop('props/int_lockers', 80.6, 4.7, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 80.4, 9.6, plan=(C_LOOT, ''))
    s.labels.append((70, 2.4, 'контора / весовая'))
    s.prop('props/quarry_trailer_hut', 18, 9.6, plan=(C_COVER, ''))
    s.prop('props/quarry_trailer_hut', 28, 13.6, True, plan=(C_COVER, ''))
    s.labels.append((12, 4.2, 'бытовки'))
    s.prop('props/subst_oil_tank', 40, 9.0, plan=(C_COVER, ''))
    s.prop('props/quarry_hopper', 94, 14.8, plan=(C_COVER, ''))
    s.prop('props/quarry_sand_pile', 88, 9.0, plan=(C_COVER, ''))
    s.prop('props/fuel_truck_wreck', 32, 5.6, plan=(C_CAR, ''))
    for (x, y) in [(10, 14), (64, 3.6), (100, 6)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(42.6, 13.6), (43.4, 14), (23, 15.6), (66, 15.8), (98, 4.4)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('props/crate_military', 24.6, 6.0, plan=(C_LOOT, ''))
    lamp(s, 51, 15.4); lamp(s, 66.4, 12.0, True); lamp(s, 12.6, 16.6)
    # ---------- уступ 1: кран, конвейер ----------
    s.prop('props/quarry_conveyor', 82, 31.6, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 14, 30, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 99, 50, True, plan=(C_COVER, ''))
    s.prop('props/quarry_sand_pile', 14, 58, plan=(C_COVER, ''))
    s.prop('props/quarry_dumptruck_wreck', 98, 72, plan=(C_CAR, ''))
    s.prop('props/barrel_oil', 60.6, 33, plan=(C_SCRAP, '')); s.prop('props/barrel_oil', 61.3, 33.4, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 30, 33, plan=(C_SCRAP, ''))
    s.prop('props/crate_wood', 12, 79, plan=(C_LOOT, ''))
    lamp(s, 44, 33.6)
    # ---------- уступ 2: буровой станок, глыбы ----------
    s.prop('props/quarry_drill_rig', 72, 48, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 56, 50, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 26, 76, True, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 86, 60, plan=(C_COVER, ''))
    s.prop('props/quarry_sand_pile', 85, 80, True, plan=(C_COVER, ''))
    for (x, y) in [(27, 44), (27.7, 44.4), (88, 44)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    # ---------- дно: экскаватор, глыбы, лужи ----------
    s.prop('props/quarry_excavator_wreck', 54, 72, plan=(C_CAR, ''))
    s.prop('props/quarry_boulders', 42, 64, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 70, 62, True, plan=(C_COVER, ''))
    s.prop('props/quarry_sand_pile', 66, 80, plan=(C_COVER, ''))
    s.prop('props/quarry_boulders', 40, 82, plan=(C_COVER, ''))
    s.prop('props/crate_military', 73, 74, plan=(C_LOOT, ''))
    s.prop('props/junk_pile', 46, 80, plan=(C_SCRAP, ''))
    for (x, y) in [(60, 84), (60.8, 84.4), (37, 60)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(30, 24), (76, 24), (48, 42), (84, 68), (24, 66), (62, 60), (50, 84), (90, 26)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    lamp(s, 36, 66.4); lamp(s, 76, 84.6, True)
    s.prop('props/concrete_barrier', 6, 87.6, plan=(C_COVER, '')); s.prop('props/concrete_barrier', 104, 87.6, plan=(C_COVER, ''))
    # охрана: 4 (лучник на верхней бровке простреливает кабину и площадку)
    guard(s, 66, 16.4, 'bow', 'лучник на бровке')
    guard(s, 26, 16.6, 'club', 'сторож у бытовок (дубинка)')
    guard(s, 64, 46, 'pipe', 'сторож на уступе 2 (труба)', back=True)
    guard(s, 48, 66, 'pipe', 'сторож на дне (труба)')
    s.weeds((6, 3, 104, 17), 160, [(52, 5, 64, 14), (69.5, 3.5, 82.5, 12.5)], bush=0.08)
    s.weeds((8, 18, 104, 86), 260, [(34, 56, 78, 86)], bush=0.04)
    s.forest([(1, 1, 111, 87), (43, -9, 53, 4)], 0.5)
    s.notes = ['#car|техника, контейнер (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики', '#cover|постройки, глыбы, укрытия',
               '#mech|механика крана', '#guard|сторожа', '', '!Как проходить:', '1. Пробиться к крану на уступе 1.', '2. Сесть в кабину крана: кран гудит',
               '   на всю округу, кабина простре-', '   ливается с бровки и площадки.', '3. Повернуть стрелу к завалу,',
               '   магнит берёт контейнер с крейтом.', '4. Повернуть стрелу к бетонной', '   площадке наверху и опустить.',
               '   Магнит едет по стреле, контейнер', '   висит на виду.', '5. Перфокарта I → крейт в контейнере.', '',
               '!Охрана: 3–4 (лучник на бровке,', '   труба на дне и уступе, дубинка).', '!Награда: фрагменты ×3, перфокарта II', '   гарантированно + шанс второй.', '',
               'Уступы по 3 м, между ними съезды.', 'Выходы: карьерная дорога (юг),', 'подъезд к промплощадке (север),', 'съезд на запад.',
               'Размер ~110×90 м. Клетка = 1 м.']
    s.render('quarry')


#__RT__

RTS = ['garages', 'ats', 'depot', 'quarry']


def export_cs():
    out = ['// Сгенерировано scripts/mock-tier1.py — не править руками.', 'namespace Rastvordd.Sim', '{', '    public static class Tier1Layouts', '    {']
    for k in RTS:
        fp = os.path.join(ROOT_LAYOUT, f't1-{k}.json')
        js = open(fp, encoding='utf-8').read().replace('"', '""') if os.path.exists(fp) else ''   # всегда все четыре константы
        out.append(f'        public const string {k.capitalize()} = @"{js}";')
    # хитбоксы пропсов из обоих манифестов (общий + assets/manifest_t1.json)
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
    open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'game', 'src', 'Sim', 'Tier1Layouts.g.cs'), 'w', encoding='utf-8').write(chr(10).join(out))


if __name__ == '__main__':
    for w in sys.argv[1:] or RTS:
        globals()[w]()
    export_cs()
