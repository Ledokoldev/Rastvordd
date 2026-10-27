using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Солнечные тени на GPU: силуэт спрайта (и след стены) рисуется один раз в свой дочерний canvas item (за родителем),
    /// а наклон/длина/сила по солнцу и покачивание кроны — в вершинном шейдере из глобальных параметров sh_*.
    /// Смена солнца — смена трёх чисел, без перерисовки тысяч узлов на CPU.
    /// </summary>
    public static class GpuShade
    {
        const string Globals = @"
global uniform float sh_skew;   // Shade.Skew: >0 — тень вправо (вечер)
global uniform float sh_flat;   // Shade.Flat: длина тени (доля высоты)
global uniform float sh_str;    // Shade.Strength: сила (ночью слабая)
";

        // COLOR (модуляция прямоугольника): r — длина слоя, g — веер полутени + 0.5, a — непрозрачность без учёта солнца
        const string SpriteCode = "shader_type canvas_item;\n" + Globals + @"
uniform float sway_amp = 0.0;
uniform float sway_height = 120.0;
varying float v_a;
varying float v_soft;
void vertex() {
    vec2 v = VERTEX;
    if (sway_amp > 0.0) {
        // крона качается — и её тень вместе с ней (та же формула, что у SwayShader)
        vec2 base_px = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xy;
        float h = clamp(-v.y / sway_height, 0.0, 1.0);
        float ph = (base_px.x + v.x) * 0.031 + base_px.y * 0.017;
        v.x += round((sin(TIME * 1.3 + ph) * 0.7 + sin(TIME * 2.9 + ph * 1.7) * 0.3) * sway_amp * h * h);
    }
    float len = COLOR.r, spread = COLOR.g - 0.5;
    VERTEX = vec2(v.x + (-sh_skew * len + spread) * v.y, sh_flat * len * v.y);
    v_a = min(0.3, COLOR.a * sh_str);
    v_soft = 0.6 + 1.6 * abs(sh_skew);   // низкое солнце — тень длиннее и мягче к концу
}
void fragment() {
    // полутень: к дальнему концу (верх текстуры) силуэт размывается по горизонтали; шаги целыми текселями — пиксель-арт
    float far = 1.0 - UV.y;
    float dx = TEXTURE_PIXEL_SIZE.x * floor(far * v_soft * 2.0 + 0.5);
    float a = texture(TEXTURE, UV).a * 0.5 + (texture(TEXTURE, UV - vec2(dx, 0.0)).a + texture(TEXTURE, UV + vec2(dx, 0.0)).a) * 0.25;
    COLOR = vec4(0.04, 0.06, 0.14, a * v_a);
}
";

        // стена: UV.x — вес сдвига (0 — основание, 1 — сдвинутая на тень вершина), UV.y — высота стены, px;
        // COLOR.g — вариант оболочки (1 — тень вправо, 0 — влево), неподходящий схлопывается
        const string WallCode = "shader_type canvas_item;\n" + Globals + @"
void vertex() {
    float side = COLOR.g > 0.5 ? 1.0 : -1.0;
    if ((sh_skew >= 0.0) != (side > 0.0)) { VERTEX = vec2(0.0); }
    else {
        float hh = UV.y;
        vec2 o = vec2(sh_skew, -sh_flat) * hh, oref = vec2(side * 0.5, -0.5) * hh;
        VERTEX += UV.x * (o - oref);
    }
    COLOR = vec4(0.03, 0.04, 0.03, COLOR.a);
}
";

        static bool _reg;
        static ShaderMaterial _plain, _tree, _grass, _wall;

        static void Register()
        {
            if (_reg) return;
            _reg = true;
            RenderingServer.GlobalShaderParameterAdd("sh_skew", RenderingServer.GlobalShaderParameterType.Float, Shade.Skew);
            RenderingServer.GlobalShaderParameterAdd("sh_flat", RenderingServer.GlobalShaderParameterType.Float, Shade.Flat);
            RenderingServer.GlobalShaderParameterAdd("sh_str", RenderingServer.GlobalShaderParameterType.Float, Shade.Strength);
        }

        public static void SetSun(float skew, float flat, float str)
        {
            Register();
            RenderingServer.GlobalShaderParameterSet("sh_skew", skew);
            RenderingServer.GlobalShaderParameterSet("sh_flat", flat);
            RenderingServer.GlobalShaderParameterSet("sh_str", str);
        }

        static ShaderMaterial Mat(string code, float amp = 0, float height = 120)
        {
            Register();
            var m = new ShaderMaterial { Shader = new Shader { Code = code } };
            m.SetShaderParameter("sway_amp", amp);
            m.SetShaderParameter("sway_height", height);
            return m;
        }

        static ShaderMaterial SpriteMat(CanvasItem ci)
        {
            var m = ci.Material;
            if (m != null && m == VisionFx.TreeMat) return _tree ??= Mat(SpriteCode, 2.2f, 170f);
            if (m != null && m == VisionFx.GrassMat) return _grass ??= Mat(SpriteCode, 1.3f, 40f);
            return _plain ??= Mat(SpriteCode);
        }

        sealed class Rec { public ulong Frame; public int Idx; public readonly List<Rid> Items = new(); }
        static readonly Dictionary<CanvasItem, Rec> Recs = new();

        /// <summary>Очередной дочерний item узла для тени в текущем _Draw (между перерисовками переиспользуются).</summary>
        static Rid Next(CanvasItem ci, ShaderMaterial mat)
        {
            var rs = RenderingServer.Singleton;
            ulong now = Engine.GetProcessFrames();
            if (!Recs.TryGetValue(ci, out var r))
            {
                r = new Rec { Frame = now };
                Recs[ci] = r;
                ci.TreeExited += () => { if (Recs.Remove(ci, out var rr)) foreach (var it in rr.Items) RenderingServer.FreeRid(it); };
            }
            else if (r.Frame != now)
            {
                r.Frame = now; r.Idx = 0;
                foreach (var it in r.Items) RenderingServer.CanvasItemClear(it);
            }
            if (r.Idx >= r.Items.Count)
            {
                var it = RenderingServer.CanvasItemCreate();
                RenderingServer.CanvasItemSetParent(it, ci.GetCanvasItem());
                RenderingServer.CanvasItemSetDrawBehindParent(it, true);
                RenderingServer.CanvasItemSetDefaultTextureFilter(it, RenderingServer.CanvasItemTextureFilter.Nearest);
                r.Items.Add(it);
            }
            var item = r.Items[r.Idx++];
            RenderingServer.CanvasItemSetMaterial(item, mat.GetRid());
            return item;
        }

        /// <summary>Начало _Draw узла: следующая тень пойдёт в первый item (вызывать не обязательно — новый кадр сбрасывает сам).</summary>
        public static void Begin(CanvasItem ci) { if (Recs.TryGetValue(ci, out var r)) { r.Idx = 0; foreach (var it in r.Items) RenderingServer.CanvasItemClear(it); r.Frame = Engine.GetProcessFrames(); r.Idx = 0; } }

        public static void Sprite(CanvasItem ci, Texture2D tex, float scale, float alpha, float sink, Vector2 at)
        {
            var item = Next(ci, SpriteMat(ci));
            RenderingServer.CanvasItemSetTransform(item, new Transform2D(new Vector2(scale, 0), new Vector2(0, scale), at));
            var diffuse = tex is CanvasTexture ct ? ct.DiffuseTexture : tex;
            if (diffuse == null) return;
            var rect = new Rect2(-tex.GetWidth() / 2f, -tex.GetHeight() + sink, tex.GetWidth(), tex.GetHeight());
            for (int i = 0; i < 3; i++)
            {
                float len = 1f - 0.08f * i, spread = i == 1 ? 0.025f : i == 2 ? -0.025f : 0f;
                RenderingServer.CanvasItemAddTextureRect(item, rect, diffuse.GetRid(), false, new Color(len, spread + 0.5f, 0, alpha * 0.62f));
            }
        }

        /// <summary>Тень стены: прямоугольник основания [x0,x1]×[−d,0] протянут на высоту hh по солнцу (выпуклая оболочка в шейдере).</summary>
        public static void Wall(CanvasItem ci, float x0, float x1, float d, float hh, float alpha)
        {
            if (x1 - x0 < 0.5f || hh < 0.5f) return;
            var item = Next(ci, _wall ??= Mat(WallCode));
            float r = 0.5f * hh;
            // тень вправо (+): (x0,0) (x1,0) (x1+o) (x1,−d)+o (x0,−d)+o (x0,−d); влево (−): (x0,0) (x1,0) (x1,−d) (x1,−d)+o (x0,−d)+o (x0,0)+o
            var oR = new Vector2(r, -r); var oL = new Vector2(-r, -r);
            Add(item, new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, 0) + oR, new Vector2(x1, -d) + oR, new Vector2(x0, -d) + oR, new Vector2(x0, -d) },
                new[] { 0f, 0, 1, 1, 1, 0 }, 1f, hh, alpha);
            Add(item, new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, -d), new Vector2(x1, -d) + oL, new Vector2(x0, -d) + oL, new Vector2(x0, 0) + oL },
                new[] { 0f, 0, 0, 1, 1, 1 }, 0f, hh, alpha);
        }

        static void Add(Rid item, Vector2[] pts, float[] w, float side, float hh, float alpha)
        {
            var cols = new Color[pts.Length]; var uvs = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) { cols[i] = new Color(0, side, 0, alpha); uvs[i] = new Vector2(w[i], hh); }
            RenderingServer.CanvasItemAddPolygon(item, pts, cols, uvs);
        }
    }
}
