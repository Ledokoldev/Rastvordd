"""Выживший-бот: синий спортивный костюм с белыми лампасами, кепка, кеды."""
from _char import figure, stripes


def head(k):
    cap = k.blob(0.12, (-0.01, 0, 1.72), "black", subdiv=2, scale=(1, 0.9, 0.7), jitter=0.0, smooth=True)
    for v in cap.data.vertices:
        v.co.z = max(v.co.z, -0.005)
    return [cap, k.box((0.1, 0.17, 0.012), (0.12, 0, 1.72), "black", bevel=0.004)]       # козырёк


def build(k):
    figure(k, top="track", legs="track", boots="white", head_extra=head, body_extra=lambda k: stripes(k))
