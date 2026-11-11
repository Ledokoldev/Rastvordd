"""
Мини-библиотека для генерации low-poly моделей в .glb (glTF 2.0) без внешних зависимостей, кроме numpy.

Договорённость (docs/design/27-3d-client.md): 1 единица = 1 м, Y — вверх, «перед» модели — +X,
опорная точка — на земле в центре. Освещение гранёное (у каждого треугольника свои вершины и нормаль) —
так выглядит low-poly и так модели читаются сверху.

Модель — дерево узлов Part: у каждого имя, смещение (сустав), поворот и набор треугольников с материалами.
Имена частей персонажа (hips, torso, head, arm_l, arm_r, leg_l, leg_r) клиент использует для анимации.
"""
import json
import math
import struct

import numpy as np


# ---------------------------------------------------------------- материалы

class Mat:
    def __init__(self, name, rgb, rough=0.85, metal=0.0, emissive=None, alpha=1.0):
        self.name = name
        self.rgb = tuple(float(c) for c in rgb)
        self.rough = rough
        self.metal = metal
        self.emissive = emissive
        self.alpha = alpha

    def key(self):
        return (self.rgb, self.rough, self.metal, self.emissive, self.alpha)


def hexc(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def M(name, color, rough=0.85, metal=0.0, emissive=None, alpha=1.0):
    rgb = hexc(color) if isinstance(color, str) else color
    em = hexc(emissive) if isinstance(emissive, str) else emissive
    return Mat(name, rgb, rough, metal, em, alpha)


# ---------------------------------------------------------------- геометрия

def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]], dtype=np.float64)


def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]], dtype=np.float64)


def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]], dtype=np.float64)


class Geo:
    """Набор треугольников: tris — список (материал, массив N×3×3)."""

    def __init__(self):
        self.tris = []

    def add(self, mat, tri_array):
        tri_array = np.asarray(tri_array, dtype=np.float64).reshape(-1, 3, 3)
        if len(tri_array):
            self.tris.append((mat, tri_array))
        return self

    def merge(self, other, R=None, t=(0, 0, 0)):
        for mat, tr in other.tris:
            v = tr.reshape(-1, 3)
            if R is not None:
                v = v @ np.asarray(R).T
            v = v + np.asarray(t, dtype=np.float64)
            self.tris.append((mat, v.reshape(-1, 3, 3)))
        return self


def _quads_to_tris(quads):
    out = []
    for q in quads:
        a, b, c, d = q
        out.append((a, b, c))
        out.append((a, c, d))
    return out


def box(mat, center, size, R=None):
    """Коробка: центр, размер (x, y, z), необязательный поворот."""
    cx, cy, cz = center
    sx, sy, sz = (s * 0.5 for s in size)
    p = np.array([[-sx, -sy, -sz], [sx, -sy, -sz], [sx, sy, -sz], [-sx, sy, -sz],
                  [-sx, -sy, sz], [sx, -sy, sz], [sx, sy, sz], [-sx, sy, sz]], dtype=np.float64)
    if R is not None:
        p = p @ np.asarray(R).T
    p += (cx, cy, cz)
    faces = [(4, 5, 6, 7), (1, 0, 3, 2), (5, 1, 2, 6), (0, 4, 7, 3), (7, 6, 2, 3), (0, 1, 5, 4)]
    quads = [[p[i] for i in f] for f in faces]
    return Geo().add(mat, _quads_to_tris(quads))


