// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Vertex Color Unlit" {
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Color ("Color", Color) = (1,1,1,1)
	}
	SubShader 
	{
		Pass
		{
			CGPROGRAM
			// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it does not contain a surface program or both vertex and fragment programs.
			#pragma exclude_renderers gles
			#pragma exclude_renderers d3d11
			#pragma exclude_renderers d3d11_9x
			
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			
			#include "UnityCG.cginc"
				
			struct vertexInput 
			{
	            float4 vertex : POSITION;
	            float4 texcoord : TEXCOORD0;
	            float4 color: COLOR0;
		    };
	        struct vertexOutput 
	        {
	            float4 pos : POSITION;
	            float2 uv : TEXCOORD0;
	            float4 color: COLOR0;
	        };
	        
	        float4 _MainTex_ST;
			
			uniform sampler2D _MainTex;
			
			vertexOutput vert(vertexInput IN)
			{
				vertexOutput o;
				o.pos = UnityObjectToClipPos( IN.vertex );
				o.uv = TRANSFORM_TEX(IN.texcoord, _MainTex);
				o.color = IN.color;
				return o;
			}
			
			
			float4 frag(vertexOutput IN) : COLOR
			{
				return float4(IN.color * tex2D(_MainTex, IN.uv).rgb,1);
			}
			ENDCG
		}
	}
} 
