shader_type spatial;

// Forge hangar floor: dark with a neon grid on block boundaries, fading into the distance.
uniform vec3 line_color : source_color = vec3(0.1, 0.85, 1.0);
uniform float cell = 2.5;
uniform float fade_radius = 60.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Block centers sit on multiples of `cell`, so boundaries are offset by half a cell.
	vec2 p = world_position.xz / cell + 0.5;
	vec2 d = abs(fract(p) - 0.5);
	vec2 width = max(fwidth(p) * 1.5, vec2(1e-4));
	vec2 lines = smoothstep(0.5 - width, vec2(0.5), d);
	float grid = max(lines.x, lines.y);
	// Every 4th line is brighter, like a blueprint sheet.
	vec2 major_p = world_position.xz / (cell * 4.0) + 0.5 / 4.0;
	vec2 major_d = abs(fract(major_p) - 0.5);
	vec2 major = smoothstep(0.5 - fwidth(major_p) * 1.5 - 1e-4, vec2(0.5), major_d);
	float fade = 1.0 - smoothstep(fade_radius * 0.4, fade_radius, length(world_position.xz));
	ALBEDO = vec3(0.004, 0.008, 0.014);
	// Matte: a glossy floor turns the key light into a glaring streak behind the design.
	ROUGHNESS = 0.95;
	SPECULAR = 0.1;
	EMISSION = line_color * (grid * 0.5 + max(major.x, major.y) * 1.2) * fade;
}
