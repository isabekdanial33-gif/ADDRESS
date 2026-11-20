"""Height (displacement) maps for the ambientCG surfaces, 1K is plenty for parallax. python -I fetch_heights.py"""
import io, json, os, urllib.request, zipfile, concurrent.futures as cf
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, "Game", "Assets", "ThirdParty", "AmbientCG")
def one(aid):
    out = os.path.join(GAME, aid)
    if not os.path.isdir(out): return f"skip {aid}"
    if any("displacement" in f.lower() for f in os.listdir(out)): return f"cached {aid}"
    try:
        req = urllib.request.Request(f"https://ambientcg.com/get?file={aid}_1K-JPG.zip", headers={"User-Agent": "address-asset-fetcher/1.0"})
        data = urllib.request.urlopen(req, timeout=120).read()
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for n in z.namelist():
                if n.lower().endswith("displacement.jpg"):
                    open(os.path.join(out, os.path.basename(n)), "wb").write(z.read(n)); return f"ok {aid}"
        return f"none {aid}"
    except Exception as e: return f"FAIL {aid}: {e}"
m = json.load(open(os.path.join(ROOT, "Tools", "assets_manifest.json")))
with cf.ThreadPoolExecutor(4) as ex:
    for r in ex.map(one, m["ambientcg_materials"]): print(r)
