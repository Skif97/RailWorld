#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Based on the game's blockhighlights.vsh (1.22.3) with two additions.
// 1. The vertex colour is multiplied by tint. Fills of empty track part places are built white (with face
//    shading) once per group, and the colour (ghost, installable, blocked) comes from tint at draw time, so one
//    mesh per group is enough.
// 2. Fog and distance fade, the same as the track itself (railinstanced.vsh) and the game's chunks. The game
//    only draws block highlights next to the player and needs neither; fills are drawn wherever the track is
//    seen, so without them they would shine through the fog at the edge of the view distance. For that the
//    matrix is split: modelMatrix places the vertex relative to the camera, viewMatrix is the camera.
// Compare with assets/game/shaders/blockhighlights.vsh after game updates.
// Comments here are ASCII only: some drivers reject other characters in shader source.

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;


uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
uniform mat4 modelMatrix;
uniform vec4 tint;
uniform vec4 rgbaFogIn;
uniform float fogMinIn;
uniform float fogDensityIn;

out vec4 color;
out vec4 rgbaFog;
out float fogAmount;

#include vertexflagbits.ash
#include shadowcoords.vsh
#include fogandlight.vsh

void main(void)
{
	vec4 worldPos = modelMatrix * vec4(vertexPositionIn, 1.0);
	vec4 cameraPos = viewMatrix * worldPos;
	
	color = vertexColor * tint;
	// Distance fade out, as in railinstanced.vsh; transparent here, so only down to 0
	color.a *= clamp(20 * (1.10 - length(worldPos.xz) / viewDistance) - 5, 0, 1);
	gl_Position = projectionMatrix * cameraPos;
	
	// We are cheap. We pretend the highlights are closer to the camera to enforce it
	// always being drawn on top
	gl_Position.w += 0.0004;
	
	rgbaFog = rgbaFogIn;
	fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);
	glowLevel = 0;
}
