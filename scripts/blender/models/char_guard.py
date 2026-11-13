"""Охранник (NPC): полевая форма, стальная каска, бронежилет с подсумками, сапоги."""
from _char import figure


def head(k):
    helm = k.blob(0.135, (-0.005, 0, 1.72), "paint_green", subdiv=3, scale=(1.05, 0.95, 0.75), jitter=0.0, smooth=True)
    for v in helm.data.vertices:
        v.co.z = max(v.co.z, 0.0)
    return [helm, k.cyl(0.14, 0.012, (-0.005, 0, 1.72), "paint_green", segments=16, bevel=0)]


def extra(k):
    vest = [k.box((0.29, 0.46, 0.34), (0, 0, 1.06), "cloth", bevel=0.05)]
    for y in (-0.1, 0.0, 0.1):                                                        # подсумки
        vest.append(k.box((0.05, 0.08, 0.1), (0.16, y, 1.08), "cloth", bevel=0.015))
    return {"torso": vest}


def build(k):
    figure(k, top="uniform", legs="uniform", boots="rubber", head_extra=head, body_extra=extra)
