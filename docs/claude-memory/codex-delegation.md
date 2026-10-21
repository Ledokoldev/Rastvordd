---
name: codex-delegation
description: "User lets Claude delegate creative tasks to OpenAI Codex (\"Астра\") with reasoning effort at most medium"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 39e9e17d-b006-4623-8921-7798c2d19e46
  modified: 2026-10-20T22:20:48.252Z
---

User allows offloading creative work (images, design texts) to OpenAI Codex, which they call "Астра" (GPT model). Codex desktop app is installed and logged in; CLI at C:\Program Files\WindowsApps\OpenAI.Codex_*\app\resources\codex.exe, `codex exec` supports image_generation. Always pass `-c model_reasoning_effort="medium"` (or lower).

**Why:** User explicitly said effort must be no more than medium (same rule as for Claude subagents).
**How to apply:** For Раствордд use scripts/gen-assets.ps1 and scripts/gpt-tasks.ps1 in C:\Users\Xbaza\Desktop\Rastvordd. Related: [[rastvordd-game-concept]].
