Shader "Hidden/TemporalShader"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct Input
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;
    sampler2D _HistoryTex;

    float2 _JitterUV;
    float2 _PastJitterUV;

    sampler2D _CameraMotionVectorsTexture;
    sampler2D _CameraDepthTexture;
    float4 _CameraDepthTexture_TexelSize;

    float4 _MainTex_TexelSize;

    Varyings vertex(in Input input)
    {
        Varyings output;

        output.vertex = UnityObjectToClipPos(input.vertex);
        output.uv = input.uv;

    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            output.uv.y = 1. - input.uv.y;
    #endif

        return output;
    }

    // Adapted from playdead solution
    // https://github.com/playdeadgames/temporal
    float4 clip_aabb(float3 aabb_min, float3 aabb_max, float4 p, float4 colour)
    {
        // find center and extents
        float3 center = 0.5 * (aabb_max + aabb_min);
        float3 extents = 0.5 * (aabb_max - aabb_min);

        //distance colour is from center of aabb
        float4 dist = colour - float4(center, p.w);

        float3 repeat = abs(dist.xyz / extents);
        float repeatmax = max(repeat.x, max(repeat.y, repeat.z));

        // if outside extents
        if (repeatmax > 1.0)
            return float4(center, p.w) + dist / repeatmax;
        else
            return colour;// point inside aabb
    }

    float4 sampleColour(sampler2D tex, float2 uv) 
    {
        return tex2D(tex, uv);
    }


    float4 fragment(Varyings input) : SV_Target
    {
        float2 uv = input.uv;

        //sample motion vector
        float2 motion = tex2D(_CameraMotionVectorsTexture, uv).xy;

        // sample colour from main texture and unjitter
        float4 colour = tex2D(_MainTex, uv - _JitterUV);

        // sample history colour, subtract motion from uv to reproject
        float4 history = tex2D(_HistoryTex, uv - motion);
  
        //neighbourhood texel size
        float2 du = float2(_MainTex_TexelSize.x, 0.0);
        float2 dv = float2(0.0, _MainTex_TexelSize.y);

        //sample colour from each square in neighbourhood
        float4 ctl = sampleColour(_MainTex, uv - dv - du);
        float4 ctc = sampleColour(_MainTex, uv - dv);
        float4 ctr = sampleColour(_MainTex, uv - dv + du);
        float4 cml = sampleColour(_MainTex, uv - du);
        float4 cmc = sampleColour(_MainTex, uv);
        float4 cmr = sampleColour(_MainTex, uv + du);
        float4 cbl = sampleColour(_MainTex, uv + dv - du);
        float4 cbc = sampleColour(_MainTex, uv + dv);
        float4 cbr = sampleColour(_MainTex, uv + dv + du);


        // find min and max for whole neighbourhood
        float4 cmin = min(ctl, min(ctc, min(ctr, min(cml, min(cmc, min(cmr, min(cbl, min(cbc, cbr))))))));
        float4 cmax = max(ctl, max(ctc, max(ctr, max(cml, max(cmc, max(cmr, max(cbl, max(cbc, cbr))))))));

        // avg colour of neighbourhood
        float4 cavg = (ctl + ctc + ctr + cml + cmc + cmr + cbl + cbc + cbr) / 9.0;

        //same as above but for plus shaped neighbourhood to blend
        float4 cmin5 = min(ctc, min(cml, min(cmc, min(cmr, cbc))));
        float4 cmax5 = max(ctc, max(cml, max(cmc, max(cmr, cbc))));
        float4 cavg5 = (ctc + cml + cmc + cmr + cbc) / 5.0;

        // blend neighbourhoods together.
        cmin = 0.5 * (cmin + cmin5);
        cmax = 0.5 * (cmax + cmax5);
        cavg = 0.5 * (cavg + cavg5);

        // clip history colour
        history = clip_aabb(cmin.xyz, cmax.xyz, clamp(cavg, cmin, cmax), history);

        //luminance testing stuff
        float luminance = Luminance(colour.rgb);
        float luminanceH = Luminance(history.rgb);

        float diff = abs(luminance - luminanceH) / max(luminance, max(luminanceH, 0.2));
        float weight = 1.0 - diff;
        float weightSQR = weight * weight;
        float feedback = lerp(0.88f, 0.97f, weightSQR);

        return lerp(colour, history, feedback);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

            Pass
        {
            CGPROGRAM
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }
    }
}