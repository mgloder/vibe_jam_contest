// Additive “beam” along the card’s X axis: bright at x≈0 (shooter) fading toward the target.
shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float t : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	// 0 = near shooter, 1 = impact end
	float x = 1.0 - UV.x;
	float core = pow(x, 0.35) * 0.55;
	float tip = (1.0 - UV.x) * (1.0 - t) * 0.4;
	float a = (core + tip) * (1.0 - 0.7 * t);
	vec3 c = mix(vec3(0.3, 0.55, 1.0), mix(vec3(1.0, 0.75, 0.25), vec3(1.0, 0.3, 0.1), UV.x), 0.5 + 0.5 * UV.x);
	COLOR = vec4(c, clamp(a, 0.0, 1.0) * 0.75);
}
