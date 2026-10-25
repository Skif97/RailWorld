#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Based on the game's blockhighlights.fsh (1.22.3): the colour gets the fog the vertex shader computed
// (see railfill.vsh) before it goes to OIT, as the game's applyFog does for other geometry.

in vec4 color;
in float glowLevel;
in vec4 rgbaFog;
in float fogAmount;

uniform sampler2D particleTex;

#include fogandlight.fsh
#include oit.fsh

void main()
{
    OIT(applyFog(color, fogAmount), glowLevel);
}
