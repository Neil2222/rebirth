shader_type spatial;

// Low-detail planet surface. Within near_radius of the camera the streamed voxel chunks
// take over, so this mesh gets out of the way there.
uniform float near_radius = 126.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (distance(world_position, CAMERA_POSITION_WORLD) < near_radius) {
		discard;
	}
	ALBEDO = COLOR.rgb; // linear vertex colours
	ROUGHNESS = 0.9;
}
