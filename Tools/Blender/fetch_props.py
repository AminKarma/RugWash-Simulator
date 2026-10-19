"""Télécharge (une fois) les paquets glTF 1k des accessoires Poly Haven (CC0) dans %TEMP%/ph/models/<id>/.
Python système : python Tools/Blender/fetch_props.py"""
import json, os, sys, urllib.request

sys.path.insert(0, os.path.dirname(__file__))
from props_list import PROPS

UA = {"User-Agent": "RugWashSim-dev"}
ROOT = os.path.join(os.environ["TEMP"], "ph", "models")


def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return json.load(r)


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r, open(path, "wb") as f:
        f.write(r.read())


for alias, asset, _, _ in PROPS:
    files = get_json(f"https://api.polyhaven.com/files/{asset}")
    gltf = files["gltf"]["1k"]["gltf"]
    folder = os.path.join(ROOT, asset)
    download(gltf["url"], os.path.join(folder, os.path.basename(gltf["url"])))
    for rel, info in gltf.get("include", {}).items():
        download(info["url"], os.path.join(folder, rel))
    print(alias, asset, "ok", flush=True)
