shader_type spatial;

// Per-face UVs run 0..1, so darkening near the UV border outlines every block face.
uniform float edge_width = 0.035;

void fragment() {
	vec2 d = min(UV, 1.0 - UV);
	float edge = 1.0 - smoothstep(edge_width * 0.5, edge_width, min(d.x, d.y));
	ALBEDO = COLOR.rgb * mix(1.0, 0.45, edge);
	METALLIC = 0.35;
	ROUGHNESS = mix(0.65, 0.85, edge);
}
