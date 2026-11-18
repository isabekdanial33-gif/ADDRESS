"""Download CC0 assets listed in assets_manifest.json into the Unity project.

Usage: python -I fetch_assets.py [--res 2k]
Idempotent: skips files that already exist with the right size.
Models  -> Game/Assets/ThirdParty/PolyHaven/<id>/  (glTF + textures)
Materials -> Game/Assets/ThirdParty/AmbientCG/<id>/ (2K JPG maps, zip removed)
"""
import io, json, os, sys, urllib.request, zipfile, concurrent.futures as cf

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, "Game", "Assets", "ThirdParty")
UA = {"User-Agent": "address-asset-fetcher/1.0"}
RES = sys.argv[sys.argv.index("--res") + 1] if "--res" in sys.argv else "2k"


def get(url, tries=6):
    import time
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=120) as r:
                return r.read()
        except Exception:
            if i == tries - 1:
                raise
            time.sleep(3 * (i + 1))


def save(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def fetch_model(mid):
    out = os.path.join(GAME, "PolyHaven", mid)
    try:
        files = json.loads(get(f"https://api.polyhaven.com/files/{mid}"))
        g = files["gltf"].get(RES) or files["gltf"]["2k"]
        g = g["gltf"]
        jobs = [(os.path.join(out, os.path.basename(g["url"])), g["url"], g["size"])]
        for rel, info in g.get("include", {}).items():
            jobs.append((os.path.join(out, rel), info["url"], info["size"]))
        n = 0
        for path, url, size in jobs:
            if os.path.exists(path) and os.path.getsize(path) == size:
                continue
            save(path, get(url)); n += 1
        return f"model {mid}: {len(jobs)} files ({n} new)"
    except Exception as e:  # keep going; report at end
        return f"FAIL model {mid}: {e}"


def fetch_material(aid):
    out = os.path.join(GAME, "AmbientCG", aid)
    if os.path.isdir(out) and os.listdir(out):
        return f"material {aid}: cached"
    try:
        data = get(f"https://ambientcg.com/get?file={aid}_2K-JPG.zip")
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for n in z.namelist():
                low = n.lower()
                # keep only maps Unity URP Lit uses; skip preview/usd/mtlx/displacement
                if low.endswith(".jpg") and any(k in low for k in ("color", "normalgl", "roughness", "metalness", "ambientocclusion", "opacity")):
                    save(os.path.join(out, os.path.basename(n)), z.read(n))
        return f"material {aid}: ok"
    except Exception as e:
        return f"FAIL material {aid}: {e}"


def main():
    m = json.load(open(os.path.join(ROOT, "Tools", "assets_manifest.json")))
    with cf.ThreadPoolExecutor(3) as ex:
        res = list(ex.map(fetch_model, m["polyhaven_models"])) + list(ex.map(fetch_material, m["ambientcg_materials"]))
    for r in res:
        print(r)
    print("FAILED:", sum(r.startswith("FAIL") for r in res))


if __name__ == "__main__":
    main()
