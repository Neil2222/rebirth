shader_type spatial;

// Forge workbench: a sage-green cutting mat with light ruler lines on block boundaries and a
// bolder line every four blocks, fading into the warm workshop around it.
uniform vec3 mat_color : source_color = vec3(0.42, 0.58, 0.50);
uniform vec3 line_color : source_color = vec3(0.86, 0.93, 0.86);
uniform vec3 surround : source_color = vec3(0.93, 0.87, 0.78);
uniform float cell = 2.5;
uniform float fade_radius = 55.0;

varying vec3 world_position;

float grid_line(vec2 p, float width_px) {
	vec2 d = abs(fract(p) - 0.5);
	vec2 w = max(fwidth(p) * width_px, vec2(1e-4));
	vec2 l = smoothstep(0.5 - w, vec2(0.5), d);
	return max(l.x, l.y);
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Block centers sit on multiples of `cell`, so boundaries are offset by half a cell.
	vec2 p = world_position.xz / cell + 0.5;
	float minor = grid_line(p, 1.2);
	float major = grid_line(p / 4.0 + 0.5 / 4.0, 2.0);
	vec3 mat = mix(mat_color, line_color, max(minor * 0.45, major * 0.8));
	float fade = smoothstep(fade_radius * 0.5, fade_radius, length(world_position.xz));
	ALBEDO = mix(mat, surround, fade);
	ROUGHNESS = 0.95;
	SPECULAR = 0.15;
}
