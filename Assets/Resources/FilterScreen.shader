// src/platform/filter-screen.mjs shaders, GLSL -> HLSL 1:1: one pass per JS program (PALETTE, COPY, XBR,
// SOFT, CRT). Lives under Resources/ so FilterScreen.cs can load it and the player build keeps it.
// uTex / uIndex are _MainTex (what Graphics.Blit binds); every other uniform keeps its JS name.
// texAt = texture2D (a reserved word in HLSL), as tex2Dlod: no mipmaps, and legal after the CRT early return.
Shader "Hidden/FilterScreen"
{
    Properties
    {
        _MainTex ("uTex", 2D) = "black" {}
        uPalette ("uPalette", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D uPalette;
        float uFlip; // 1 when drawing to the canvas (texture row 0 is the top of the frame)
        float2 uSize;
        float2 uOut;   // output size in pixels
        float uLines;  // source scanline count

        struct v2f { float4 pos : SV_POSITION; float2 vUv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.vUv = v.texcoord;
            o.vUv.y = lerp(o.vUv.y, 1.0 - o.vUv.y, uFlip);
            return o;
        }

        float4 texAt(sampler2D s, float2 uv) { return tex2Dlod(s, float4(uv, 0.0, 0.0)); }
        ENDCG

        // Pass 0 (PALETTE): palette lookup.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float index = texAt(_MainTex, i.vUv).r;
                return float4(texAt(uPalette, float2((index * 255.0 + 0.5) / 256.0, 0.5)).rgb, 1.0);
            }
            ENDCG
        }

        // Pass 1 (COPY).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return texAt(_MainTex, i.vUv); }
            ENDCG
        }

        // Pass 2 (XBR): xBR level 2 (Hyllian's edge-directed interpolation): each output pixel evaluates the
        // one corner of its source pixel it falls into.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            static const float TH = 15.0;
            static const float3 Y = float3(0.2126, 0.7152, 0.0722);
            float df(float3 a, float3 b) { return abs(dot(a, Y) - dot(b, Y)) * 255.0; }
            bool eq(float3 a, float3 b) { return df(a, b) < TH; }
            float3 tx(float2 base, float2 px, float x, float y) { return texAt(_MainTex, base + px * float2(x, y)).rgb; }

            float4 frag(v2f i) : SV_Target
            {
                float2 px = 1.0 / uSize;
                float2 fp = frac(i.vUv * uSize);
                float2 base = (floor(i.vUv * uSize) + 0.5) * px;
                // Mirror the neighbourhood so the corner under evaluation is always "bottom-right".
                float2 sx = float2(fp.x >= 0.5 ? 1.0 : -1.0, fp.y >= 0.5 ? 1.0 : -1.0);
                float2 d = abs(fp - 0.5) * 2.0; // 0 at the pixel centre, 1 at the corner
                float3 E = tx(base, px, 0.0, 0.0);
                float3 c1 = tx(base, px, sx.x, 0.0);        // F: orthogonal neighbour (x)
                float3 c2 = tx(base, px, 0.0, sx.y);        // H: orthogonal neighbour (y)
                float3 c3 = tx(base, px, sx.x, sx.y);       // I: diagonal
                float3 c4 = tx(base, px, 0.0, -sx.y);       // B
                float3 c5 = tx(base, px, -sx.x, 0.0);       // D
                float3 c6 = tx(base, px, sx.x, -sx.y);      // C
                float3 c7 = tx(base, px, -sx.x, sx.y);      // G
                float3 c8 = tx(base, px, 2.0 * sx.x, sx.y); // I4
                float3 c9 = tx(base, px, sx.x, 2.0 * sx.y); // I5
                float3 c10 = tx(base, px, 2.0 * sx.x, 0.0); // F4
                float3 c11 = tx(base, px, 0.0, 2.0 * sx.y); // H5
                float3 c12 = tx(base, px, -sx.x, -sx.y);    // A
                float3 res = E;
                float edgeE = df(E, c3) * 4.0 + df(E, c4) + df(E, c5) + df(c1, c9) + df(c2, c8);
                float edgeD = df(c1, c2) * 4.0 + df(c1, c6) + df(c1, c10) + df(c2, c7) + df(c2, c11);
                bool edge = edgeE > edgeD && !eq(E, c3) && !(eq(c1, c2) && eq(E, c12));
                if (edge)
                {
                    bool lv2a = eq(c1, c4) && !eq(c1, c5) && df(c2, c3) < TH * 2.0;
                    bool lv2b = eq(c2, c5) && !eq(c2, c4) && df(c1, c3) < TH * 2.0;
                    float t;
                    if (lv2a) t = (d.x + d.y * 0.5) > 0.75 ? 1.0 : 0.0;
                    else if (lv2b) t = (d.x * 0.5 + d.y) > 0.75 ? 1.0 : 0.0;
                    else t = (d.x + d.y) > 1.0 ? 1.0 : 0.0;
                    float3 pick = df(E, c1) <= df(E, c2) ? c1 : c2;
                    res = lerp(res, pick, t);
                }
                return float4(res, 1.0);
            }
            ENDCG
        }

        // Pass 3 (SOFT): bilinear 4x with a dither-merging blur and a light sharpen, for a painted look.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 vUv = i.vUv;
                float2 px = 1.0 / uSize;
                float3 c = texAt(_MainTex, vUv).rgb * 0.36;
                c += texAt(_MainTex, vUv + float2(px.x * 0.6, 0.0)).rgb * 0.12;
                c += texAt(_MainTex, vUv - float2(px.x * 0.6, 0.0)).rgb * 0.12;
                c += texAt(_MainTex, vUv + float2(0.0, px.y * 0.6)).rgb * 0.12;
                c += texAt(_MainTex, vUv - float2(0.0, px.y * 0.6)).rgb * 0.12;
                c += texAt(_MainTex, vUv + px * 0.6).rgb * 0.04;
                c += texAt(_MainTex, vUv - px * 0.6).rgb * 0.04;
                c += texAt(_MainTex, vUv + float2(px.x, -px.y) * 0.6).rgb * 0.04;
                c += texAt(_MainTex, vUv - float2(px.x, -px.y) * 0.6).rgb * 0.04;
                // unsharp mask against a wider blur to keep edges crisp
                float3 wide = (texAt(_MainTex, vUv + float2(px.x * 1.5, 0.0)).rgb + texAt(_MainTex, vUv - float2(px.x * 1.5, 0.0)).rgb + texAt(_MainTex, vUv + float2(0.0, px.y * 1.5)).rgb + texAt(_MainTex, vUv - float2(0.0, px.y * 1.5)).rgb) * 0.25;
                c = clamp(c + (c - wide) * 0.5, 0.0, 1.0);
                return float4(c, 1.0);
            }
            ENDCG
        }

        // Pass 4 (CRT): curvature, glow, scanlines, aperture grille, vignette.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float2 curve(float2 uv)
            {
                uv = uv * 2.0 - 1.0;
                float2 off = abs(uv.yx) / float2(6.0, 4.5);
                uv = uv + uv * off * off;
                return uv * 0.5 + 0.5;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 uv = curve(i.vUv);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return float4(0.0, 0.0, 0.0, 1.0);
                float3 col = texAt(_MainTex, uv).rgb;
                // soft glow from the horizontal neighbours
                float2 px = 1.0 / uOut;
                float3 glow = texAt(_MainTex, uv + float2(px.x * 2.0, 0.0)).rgb + texAt(_MainTex, uv - float2(px.x * 2.0, 0.0)).rgb;
                col = col * 0.85 + glow * 0.12;
                // scanlines aligned to the source rows
                float line_ = sin(uv.y * uLines * 3.14159265 * 2.0);
                col *= 0.82 + 0.18 * (0.5 + 0.5 * line_);
                // subtle aperture-grille tint (gl_FragCoord.x == SV_POSITION.x: both are pixel centres, x from the left)
                float mask = fmod(i.pos.x, 3.0);
                col *= float3(mask < 1.0 ? 1.06 : 0.97, mask >= 1.0 && mask < 2.0 ? 1.06 : 0.97, mask >= 2.0 ? 1.06 : 0.97);
                // vignette
                float2 v = uv * (1.0 - uv);
                col *= pow(v.x * v.y * 24.0, 0.15);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
