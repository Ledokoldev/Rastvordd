"""Проверка графа РТ (скилл rt-layout-check): достижимость цели с ключами, выходы, входы горячей зоны, темп.

Запуск: python scripts/rt-check.py docs/design/rt-graphs/<рт>.json
Приём поиска «комната + набор ключей» — по скиллу level-design (gamedev-skills/awesome-gamedev-agent-skills, Apache-2.0).
"""
import json
import sys


def reachable(rooms, start, keys0=frozenset()):
    """Все состояния (комната, ключи), достижимые из start."""
    seen = set()
    stack = [(start, frozenset(keys0))]
    while stack:
        room, keys = stack.pop()
        info = rooms.get(room, {})
        if info.get("grants"):
            g = info["grants"]
            keys = keys | (frozenset(g) if isinstance(g, list) else frozenset([g]))
        state = (room, keys)
        if state in seen:
            continue
        seen.add(state)
        for ex in info.get("exits", []):
            need = ex.get("needs")
            needs = need if isinstance(need, list) else ([need] if need else [])
            if all(n in keys for n in needs):
                stack.append((ex["to"], keys))
    return seen


def check(g):
    problems = []
    rooms, goal = g["rooms"], g["goal"]
    exits = set(g.get("exits", []))
    for s in g["start"]:
        states = reachable(rooms, s)
        if not any(r == goal for r, _ in states):
            problems.append(f"цель «{goal}» недостижима из старта «{s}» (ключ за своей дверью или нет пути)")
        goal_states = [k for r, k in states if r == goal]
        for keys in goal_states[:1]:
            after = reachable(rooms, goal, keys)
            if not any(r in exits for r, _ in after):
                problems.append(f"из цели «{goal}» (старт «{s}») не дойти ни до одного выхода")
        # ловушки: из любой достижимой комнаты должен быть путь к выходу
        for r, keys in states:
            if r in exits:
                continue
            if not any(rr in exits for rr, _ in reachable(rooms, r, keys)):
                problems.append(f"ловушка: из «{r}» нет пути к выходу")
    hot = g.get("hot")
    if hot:
        ins = {name for name, info in rooms.items() for ex in info.get("exits", []) if ex["to"] == hot}
        outs = {ex["to"] for ex in rooms.get(hot, {}).get("exits", [])}
        if len(ins) < 3:
            problems.append(f"в горячую зону «{hot}» входов {len(ins)} (нужно ≥ 3): {sorted(ins)}")
        if len(outs) < 2:
            problems.append(f"из горячей зоны «{hot}» отходов {len(outs)} (нужно ≥ 2)")
    beats = g.get("beats", [])
    run = 0
    for i, b in enumerate(beats):
        run = run + 1 if b["i"] >= 0.7 else 0
        if run > 2:
            problems.append(f"темп: «{b['room']}» — {run} высоких бита подряд")
        if b["type"] == "climax" and (i == 0 or beats[i - 1]["type"] != "rest"):
            problems.append(f"темп: перед кульминацией «{b['room']}» нет отдыха")
    if beats and beats[0]["type"] not in ("teach", "rest"):
        problems.append("темп: начинается не со знакомства/отдыха")
    return list(dict.fromkeys(problems))


if __name__ == "__main__":
    with open(sys.argv[1], encoding="utf-8") as f:
        graph = json.load(f)
    found = check(graph)
    for p in found:
        print("ПРОБЛЕМА:", p)
    print("всё годно" if not found else f"проблем: {len(found)}")
    sys.exit(1 if found else 0)
