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
        self.rooms = []            # метки типа комнат для обстановки (Dressing.cs): (x, y, тип)
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

    def room(self, x, y, kind):
        """Тип комнаты для обстановки (game/src/Sim/Dressing.cs): точка внутри комнаты и тип (barracks, office, control, workshop,
        storage, kitchen, washroom, corridor, electrical, medbay, shop)."""
        self.rooms.append((x, y, kind))

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
               'decals': self.ex['decals'], 'roofs': self.roofs, 'marks': [(x, y, lab) for (x, y, col, lab) in self.marks], 'rooms': self.rooms}
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


def blob(x0, y0, x1, y1, j=0.9, cut=(1.5, 4.0), step=2.2, rng=None):
    """Неровный участок вместо прямоугольника: срезанные углы, рваные края (точки по краю с разбросом)."""
    r = rng or random
    c = [r.uniform(*cut) for _ in range(4)]
    base = [(x0 + c[0], y0), (x1 - c[1], y0), (x1, y0 + c[1]), (x1, y1 - c[2]), (x1 - c[2], y1), (x0 + c[3], y1), (x0, y1 - c[3]), (x0, y0 + c[0])]
    pts = []
    for (p, q) in zip(base, base[1:] + base[:1]):
        L_ = ((q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2) ** .5
        n = max(1, int(L_ / step))
        nx, ny = (q[1] - p[1]) / max(L_, 1e-6), -(q[0] - p[0]) / max(L_, 1e-6)
        for i in range(n):
            t = i / n
            k = 0 if i == 0 else r.uniform(-j, j)
            pts.append((round(p[0] + (q[0] - p[0]) * t + nx * k, 2), round(p[1] + (q[1] - p[1]) * t + ny * k, 2)))
    return pts


def bez(p0, p1, p2, n=8):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0], (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])
            for t in (i / n for i in range(n + 1))]


def road(s, pts, w, tile='ground_asphalt', edge='ground_gravel', tracks=True, zone=None):
    """Извилистая дорога по ломаной: полосы по участкам и «шайбы» на стыках, обочина гравием, колеи и трещины."""
    import math
    for layer, ww, blur in ((edge, w + 2.4, 4), (tile, w, 2)):
        if layer is None:
            continue
        for (p, q) in zip(pts, pts[1:]):
            dx, dy = q[0] - p[0], q[1] - p[1]
            ln = math.hypot(dx, dy) or 1
            nx, ny = -dy / ln * ww / 2, dx / ln * ww / 2
            ex, ey = dx / ln * 0.6, dy / ln * 0.6
            s.ground(layer, [(round(p[0] + nx - ex, 2), round(p[1] + ny - ey, 2)), (round(q[0] + nx + ex, 2), round(q[1] + ny + ey, 2)),
                             (round(q[0] - nx + ex, 2), round(q[1] - ny + ey, 2)), (round(p[0] - nx - ex, 2), round(p[1] - ny - ey, 2))], blur,
                     zone=zone if layer == tile and p == pts[0] else None)
    for (p, q) in zip(pts, pts[1:]):
        dx, dy = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(dx, dy) or 1
        ang = round(-math.degrees(math.atan2(dy, dx)) + 90, 1)
        k = int(ln / 5)
        for i in range(k):
            t = (i + random.random()) / max(k, 1)
            x, y = p[0] + dx * t, p[1] + dy * t
            if tracks and random.random() < 0.55:
                s.dec('decal_tire_tracks_mud', x + random.uniform(-w / 4, w / 4), y + random.uniform(-w / 4, w / 4), 1.0, ang)
            elif random.random() < 0.6:
                s.dec(random.choice(['decal_asphalt_crack', 'decal_crack_asphalt', 'decal_grass_tuft_2', 'decal_pebbles']), x + random.uniform(-w / 3, w / 3), y, 1.0, random.choice([0, 90, 180, 270]))
        s.keepout.append((min(p[0], q[0]) - w / 2, min(p[1], q[1]) - w / 2, max(p[0], q[0]) + w / 2, max(p[1], q[1]) + w / 2))


def stair(a, b, step=2.0):
    """Наклонный участок забора ступеньками (стены только по осям): точки от a к b."""
    pts = [a]
    x, y = a
    n = max(1, int(max(abs(b[0] - a[0]), abs(b[1] - a[1])) / step))
    sx, sy = (b[0] - a[0]) / n, (b[1] - a[1]) / n
    for _ in range(n):
        x += sx
        pts.append((round(x, 2), round(y, 2)))
        y += sy
        pts.append((round(x, 2), round(y, 2)))
    return pts


def mess(s, area, n, kinds=('decal_papers_scatter', 'decal_debris', 'decal_newspapers', 'decal_cigarette_butts', 'decal_glass_shards', 'decal_cables')):
    """Мусор на полу комнаты: бумаги, обломки, окурки, стекло, кабели (декали — без хитбоксов)."""
    x0, y0, x1, y1 = area
    for _ in range(n):
        s.dec(random.choice(kinds), random.uniform(x0 + 0.6, x1 - 0.6), random.uniform(y0 + 0.6, y1 - 0.6), 1.0, random.choice([0, 90, 180, 270]))


def litter(s, area, names, n, avoid=(), gap=1.1):
    """Россыпь мелочи по комнате: не ближе gap друг к другу и мимо дверей (avoid — прямоугольники проходов)."""
    x0, y0, x1, y1 = area
    placed, tries = [], 0
    while len(placed) < n and tries < n * 40:
        tries += 1
        x, y = random.uniform(x0 + 0.7, x1 - 0.7), random.uniform(y0 + 1.0, y1 - 0.4)
        if any(a <= x <= c and b <= y <= d for (a, b, c, d) in avoid) or any((x - u) ** 2 + (y - v) ** 2 < gap * gap for (u, v) in placed):
            continue
        placed.append((x, y))
        s.prop(random.choice(names), x, y, random.random() < .5)


F = lambda n: 'furniture/' + n
C = lambda n: 'containers/' + n


