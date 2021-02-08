Shader "Custom/TransparentColored" {
    Properties {
        // Adds Color field we can modify
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }
    
    SubShader {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        LOD 100
    
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha 

        Pass {
            Lighting Off
            SetTexture [_MainTex] { 
                // Sets _Color as the 'constant' variable
                constantColor[_Color]
                
                // Multiplies color (in constant) with texture
                combine constant * texture
            } 
        }
    }
}
