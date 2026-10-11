#version 330 core
// Copy of the game's instanced.vsh (1.22.3) with one added uniform: origin.
// Instance matrices are relative to the corner of their chunk; origin is that corner relative
// to the camera. If the game changes its instanced.vsh, compare this file against it.
// Comments here are ASCII only: some drivers reject other characters in shader source.
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPosition;  // Per vertex
layout(location = 1) in vec2 uvIn;				// Per vertex
layout(location = 2) in vec4 rgbaBlockIn;		// Per vertex (rgb = block light, a=sun light level)
layout(location = 3) in int renderFlagsIn; 	// Per vertex

layout(location = 4) in vec4 rgbaLightIn;		// Per instance
layout(location = 5) in mat4 transform;	 	// Per instance

uniform vec4 rgbaFogIn;
uniform vec3 rgbaAmbientIn;	
uniform float fogMinIn;
uniform float fogDensityIn;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec3 origin;

out vec4 color;
out vec2 uv;
out vec4 rgbaFog;
out float fogAmount;
out vec3 normal;
out vec4 worldPos;

#if SSAOLEVEL > 0
out vec4 fragPosition;
out vec4 gnormal;
#endif

flat out int renderFlags;

#include vertexflagbits.ash
#include shadowcoords.vsh
#include fogandlight.vsh

void main()
{
	worldPos = transform * vec4(vertexPosition, 1.0);
	worldPos.xyz += origin;
	vec4 cameraPos = modelViewMatrix * worldPos;
	
	calcShadowMapCoords(modelViewMatrix, worldPos);
	
	uv = uvIn;
	color = applyLight(rgbaAmbientIn, rgbaLightIn * rgbaBlockIn, renderFlagsIn, cameraPos);
	rgbaFog = rgbaFogIn;
	
	// Distance fade out
	color.a = clamp(20 * (1.10 - length(worldPos.xz) / viewDistance) - 5, -1, 1);
	gl_Position = projectionMatrix * cameraPos;
	
	fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);
	renderFlags = renderFlagsIn;
	
	normal = unpackNormal(renderFlagsIn);
	normal = normalize((transform * vec4(normal.x, normal.y, normal.z, 0)).xyz);

	#if SSAOLEVEL > 0
	fragPosition = cameraPos;
	gnormal = modelViewMatrix * vec4(normal.xyz, 0);
	#endif
}
