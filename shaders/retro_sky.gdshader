shader_type sky;

// A soft, storybook space: dusky plum overhead, a warm peach glow around the sun, teal on the far
// side, faint nebula clouds and gentle stars. Bright enough to light the scene softly.
uniform vec3 plum : source_color = vec3(0.20, 0.15, 0.34);
uniform vec3 peach : source_color = vec3(1.00, 0.70, 0.48);
uniform vec3 teal : source_color = vec3(0.12, 0.34, 0.42);
uniform vec3 rose : source_color = vec3(0.85, 0.45, 0.60);
uniform float star_scale = 180.0;
uniform float star_density = 0.004;

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

float noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float n000 = hash13(i), n100 = hash13(i + vec3(1, 0, 0));
	float n010 = hash13(i + vec3(0, 1, 0)), n110 = hash13(i + vec3(1, 1, 0));
	float n001 = hash13(i + vec3(0, 0, 1)), n101 = hash13(i + vec3(1, 0, 1));
	float n011 = hash13(i + vec3(0, 1, 1)), n111 = hash13(i + vec3(1, 1, 1));
	return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
		mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float toward_sun = LIGHT0_ENABLED ? dot(dir, LIGHT0_DIRECTION) : 0.0;

	vec3 col = mix(teal, plum, smoothstep(-0.9, 0.2, toward_sun));
	col = mix(col, peach * 0.75, pow(max(toward_sun, 0.0), 4.0));

	// Soft nebula clouds in rose and peach.
	float clouds = noise3(dir * 2.2) * 0.6 + noise3(dir * 5.1 + 7.0) * 0.4;
	col = mix(col, rose * 0.55, smoothstep(0.55, 0.85, clouds) * 0.45);

	// Gentle round stars, dimmer near the sun's glow.
	if (!AT_CUBEMAP_PASS) {
		vec3 p = dir * star_scale;
		vec3 cell = floor(p);
		if (hash13(cell) > 1.0 - star_density) {
			vec3 center = cell + 0.5 + (vec3(hash13(cell + 1.7), hash13(cell + 3.1), hash13(cell + 5.3)) - 0.5) * 0.5;
			float star = smoothstep(0.35, 0.0, length(p - center));
			col += vec3(1.0, 0.95, 0.85) * star * (0.4 + 0.6 * hash13(cell + 9.0)) * (1.0 - max(toward_sun, 0.0));
		}
	}

	// A big soft sun.
	if (LIGHT0_ENABLED) {
		col += LIGHT0_COLOR * (smoothstep(0.9975, 0.999, toward_sun) * 2.5 + pow(max(toward_sun, 0.0), 60.0) * 0.35);
	}
	COLOR = col;
}
