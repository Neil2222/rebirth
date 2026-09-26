shader_type spatial;

// Parcels in tubes: glossy candy-like pods in the item's colour (per-instance COLOR), with a gentle
// inner glow so they read clearly through the glass.
void fragment() {
	vec3 color = COLOR.rgb;
	ALBEDO = color;
	ROUGHNESS = 0.25;
	SPECULAR = 0.6;
	RIM = 0.4;
	EMISSION = color * 0.35;
}