# =====================================================================================
def radio():
    s = Scene('Радиоцентр «Маяк» — РТ тира 2 (план, север сверху)', 184, 136, M=10, seed=303)
    s.legend = LEGEND
    rng = random.Random(77)
    s.ground('ground_grass')
    BR, ST, MT = 'wallface_brick', 'wallface_stucco', 'wallface_metal'
    PO2, CON, PROF, WOOD = 'wallface_fence_po2', 'fence_concrete_plain', 'fence_metal_profile', 'fence_wood_board'

    # ---------- территория: неровный двор по рельефу (срезанные углы, уступы), не прямоугольник ----------
    yard = ([(46, 126), (22, 123)] + stair((22, 123), (8, 109))[1:] + [(8, 72), (10, 72), (10, 40), (7, 40)] + stair((7, 18), (17, 6))
            + [(70, 6), (70, 4), (124, 4), (124, 7), (160, 7)] + stair((160, 7), (178, 25))[1:] + [(178, 60), (176, 60), (176, 96), (179, 96), (179, 124)]
            + stair((179, 124), (168, 130))[1:] + [(130, 130), (130, 126), (100, 126)])
    s.ground('ground_gravel', yard, 6, zone=(C_ZONE['grav'], 'двор радиоцентра'))
    s.ground('ground_dirt', blob(9, 98, 40, 124, j=1.6, rng=rng), 6)
    s.ground('ground_dirt', blob(126, 26, 160, 50, j=1.8, rng=rng), 6)
    # дороги: от КПП к площадке, вокруг неё, к главному входу, к гаражу, грунтовки к мачтам и дизельной
    s.ground('ground_gravel', s.rect(88.5, 124, 101.5, s.H + s.M), 5)
    s.ground('ground_asphalt', s.rect(90, 124, 100, s.H + s.M), 2)
    s.keepout.append((88, 124, 102, s.H + s.M + 5))
    road(s, bez((95, 128), (96, 112), (90, 98)) + [(88, 90)], 7.5, zone=(C_ZONE['asph'], ''))
    road(s, bez((108, 60), (112, 54), (100, 50)) + bez((100, 50), (70, 48), (40, 52))[1:], 7, zone=(C_ZONE['asph'], 'проезд'))
    road(s, bez((100, 50), (102, 40), (102, 30)), 6.5)
    road(s, bez((108, 60), (110, 70), (110, 86)), 6)
    road(s, bez((110, 86), (120, 102), (150, 104)) + [(150, 110)], 5, tile='ground_dirt', edge=None)
    road(s, bez((116, 50), (128, 40), (140, 32)) + bez((140, 32), (150, 28), (152, 22))[1:], 4, tile='ground_dirt', edge=None)
    road(s, bez((40, 52), (22, 58), (20, 78)), 4.5, tile='ground_dirt', edge=None)
    # площадка приёма груза: стёртый бетон, края раскрошены, трава в швах
    pad = blob(76, 56, 108, 86, j=1.2, cut=(2, 5), rng=rng)
    s.ground('ground_concrete', pad, 3, zone=(C_ZONE['conc'], 'площадка приёма груза'))
    s.dec('radio_landing_x', 92, 71)
    mech(s, 92, 71, 'pad', 'ПЛОЩАДКА: сюда падает груз')
    for _ in range(26):
        s.dec(random.choice(['decal_crack_concrete', 'decal_crack_concrete', 'decal_grass_tuft_1', 'decal_moss_patch', 'decal_puddle']), random.uniform(77, 107), random.uniform(57, 85), 1.0, random.choice([0, 90, 180, 270]))
    for (x, y) in [(79, 82), (105, 59), (100, 84)]:
        s.dec('decal_puddle_big', x, y, 1.0)
    # антенное поле — песок неровным пятном, бурьян
    s.ground('ground_dry', blob(113, 57, 172, 101, j=2.5, cut=(4, 9), step=3, rng=rng), 6, zone=((96, 104, 70), 'антенное поле (радиация)'))
    s.ground('ground_concrete', blob(45, 75, 63, 91, j=1.0, rng=rng), 3, zone=(C_ZONE['conc'], 'антенна'))
    s.ground('ground_asphalt', blob(102, 106, 135, 124, j=1.3, cut=(2, 6), rng=rng), 3, zone=(C_ZONE['asph'], 'стоянка'))
    s.ground('ground_concrete', blob(137, 98, 175, 125, j=1.2, rng=rng), 3, zone=(C_ZONE['conc'], 'дизельная'))
    s.ground('ground_concrete', blob(12, 57, 43, 75, j=1.0, rng=rng), 3, zone=(C_ZONE['conc'], 'контейнеры'))
    for (x, y) in [(84, 104), (60, 52), (126, 112), (30, 96), (140, 60), (70, 30), (150, 96)]:
        s.dec('decal_puddle_big', x, y, 1.0)
    for (x, y, k) in [(95, 100, 1.5), (110, 112, 1.3), (150, 112, 1.6), (104, 24, 1.4), (60, 50, 1.2), (124, 118, 1.3)]:
        s.dec('decal_oil_stain', x, y, k)
    for (x, y, r) in [(68, 30, 90), (141, 96, 0), (118, 44, 0), (130, 60, 90), (40, 56, 0)]:
        s.dec('decal_cables', x, y, 1.0, r)
    tufts(s, (7, 4, 179, 130), 180, [(11, 7, 65, 50), (73, 8, 116, 33)])

    # ---------- корпус передатчика: Т-образный — зал, восточное крыло, южное крыло, пристройка, тамбур ----------
    # полы — свой на каждую комнату
    for (tile, r, lab) in [('ground_concrete', (12, 11, 44, 33), 'аппаратный зал'), ('floor_tile', (44, 14, 64, 23), 'пульт вызова'),
                           ('floor_linoleum_green', (44, 23, 54, 33), 'дежурка'), ('floor_parquet', (54, 23, 64, 33), 'нач. центра'),
                           ('floor_linoleum', (16, 33, 58, 36.5), 'коридор'), ('ground_concrete', (16, 36.5, 28, 45), 'склад'),
                           ('floor_tile', (28, 36.5, 32, 45), 'лестн.'), ('floor_tile_brown', (32, 36.5, 42, 49), 'вестибюль'),
                           ('floor_linoleum_green', (42, 36.5, 58, 45), 'класс связи'), ('floor_tile', (50, 8, 60, 14), 'аккум.')]:
        s.ground(tile, s.rect(*r), 0, zone=(C_ZONE['floor'] if 'floor' in tile else C_ZONE['conc'], lab))
    # зал (высокий кирпич)
    s.walls([(12, 33), (12, 11), (44, 11), (44, 14)], 5.0, BR, gaps=[(12, 20, 12, 21.8)])            # боковая дверь зала (запад)
    s.walls([(44, 14), (44, 33)], 5.0, BR, gaps=[(44, 16.5, 44, 18.3)])                                # зал → пульт
    # восточное крыло (штукатурка), уступ северной стены
    s.walls([(44, 14), (50, 14)], 4.2, ST); s.walls([(60, 14), (64, 14), (64, 33), (58, 33)], 4.2, ST, gaps=[(64, 27, 64, 28.8)])   # окно-дверь кабинета? — нет: запасной выход начальника (восток)
    s.walls([(44, 23), (64, 23)], 4.2, ST, gaps=[(49, 23, 50.8, 23)])                                 # дежурка → пульт
    s.wall(54, 23, 54, 33, 4.2, ST)
    # пристройка-аккумуляторная (север, железная кровля), дверь из пультовой
    s.walls([(50, 14), (50, 8), (60, 8), (60, 14)], 3.2, BR)
    s.walls([(50, 14), (60, 14)], 3.2, BR, gaps=[(52, 14, 53.8, 14)])
    # южное крыло: коридор и комнаты, уже зала (Т-образный силуэт)
    s.walls([(12, 33), (16, 33)], 5.0, BR)
    s.walls([(16, 33), (44, 33)], 4.2, ST, gaps=[(26, 33, 28.6, 33)])
    s.walls([(44, 33), (54, 33)], 4.2, ST, gaps=[(48, 33, 49.8, 33)])
    s.walls([(54, 33), (58, 33)], 4.2, ST, gaps=[(55, 33, 56.8, 33)])
    s.walls([(16, 33), (16, 45), (32, 45)], 4.2, ST)
    s.walls([(42, 45), (58, 45), (58, 33)], 4.2, ST, gaps=[(58, 34, 58, 35.8)])                       # торец коридора (восток)
    s.walls([(16, 36.5), (58, 36.5)], 4.2, ST, gaps=[(20, 36.5, 21.8, 36.5), (29, 36.5, 30.8, 36.5), (35, 36.5, 39, 36.5), (49, 36.5, 50.8, 36.5)])
    s.wall(28, 36.5, 28, 45, 4.2, ST); s.wall(32, 36.5, 32, 45, 4.2, ST); s.wall(42, 36.5, 42, 45, 4.2, ST)
    # тамбур главного входа — выдвинут на юг и приподнят (крыльцо строит игра)
    s.walls([(32, 45), (32, 49), (36, 49)], 3.6, BR); s.walls([(38.6, 49), (42, 49), (42, 45)], 3.6, BR)
    s.roof(12, 11, 44, 33, 5.0, 'roof_metal')
    s.roof(44, 14, 64, 33, 4.2, 'roof_slate')
    s.roof(50, 8, 60, 14, 3.2, 'roof_metal_green')
    s.roof(16, 33, 58, 45, 4.2, 'roof_slate_mossy')
    s.roof(32, 45, 42, 49, 3.6, 'roof_concrete_slab')
    s.labels += [(5.6, 19.0, 'бок. дверь'), (58.6, 33.4, 'дверь'), (33.0, 50.2, 'главный вход (тамбур)'), (64.6, 26.4, 'запасн.')]
    s.prop('props/door_lamp_post', 35.0, 50.6); s.prop('props/door_lamp_post', 10.9, 19.4); s.prop('props/door_lamp_post', 59.3, 33.8)
    s.prop('props/shed_canopy', 61.5, 44.6, plan=(C_COVER, 'навес'))
    s.prop(C('barrels_group_angled'), 60.4, 41.0, plan=(C_SCRAP, ''))
    s.prop(F('bench_indoor'), 46.0, 48.0)

    # аппаратный зал: передатчики у северной стены, операторские кресла, три пары рядов стоек, кабель-каналы, ЗИП
    for x in (16.5, 22.5, 28.5, 34.5, 40.5):
        s.prop('props/radio_transmitter_big', x, 13.4, plan=(C_COVER, ''))
        s.prop(F('chair_office'), x + random.uniform(-1, 1), 15.2)
    rack_row(s, 15.2, 19.6, 4, False, opened=(2,), stash=(1,))
    rack_row(s, 32.2, 19.6, 4, False, stash=(3,))
    rack_row(s, 15.2, 25.0, 4, True, stash=(0,))
    rack_row(s, 32.2, 25.0, 4, True, opened=(1,), stash=(2,))
    rack_row(s, 15.2, 30.4, 4, False, stash=(2,))
    rack_row(s, 32.2, 30.4, 4, False, opened=(0,), stash=(3,))
    for (x, y) in [(18.0, 22.4), (36.0, 22.4), (18.0, 27.8), (36.0, 27.8)]:
        s.dec('decal_cables', x, y, 1.0, 0); s.dec('decal_cables', x + 3, y, 1.0, 0)
    s.prop(F('int_cable_trays'), 41.6, 22.0, plan=(C_COVER, ''))
    s.prop(F('int_operator_desk'), 20.0, 32.4, plan=(C_COVER, ''))
    s.prop(F('int_parts_crates'), 42.2, 32.4, plan=(C_COVER, ''))
    s.prop(F('int_tube_crates'), 13.4, 27.6, plan=(C_COVER, ''))
    for (x, y) in [(12.6, 13.0), (43.4, 25.6), (12.6, 32.6)]:
        s.prop(F('int_fire_extinguisher'), x, y)
    s.prop('props/subst_cable_drum', 42.4, 28.4, plan=(C_COVER, ''))
    s.labels.append((27.2, 15.9, 'передатчики'))
    mess(s, (12, 15, 44, 33), 30)
    for (n, x, y) in [(C('crate_wood_small'), 31.0, 32.6), (C('cardboard_box_angled'), 32.6, 32.5), (C('ammo_box'), 34.0, 32.6),
                      (C('cardboard_boxes_stack'), 36.2, 32.4), (F('medical_cabinet'), 13.2, 15.6), (F('locker_metal'), 13.0, 18.0),
                      (C('cardboard_box_angled'), 29.2, 16.6), (F('chair_office'), 23.6, 22.6), (F('stool_metal'), 37.4, 27.6),
                      (C('crate_wood_small'), 43.0, 14.6), (F('int_tube_crates'), 43.0, 31.0)]:
        s.prop(n, x, y)
    litter(s, (14, 15.5, 43, 18.6), ['clutter/clutter_planks', 'clutter/clutter_bucket', 'clutter/clutter_pallet'], 4)
    litter(s, (14, 26.0, 43, 29.4), ['clutter/clutter_planks', 'clutter/clutter_bucket'], 3, avoid=[(25, 25, 30, 33)])
    # аккумуляторная
    s.prop('props/ats_battery_rack', 55.0, 10.4, plan=(C_COVER, ''))
    s.prop('props/ats_rectifier', 51.2, 9.2, plan=(C_COVER, ''))
    s.prop(C('canister_plastic'), 58.8, 13.0)
    # пульт вызова
    s.prop('props/radio_console', 54, 17.6, plan=(C_COVER, ''))
    mech(s, 54, 18.5, 'console', 'ПУЛЬТ ВЫЗОВА (ток + код + перфокарта II)')
    s.prop('props/radio_transmitter_big', 60.8, 16.4, plan=(C_COVER, ''))
    s.prop('props/radio_rack_side', 46.8, 16.4, plan=(C_COVER, ''))
    s.prop(F('int_operator_desk'), 47.6, 22.2, plan=(C_COVER, ''))
    s.prop(F('chair_office'), 47.0, 21.0)
    s.prop(F('int_notice_board'), 57.6, 15.0, plan=(C_COVER, ''))
    s.prop('props/radio_parts_shelf', 62.6, 22.4, plan=(C_COVER, ''))
    s.prop(F('int_fire_extinguisher'), 44.6, 22.6)
    mess(s, (44, 14, 64, 23), 6)
    # дежурка: журнал, телефоны, чайник, койка, шкафчики, доска объявлений
    s.prop('props/radio_duty_desk', 48.6, 27.0, plan=(C_COVER, ''))
    mech(s, 48.6, 27.9, 'journal', 'ЖУРНАЛ ДЕЖУРНОГО (код)')
    s.prop(F('chair_office'), 48.6, 25.9)
    s.prop('props/ats_desk_phones', 46.2, 24.4, plan=(C_COVER, ''))
    s.prop(F('int_tea_table'), 52.6, 27.0, plan=(C_COVER, ''))
    s.prop(F('locker_metal'), 52.9, 30.0, plan=(C_COVER, ''))
    s.prop(F('cot_folding_side'), 46.4, 32.6)
    s.prop(F('int_notice_board'), 52.6, 23.6, plan=(C_COVER, ''))
    s.prop(F('coat_rack'), 53.4, 32.6)
    mess(s, (44, 23, 54, 33), 5, ('decal_papers_scatter', 'decal_cigarette_butts', 'decal_newspapers'))
    # кабинет начальника: ковёр, стол, сейф, книжный шкаф, портрет, знамя, диван
    s.dec('decal_carpet_red', 59.0, 28.0, 1.0, 90)
    s.prop(F('table_office'), 59.0, 27.6, plan=(C_COVER, ''))
    s.prop(F('chair_office'), 59.0, 26.0)
    s.prop(F('chair_kitchen'), 58.2, 29.4); s.prop(F('chair_kitchen'), 60.0, 29.4)
    s.prop(F('bookcase'), 55.6, 24.0, plan=(C_COVER, ''))
    s.prop(F('bookcase'), 56.6, 24.0, plan=(C_COVER, ''))
    s.prop(F('int_portrait_easel'), 61.0, 24.2, plan=(C_COVER, ''))
    s.prop(F('int_flag_stand'), 63.2, 24.4, plan=(C_COVER, ''))
    s.prop(F('int_sofa_office'), 61.0, 32.6, plan=(C_COVER, ''))
    s.prop(F('int_dead_plant'), 63.2, 30.0)
    s.prop(F('tv_crt_angled'), 63.0, 25.8)
    mech(s, 63.2, 32.2, 'safe', 'сейф начальника')
    # коридор: скамьи, огнетушители, цветок, мусор и стекло
    for x in (18.5, 33.0, 45.0):
        s.prop(F('bench_indoor'), x, 33.7)
    s.prop(F('int_fire_extinguisher'), 24.0, 33.6)
    s.prop(F('int_dead_plant'), 52.6, 33.8)
    s.prop(F('int_notice_board'), 41.0, 33.8, plan=(C_COVER, ''))
    mess(s, (16, 33, 58, 36.5), 18, ('decal_glass_shards', 'decal_papers_scatter', 'decal_debris', 'decal_leaves_scatter'))
    # склад: стеллажи с коробками, ЗИП, ящик тира 2
    s.prop(F('rack_warehouse_angled'), 18.4, 38.6, plan=(C_COVER, ''))
    s.prop(F('rack_warehouse_angled'), 25.6, 38.6, True, plan=(C_COVER, ''))
    s.prop(F('shelf_metal_rack'), 16.8, 44.6, plan=(C_COVER, ''))
    s.prop('props/radio_parts_shelf', 21.4, 44.6, plan=(C_COVER, ''))
    s.prop(C('cardboard_boxes_stack'), 18.6, 42.0)
    s.prop(F('int_tube_crates'), 26.6, 42.0, plan=(C_COVER, ''))
    s.prop('props/crate_military', 24.6, 44.6, plan=(C_LOOT, ''))
    mess(s, (16, 36.5, 28, 45), 8)
    for (n, x, y) in [(C('cardboard_boxes_stack'), 22.6, 40.6), (C('crate_wood_stack'), 17.4, 41.4), (C('cardboard_box_angled'), 20.4, 43.4), (C('ammo_box'), 23.0, 42.8)]:
        s.prop(n, x, y)
    # лестница на крышу и вестибюль
    s.labels.append((28.3, 41.0, 'на крышу'))
    s.prop(C('crate_wood_small'), 30.0, 44.4)
    s.prop(F('bench_indoor'), 34.6, 44.6)
    s.prop('props/phone_booth', 41.0, 38.6, plan=(C_COVER, ''))
    s.prop(F('int_dead_plant'), 33.0, 38.0)
    s.prop('outdoor/trash_urn', 40.8, 44.6)
    s.prop(F('coat_rack'), 33.2, 48.2)
    mess(s, (32, 36.5, 42, 49), 4, ('decal_glass_shards', 'decal_leaves_scatter', 'decal_newspapers'))
    # класс связи: парты, стол преподавателя, стойка-тренажёр
    for (x, y) in [(46.0, 40.2), (49.4, 40.2), (52.8, 40.2), (46.0, 43.6), (49.4, 43.6), (52.8, 43.6)]:
        s.prop(F('school_desk'), x, y)
        s.prop(F('chair_school'), x, y + 0.7)
    s.prop(F('table_office'), 56.0, 38.2, plan=(C_COVER, ''))
    s.prop('props/radio_rack_open', 43.2, 37.6, plan=(C_COVER, ''))
    s.prop(F('int_notice_board'), 46.4, 37.4, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 56.6, 44.4, plan=(C_LOOT, ''))
    mess(s, (42, 36.5, 58, 45), 5)

    # ---------- казарма (Г-образная, с умывальной) и гараж с навесом ----------
    for (tile, r, lab) in [('floor_wood', (74, 11, 88, 26), 'кубрик'), ('floor_tile', (88, 11, 92, 26), 'оруж.'), ('floor_tile', (74, 26, 82, 32), 'умыв.'),
                           ('ground_concrete', (92, 9, 112, 28), 'гараж')]:
        s.ground(tile, s.rect(*r), 0, zone=(C_ZONE['floor'] if 'floor' in tile else C_ZONE['conc'], lab))
    s.walls([(74, 26), (74, 11), (92, 11)], 3.6, BR)
    s.walls([(82, 26), (92, 26)], 3.6, BR, gaps=[(84, 26, 85.8, 26), (89, 26, 90.6, 26)])
    s.walls([(74, 26), (82, 26)], 3.6, BR, gaps=[(77, 26, 78.8, 26)])
    s.walls([(74, 26), (74, 32), (82, 32), (82, 26)], 3.0, ST)
    s.wall(88, 11, 88, 26, 3.6, BR)
    s.walls([(92, 26), (92, 28)], 4.4, MT)
    s.walls([(92, 11), (92, 9), (112, 9), (112, 28), (108, 28)], 4.4, MT)
    s.walls([(92, 28), (96, 28)], 4.4, MT)
    s.wall(92, 11, 92, 26, 4.4, MT)
    s.roof(74, 11, 92, 26, 3.6, 'roof_slate_new')
    s.roof(74, 26, 82, 32, 3.0, 'roof_ruberoid_torn')
    s.roof(92, 9, 112, 28, 4.4, 'roof_metal_patched')
    s.prop('props/gate_leaf_open', 97.3, 29.6); s.prop('props/gate_leaf_open', 106.7, 29.6, True)
    s.prop('props/shed_canopy', 115.4, 27.6, plan=(C_COVER, 'навес'))
    s.labels += [(96.0, 30.6, 'ворота гаража'), (88.3, 13.0, 'оружейка')]
    s.prop('props/door_lamp_post', 83.4, 27.4)
    # кубрик: двухъярусные кровати, шкафчики, стол со стульями, печка, сушилка, сапоги
    for x in (76.0, 79.2, 82.4, 85.6):
        s.prop(F('bunk_bed_side'), x, 12.6, plan=(C_COVER, ''))
    for x in (76.0, 79.2, 82.4):
        s.prop(F('bunk_bed_side'), x, 17.4, plan=(C_COVER, ''))
    for x in (79.4, 80.3, 81.2):
        s.prop(F('locker_metal'), x, 25.7, plan=(C_COVER, ''))
    s.prop(F('wardrobe_single'), 87.2, 13.2, plan=(C_COVER, ''))
    s.prop(F('tv_on_stand'), 87.3, 25.6)
    for (x, y) in [(84.0, 21.6), (83.0, 23.0)]:
        s.prop(F('stool_wood'), x, y)
    s.prop(C('suitcase_old'), 77.4, 18.6)
    litter(s, (74, 18.5, 88, 25.5), ['clutter/int_boots_pair', 'clutter/clutter_mattress', 'clutter/clutter_bucket', 'clutter/clutter_planks'], 6, avoid=[(83.5, 23, 86.5, 26), (76.5, 23, 79.3, 26)])
    s.prop('clutter/clutter_bucket', 87.0, 19.6)
    s.prop(F('lockers_row_angled'), 86.6, 17.6, plan=(C_COVER, ''))
    s.prop(F('table_kitchen'), 80.6, 21.6, plan=(C_COVER, ''))
    for (x, y) in [(79.8, 22.4), (81.4, 22.4), (80.6, 20.2)]:
        s.prop(F('chair_kitchen'), x, y)
    s.prop('props/int_burzhuika', 75.4, 24.8)
    s.prop(F('int_drying_rack'), 86.4, 21.4, plan=(C_COVER, ''))
    for (x, y) in [(77.8, 13.4), (83.8, 18.6), (76.6, 22.0)]:
        s.prop('clutter/int_boots_pair', x, y)
    s.prop(C('crate_wood_small'), 75.2, 18.6)
    mess(s, (74, 11, 88, 26), 14, ('decal_papers_scatter', 'decal_cigarette_butts', 'decal_newspapers', 'decal_debris', 'decal_trash_scatter'))
    # умывальная
    for x in (75.4, 76.5, 80.8):
        s.prop(F('washbasin'), x, 27.0)
    s.prop(F('toilet'), 81.0, 31.6)
    s.prop('clutter/clutter_bucket', 75.2, 31.2)
    # оружейка: стеллажи, ящик тира 2, цинки
    s.prop(F('shelf_metal_rack'), 90.0, 11.8, plan=(C_COVER, ''))
    s.prop('props/crate_military', 90.0, 21.0, plan=(C_LOOT, ''))
    s.prop(C('ammo_box'), 89.0, 23.6)
    s.prop(F('int_parts_crates'), 90.0, 16.4, plan=(C_COVER, ''))
    # гараж: смотровая яма, верстак, стена инструментов, покрышки, бочки, баллоны, радиорелейка
    s.dec('decal_garage_pit', 98.0, 18.0)
    s.prop('vehicles/radio_relay_truck', 106.0, 22.4, plan=(C_CAR, ''))
    s.prop(F('table_workshop'), 101.0, 10.6, plan=(C_COVER, ''))
    s.prop(F('tool_rack'), 96.0, 9.6, plan=(C_COVER, ''))
    s.prop(F('tool_rack'), 105.0, 9.6, plan=(C_COVER, ''))
    s.prop('outdoor/tires_pile', 110.4, 27.0, plan=(C_COVER, ''))
    s.prop(C('barrels_group_angled'), 93.8, 23.0, plan=(C_SCRAP, ''))
    s.prop('props/gar_shelf_jars', 109.6, 9.9, plan=(C_COVER, ''))
    s.prop('props/gar_engine_stand', 95.0, 17.6, plan=(C_COVER, ''))
    s.prop('props/gar_car_jacks', 102.6, 16.4, plan=(C_CAR, ''))
    s.prop('props/gar_workbench_vise', 107.0, 9.9, plan=(C_COVER, ''))
    litter(s, (93, 23.5, 111, 27.6), ['clutter/clutter_tire', 'clutter/clutter_canister', 'clutter/clutter_planks', 'clutter/clutter_pallet'], 5, avoid=[(96, 24, 108, 28)])
    s.prop('props/gar_tire_stack', 111.0, 24.0, plan=(C_COVER, ''))
    s.prop('props/gar_scrap_heap', 110.0, 12.8, plan=(C_SCRAP, ''))
    s.prop(F('stool_metal'), 101.4, 11.6)
    mess(s, (92, 9, 112, 28), 8, ('decal_oil_puddle', 'decal_trash_scatter', 'decal_debris', 'decal_oil_stain'))
    s.prop('props/crate_military', 110.6, 15.6, plan=(C_LOOT, ''))
    s.prop(C('gas_cylinders_rack_angled'), 93.6, 27.0, plan=(C_COVER, ''))
    s.prop(C('tool_chest_angled'), 94.4, 11.4)
    s.prop(C('canister_steel'), 100.0, 13.0); s.prop(C('canister_steel'), 100.5, 13.2)
    s.prop('props/barrel_oil', 94.0, 21.0, plan=(C_SCRAP, ''))
    for (x, y) in [(102, 16), (108, 18), (96, 24)]:
        s.dec('decal_oil_stain', x, y, 1.2)

    # ---------- мачты (северо-восток): две стоят, третья упала к северному забору ----------
    s.prop('props/radio_mast', 152, 19, plan=(C_COVER, 'мачта 1'))
    s.prop('props/radio_mast', 163, 46, plan=(C_COVER, 'мачта 2'))
    mech(s, 152, 19, 'mast', 'мачта 1'); mech(s, 163, 46, 'mast', 'мачта 2')
    for (cx, cy) in [(152, 19), (163, 46)]:
        for (ax, ay) in [(-11, 6), (11, 6), (0, -12)]:
            x, y = cx + ax, cy + ay
            if 8 < x < 175 and 8 < y < 124 and not (cx == 152 and ay == -12):
                s.prop('props/concrete_barrier', x, y, plan=(C_COVER, ''))
                s.lines.append(([(cx, cy), (x, y)], (160, 160, 150), 1))
    fallen(s, 'props/radio_mast_fallen', (136.0, 27.5), (118.1, 3.4))
    s.prop('props/radio_antenna_scrap', 140.5, 31.0, plan=(C_SCRAP, ''))
    s.prop('props/junk_pile', 133.0, 31.0, plan=(C_SCRAP, ''))
    s.prop('props/radio_antenna_scrap', 122.0, 24.0, True, plan=(C_SCRAP, ''))
    s.prop('outdoor/tank_traps_row_side', 128.0, 38.0, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 146.0, 37.0, plan=(C_COVER, ''))
    s.labels.append((116.0, 1.6, 'ПРОЛОМ под мачтой'))

    # ---------- антенное поле: ряды диполей (часть повалена), фидеры, барабаны ----------
    for yi, y in enumerate((66, 76, 86, 96)):
        for xi, x in enumerate(range(120, 171, 8)):
            if (yi, xi) in ((1, 2), (2, 5), (3, 0), (0, 6)):
                continue                                  # повалены — лежат кучей
            s.prop('props/radio_dipole', x + (3 if yi % 2 else 0) + rng.uniform(-0.6, 0.6), y + rng.uniform(-0.8, 0.8), plan=(C_COVER, ''))
    for (x, y) in [(136, 76), (160, 86)]:
        s.prop('props/radio_antenna_scrap', x, y, plan=(C_SCRAP, ''))
    s.prop('props/radio_cable_rack', 124, 81, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 152, 71, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 140, 91.5, plan=(C_COVER, ''))
    s.prop('props/quarry_container', 136, 56, plan=(C_COVER, ''))
    s.labels.append((116, 59.0, 'диполи на столбах (низкие укрытия)'))

    # ---------- параболическая антенна ----------
    s.prop('props/radio_dish', 54, 87, plan=(C_COVER, 'антенна'))
    s.prop('props/subst_relay_cabinet', 60.8, 88.6, plan=(C_COVER, ''))

    # ---------- площадка: пусто посередине, укрытия по краям ----------
    s.prop('props/radio_flare_launcher', 111.5, 60.5, plan=(C_COVER, ''))
    mech(s, 111.5, 61.4, 'flare', 'ПУСКОВАЯ РАКЕТ')
    for (x, y, f) in [(75.0, 57.0, False), (109.0, 56.6, True), (75.6, 86.4, False), (108.6, 86.8, True)]:
        lamp(s, x, y, f)
    s.prop('props/radio_sandbag_post', 70.0, 98.0, plan=(C_COVER, 'пост'))
    s.prop('outdoor/sandbags_wall', 114.0, 90.0, plan=(C_COVER, ''))
    s.prop('outdoor/sandbags_curve_angled', 72.0, 62.0, plan=(C_COVER, ''))
    s.prop('props/quarry_container', 64.0, 74.0, plan=(C_COVER, ''))
    s.prop('vehicles/truck_zil130_angled', 42.0, 64.0, True, plan=(C_CAR, ''))
    s.prop('props/concrete_barrier', 84.0, 93.6, plan=(C_COVER, '')); s.prop('props/concrete_barrier', 86.0, 93.8, plan=(C_COVER, ''))
    s.prop('props/concrete_barrier', 116.0, 52.0, plan=(C_COVER, ''))

    # ---------- дизельная (Г-образная), навес над ёмкостью, КТП; калитка в восточном заборе ----------
    s.ground('ground_concrete', s.rect(146, 102, 168, 116), 0)
    s.ground('ground_concrete', s.rect(158, 116, 168, 121), 0)
    s.walls([(146, 116), (146, 102), (168, 102), (168, 121), (158, 121), (158, 116), (146, 116)], 4.0, MT,
            gaps=[(168, 108, 168, 109.8), (150, 116, 152.6, 116)])
    s.roof(146, 102, 168, 116, 4.0, 'roof_metal_patched')
    s.roof(158, 116, 168, 121, 3.4, 'roof_metal_red')
    s.labels.append((146.3, 102.9, 'дизельная'))
    s.prop('props/radio_diesel_big', 156.0, 108.2, plan=(C_COVER, ''))
    mech(s, 156.0, 109.2, 'diesel', 'ДИЗЕЛЬ (ток на 10 мин)')
    s.prop('props/crate_military', 166.6, 104.4, plan=(C_LOOT, ''))
    for (x, y) in [(148.2, 103.6), (149.0, 104.0), (166.4, 119.6), (159.6, 119.6)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    s.prop(F('tool_rack'), 161.0, 102.6, plan=(C_COVER, ''))
    s.prop(F('table_workshop'), 148.8, 110.6, plan=(C_COVER, ''))
    s.prop(F('int_fire_extinguisher'), 146.8, 108.0)
    s.prop(C('canister_steel'), 163.6, 113.0); s.prop(C('canister_steel'), 164.1, 113.3)
    s.prop('props/radio_fuel_tank', 141.0, 110.0, plan=(C_COVER, 'ГСМ'))
    s.prop('props/shed_canopy', 141.0, 112.6)
    s.prop('props/subst_oil_tank', 152.0, 125.0, plan=(C_COVER, ''))
    s.prop('props/subst_ktp', 140.6, 121.4, plan=(C_COVER, 'КТП'))
    mech(s, 178.2, 109.0, 'sidegate', 'калитка (заперта, выбить 4 с)')
    s.prop('props/door_lamp_post', 170.0, 107.0)
    s.prop('props/door_lamp_post', 153.6, 117.4)

    # ---------- забор: ПО-2, бетонные плиты, профлист, доски на заплатке; рухнувший участок ----------
    def fence(pts, tex, gaps=()):
        s.walls(pts, 2.4, tex, gaps=gaps)
    fence([(90, 126), (46, 126), (46, 123), (22, 123)], CON)
    fence(stair((22, 123), (8, 109)), CON)
    fence([(8, 109), (8, 92)], CON)
    fence([(8, 84), (8, 72), (10, 72), (10, 40), (7, 40), (7, 18)], CON)
    s.wall(8, 84, 8, 92, 0.9, CON)                                                   # рухнувший участок: остался низкий цоколь — не пролезть, но видно насквозь
    s.prop('props/fence_panels_fallen', 4.4, 90.6, plan=(C_COVER, 'забор рухнул'))
    s.prop('outdoor/ruins_concrete_pile_angled', 5.0, 86.0, plan=(C_COVER, ''))
    s.dec('decal_bricks_scatter', 10.0, 86.0); s.dec('decal_bricks_scatter', 9.4, 91.0, 1.0, 90)
    fence(stair((7, 18), (17, 6)), PO2)
    fence([(17, 6), (70, 6), (70, 4), (124, 4), (124, 7), (160, 7)], PO2, gaps=[(116.5, 4, 121, 4)])
    fence(stair((160, 7), (178, 25)), PROF)
    fence([(178, 25), (178, 60), (176, 60), (176, 96), (179, 96), (179, 124)], PROF, gaps=[(179, 107.6, 179, 110.4)])
    s.wall(179, 107.6, 179, 110.4, 2.4, 'wallface_door_metal')
    fence(stair((179, 124), (168, 130)), PROF)
    fence([(168, 130), (148, 130)], WOOD)
    fence([(148, 130), (130, 130), (130, 126), (100, 126)], CON)
    s.prop('props/subst_kpp', 85.0, 124.6, plan=(C_COVER, 'КПП'))
    s.prop('props/gai_barrier', 95.0, 128.8, plan=(C_COVER, ''))
    s.prop('props/gate_leaf_open', 91.0, 127.8); s.prop('props/gate_leaf_open', 99.0, 127.8, True)
    s.prop('outdoor/bus_stop_concrete', 82.0, 133.0)
    s.labels += [(88.0, 131.0, 'КПП, шлагбаум')]
    for (n, x, y, f) in [('vehicles/car_vaz2106_white_angled', 106, 111.5, False), ('vehicles/van_uaz452_side', 114, 112.0, False),
                         ('vehicles/jeep_uaz469_angled', 121, 111.0, True), ('vehicles/wreck_burnt_sedan_side', 129, 119.5, False),
                         ('vehicles/truck_gaz53_angled', 110, 121.4, True)]:
        s.prop(n, x, y, f, plan=(C_CAR, ''))
    lamp(s, 88.6, 104); lamp(s, 101.6, 108, True); lamp(s, 70, 46.8); lamp(s, 116, 47.2, True); lamp(s, 30, 56); lamp(s, 136, 103, True)

    # ---------- вышка охраны ----------
    s.prop('props/radio_watchtower', 21.0, 115.6, plan=(C_COVER, 'вышка'))
    mech(s, 21.0, 115.0, 'tower', 'вышка с прожектором')

    # ---------- запад: контейнерный двор, насосная (Г), водонапорная башня; юг: автобус, бюст ----------
    for (x, y, f) in [(18, 62.4, False), (18, 65.4, True), (31, 62.4, False), (25, 72.6, True), (37.6, 72.0, False)]:
        s.prop('props/quarry_container', x, y, f, plan=(C_COVER, ''))
    s.prop('props/crate_wood', 24.6, 66.2, plan=(C_LOOT, ''))
    s.prop('props/radio_cable_rack', 36.6, 67.0, plan=(C_COVER, ''))
    s.ground('ground_concrete', s.rect(14, 80, 26, 90), 0, zone=(C_ZONE['conc'], 'насосная'))
    s.ground('ground_concrete', s.rect(14, 90, 20, 94), 0)
    s.walls([(20, 94), (14, 94), (14, 80), (26, 80), (26, 90), (20, 90), (20, 94)], 3.2, BR, gaps=[(22, 90, 23.8, 90)])
    s.roof(14, 80, 26, 90, 3.2, 'roof_ruberoid_patched')
    s.roof(14, 90, 20, 94, 2.8, 'roof_ruberoid_torn')
    s.prop('props/ats_diesel_gen', 20, 84.6, plan=(C_COVER, ''))
    s.prop(F('shelf_metal_rack'), 24.4, 80.9, plan=(C_COVER, ''))
    s.prop('props/barrel_oil', 15.4, 93.0, plan=(C_SCRAP, ''))
    s.prop('clutter/clutter_bucket', 18.6, 88.6)
    s.prop('outdoor/water_tower', 33.0, 90.0, plan=(C_COVER, ''))
    s.prop('vehicles/bus_paz672_angled', 52.0, 117.0, plan=(C_CAR, ''))
    s.prop('outdoor/concrete_rings', 44.0, 104.0, plan=(C_COVER, ''))
    s.prop('outdoor/concrete_slabs_stack_side', 58.0, 66.0, plan=(C_COVER, ''))
    s.prop('outdoor/lenin_bust', 82.0, 100.0, plan=(C_COVER, ''))
    s.prop('outdoor/sandbags_curve_angled', 150.0, 56.0, plan=(C_COVER, ''))
    s.prop('outdoor/dumpster_skip_side', 70.0, 34.0, plan=(C_COVER, ''))
    s.prop('vehicles/jeep_uaz469_angled', 84.0, 42.0, plan=(C_CAR, ''))
    s.prop('outdoor/sandbags_wall', 104.6, 40.0, plan=(C_COVER, ''))

    # ---------- хлам, бочки ----------
    for (x, y) in [(20, 9.0), (68, 8.6), (172, 60), (12, 70), (36, 100), (130, 46), (66, 120), (172, 124)]:
        s.prop('props/junk_pile', x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(66.6, 13.6), (67.4, 14.0), (11.8, 50), (12.6, 50.4), (118, 104), (118.8, 104.4), (44, 104), (172.2, 30), (60, 124), (60.8, 124.3)]:
        s.prop(random.choice(SCRAP), x, y, plan=(C_SCRAP, ''))
    for (x, y) in [(20, 96), (128, 42), (82, 38), (150, 62), (30, 76), (100, 36), (60, 110)]:
        s.prop(random.choice(CL), x, y, random.random() < .5)
    s.prop('props/subst_cable_drum', 22, 52, plan=(C_COVER, ''))
    s.prop('props/subst_cable_drum', 24.4, 53, plan=(C_COVER, ''))
    s.prop('props/radio_antenna_scrap', 34, 112, plan=(C_SCRAP, ''))

    # ---------- охрана: 9 ----------
    guard(s, 88.0, 119.6, 'pps', 'КПП (ППС-43)')
    guard(s, 102.0, 120.6, 'pps', 'КПП (ППС-43)', back=True)
    guard(s, 21.0, 113.4, 'svt', 'вышка: снайпер (СВТ-40)')
    guard(s, 70.0, 96.0, 'pps', 'пост у площадки (ППС-43)', back=True)
    guard(s, 124.0, 52.0, 'grach', 'патруль (Грач)')
    guard(s, 40.0, 94.0, 'pps', 'патруль (ППС-43)', back=True)
    guard(s, 38.0, 35.0, 'grach', 'коридор (Грач)')
    guard(s, 28.0, 27.8, 'pps', 'аппаратный зал (ППС-43)')
    guard(s, 100.0, 25.0, 'grach', 'гараж (Грач)', back=True)

    s.weeds((7, 4, 179, 130), 1300, [(11, 7, 65, 50), (73, 8, 116, 33), (145, 101, 169, 122), (45, 75, 63, 91), (12, 57, 43, 75), (13, 79, 27, 95),
                                     (76, 56, 108, 86), (101, 106, 135, 124), (88, 50, 112, 128)], bush=0.07)
    # трава по краям у забора гуще
    s.weeds((7, 4, 179, 12), 160, bush=0.15); s.weeds((7, 118, 179, 130), 160, bush=0.12); s.weeds((7, 4, 14, 130), 120, bush=0.15); s.weeds((170, 4, 179, 130), 120, bush=0.15)
    s.forest([(5, 2, 181, 132), (88, 124, 102, 146)], 0.5)
    s.notes = ['#car|машины (укрытия)', '#scrap|бочки и хлам с ломом', '#loot|ящики тира 2', '#cover|постройки, мебель, укрытия',
               '#mech|механика', '#guard|охрана (9)', '', '!Как вызвать груз:', '1. Журнал дежурного (держать E 3 с)',
               '   — личный 4-значный код.', '2. Дизель (держать E 5 с, шум 60 м)', '   — ток корпусу на 10 минут.',
               '3. Пульт: ввести код + перфокарта II.', '4. Красная ракета с пусковой — видна', '   всей карте, по радио: «груз через',
               '   90 секунд».', '5. Ан-2 сбрасывает контейнер на', '   площадку: заперт 30 с, вскрыть 10 с.', '6. Перезарядка РТ — 20 минут.', '',
               '!Охрана: 9 (ППС-43, Грач, СВТ-40',
               '   на вышке), армейская броня.', '!Радиация 30 по всей территории.', '',
               '!Лут: 4 ящика тира 2, сейф', '   начальника, стойки (радиолампы,', '   радиодетали, золотые контакты),',
               '   контейнер сброса.', '',
               'Входы: КПП (юг), пролом под', 'упавшей мачтой (север), калитка у', 'дизельной (восток, выбить 4 с).',
               'Забор по рельефу: ПО-2, плиты,', 'профлист, доски; на западе рухнул', '(завален плитами).',
               'Территория ~170×125 м. Клетка = 1 м.']
    # типы комнат для обстановки (Dressing.cs); остальные — по мебели внутри
    s.room(37, 47, 'corridor')
    s.room(163, 118, 'storage')
    s.room(17, 92, 'storage')
    s.room(90, 18, 'corridor')
    s.room(78, 29, 'washroom')
    s.room(81, 18, 'barracks')
    s.room(102, 18, 'workshop')
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
