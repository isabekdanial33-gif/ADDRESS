// Old, slightly dirty CRT: barrel curvature, scanlines + aperture grille, RGB bleed, soft blur, vignette,
// rolling brightness band, flicker, noise and smudges/dust on the glass. Used on a RawImage that shows the
// computer UI rendered into a RenderTexture.
Shader "ADDRESS/CRT"
{
    Properties
    {
        _MainTex ("Screen", 2D) = "black" {}
        _Curvature ("Curvature", Range(0, 0.4)) = 0.12
        _Scan ("Scanline strength", Range(0, 1)) = 0.35
        _Bleed ("Colour bleed (px)", Range(0, 4)) = 1.3
        _Noise ("Noise", Range(0, 0.3)) = 0.06
        _Dirt ("Dirt", Range(0, 1)) = 0.55
        _Lines ("Lines", Float) = 540
        _Grid ("Pixel grid (old resolution)", Vector) = (1066, 600, 0, 0)
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float _Curvature, _Scan, _Bleed, _Noise, _Dirt, _Lines; float4 _Color, _Grid;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            v2f vert (appdata_full v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; o.col = v.color * _Color; return o; }

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 4; k++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

            float2 curve(float2 uv)
            {
                uv = uv * 2 - 1;
                float2 off = abs(uv.yx) * _Curvature;
                uv = uv + uv * off * off * 2.2;
                return uv * 0.5 + 0.5;
            }

            float4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = curve(i.uv);
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return float4(0.01, 0.01, 0.01, 1);
                // tiny horizontal jitter, sometimes a tear line
                float tear = step(0.995, hash(float2(floor(t * 7), 3.1))) * step(abs(uv.y - frac(t * 0.37)), 0.01) * 0.006;
                uv.x += (vnoise(float2(uv.y * 40, t * 9)) - 0.5) * 0.0012 + tear;
                // the old screen only had ~800x600 pixels: snap to that grid (big, slightly soft pixels)
                uv = (floor(uv * _Grid.xy) + 0.5) / _Grid.xy;
                float2 px = 1.0 / _Grid.xy * _Bleed * 0.5;
                // colour bleed + soft blur
                float3 c;
                c.r = tex2D(_MainTex, uv + float2(px.x, 0)).r;
                c.g = tex2D(_MainTex, uv).g;
                c.b = tex2D(_MainTex, uv - float2(px.x, 0)).b;
                float3 blur = (tex2D(_MainTex, uv + float2(px.x * 1.5, 0)).rgb + tex2D(_MainTex, uv - float2(px.x * 1.5, 0)).rgb + tex2D(_MainTex, uv + float2(0, px.y)).rgb + tex2D(_MainTex, uv - float2(0, px.y)).rgb) * 0.25;
                c = lerp(c, blur, 0.35) + blur * 0.12; // phosphor glow
                // scanlines + aperture grille
                float sl = 0.5 + 0.5 * sin(uv.y * _Lines * 3.14159 * 2);
                c *= lerp(1, 0.55 + 0.45 * sl, _Scan);
                float ag = fmod(floor(i.uv.x * _ScreenParams.x), 3);
                c *= lerp(1, (ag == 0 ? float3(1.08, 0.94, 0.94) : ag == 1 ? float3(0.94, 1.08, 0.94) : float3(0.94, 0.94, 1.08)), 0.6);
                // rolling bright band, flicker
                float band = smoothstep(0.0, 0.08, abs(frac(uv.y * 0.9 - t * 0.06) - 0.5));
                c *= 0.93 + 0.07 * band;
                c *= 0.97 + 0.03 * sin(t * 61.0);
                // vignette
                float2 v = uv * (1 - uv); c *= pow(saturate(v.x * v.y * 18), 0.28);
                // noise
                c += (hash(i.uv * 900 + t) - 0.5) * _Noise;
                // dirty glass: smudges dim and haze the picture, dust specks
                float sm = fbm(i.uv * float2(3.2, 2.4) + 7.3);
                float smudge = smoothstep(0.52, 0.8, sm) * _Dirt;
                c = lerp(c, c * 0.82 + 0.035, smudge);
                float dust = step(0.9975, hash(floor(i.uv * float2(620, 470)))) * _Dirt;
                c = lerp(c, float3(0.5, 0.5, 0.48), dust * 0.5);
                // slight green-grey cast of old phosphor
                c *= float3(0.98, 1.0, 0.96);
                return float4(saturate(c), 1) * i.col;
            }
            ENDCG
        }
    }
}
