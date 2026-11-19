#!/bin/bash
# itch_dl.sh <game-url> <outdir> : downloads every file of a free / name-your-price itch.io page
G="$1"; OUT="$2"; mkdir -p "$OUT"; J=$(mktemp)
curl -sL -c $J -A "Mozilla/5.0" "$G" -o "$OUT/.page.html"
tok=$(grep -o 'name="csrf_token" value="[^"]*"' "$OUT/.page.html" | head -1 | sed 's/.*value="//;s/"//')
U=$(curl -sL -b $J -c $J -A "Mozilla/5.0" -X POST "$G/download_url" --data-urlencode "csrf_token=$tok" | python -c "import sys,json;print(json.load(sys.stdin).get('url',''))")
[ -z "$U" ] && { echo "no download url for $G"; exit 1; }
curl -sL -b $J -c $J -A "Mozilla/5.0" "$U" -o "$OUT/.dl.html"
tok=$(grep -o 'name="csrf_token" value="[^"]*"' "$OUT/.dl.html" | head -1 | sed 's/.*value="//;s/"//')
key=""
sleep 2
for id in $(grep -oE 'data-upload_id="[0-9]+"' "$OUT/.dl.html" | grep -oE '[0-9]+' | sort -u); do
  F=$(curl -sL -b $J -A "Mozilla/5.0" -X POST "$G/file/$id?source=game_download&key=$key" --data-urlencode "csrf_token=$tok" | python -c "import sys,json;print(json.load(sys.stdin).get('url',''))")
  name=$(python - "$OUT/.dl.html" "$id" <<'PY'
import sys,re,html
s=open(sys.argv[1],encoding='utf-8',errors='ignore').read()
m=re.search(r'data-upload_id="%s".*?class="name"[^>]*>([^<]+)'%sys.argv[2], s, re.S)
print(html.unescape(m.group(1)).strip() if m else sys.argv[2]+'.bin')
PY
)
  echo "F=${F:0:60}"; sleep 2; [ -n "$F" ] && curl -sL "$F" -o "$OUT/$name" && echo "got $name $(stat -c %s "$OUT/$name")"
done
