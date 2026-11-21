#ifndef OCC_HOLE_INCLUDED
#define OCC_HOLE_INCLUDED
// See-through hole for tree foliage/bark. Globals are set every frame by TreeOcclusionFader.
float4 _OccCamPos;   // xyz = camera position
float4 _OccDir;      // xyz = unit vector camera -> avatar, w = distance camera -> avatar
float4 _OccParams;   // x = hole radius (m), y = softness 0..1 (dithered falloff as fraction of radius), z = margin in front of avatar (m), w = strength 0..1

// 8x8 ordered (Bayer) dither threshold from pixel coordinates, in (0,1).
float OccBayer8(float2 pixel)
{
    uint x = (uint)pixel.x & 7u;
    uint y = (uint)pixel.y & 7u;
    uint xy = x ^ y;
    uint b = ((xy & 1u) << 5) | ((x & 1u) << 4) | ((xy & 2u) << 2) | ((x & 2u) << 1) | ((xy & 4u) >> 1) | ((x & 4u) >> 2);
    return (b + 0.5) / 64.0;
}

// Alpha in -> Alpha out. Pixels inside the cylinder around the camera->avatar ray (and nearer than the avatar)
// are removed with an ordered dither whose density ramps smoothly from the edge to the centre.
void OccHole_float(float Alpha, float3 WorldPos, float4 ScreenPos, out float AlphaOut)
{
    AlphaOut = Alpha;
#if defined(SHADERPASS_SHADOWCASTER) && defined(SHADERPASS) && (SHADERPASS == SHADERPASS_SHADOWCASTER)
    return;
#else
    float strength = _OccParams.w;
    float r = _OccParams.x * strength;
    if (r <= 1e-3) return;
    float3 toFrag = WorldPos - _OccCamPos.xyz;
    float along = dot(toFrag, _OccDir.xyz);
    if (along > _OccDir.w - _OccParams.z) return;                 // behind / beside the avatar: never cut
    float radial = length(toFrag - _OccDir.xyz * along);
    float soft = max(r * _OccParams.y, 1e-4);
    float cov = saturate((r - radial) / soft);
    cov = cov * cov * (3.0 - 2.0 * cov);
    float2 pix = (ScreenPos.xy / max(ScreenPos.w, 1e-5)) * _ScreenParams.xy;
    if (OccBayer8(pix) < cov) AlphaOut = 0.0;
#endif
}
#endif
