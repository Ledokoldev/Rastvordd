from _tree import deciduous


def build(k):
    deciduous(k, seed=41, h=6.0, crown=(2.0, 2.0, 1.8), leaves=('leaf', 'leaf_dark'), trunk_r=0.2, fruit='rowan_berry', n_fruit=22)
