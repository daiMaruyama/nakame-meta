// NakameMeta / ProceduralFacade
// World-space procedural building facade for URP. No UVs required — the window
// grid is derived from world position (triplanar), so it works on PLATEAU meshes
// that carry only POSITION + NORMAL. Windows light up randomly for a night look.
// One shader, parameterised per building category via material instances.
Shader "NakameMeta/ProceduralFacade"
{
    Properties
    {
        _WallColor        ("Wall Color", Color)            = (0.16, 0.17, 0.20, 1)
        _RoofColor        ("Roof Color", Color)            = (0.07, 0.07, 0.09, 1)
        _WindowSize       ("Window Spacing (x=width, y=floor) m", Vector) = (2.6, 3.2, 0, 0)
        _WindowFill       ("Window Fill", Range(0,1))      = 0.6
        _LitProbability   ("Lit Window Probability", Range(0,1)) = 0.35
        _WindowEmission   ("Window Emission Color", Color) = (1.0, 0.82, 0.5, 1)
        _EmissionStrength ("Emission Strength", Range(0,12)) = 3.5
        _GlassDarkness    ("Unlit Glass Darkness", Range(0,1)) = 0.45
        _RegionVariation  ("Per-Building Color Variation", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WallColor;
                float4 _RoofColor;
                float4 _WindowSize;
                float  _WindowFill;
                float  _LitProbability;
                float4 _WindowEmission;
                float  _EmissionStrength;
                float  _GlassDarkness;
                float  _RegionVariation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posIn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmIn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posIn.positionCS;
                OUT.positionWS  = posIn.positionWS;
                OUT.normalWS    = nrmIn.normalWS;
                OUT.fogFactor   = ComputeFogFactor(posIn.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n  = normalize(IN.normalWS);
                float3 wp = IN.positionWS;
                float3 an = abs(n);

                // roofs / floors: normal mostly vertical -> no windows
                float roofMask = step(0.6, an.y);

                // triplanar facade coords from the horizontal axis the wall faces
                float horiz = (an.x > an.z) ? wp.z : wp.x;
                float2 fuv  = float2(horiz / max(_WindowSize.x, 0.01),
                                     wp.y  / max(_WindowSize.y, 0.01));

                float2 cell = floor(fuv);
                float2 f    = frac(fuv);

                // window pane within the cell
                float halfFill = _WindowFill * 0.5;
                float2 inWin = step(0.5 - halfFill, f) * step(f, (0.5 + halfFill).xx);
                float windowMask = inWin.x * inWin.y * (1.0 - roofMask);

                // each window independently lit
                float r   = hash21(cell + 0.5);
                float lit = step(1.0 - _LitProbability, r);

                // low-frequency variation so neighbouring buildings differ
                float region = hash21(floor(wp.xz / 12.0));
                float3 wall  = _WallColor.rgb * (1.0 + (region - 0.5) * 2.0 * _RegionVariation);
                wall = lerp(wall, _RoofColor.rgb, roofMask);

                // unlit windows read as dark glass
                float3 albedo = lerp(wall, wall * _GlassDarkness, windowMask * (1.0 - lit));

                // simple URP lighting (ambient SH + main directional)
                Light mainLight = GetMainLight();
                float  ndl      = saturate(dot(n, mainLight.direction));
                float3 ambient  = SampleSH(n);
                float3 col      = albedo * (ambient + mainLight.color * ndl);

                // emissive lit windows
                col += _WindowEmission.rgb * _EmissionStrength * windowMask * lit;

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
