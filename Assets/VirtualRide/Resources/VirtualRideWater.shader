Shader "VirtualRide/LakeWater"
{
    Properties { _Color ("Water", Color) = (.12,.40,.46,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color;
        struct Input { float3 worldPos; float3 viewDir; };
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float2 p=i.worldPos.xz;
            float a=sin(p.x*2.1+p.y*.6+_Time.y*.65);
            float b=sin(p.x*.7-p.y*3.3+_Time.y*.9);
            o.Normal=normalize(float3(a*.06,b*.055,1));
            float fresnel=pow(1-saturate(dot(normalize(i.viewDir),o.Normal)),4);
            o.Albedo=lerp(_Color.rgb,float3(.52,.71,.78),fresnel*.8);
            o.Emission=float3(.34,.47,.54)*fresnel*.13;
            o.Smoothness=.89; o.Metallic=.20; o.Alpha=1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
