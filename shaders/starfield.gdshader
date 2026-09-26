shader_type sky;

uniform float star_scale = 300.0;
uniform float star_density = 0.004;

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	vec3 p = dir * star_scale;
	vec3 cell = floor(p);
	vec3 col = vec3(0.0);

	if (hash13(cell) > 1.0 - star_density) {
		vec3 jitter = vec3(hash13(cell + 1.7), hash13(cell + 3.1), hash13(cell + 5.3)) - 0.5;
		vec3 center = cell + 0.5 + jitter * 0.5;
		float brightness = smoothstep(0.45, 0.0, length(p - center));
		vec3 tint = mix(vec3(0.7, 0.8, 1.0), vec3(1.0, 0.85, 0.7), hash13(cell + 9.1));
		col += tint * brightness * (0.6 + 2.0 * hash13(cell + 11.0));
	}

	// Faint cyan/magenta haze so space isn't pure black.
	col += mix(vec3(0.020, 0.004, 0.030), vec3(0.000, 0.018, 0.028), 0.5 + 0.5 * dir.y);

	if (LIGHT0_ENABLED) {
		float s = max(dot(dir, LIGHT0_DIRECTION), 0.0);
		col += LIGHT0_COLOR * (pow(s, 3000.0) * 6.0 + pow(s, 80.0) * 0.15);
	}

	COLOR = col;
}
