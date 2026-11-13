"""Ректификационная колонна: колонна с фланцами царг, куб-испаритель, дефлегматор сверху, медные трубки,
банка отбора, лесенка, щиток. Квадратный след 1,85, высота ~2,4 м."""


def build(k):
    k.box((1.8, 1.8, 0.1), (0, 0, 0), "steel", bevel=0.01)                                     # площадка
    k.cyl(0.42, 0.8, (-0.35, -0.3, 0.1), "rust_dark", segments=20, bevel=0.015)                 # куб
    k.cyl(0.43, 0.08, (-0.35, -0.3, 0.9), "steel", segments=20, bevel=0.01, radius_top=0.2)
    k.cyl(0.16, 1.7, (0.35, 0.3, 0.1), "steel", segments=16, bevel=0.008)                       # колонна
    for i in range(7):
        k.cyl(0.18, 0.04, (0.35, 0.3, 0.25 + i * 0.22), "rust", segments=16, bevel=0.004)       # фланцы
    k.cyl(0.22, 0.4, (0.35, 0.3, 1.8), "tin", segments=16, bevel=0.01)                          # дефлегматор
    k.cyl(0.05, 0.2, (0.35, 0.3, 2.2), "tin", segments=10, bevel=0.004, radius_top=0.03)
    k.cyl(0.05, 0.5, (-0.35, -0.3, 0.98), "copper", segments=10, bevel=0)                        # пар в колонну
    k.cyl(0.05, 0.6, (-0.35, -0.3, 1.45), "copper", segments=10, axis="X", bevel=0)
    k.cyl(0.05, 0.6, (0.25, -0.3, 1.45), "copper", segments=10, axis="Y", bevel=0)
    k.cyl(0.02, 0.4, (0.57, 0.3, 2.0), "copper", segments=8, axis="X", bevel=0)                  # отбор
    k.cyl(0.02, 1.58, (0.97, 0.3, 0.42), "copper", segments=8, bevel=0)
    k.cyl(0.09, 0.3, (0.97, 0.3, 0.1), "glass", segments=14, bevel=0.006)
    for y in (0.62, 0.86):                                                                      # лесенка
        k.box((0.04, 0.04, 2.1), (-0.25, y, 0.1), "paint_green", bevel=0.005)
    for i in range(8):
        k.box((0.03, 0.26, 0.03), (-0.25, 0.74, 0.35 + i * 0.25), "paint_green", bevel=0.004)
    k.box((0.3, 0.3, 0.5), (-0.6, 0.5, 0.1), "paint_blue", bevel=0.015)                        # щиток
    k.box((0.05, 0.006, 0.05), (-0.6, 0.347, 0.45), "red", bevel=0)
    k.stencil("НЕ КУРИТЬ", (-0.35, -0.72, 0.55), size=0.05, wrap=0.42)
