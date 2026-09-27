shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// Nexus flow line: a soft tube with dashes travelling from source (UV.y = 1 end) to destination.
uniform vec3 tint : source_color = vec3(1.0, 0.85, 0.45);
uniform float length = 100.0;   // metres, so dashes keep their size on long routes
uniform float busy = 0.0;       // 1 while a bot is on the route

void fragment() {
	float along = (1.0 - UV.y) * length;
	float dash = step(0.5, fract(along / 6.0 - TIME * (0.6 + busy * 0.9)));
	float base = 0.18 + busy * 0.12;
	ALBEDO = tint * (base + dash * (0.35 + busy * 0.4));
}
