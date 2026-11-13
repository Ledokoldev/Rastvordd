"""Игрок по умолчанию: ватник хаки со строчкой, тёмные штаны, кирзовые сапоги, шапка-петушок."""
from _char import figure


def head(k):
    cap = k.blob(0.122, (-0.01, 0, 1.73), "track", subdiv=2, scale=(1, 0.9, 0.85), jitter=0.0, smooth=True)
    for v in cap.data.vertices:
        v.co.z = max(v.co.z, -0.01)
    return [cap, k.box((0.21, 0.19, 0.03), (-0.01, 0, 1.715), "white", bevel=0.012)]     # отворот


def extra(k):
    out = {"torso": []}
    for z in (1.0, 1.1, 1.2, 1.3):                                                       # строчка ватника
        out["torso"].append(k.box((0.006, 0.4, 0.008), (0.122, 0, z), "pants", bevel=0))
    out["torso"].append(k.box((0.006, 0.01, 0.42), (0.123, 0, 0.98), "pants", bevel=0))  # планка
    out["torso"].append(k.box((0.1, 0.36, 0.05), (-0.13, 0, 1.38), "jacket", bevel=0.02))  # воротник
    return out


def build(k):
    figure(k, top="jacket", legs="pants", boots="rubber", hands="skin", head_extra=head, body_extra=extra)
