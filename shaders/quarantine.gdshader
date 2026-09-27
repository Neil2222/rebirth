shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// A soft purple shimmer around a quarantined machine: drifting bands and a slow pulse.
void fragment() {
	float rim = 1.0 - abs(dot(NORMAL, VIEW));
	float bands = 0.5 + 0.5 * sin((UV.y * 12.0) - TIME * 2.5);
	float pulse = 0.6 + 0.4 * sin(TIME * 2.0);
	ALBEDO = vec3(0.62, 0.35, 0.95) * (0.08 + 0.35 * rim * rim + 0.12 * bands) * pulse;
}