def prism(mat, base_y, top_y, poly, top_poly=None):
    """Призма/усечённая призма по многоугольнику в плоскости XZ (по часовой или против — нормали считаются по факту)."""
    poly = [np.array(p, dtype=np.float64) for p in poly]
    top_poly = [np.array(p, dtype=np.float64) for p in (top_poly or poly)]
    n = len(poly)
    tris = []
    for i in range(n):
        a0 = (poly[i][0], base_y, poly[i][1]); b0 = (poly[(i + 1) % n][0], base_y, poly[(i + 1) % n][1])
        a1 = (top_poly[i][0], top_y, top_poly[i][1]); b1 = (top_poly[(i + 1) % n][0], top_y, top_poly[(i + 1) % n][1])
        tris += _quads_to_tris([(a0, b0, b1, a1)])
    for i in range(1, n - 1):
        tris.append(((top_poly[0][0], top_y, top_poly[0][1]), (top_poly[i][0], top_y, top_poly[i][1]), (top_poly[i + 1][0], top_y, top_poly[i + 1][1])))
        tris.append(((poly[0][0], base_y, poly[0][1]), (poly[i + 1][0], base_y, poly[i + 1][1]), (poly[i][0], base_y, poly[i][1])))
    return Geo().add(mat, tris)


def cyl(mat, base, height, r_bottom, r_top=None, seg=8, R=None, caps=True, phase=0.0):
    """Цилиндр/конус вдоль оси Y от base вверх на height (R — поворот вокруг base)."""
    r_top = r_bottom if r_top is None else r_top
    ring0, ring1 = [], []
    for i in range(seg):
        a = phase + 2 * math.pi * i / seg
        ring0.append(np.array([math.cos(a) * r_bottom, 0, math.sin(a) * r_bottom]))
        ring1.append(np.array([math.cos(a) * r_top, height, math.sin(a) * r_top]))
    tris = []
    for i in range(seg):
        j = (i + 1) % seg
        if r_top > 1e-6:
            tris += _quads_to_tris([(ring0[i], ring0[j], ring1[j], ring1[i])])
        else:
            tris.append((ring0[i], ring0[j], np.array([0, height, 0.0])))
    if caps:
        c0 = np.array([0, 0, 0.0]); c1 = np.array([0, height, 0.0])
        for i in range(seg):
            j = (i + 1) % seg
            tris.append((c0, ring0[j], ring0[i]))
            if r_top > 1e-6:
                tris.append((c1, ring1[i], ring1[j]))
    v = np.array(tris, dtype=np.float64).reshape(-1, 3)
    if R is not None:
        v = v @ np.asarray(R).T
    v += np.asarray(base, dtype=np.float64)
    return Geo().add(mat, v.reshape(-1, 3, 3))


def cyl_between(mat, a, b, r, seg=6, r2=None):
    """Цилиндр от точки a до точки b (ветки, ручки, трубы)."""
    a = np.asarray(a, dtype=np.float64); b = np.asarray(b, dtype=np.float64)
    d = b - a
    L = float(np.linalg.norm(d))
    if L < 1e-6:
        return Geo()
    y = d / L
    tmp = np.array([1.0, 0, 0]) if abs(y[0]) < 0.9 else np.array([0, 0, 1.0])
    x = np.cross(y, tmp); x /= np.linalg.norm(x)
    z = np.cross(x, y)
    R = np.stack([x, y, z], axis=1)
    return cyl(mat, a, L, r, r if r2 is None else r2, seg=seg, R=R)


_ICO = None


