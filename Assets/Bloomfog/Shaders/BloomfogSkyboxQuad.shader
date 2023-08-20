Shader "Custom/Bloomfog/BloomfogSkyboxQuad" {
  SubShader {
    Tags { "Queue"="Geometry" "RenderType"="Opaque" }
    ZWrite Off Cull Off

    Pass {
      CGPROGRAM
      #pragma multi_compile __ _ENABLE_BLOOM_FOG
      #pragma vertex vert
      #pragma fragment frag
      
      #include "UnityCG.cginc"

      struct appdata {
        float4 vertex : POSITION;
      };
      struct v2f {
        float4 vertex : SV_POSITION;
        float4 screenPos : TEXCOORD0;
      };

      sampler2D _BloomfogTex;

      v2f vert(appdata v) {
        v2f o;
        o.vertex = float4(v.vertex.xy, 0, 1);
        o.screenPos = ComputeNonStereoScreenPos(o.vertex);
        return o;
      }

      float4 frag(v2f i) : SV_Target {
        float2 screenSpaceUV = i.screenPos.xy / i.screenPos.w;
        return tex2D(_BloomfogTex, screenSpaceUV);
      }
      ENDCG
    }
  }
}