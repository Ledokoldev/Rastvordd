"""Мини-клиент ComfyUI API (только стандартная библиотека): загрузка картинки, очередь, ожидание, скачивание кадров."""
import json
import time
import urllib.parse
import urllib.request
import uuid

HOST = "http://127.0.0.1:8188"


def upload_image(path: str, name: str) -> str:
    boundary = uuid.uuid4().hex
    data = open(path, "rb").read()
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
        f"Content-Type: image/png\r\n\r\n").encode() + data + (
        f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue"
        f"\r\n--{boundary}--\r\n").encode()
    req = urllib.request.Request(f"{HOST}/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return json.loads(urllib.request.urlopen(req).read())["name"]


def queue(prompt: dict) -> str:
    req = urllib.request.Request(f"{HOST}/prompt", data=json.dumps({"prompt": prompt, "client_id": "rastvordd"}).encode(),
                                 headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req).read())["prompt_id"]


def wait(prompt_id: str, timeout: float = 3600, poll: float = 5) -> dict:
    t0 = time.time()
    while time.time() - t0 < timeout:
        h = json.loads(urllib.request.urlopen(f"{HOST}/history/{prompt_id}").read())
        if prompt_id in h:
            st = h[prompt_id].get("status", {})
            if st.get("status_str") == "error":
                raise RuntimeError(json.dumps(st, ensure_ascii=False)[:2000])
            if st.get("completed") or h[prompt_id].get("outputs"):
                return h[prompt_id]
        time.sleep(poll)
    raise TimeoutError(prompt_id)


def download_images(entry: dict, out_dir) -> list:
    import pathlib
    out = pathlib.Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    files = []
    for node in entry.get("outputs", {}).values():
        for img in node.get("images", []):
            q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img.get("subfolder", ""), "type": img.get("type", "output")})
            dst = out / img["filename"]
            dst.write_bytes(urllib.request.urlopen(f"{HOST}/view?{q}").read())
            files.append(dst)
    return sorted(files)
