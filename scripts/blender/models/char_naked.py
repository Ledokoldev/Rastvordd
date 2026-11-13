"""Персонаж без одежды (база, на которую клиент надевает оболочки вещей): кожа, серое бельё, босиком."""
from _char import figure


def build(k):
    figure(k, top="skin", legs="skin", boots="skin", body_extra=lambda k: {
        "hips": [k.box((0.23, 0.35, 0.16), (0, 0, 0.79), "under", bevel=0.06)]})
