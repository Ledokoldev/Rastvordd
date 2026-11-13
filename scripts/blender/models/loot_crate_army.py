"""Армейский ящик (лут): хаки, низкий и длинный, рейки на торцах, две защёлки, скобы, две жёлтые полосы маркировки."""
from _crate import H, L, W, handles, latches


def build(k):
    h = H - 0.06
    k.box((L, W, h - 0.08), (0, 0, 0), "planks_green", bevel=0.008)
    k.box((L + 0.012, W + 0.012, 0.08), (0, 0, h - 0.08), "planks_green", bevel=0.01)
    for x in (-L / 2 + 0.035, L / 2 - 0.035):
        k.box((0.06, W + 0.025, h - 0.02), (x, 0, 0.01), "planks_green", bevel=0.008)
    for x in (-L / 2 + 0.02, L / 2 - 0.02):
        for y in (-W / 2 - 0.004, W / 2 + 0.004):
            for z in (0.0, h - 0.07):
                k.box((0.07, 0.07, 0.07), (x, y, z), "rust", bevel=0.005)
    handles(k, 0.26)
    latches(k, h - 0.13)
    for x in (-0.22, -0.12):
        k.box((0.05, W + 0.014, 0.004), (x, 0, h), "yellow", bevel=0.0)
    k.stencil("НЕ КАНТОВАТЬ", (0.0, -W / 2, 0.2), size=0.04)
    k.stencil("52-К", (0.2, 0, h), size=0.09, facing="+Z")
