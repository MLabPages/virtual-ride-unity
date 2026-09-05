Shader "VirtualRide/ScenicLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.25
        _DetailStrength ("Surface variation", Range(0, 1)) = 0
        _DetailScale ("Surface scale", Float) = 1
        _VertexTint ("Use vertex tint", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface Surface Standard fullforwardshadows vertex:Vert
        #pragma target 3.0

        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        half _DetailStrength;
        float _DetailScale;
        half _VertexTint;

        struct Input
        {
            float3 worldNormal;
            float3 worldPos;
            float4 vertexTint;
        };

        void Vert(inout appdata_full v, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.vertexTint = v.color;
        }

        float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float Noise(float2 p)
        {
            float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
        }

        void Surface(Input input, inout SurfaceOutputStandard output)
        {
            float2 p = input.worldPos.xz * _DetailScale;
            float variation = Noise(p)*.7+Noise(p*2.1)*.3-.5;
            float cameraDistance = distance(_WorldSpaceCameraPos,input.worldPos);
            float grain = Noise(p*43)-.5;
            float detailFade = 1-smoothstep(3,18,cameraDistance);
            output.Albedo = _Color.rgb * lerp(float3(1,1,1), input.vertexTint.rgb, _VertexTint)
                * (1 + _DetailStrength * (variation * .55 + grain * .2 * detailFade));
            output.Metallic = _Metallic;
            output.Smoothness = _Glossiness;
            output.Alpha = _Color.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
