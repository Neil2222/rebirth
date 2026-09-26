shader_type spatial;

// Voxel terrain. COLOR.rgb: material colour (linear). COLOR.a: how strongly it glows (ore veins).
// A faint world-space grid traces where the terrain crosses axis planes, Tron style.
uniform vec3 grid_color : source_color = vec3(0.1, 0.55, 0.8);
uniform float grid_spacing = 8.0;
uniform float grid_strength = 0.35;
uniform float ore_glow = 0.7;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 color = COLOR.rgb;
	float glow = COLOR.a;

	// Distance to the nearest grid plane on each axis, in screen-space-stable units.
	vec3 cell = abs(fract(world_position / grid_spacing + 0.5) - 0.5) * grid_spacing;
	// fwidth can be exactly 0 on some pixels; smoothstep(0, 0, x) is NaN and bloom would smear it.
	vec3 width = max(fwidth(world_position) * 1.2, vec3(1e-4));
	vec3 lines = 1.0 - smoothstep(vec3(0.0), width, cell);
	float grid = max(max(lines.x, lines.y), lines.z);

	ALBEDO = mix(color, color * 0.25, glow);
	ROUGHNESS = 0.9;
	EMISSION = color * glow * ore_glow + grid_color * grid * grid_strength;
}
