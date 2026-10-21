"""
Выгрузка истории работы над Раствордд из журналов Claude Code (*.jsonl):
все сообщения пользователя дословно (включая присланные посреди работы) и текстовые ответы Claude,
без вызовов инструментов и служебных вставок; дубли между ветками сессий убираются.

  python scripts/export-history.py <папка с jsonl> <файл1.jsonl> [файл2.jsonl …]
Пишет docs/history/user-messages.md и docs/history/conversation.md.
"""
import json, os, re, sys
from datetime import datetime, timezone, timedelta

MSK = timezone(timedelta(hours=3))
SYS = re.compile(r"<system-reminder>.*?</system-reminder>", re.S)
MIDTURN = re.compile(r"The user sent a new message while you were working:\s*(.*?)\s*(?:This is how Claude Code surfaces|$)", re.S)
NOISE = ("<command-name>", "<local-command", "Caveat: The messages below", "[Request interrupted", "<task-notification>",
         "This session is being continued from a previous conversation")


def clean(t):
    t = SYS.sub("", t)
    return t.strip()


def ts(s):
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00")).astimezone(MSK)
    except Exception:
        return None


def walk(path):
    """(время, кто, текст, uuid) по порядку файла."""
    out = []
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            try:
                e = json.loads(line)
            except Exception:
                continue
            if e.get("isSidechain"):
                continue
            t = ts(e.get("timestamp", ""))
            msg = e.get("message") or {}
            uid = e.get("uuid", "")
            att = e.get("attachment") or {}
            if e.get("type") == "attachment" and att.get("type") == "queued_command" and (att.get("origin") or {}).get("kind") == "human":
                txt = clean(att.get("prompt") or "")
                if txt:
                    out.append((t, "user", txt, uid))   # прислано посреди работы
                continue
            if e.get("type") == "user":
                c = msg.get("content")
                parts = []
                if isinstance(c, str):
                    parts.append(("text", c))
                elif isinstance(c, list):
                    for p in c:
                        if p.get("type") == "text":
                            parts.append(("text", p.get("text", "")))
                        elif p.get("type") == "tool_result":
                            rc = p.get("content")
                            txt = rc if isinstance(rc, str) else " ".join(x.get("text", "") for x in (rc or []) if isinstance(x, dict))
                            for m in MIDTURN.finditer(txt or ""):
                                out.append((t, "user", m.group(1).strip(), uid + "#mid"))
                for kind, txt in parts:
                    for m in MIDTURN.finditer(txt):
                        out.append((t, "user", m.group(1).strip(), uid + "#mid"))
                    txt2 = clean(MIDTURN.sub("", txt))
                    if not txt2 or e.get("isMeta") or any(n in txt2 for n in NOISE):
                        continue
                    out.append((t, "user", txt2, uid))
            elif e.get("type") == "assistant":
                c = msg.get("content")
                if isinstance(c, list):
                    txt = "\n".join(p.get("text", "") for p in c if p.get("type") == "text").strip()
                    if txt:
                        out.append((t, "claude", txt, uid))
    return out


def main():
    folder = sys.argv[1]
    files = sys.argv[2:]
    rows, seen = [], set()
    for name in files:
        for t, who, txt, uid in walk(os.path.join(folder, name)):
            key = (who, txt[:400])
            if key in seen:
                continue
            seen.add(key)
            rows.append((t or datetime.min.replace(tzinfo=MSK), who, txt, name[:8]))
    rows.sort(key=lambda r: r[0])
    os.makedirs("docs/history", exist_ok=True)

    with open("docs/history/user-messages.md", "w", encoding="utf-8") as f:
        f.write("# Все сообщения пользователя — дословно\n\n")
        f.write("Выгружено из журналов Claude Code (`scripts/export-history.py`). Время — московское. Текст не правлен: как надиктовано/написано.\n\n")
        day = None
        n = 0
        for t, who, txt, sess in rows:
            if who != "user":
                continue
            d = t.strftime("%Y-%m-%d")
            if d != day:
                f.write(f"\n## {d}\n\n")
                day = d
            n += 1
            body = txt.replace("\n", "\n> ")
            f.write(f"**{n}. {t.strftime('%H:%M')}** (сессия {sess})\n> {body}\n\n")

    with open("docs/history/conversation.md", "w", encoding="utf-8") as f:
        f.write("# Переписка целиком: пользователь и Claude\n\n")
        f.write("Выгружено из журналов Claude Code (`scripts/export-history.py`): только тексты — без вызовов инструментов и их вывода. Время — московское.\n\n")
        day = None
        for t, who, txt, sess in rows:
            d = t.strftime("%Y-%m-%d")
            if d != day:
                f.write(f"\n## {d}\n\n")
                day = d
            head = "🧑 Пользователь" if who == "user" else "🤖 Claude"
            body = txt.replace("\n", "\n> ") if who == "user" else txt
            f.write(f"### {head} · {t.strftime('%H:%M')}\n\n{'> ' + body if who == 'user' else body}\n\n")
    print(f"сообщений: {sum(1 for r in rows if r[1] == 'user')} пользователя, {sum(1 for r in rows if r[1] == 'claude')} Claude")


if __name__ == "__main__":
    main()
