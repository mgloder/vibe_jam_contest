// Short additive muzzle flash at the shooter’s cell (expert / soldier).
shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float t : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 c = UV - vec2(0.5, 0.5);
	// Slight anisotropy: shot feels more horizontal
	c.x *= 0.9;
	c.y *= 1.1;
	float r = length(c) * 2.0;
	float ang = atan(c.y, c.x);
	// A few “spokes” + hot core
	float spokes = 0.65 + 0.35 * abs(sin(ang * 4.0 + t * 3.0));
	float core = 1.0 - smoothstep(0.0, 0.22, r);
	float halo = (1.0 - smoothstep(0.15, 0.65, r)) * spokes;
	float a = (core * 1.1 + halo * 0.55) * (1.0 - t);
	vec3 hot = mix(vec3(1.0, 0.92, 0.5), vec3(1.0, 0.45, 0.15), r * 1.4);
	vec3 cool = mix(vec3(0.2, 0.35, 0.6), hot, 0.85);
	COLOR = vec4(cool, clamp(a, 0.0, 1.0));
}
