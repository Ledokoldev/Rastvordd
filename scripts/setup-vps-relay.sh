#!/usr/bin/env bash
# Проброс через VPS (D73) одной командой — для нового VPS (Ubuntu/Debian, пользователь с sudo без пароля).
# Ставит frps службой на VPS, у хоста пишет tools/frp/frpc.toml (+ качает frpc.exe) и server.txt (адрес для меню друзей).
# Запуск из Git Bash в корне репозитория:
#   scripts/setup-vps-relay.sh user@IP [путь_к_ключу]
# Потом: powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 — и раздать друзьям новый zip.
set -euo pipefail
TARGET="${1:?usage: scripts/setup-vps-relay.sh user@IP [ключ]}"
KEY="${2:-$HOME/.ssh/id_ed25519_vpn2}"
HOST="${TARGET#*@}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SSH=(ssh -i "$KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "$TARGET")
TOKEN="$(head -c 24 /dev/urandom | base64 | tr -d '/+=')"
V="$(curl -s https://api.github.com/repos/fatedier/frp/releases/latest | grep -m1 '"tag_name"' | sed -E 's/.*"v([^"]+)".*/\1/')"
echo "frp v$V → $HOST"

"${SSH[@]}" "set -e
cd /tmp && wget -q https://github.com/fatedier/frp/releases/download/v$V/frp_${V}_linux_amd64.tar.gz && tar xzf frp_${V}_linux_amd64.tar.gz
sudo install -m 755 frp_${V}_linux_amd64/frps /usr/local/bin/frps
sudo mkdir -p /etc/frp
printf 'bindPort = 7000\nauth.token = \"%s\"\nallowPorts = [{ single = 7777 }]\n' '$TOKEN' | sudo tee /etc/frp/frps.toml >/dev/null
sudo chmod 600 /etc/frp/frps.toml
printf '[Unit]\nDescription=frps (Rastvordd relay)\nAfter=network-online.target\n[Service]\nExecStart=/usr/local/bin/frps -c /etc/frp/frps.toml\nRestart=always\n[Install]\nWantedBy=multi-user.target\n' | sudo tee /etc/systemd/system/frps.service >/dev/null
sudo systemctl daemon-reload && sudo systemctl enable --now frps && sudo systemctl restart frps
if command -v ufw >/dev/null && sudo ufw status | grep -q active; then sudo ufw allow 7000/tcp; sudo ufw allow 7777/tcp; fi
sleep 1; systemctl is-active frps"

mkdir -p "$ROOT/tools/frp"
if [ ! -f "$ROOT/tools/frp/frpc.exe" ]; then
  T="$(mktemp -d)"
  curl -sL -o "$T/frp.zip" "https://github.com/fatedier/frp/releases/download/v$V/frp_${V}_windows_amd64.zip"
  unzip -q -o "$T/frp.zip" -d "$T" && cp "$T"/frp_*/frpc.exe "$ROOT/tools/frp/" && rm -rf "$T"
fi
printf 'serverAddr = "%s"\nserverPort = 7000\nauth.token = "%s"\n\n[[proxies]]\nname = "rastvordd"\ntype = "tcp"\nlocalIP = "127.0.0.1"\nlocalPort = 7777\nremotePort = 7777\n' "$HOST" "$TOKEN" > "$ROOT/tools/frp/frpc.toml"
echo "$HOST:7777" > "$ROOT/server.txt"
echo "готово: tools/frp/frpc.toml, server.txt → $HOST:7777. Если у хостера свой брандмауэр — открыть 7000/tcp и 7777/tcp."
