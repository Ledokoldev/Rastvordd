# Макеты РТ тира 2 (Радиоцентр «Маяк») из игровых пиксельных спрайтов.
# Копия scripts/mock-tier1.py (32 px/м, высоты стен ×0.75, тени от силуэта, крыши с начинкой) + метки механик MECH:* для игры.
# Запуск: python scripts/mock-tier2.py [radio]
#   → docs/design/img/t2-<rt>-mock.png, -mock-half.png, -inside-half.png (без крыш), -plan.png;
#   → assets/layouts/t2-<rt>.json и game/src/Sim/Tier2Layouts.g.cs
import json, os, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__))).replace('\\', '/') + '/'
A = ROOT + 'game/art/'
OUT = ROOT + 'docs/design/img/'
PPM, HS = 32, 0.75
MAN = {a['id']: a for a in json.load(open(ROOT + 'assets/manifest.json', encoding='utf-8'))['assets']}
for _m in ('manifest_t1', 'manifest_t2'):
    MAN.update({a['id']: a for a in json.load(open(ROOT + 'assets/' + _m + '.json', encoding='utf-8'))['assets']})
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
        inside.resize((inside.width // 2, inside.height // 2), Image.LANCZOS).save(OUT + f't2-{key}-inside-half.png')
        self.draw_roofs()
        import json
        lay = {'W': self.W, 'H': self.H, 'M': self.M, 'grounds': self.ex['grounds'], 'walls': self.ex['walls'], 'props': self.ex['props'],
               'decals': self.ex['decals'], 'roofs': self.roofs, 'marks': [(x, y, lab) for (x, y, col, lab) in self.marks]}
        os.makedirs(ROOT_LAYOUT, exist_ok=True)
        with open(os.path.join(ROOT_LAYOUT, f't2-{key}.json'), 'w', encoding='utf-8') as fh:
            json.dump(lay, fh, ensure_ascii=False)
        im = self.img.convert('RGB')
        im.save(OUT + f't2-{key}-mock.png')
        im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(OUT + f't2-{key}-mock-half.png')
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
        im.save(OUT + f't2-{key}-plan.png')


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
# тир 2: общие куски
C_MECH, C_GUARD = (90, 200, 250), (225, 70, 60)
LEGEND.update({'#mech': C_MECH, '#guard': C_GUARD, '#rad': (150, 210, 90)})
GUARD_RU = {'pps': 'охранник (ППС-43)', 'svt': 'снайпер (СВТ-40)', 'grach': 'охранник (Грач)'}


def mech(s, x, y, tag, ru=None, col=C_MECH):
    """Метка механики для игры (MECH:…) + подпись по-русски на плане."""
    s.mark(x, y, col, 'MECH:' + tag)
    if ru:
        s.labels.append((x + 0.9, y - 0.7, ru))


def guard(s, x, y, weapon, ru=None, back=False):
    """Охранник: метка для игры (MECH:guard:<pps|svt|grach>) + фигура на макете (в планировку не идёт)."""
    mech(s, x, y, 'guard:' + weapon, ru or GUARD_RU[weapon], C_GUARD)
    spr = L('chars/char_guard_' + ('b0' if back else 'f0'))
    if spr is None:
        return
    a, b = s.px(x, y)
    s.add_shadow(spr.crop(spr.getchannel('A').getbbox()), a, b)
    s.objs.append((y, lambda spr=spr, a=a, b=b: s.img.alpha_composite(spr, (a - spr.width // 2, b - spr.height + 2))))


def lamp(s, x, y, flip=False):
    s.prop('props/subst_lamp', x, y, flip)
    s.mark(x, y - 0.3, (250, 230, 120), 'MECH:lamp')


def fallen(s, name, base, top):
    """Лежащая под углом мачта: спрайт (вид сбоку, основание слева) поворачивается вокруг основания к вершине.
    В планировку не идёт как пропс — игра ставит её по меткам MECH:fallen_base / MECH:fallen_top (поворотное украшение)."""
    import math
    spr = L(name)
    mech(s, base[0], base[1], 'fallen_base', 'основание упавшей мачты')
    mech(s, top[0], top[1], 'fallen_top', 'верх мачты (на заборе)')
    s.lines.append(([base, top], (150, 120, 100), 9))
    if spr is None:
        return
    dx, dy = top[0] - base[0], top[1] - base[1]
    ang = math.degrees(math.atan2(dy, dx))
    w, h = spr.size
    R = int(math.hypot(w, h)) + 4
    can = Image.new('RGBA', (2 * R, 2 * R))
    can.alpha_composite(spr, (R, R - h // 2))
    rot = can.rotate(-ang, resample=Image.NEAREST)
    a, b = s.px(*base)
    bb = rot.getchannel('A').getbbox()
    al = rot.getchannel('A').point(lambda v: 255 if v > 100 else 0)
    layer = Image.new('L', s.img.size, 0)
    layer.paste(al, (a - R + 8, b - R + 6))
    s.shadow = ImageChops.lighter(s.shadow, layer)
    s.keepout.append((min(base[0], top[0]) - 1, min(base[1], top[1]) - 1, max(base[0], top[0]) + 1, max(base[1], top[1]) + 1))
    s.objs.append((base[1], lambda: s.img.alpha_composite(rot, (a - R, b - R))))


def rack_row(s, x0, y, n, side=False, opened=(), stash=()):
    """Ряд стоек передатчика по 2,4 м; opened — номера раскуроченных, stash — номера с меткой MECH:rack (обыск: радиолампы)."""
    for i in range(n):
        x = x0 + i * 2.5
        name = 'props/radio_rack_open' if i in opened else ('props/radio_rack_side' if side else 'props/radio_rack')
        s.prop(name, x, y, plan=(C_COVER, ''))
        if i in stash:
            mech(s, x, y + 0.6, 'rack')


# =====================================================================================
def radio():
    s = Scene('Радиоцентр «Маяк» — РТ тира 2 (план, север сверху)', 184, 136, M=10, seed=303)
    s.legend = LEGEND
    s.ground('ground_grass')
    # подъездная дорога с юга к КПП
    s.ground('ground_gravel', s.rect(88.5, 124, 101.5, s.H + s.M), 5)
    s.ground('ground_asphalt', s.rect(90, 124, 100, s.H + s.M), 2)
    s.keepout.append((88, 124, 102, s.H + s.M + 5))
    # территория за забором ПО-2: 170×120 м
    FX0, FY0, FX1, FY1 = 7, 6, 177, 126
    s.ground('ground_gravel', s.rect(FX0, FY0, FX1, FY1), 6, zone=(C_ZONE['grav'], 'двор радиоцентра'))
    s.ground('ground_dry', s.rect(114, 58, 172, 101), 6, zone=((96, 104, 70), 'антенное поле (радиация)'))
    s.ground('ground_asphalt', s.rect(12, 47.5, 142, 54), 3, zone=(C_ZONE['asph'], 'проезд'))
    s.ground('ground_asphalt', s.rect(90, 54, 100, 126), 3, zone=(C_ZONE['asph'], ''))
    s.ground('ground_asphalt', s.rect(101, 105, 136, 124), 3, zone=(C_ZONE['asph'], 'стоянка'))
    s.ground('ground_concrete', s.rect(76, 56, 108, 86), 2, zone=(C_ZONE['conc'], 'площадка приёма груза'))
    s.ground('ground_concrete', s.rect(45, 75, 63, 91), 3, zone=(C_ZONE['conc'], 'антенна'))
    s.ground('ground_concrete', s.rect(138, 99, 174, 124), 3, zone=(C_ZONE['conc'], 'дизельная'))
    s.ground('ground_dirt', s.rect(9, 104, 36, 124), 6)
    # полы корпусов
    s.ground('ground_concrete', s.rect(12, 11, 44, 33), 0, zone=(C_ZONE['conc'], 'аппаратный зал'))
    s.ground('floor_tile', s.rect(44, 11, 66, 22), 0, zone=((120, 104, 88), 'пульт вызова'))
    s.ground('floor_linoleum', s.rect(44, 22, 66, 33), 0, zone=(C_ZONE['floor'], ''))
    s.ground('floor_linoleum', s.rect(12, 33, 66, 46), 0, zone=(C_ZONE['floor'], ''))
    s.ground('floor_wood', s.rect(74, 11, 94, 27), 0, zone=(C_ZONE['floor'], 'казарма'))
    s.ground('ground_concrete', s.rect(94, 11, 114, 27), 0, zone=(C_ZONE['conc'], 'гараж'))
    s.ground('ground_concrete', s.rect(146, 102, 170, 120), 0, zone=(C_ZONE['conc'], ''))
    for (x, y, lab) in [(44.3, 22.3, 'дежурка'), (55.3, 22.3, 'нач. центра'), (12.3, 33.2, 'коридор'), (12.3, 36.8, 'склад'),
                        (26.3, 36.8, 'лестн.'), (32.3, 36.8, 'вестибюль'), (46.3, 36.8, 'класс связи'), (146.3, 102.3, 'дизель')]:
        s.labels.append((x, y + 0.6, lab))
    cracks(s, (12, 47.5, 142, 54), 40)
    cracks(s, (76, 56, 108, 86), 30)
    cracks(s, (101, 105, 136, 124), 20)
    tufts(s, (FX0, FY0, FX1, FY1), 140, [(11, 10, 67, 47), (73, 10, 115, 28)])
    for (x, y, k) in [(95, 100, 1.5), (110, 112, 1.3), (150, 112, 1.6), (104, 24, 1.4), (60, 50, 1.2), (124, 118, 1.3)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y) in [(30, 50), (95, 115), (128, 108), (84, 52)]:
        s.dec('decal_oil_puddle', x, y, 1.2)
    for (x, y, r) in [(68, 30, 90), (141, 96, 0), (118, 44, 0), (130, 60, 90), (40, 56, 0)]:
        s.dec('decal_cables', x, y, 1.0, r)
    # площадка: большой крест краской в центре двора
    s.dec('radio_landing_x', 92, 71)
    mech(s, 92, 71, 'pad', 'ПЛОЩАДКА: сюда падает груз')

    # ---------- корпус передатчика (северо-запад) ----------
    BR, ST = 'wallface_brick', 'wallface_stucco'
    s.walls([(12, 11), (66, 11)], 5.0, BR)
    s.walls([(12, 11), (12, 46)], 5.0, BR, gaps=[(12, 20, 12, 21.8)])                      # боковая дверь зала (запад)
    s.walls([(66, 11), (66, 46)], 4.2, ST, gaps=[(66, 34, 66, 35.8)])                      # дверь коридора (восток)
    s.walls([(12, 46), (66, 46)], 4.2, ST, gaps=[(37, 46, 39.6, 46)])                      # главный вход (юг)
    s.walls([(44, 11), (44, 33)], 5.0, BR, gaps=[(44, 16, 44, 17.8)])                      # зал → пульт
    s.walls([(44, 22), (66, 22)], 4.2, ST, gaps=[(49, 22, 50.8, 22)])                      # дежурка → пульт
    s.walls([(55, 22), (55, 33)], 4.2, ST)
    s.walls([(12, 33), (66, 33)], 4.2, ST, gaps=[(26, 33, 28.6, 33), (48, 33, 49.8, 33), (59, 33, 60.8, 33)])
    s.walls([(12, 36.5), (66, 36.5)], 4.2, ST, gaps=[(18, 36.5, 19.8, 36.5), (28, 36.5, 29.8, 36.5), (37, 36.5, 40, 36.5), (54, 36.5, 55.8, 36.5)])
    for x in (26, 32, 46):
        s.wall(x, 36.5, x, 46, 4.2, ST)
    s.roof(12, 11, 44, 33, 5.0, 'roof_metal')
    s.roof(44, 11, 66, 33, 4.2, 'roof_slate')
    s.roof(12, 33, 66, 46, 4.2, 'roof_slate_mossy')
    s.labels += [(5.6, 19.0, 'бок. дверь'), (66.6, 33.0, 'дверь'), (34.4, 47.0, 'главный вход')]
    s.prop('props/door_lamp_post', 35.8, 47.4); s.prop('props/door_lamp_post', 10.9, 19.4); s.prop('props/door_lamp_post', 67.3, 33.8)
    # аппаратный зал: передатчики вдоль северной стены, три ряда стоек, проход посередине
    for x in (16.5, 22.5, 28.5, 34.5, 40.5):
        s.prop('props/radio_transmitter_big', x, 13.4, plan=(C_COVER, ''))
    rack_row(s, 15.2, 19.0, 4, False, opened=(2,), stash=(1,))
    rack_row(s, 32.2, 19.0, 4, False, stash=(3,))
    rack_row(s, 15.2, 24.6, 4, True, stash=(0,))
    rack_row(s, 32.2, 24.6, 4, True, opened=(1,), stash=(2,))
    rack_row(s, 15.2, 30.2, 4, False, stash=(2,))
    rack_row(s, 32.2, 30.2, 4, False, opened=(0,), stash=(3,))
    s.labels.append((27.2, 15.6, 'передатчики'))
    s.prop('props/subst_cable_drum', 42.4, 22.2, plan=(C_COVER, ''))
    # пульт вызова
    s.prop('props/radio_console', 55, 14.6, plan=(C_COVER, ''))
    mech(s, 55, 15.5, 'console', 'ПУЛЬТ ВЫЗОВА (ток + код + перфокарта II)')
    s.prop('props/radio_transmitter_big', 61.6, 13.4, plan=(C_COVER, ''))
    s.prop('props/radio_rack_side', 47.4, 13.4, plan=(C_COVER, ''))
    s.prop('props/radio_parts_shelf', 64.6, 21.5, plan=(C_COVER, ''))
    # дежурка: журнал дежурного
    s.prop('props/radio_duty_desk', 49.6, 27.0, plan=(C_COVER, ''))
    mech(s, 49.6, 27.9, 'journal', 'ЖУРНАЛ ДЕЖУРНОГО (код)')
    s.prop('furniture/locker_metal', 53.6, 23.0, plan=(C_COVER, ''))
    s.prop('furniture/cot_folding_side', 47.0, 31.8)
    s.prop('props/int_burzhuika', 53.6, 31.6)
    # кабинет начальника: сейф
    s.prop('furniture/table_office', 60.5, 27.2, plan=(C_COVER, ''))
    s.prop('furniture/bookcase', 57.0, 22.9, plan=(C_COVER, ''))
    s.prop('furniture/armchair', 60.5, 28.6)
    mech(s, 64.4, 23.4, 'safe', 'сейф начальника')
    # склад, лестница, вестибюль, класс связи
    for x in (14.0, 16.0, 21.5, 23.6):
        s.prop('props/radio_parts_shelf', x, 37.3, plan=(C_COVER, ''))
    s.prop('props/crate_military', 22.6, 44.6, plan=(C_LOOT, ''))
    s.prop('props/subst_reel_stack', 15.4, 44.6, plan=(C_COVER, ''))
    s.labels.append((26.4, 41.0, 'на крышу'))
    s.prop('furniture/bench_indoor', 39.0, 44.8)
    s.prop('props/phone_booth', 33.4, 38.6, plan=(C_COVER, ''))
    s.prop('furniture/coat_rack', 44.8, 37.4)
    for (x, y) in [(50.0, 40.0), (54.0, 40.0), (50.0, 43.4), (54.0, 43.4)]:
        s.prop('furniture/school_desk', x, y)
    s.prop('props/ats_desk_phones', 61.0, 40.6, plan=(C_COVER, ''))
    s.prop('props/radio_rack_open', 64.6, 37.3, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 63.4, 45.2, plan=(C_LOOT, ''))

    # ---------- казарма и гараж (север, середина) ----------
    s.walls([(74, 11), (114, 11)], 3.6, BR)
    s.walls([(74, 11), (74, 27)], 3.6, BR)
    s.walls([(74, 27), (94, 27)], 3.6, BR, gaps=[(80, 27, 81.8, 27), (90, 27, 91.8, 27)])
    s.walls([(88, 11), (88, 27)], 3.6, BR, gaps=[(88, 15, 88, 16.8)])
    s.walls([(94, 11), (94, 27)], 4.4, 'wallface_metal')
    s.walls([(114, 11), (114, 27)], 4.4, 'wallface_metal')
    s.walls([(94, 27), (114, 27)], 4.4, 'wallface_metal', gaps=[(98, 27, 108, 27)])
    s.roof(74, 11, 94, 27, 3.6, 'roof_slate')
    s.roof(94, 11, 114, 27, 4.4, 'roof_metal_patched')
    s.prop('props/gate_leaf_open', 99.3, 28.6); s.prop('props/gate_leaf_open', 106.7, 28.6, True)
    s.labels += [(97.0, 29.6, 'ворота гаража'), (88.3, 13.0, 'оружейка')]
    for x in (76.6, 80.4, 84.2):
        s.prop('furniture/bunk_bed_side', x + 0.4, 13.6, plan=(C_COVER, ''))
    s.prop('furniture/lockers_row_angled', 77.0, 25.6, plan=(C_COVER, ''))
    s.prop('props/int_table', 84.4, 21.0)
    s.prop('props/int_burzhuika', 86.4, 25.6)
    s.prop('furniture/shelf_metal_rack', 91.0, 11.8, plan=(C_COVER, ''))
    s.prop('props/crate_military', 91.0, 21.0, plan=(C_LOOT, ''))
    s.prop('vehicles/radio_relay_truck', 104.0, 21.6, plan=(C_CAR, ''))
    s.prop('furniture/tool_rack', 111.6, 11.8, plan=(C_COVER, ''))
    s.prop('props/crate_military', 111.0, 15.6, plan=(C_LOOT, ''))
    s.prop('props/barrel_oil', 96.0, 12.8, plan=(C_SCRAP, '')); s.prop('props/barrel_oil', 96.8, 13.3, plan=(C_SCRAP, ''))
    s.prop('props/door_lamp_post', 79.0, 28.4)

    # ---------- мачты (северо-восток): две стоят, третья упала к северному забору ----------
    s.prop('props/radio_mast', 152, 19, plan=(C_COVER, 'мачта 1'))
    s.prop('props/radio_mast', 163, 46, plan=(C_COVER, 'мачта 2'))
    mech(s, 152, 19, 'mast', 'мачта 1'); mech(s, 163, 46, 'mast', 'мачта 2')
    for (cx, cy) in [(152, 19), (163, 46)]:
        for (ax, ay) in [(-11, 6), (11, 6), (0, -12)]:
            x, y = cx + ax, cy + ay
            if FX0 + 1 < x < FX1 - 1 and FY0 + 1 < y < FY1 - 1:
                s.prop('props/concrete_barrier', x, y, plan=(C_COVER, ''))
                s.lines.append(([(cx, cy), (x, y)], (160, 160, 150), 1))
    fallen(s, 'props/radio_mast_fallen', (136.0, 27.5), (118.1, 3.4))
    s.prop('props/radio_antenna_scrap', 140.5, 31.0, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 133.0, 31.0, plan=(C_SCRAP, ''))
    s.labels.append((118.0, 0.4, 'ПРОЛОМ под мачтой'))

    # ---------- антенное поле (юго-восток от центра): ряды диполей ----------
    for y in (66, 76, 86, 96):
        for x in range(120, 171, 8):
            s.prop('props/radio_dipole', x + (2 if y % 20 == 6 else 0), y, plan=(C_COVER, ''))
    s.prop('props/radio_cable_rack', 124, 81, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 152, 71, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 140, 91.5, plan=(C_COVER, ''))
    s.prop('props/quarry_container', 136, 56, plan=(C_COVER, ''))
    s.labels.append((116, 59.0, 'диполи на столбах (низкие укрытия)'))

    # ---------- параболическая антенна (юго-запад от центра) ----------
    s.prop('props/radio_dish', 54, 87, plan=(C_COVER, 'антенна'))
    s.prop('props/subst_relay_cabinet', 60.8, 88.6, plan=(C_COVER, ''))

    # ---------- площадка приёма груза: пусто посередине, укрытия по краям ----------
    s.prop('props/radio_flare_launcher', 111.5, 60.5, plan=(C_COVER, ''))
    mech(s, 111.5, 61.4, 'flare', 'ПУСКОВАЯ РАКЕТ')
    for (x, y, f) in [(74.6, 56.0, False), (109.4, 56.0, True), (74.6, 87.2, False), (109.4, 87.2, True)]:
        lamp(s, x, y, f)
    s.prop('props/radio_sandbag_post', 70.0, 98.0, plan=(C_COVER, 'пост'))
    s.prop('outdoor/sandbags_wall', 112.6, 90.0, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 70.0, 66.0, plan=(C_COVER, ''))
    s.prop('props/quarry_container', 64.0, 74.0, plan=(C_COVER, ''))
    s.prop('vehicles/truck_zil130_side', 40.0, 64.0, True, plan=(C_CAR, ''))
    s.prop('props/concrete_barrier', 84.0, 92.6, plan=(C_COVER, '')); s.prop('props/concrete_barrier', 86.0, 92.8, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 104.0, 52.0, plan=(C_COVER, ''))

    # ---------- дизельная, ёмкость ГСМ, КТП (юго-восток); калитка в восточном заборе ----------
    MT = 'wallface_metal'
    s.walls([(146, 102), (170, 102)], 4.0, MT)
    s.walls([(146, 102), (146, 120)], 4.0, MT)
    s.walls([(170, 102), (170, 120)], 4.0, MT, gaps=[(170, 108, 170, 109.8)])
    s.walls([(146, 120), (170, 120)], 4.0, MT, gaps=[(152, 120, 154.6, 120)])
    s.roof(146, 102, 170, 120, 4.0, 'roof_metal_patched')
    s.prop('props/radio_diesel_big', 156.0, 108.2, plan=(C_COVER, ''))
    mech(s, 156.0, 109.2, 'diesel', 'ДИЗЕЛЬ (ток на 10 мин)')
    s.prop('props/crate_military', 166.6, 104.4, plan=(C_LOOT, ''))
    for (x, y) in [(148.2, 103.6), (149.0, 104.0), (148.4, 118.6), (168.4, 118.6)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop('furniture/tool_rack', 160.6, 102.8, plan=(C_COVER, ''))
    s.prop('props/radio_fuel_tank', 141.0, 110.0, plan=(C_COVER, 'ГСМ'))
    s.prop('props/subst_oil_tank', 157.0, 125.0, plan=(C_COVER, ''))
    s.prop('props/subst_ktp', 140.6, 121.4, plan=(C_COVER, 'КТП'))
    s.wall(FX1, 107.6, FX1, 110.4, 2.2, 'wallface_door_metal')
    mech(s, FX1 - 0.8, 109.0, 'sidegate', 'калитка (заперта, выбить 4 с)')
    s.prop('props/door_lamp_post', 171.4, 107.4)
    s.prop('props/door_lamp_post', 151.0, 121.6)

    # ---------- забор, КПП, стоянка ----------
    s.walls([(90, FY1), (FX0, FY1), (FX0, FY0), (FX1, FY0), (FX1, FY1), (100, FY1)], 2.4, 'wallface_fence_po2',
            gaps=[(118, FY0, 122.4, FY0), (FX1, 107.6, FX1, 110.4)])
    s.prop('props/subst_kpp', 85.0, 124.6, plan=(C_COVER, 'КПП'))
    s.prop('props/gai_barrier', 95.0, 128.8, plan=(C_COVER, ''))
    s.prop('props/gate_leaf_open', 91.0, 127.8); s.prop('props/gate_leaf_open', 99.0, 127.8, True)
    s.labels += [(88.0, 131.0, 'КПП, шлагбаум')]
    for (n, x, y, f) in [('vehicles/car_vaz2106_white_side', 106, 111.5, False), ('vehicles/van_uaz452_side', 113, 111.5, False),
                         ('vehicles/jeep_uaz469_side', 120, 111.5, True), ('vehicles/wreck_burnt_sedan_side', 128, 119.5, False),
                         ('vehicles/truck_gaz53_side', 110, 121.0, True)]:
        s.prop(n, x, y, f, plan=(C_CAR, ''))
    lamp(s, 88.6, 104); lamp(s, 101.6, 104, True); lamp(s, 70, 46.8); lamp(s, 116, 46.8, True); lamp(s, 30, 56); lamp(s, 136, 103, True)

    # ---------- вышка охраны (юго-запад) ----------
    s.prop('props/radio_watchtower', 15.0, 120.6, plan=(C_COVER, 'вышка'))
    mech(s, 15.0, 120.0, 'tower', 'вышка с прожектором')

    # ---------- хлам, бочки ----------
    for (x, y) in [(9.6, 8.6), (70.4, 8.6), (174.4, 60), (9.6, 70), (36, 100), (130, 46), (66, 120), (174, 124)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(68.6, 13.6), (69.4, 14.0), (9.8, 50), (10.6, 50.4), (118, 104), (118.8, 104.4), (44, 104), (174.2, 30), (60, 124), (60.8, 124.3)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(20, 96), (128, 42), (82, 40), (150, 60), (30, 74), (100, 36), (60, 110)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    s.prop('props/subst_cable_drum', 22, 60, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 24.4, 61, plan=(C_COVER, ''))
    s.prop('props/radio_antenna_scrap', 34, 112, plan=(C_SCRAP, ''))

    # ---------- запад: контейнерный двор и насосная, юг: ржавый автобус, бюст ----------
    s.ground('ground_concrete', s.rect(12, 58, 42, 74), 4, zone=(C_ZONE['conc'], 'контейнеры'))
    for (x, y, f) in [(18, 62.4, False), (18, 65.4, True), (31, 62.4, False), (25, 72.6, True), (37.6, 72.6, False)]:
        s.prop('props/quarry_container', x, y, f, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 24.6, 66.2, plan=(C_LOOT, ''))
    s.prop('props/radio_cable_rack', 36.6, 67.0, plan=(C_COVER, ''))
    s.walls([(14, 80), (28, 80), (28, 92), (14, 92), (14, 80)], 3.2, BR, gaps=[(20, 92, 21.8, 92)])
    s.roof(14, 80, 28, 92, 3.2, 'roof_ruberoid_patched')
    s.ground('ground_concrete', s.rect(14, 80, 28, 92), 0, zone=(C_ZONE['conc'], 'насосная'))
    s.prop('props/ats_diesel_gen', 21, 84.6, plan=(C_COVER, ''))
    s.prop('furniture/shelf_metal_rack', 26.2, 80.9, plan=(C_COVER, ''))
    s.prop('props/barrel_oil', 15.4, 90.6, plan=(C_SCRAP, ''))
    s.prop('outdoor/water_tower', 34.0, 90.0, plan=(C_COVER, ''))
    s.prop('vehicles/bus_paz672_side', 50.0, 118.0, plan=(C_CAR, ''))
    s.prop('outdoor/concrete_rings', 44.0, 104.0, plan=(C_COVER, ''))
    s.prop('outdoor/concrete_slabs_stack_side', 58.0, 66.0, plan=(C_COVER, ''))
    s.prop('outdoor/lenin_bust', 82.0, 100.0, plan=(C_COVER, ''))
    s.prop('outdoor/tank_traps_row_side', 128.0, 36.0, plan=(C_COVER, ''))
    s.prop('props/radio_antenna_scrap', 122.0, 24.0, True, plan=(C_SCRAP, ''))
    s.prop('props/subst_cable_drum', 145.0, 36.0, plan=(C_COVER, ''))
    s.prop('outdoor/sandbags_curve_angled', 150.0, 56.0, plan=(C_COVER, ''))
    s.prop('outdoor/dumpster_skip_side', 78.0, 33.0, plan=(C_COVER, ''))
    s.prop('vehicles/jeep_uaz469_angled', 84.0, 44.0, plan=(C_CAR, ''))
    s.prop('outdoor/sandbags_wall', 104.6, 44.0, plan=(C_COVER, ''))

    # ---------- охрана: 9 ----------
    guard(s, 88.0, 119.6, 'pps', 'КПП (ППС-43)')
    guard(s, 102.0, 120.6, 'pps', 'КПП (ППС-43)', back=True)
    guard(s, 15.0, 118.4, 'svt', 'вышка: снайпер (СВТ-40)')
    guard(s, 70.0, 96.0, 'pps', 'пост у площадки (ППС-43)', back=True)
    guard(s, 124.0, 52.0, 'grach', 'патруль (Грач)')
    guard(s, 40.0, 94.0, 'pps', 'патруль (ППС-43)', back=True)
    guard(s, 40.0, 34.8, 'grach', 'коридор (Грач)')
    guard(s, 28.0, 27.4, 'pps', 'аппаратный зал (ППС-43)')
    guard(s, 100.0, 24.6, 'grach', 'гараж (Грач)', back=True)

    s.weeds((FX0, FY0, FX1, FY1), 1100, [(11, 10, 67, 47), (12, 47.5, 142, 54), (90, 54, 100, 126), (76, 56, 108, 86), (101, 105, 136, 124),
                                        (73, 10, 115, 28), (145, 101, 171, 121), (45, 75, 63, 91), (12, 58, 42, 74), (13, 79, 29, 93)], bush=0.06)
    s.forest([(FX0 - 1, FY0 - 1, FX1 + 1, FY1 + 1), (88, 124, 102, 146)], 0.5)
    s.notes = ['#car|машины (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики тира 2', '#cover|постройки, стойки, укрытия',
               '#mech|механика', '#guard|охрана (9)', '', '!Как вызвать груз:', '1. Журнал дежурного (держать E 3 с)',
               '   — личный 4-значный код.', '2. Дизель (держать E 5 с, шум 60 м)', '   — ток корпусу на 10 минут.',
               '3. Пульт: ввести код + перфокарта II.', '4. Красная ракета с пусковой — видна', '   всей карте, по радио: «груз через',
               '   90 секунд».', '5. Ан-2 сбрасывает контейнер на', '   площадку: заперт 30 с, вскрыть 10 с.', '6. Перезарядка РТ — 20 минут.', '',
               '!Охрана: 9 (ППС-43, Грач, СВТ-40',
               '   на вышке), армейская броня.', '!Радиация 30 по всей территории.', '',
               '!Лут: 4 ящика тира 2, сейф', '   начальника, стойки (радиолампы,', '   радиодетали, золотые контакты),',
               '   контейнер сброса.', '',
               'Входы: КПП (юг), пролом под', 'упавшей мачтой (север), калитка у', 'дизельной (восток, выбить 4 с).',
               'Территория 170×120 м. Клетка = 1 м.']
    s.render('radio')


#__RT__

RTS = ['radio']


def export_cs():
    out = ['// Сгенерировано scripts/mock-tier2.py — не править руками.', 'namespace Rastvordd.Sim', '{', '    public static class Tier2Layouts', '    {']
    for k in RTS:
        fp = os.path.join(ROOT_LAYOUT, f't2-{k}.json')
        js = open(fp, encoding='utf-8').read().replace('"', '""') if os.path.exists(fp) else ''
        out.append(f'        public const string {k.capitalize()} = @"{js}";')
    # хитбоксы новых пропсов тира 2 (assets/manifest_t2.json); общие — в Tier0/Tier1Layouts.Foot
    t2 = json.load(open(ROOT + 'assets/manifest_t2.json', encoding='utf-8'))['assets']
    out.append('        public static readonly System.Collections.Generic.Dictionary<string, (float w, float d, float h, bool legs)> Foot = new()')
    out.append('        {')
    for a in t2:
        f = a.get('foot')
        if not f: continue
        legs = isinstance(f, dict)
        w, d, h = f['legs'] if legs else f
        out.append(f'            ["{a["id"]}"] = ({w}f, {d}f, {h}f, {"true" if legs else "false"}),')
    out.append('        };')
    out += ['    }', '}', '']
    open(os.path.join(ROOT, 'game', 'src', 'Sim', 'Tier2Layouts.g.cs'), 'w', encoding='utf-8').write(chr(10).join(out))


if __name__ == '__main__':
    for w in sys.argv[1:] or RTS:
        globals()[w]()
    export_cs()
