shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, shadows_disabled, blend_mix;

// The Box: a vast, soft lattice around the whole star cluster, seen from the inside. Faint lines in
// the sky that remind you where the Curator drew the border. `hole` opens it where the Lance struck.
uniform vec3 line_color : source_color = vec3(0.86, 0.78, 1.0);
uniform float opacity = 0.035;   // barely there: a hint, not a cage
uniform vec3 hole_dir = vec3(0.0, 1.0, 0.0);
uniform float hole = 0.0;          // 0 closed .. 1 wide open
uniform vec3 light_color : source_color = vec3(1.0, 0.95, 0.82);

varying vec3 local_dir;

void vertex() {
	local_dir = VERTEX;
}

void fragment() {
	float len = length(local_dir);
	vec3 d = len > 0.0001 ? local_dir / len : vec3(0.0, 1.0, 0.0);
	vec2 g = vec2(atan(d.z, d.x) / 6.2831853 * 20.0, asin(clamp(d.y, -1.0, 1.0)) / 3.1415926 * 10.0);
	vec2 w = max(fwidth(g), vec2(0.0001));
	vec2 grid = abs(fract(g - 0.5) - 0.5) / w;
	float line = 1.0 - clamp(min(grid.x, grid.y) - 0.6, 0.0, 1.0);

	float hole_len = length(hole_dir);
	vec3 h = hole_len > 0.0001 ? hole_dir / hole_len : vec3(0.0, 1.0, 0.0);
	float c = dot(d, h);
	float edge = cos(hole * 0.32);
	float open = hole > 0.001 ? smoothstep(edge - 0.0015, edge + 0.0015, c) : 0.0;
	float rim = hole > 0.001 ? exp(-abs(c - edge) * 900.0) : 0.0;

	ALBEDO = mix(line_color, light_color * 1.6, max(open, rim));
	ALPHA = clamp(max(line * opacity, max(open * 0.97, rim)), 0.0, 1.0);
}
