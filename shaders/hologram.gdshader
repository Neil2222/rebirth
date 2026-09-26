shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// A design being printed: translucent scanlines, filled in from the bottom up to `progress`,
// with a bright band where the print head currently is.
uniform vec3 tint : source_color = vec3(0.62, 0.35, 1.0);
uniform float progress = 0.0;
uniform vec3 base_point;   // world position of the model's bottom
uniform vec3 up_dir = vec3(0.0, 1.0, 0.0);
uniform float height = 1.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float h = dot(world_position - base_point, up_dir) / max(height, 0.001);
	float head = 1.0 - smoothstep(0.0, 0.025, abs(h - progress));
	// Above the print head only a faint outline of what is still to come.
	float built = step(h, progress);
	float scan = 0.6 + 0.4 * step(0.5, fract(dot(world_position, up_dir) * 3.0 - TIME * 0.8));
	ALBEDO = tint * (built * 0.35 * scan + (1.0 - built) * 0.04) + tint * head * 3.0;
}
