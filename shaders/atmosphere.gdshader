shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;

// Thin atmosphere seen from space: a soft haze around the rim that fades towards the planet's face.
uniform vec3 tint : source_color = vec3(1.0, 0.72, 0.52); // warm dusty haze
uniform float strength = 0.7;

void fragment() {
	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = tint * pow(rim, 3.0) * strength;
}
