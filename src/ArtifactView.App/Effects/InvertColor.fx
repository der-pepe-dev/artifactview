// Pixel shader: inverts RGB channels while preserving alpha.
// Compiled to InvertColor.ps with:  fxc /T ps_2_0 /O0 /Fo InvertColor.ps InvertColor.fx
sampler2D implicitInput : register(s0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(implicitInput, uv);
    c.rgb = 1.0 - c.rgb;
    return c;
}
