shader_type spatial;

// Neon block surfaces.
//   COLOR.rgb  neon paint (linear)       COLOR.a  body shade (dark base brightness)
//   UV         0..1 across each face     UV2.x    block health 0..1, UV2.y per-block random
uniform float edge_width = 0.045;
uniform float glow_strength = 3.2;

float hash(float n) {
	return fract(sin(n) * 43758.5453);
}

void fragment() {
	vec2 d = min(UV, 1.0 - UV);
	float edge_distance = min(d.x, d.y);
	float edge = 1.0 - smoothstep(edge_width * 0.5, edge_width, edge_distance);
	// A faint inset panel line gives large flat walls some structure.
	float inset = 1.0 - smoothstep(0.004, 0.012, abs(edge_distance - 0.18));

	float health = UV2.x;
	// Damaged blocks dim and, below half health, flicker.
	float flicker = health < 0.5 ? 0.55 + 0.45 * step(0.35, hash(floor(TIME * 12.0) + UV2.y * 97.0)) : 1.0;

	ALBEDO = vec3(COLOR.a) * mix(vec3(1.0), COLOR.rgb, 0.15);
	METALLIC = 0.6;
	ROUGHNESS = 0.35;
	EMISSION = COLOR.rgb * (edge + 0.22 * inset) * glow_strength * mix(0.12, 1.0, health) * flicker;
}