def _icosphere(sub):
    t = (1 + 5 ** 0.5) / 2
    v = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
         (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    v = [np.array(p, dtype=np.float64) / np.linalg.norm(p) for p in v]
    for _ in range(sub):
        cache = {}
        nf = []

        def mid(i, j):
            k = (min(i, j), max(i, j))
            if k not in cache:
                m = (v[i] + v[j]) / 2
                v.append(m / np.linalg.norm(m))
                cache[k] = len(v) - 1
            return cache[k]

        for a, b, c in f:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            nf += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        f = nf
    return np.array(v), f


def blob(mat, center, radius, sub=1, jitter=0.0, rng=None, squash=(1, 1, 1), flat_bottom=None):
    """Гранёный шар/камень/крона: icosphere с шумом. flat_bottom — срезать всё ниже этой высоты (камни на земле)."""
    v, f = _icosphere(sub)
    v = v.copy()
    if jitter > 0:
        rng = rng or np.random.default_rng(1)
        v *= (1 + rng.uniform(-jitter, jitter, size=(len(v), 1)))
    v *= np.asarray(radius if np.ndim(radius) else (radius, radius, radius)) * np.asarray(squash)
    v += np.asarray(center, dtype=np.float64)
    if flat_bottom is not None:
        v[:, 1] = np.maximum(v[:, 1], flat_bottom)
    tris = [(v[a], v[b], v[c]) for a, b, c in f]
    return Geo().add(mat, tris)


def quad(mat, a, b, c, d, double=True):
    g = Geo().add(mat, _quads_to_tris([(a, b, c, d)]))
    if double:
        g.add(mat, _quads_to_tris([(d, c, b, a)]))
    return g


# ---------------------------------------------------------------- модель

class Part:
    def __init__(self, name, t=(0, 0, 0), R=None, geo=None):
        self.name = name
        self.t = tuple(float(x) for x in t)
        self.R = R
        self.geo = geo or Geo()
        self.children = []

    def child(self, name, t=(0, 0, 0), R=None):
        c = Part(name, t, R)
        self.children.append(c)
        return c

    def add(self, g):
        self.geo.merge(g)
        return self


def _rot_to_quat(R):
    R = np.asarray(R, dtype=np.float64)
    tr = R[0, 0] + R[1, 1] + R[2, 2]
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2
        w = 0.25 * s; x = (R[2, 1] - R[1, 2]) / s; y = (R[0, 2] - R[2, 0]) / s; z = (R[1, 0] - R[0, 1]) / s
    elif R[0, 0] > R[1, 1] and R[0, 0] > R[2, 2]:
        s = math.sqrt(1.0 + R[0, 0] - R[1, 1] - R[2, 2]) * 2
        w = (R[2, 1] - R[1, 2]) / s; x = 0.25 * s; y = (R[0, 1] + R[1, 0]) / s; z = (R[0, 2] + R[2, 0]) / s
    elif R[1, 1] > R[2, 2]:
        s = math.sqrt(1.0 + R[1, 1] - R[0, 0] - R[2, 2]) * 2
        w = (R[0, 2] - R[2, 0]) / s; x = (R[0, 1] + R[1, 0]) / s; y = 0.25 * s; z = (R[1, 2] + R[2, 1]) / s
    else:
        s = math.sqrt(1.0 + R[2, 2] - R[0, 0] - R[1, 1]) * 2
        w = (R[1, 0] - R[0, 1]) / s; x = (R[0, 2] + R[2, 0]) / s; y = (R[1, 2] + R[2, 1]) / s; z = 0.25 * s
    q = np.array([x, y, z, w]); q /= np.linalg.norm(q)
    return [float(c) for c in q]


def write_glb(path, root):
    """Пишет модель (дерево Part) в .glb: по мешу на часть, по примитиву на материал, гранёные нормали."""
    gltf = {"asset": {"version": "2.0", "generator": "rastvordd gen-3d-assets"},
            "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [], "meshes": [], "materials": [],
            "accessors": [], "bufferViews": [], "buffers": []}
    blob_ = bytearray()
    mat_index = {}

    def material(m):
        k = m.key()
        if k in mat_index:
            return mat_index[k]
        pbr = {"baseColorFactor": [m.rgb[0], m.rgb[1], m.rgb[2], m.alpha], "roughnessFactor": m.rough, "metallicFactor": m.metal}
        md = {"name": m.name, "pbrMetallicRoughness": pbr, "doubleSided": True}
        if m.emissive:
            md["emissiveFactor"] = list(m.emissive)
        if m.alpha < 1:
            md["alphaMode"] = "BLEND"
        gltf["materials"].append(md)
        mat_index[k] = len(gltf["materials"]) - 1
        return mat_index[k]

    def view(data, target):
        nonlocal blob_
        while len(blob_) % 4:
            blob_.append(0)
        off = len(blob_)
        blob_ += data
        gltf["bufferViews"].append({"buffer": 0, "byteOffset": off, "byteLength": len(data), "target": target})
        return len(gltf["bufferViews"]) - 1

    def mesh_of(geo, name):
        by_mat = {}
        for m, tr in geo.tris:
            by_mat.setdefault(m.key(), [m, []])[1].append(tr)
        prims = []
        for k, (m, arrs) in by_mat.items():
            tr = np.concatenate(arrs, axis=0)
            e1 = tr[:, 1] - tr[:, 0]
            e2 = tr[:, 2] - tr[:, 0]
            n = np.cross(e1, e2)
            ln = np.linalg.norm(n, axis=1, keepdims=True)
            keep = ln[:, 0] > 1e-12
            tr, n, ln = tr[keep], n[keep], ln[keep]
            if len(tr) == 0:
                continue
            n = n / ln
            # нормаль — по порядку вершин; материалы двусторонние, так что грань, смотрящая «внутрь», не даёт дыры
            pos = tr.reshape(-1, 3).astype(np.float32)
            nor = np.repeat(n, 3, axis=0).astype(np.float32)
            pv = view(pos.tobytes(), 34962)
            nv = view(nor.tobytes(), 34962)
            gltf["accessors"].append({"bufferView": pv, "componentType": 5126, "count": len(pos), "type": "VEC3",
                                      "min": [float(x) for x in pos.min(axis=0)], "max": [float(x) for x in pos.max(axis=0)]})
            pa = len(gltf["accessors"]) - 1
            gltf["accessors"].append({"bufferView": nv, "componentType": 5126, "count": len(nor), "type": "VEC3"})
            na = len(gltf["accessors"]) - 1
            prims.append({"attributes": {"POSITION": pa, "NORMAL": na}, "material": material(m)})
        if not prims:
            return None
        gltf["meshes"].append({"name": name, "primitives": prims})
        return len(gltf["meshes"]) - 1

    def node_of(part):
        idx = len(gltf["nodes"])
        nd = {"name": part.name}
        gltf["nodes"].append(nd)
        if any(abs(x) > 1e-9 for x in part.t):
            nd["translation"] = list(part.t)
        if part.R is not None:
            nd["rotation"] = _rot_to_quat(part.R)
        mi = mesh_of(part.geo, part.name)
        if mi is not None:
            nd["mesh"] = mi
        kids = [node_of(c) for c in part.children]
        if kids:
            nd["children"] = kids
        return idx

    node_of(root)
    while len(blob_) % 4:
        blob_.append(0)
    gltf["buffers"].append({"byteLength": len(blob_)})
    js = json.dumps(gltf, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    while len(js) % 4:
        js += b" "
    total = 12 + 8 + len(js) + 8 + len(blob_)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(js), 0x4E4F534A))
        f.write(js)
        f.write(struct.pack("<II", len(blob_), 0x004E4942))
        f.write(bytes(blob_))
    return total


def read_glb(path):
    """Проверка: читает .glb обратно и возвращает (json, bin)."""
    with open(path, "rb") as f:
        data = f.read()
    magic, ver, total = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67 and ver == 2 and total == len(data), "битый заголовок"
    jl, jt = struct.unpack_from("<II", data, 12)
    assert jt == 0x4E4F534A
    js = json.loads(data[20:20 + jl])
    bl, bt = struct.unpack_from("<II", data, 20 + jl)
    assert bt == 0x004E4942
    bin_ = data[28 + jl:28 + jl + bl]
    assert len(bin_) == js["buffers"][0]["byteLength"], "длина буфера"
    for bv in js["bufferViews"]:
        assert bv["byteOffset"] + bv["byteLength"] <= len(bin_), "bufferView за пределами"
        assert bv["byteOffset"] % 4 == 0
    for a in js["accessors"]:
        bv = js["bufferViews"][a["bufferView"]]
        assert a["count"] * 12 == bv["byteLength"], "accessor/bufferView"
    return js, bin_
