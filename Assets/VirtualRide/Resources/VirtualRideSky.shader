Shader "VirtualRide/DaylightSky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // Stereo macros let Quest's single-pass (multiview) rendering draw the sky in both eyes.
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex); o.direction = v.vertex.xyz; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(i), hash(i+float2(1,0)), f.x),
                            lerp(hash(i+float2(0,1)), hash(i+1), f.x), f.y);
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float h = saturate(d.y);
                float3 col = lerp(float3(.78,.86,.87), float3(.18,.46,.70), pow(h,.45));
                float alignment = saturate(dot(d,normalize(float3(.34,.64,-.42))));
                col += float3(1,.74,.38) * (pow(alignment,20)*.09 + pow(alignment,160)*.22);
                col = lerp(col, float3(1,.96,.82), smoothstep(.9993,.9997,alignment));
                // Fixed cloud field: identical weather and lighting for every participant.
                float2 p = d.xz / max(.13,d.y) * 2.4;
                float n = noise(p)*.58 + noise(p*2.1)*.27 + noise(p*4.2)*.15;
                float clouds = smoothstep(.52,.73,n) * smoothstep(.035,.25,d.y) * .80;
                col = lerp(col, float3(.97,.97,.92), clouds);
                return fixed4(col,1);
            }
            ENDCG
        }
    }
}
