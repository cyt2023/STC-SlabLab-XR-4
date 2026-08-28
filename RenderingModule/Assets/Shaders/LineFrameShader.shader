Shader "VolumeRendering/LineFrameShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent"}
        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        CULL Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                UNITY_VERTEX_OUTPUT_STEREO
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _MainTex_ST;

            //  顶点着色器
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);     //  设置顶点ID
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);   //  初始化顶点输出
                o.vertex = UnityObjectToClipPos(v.vertex);  //  顶点变换
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);       //  纹理变换
                return o;
            }

            //  片元着色器
            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);

                if(!(i.uv.x < 0.01 || i.uv.x > 0.99 || i.uv.y < 0.01 || i.uv.y > 0.99)){
					col.a = 0;
				}
                //  改颜色
                if(col.a != 0){
					col = _Color;
                }

                //  改位置

     //            float dis = distance(i.uv, float2(0.5f, 0.5f));
     //            if(dis < 0.5f && dis > 0.45f){
					// col = _Color;
     //            }

                return col;
            }
            ENDCG
        }
    }
}
