// Radial “corruption” burst: suggests a civilian at the center being taken over; expands and fades.
shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 c = UV - vec2(0.5, 0.5);
	// Slightly oval (seen from game angle)
	c.y *= 0.75;
	float r = length(c) * 2.0;
	float expand = progress * 1.15;
	// Outward-sharp ring
	float ring = smoothstep(expand - 0.2, expand - 0.04, r) * (1.0 - smoothstep(expand + 0.02, expand + 0.2, r));
	// Tighter inner flare at the start
	float core = (1.0 - smoothstep(0.0, 0.18 + progress * 0.1, r)) * (1.0 - progress) * 1.4;
	float wobble = sin(r * 28.0 - progress * 10.0) * 0.1 + 1.0;
	float a = (ring * wobble + core * 0.9) * (1.0 - 0.4 * progress);
	// Maroon + sick magenta; reads as “turned” / hostile
	vec3 in_col = mix(vec3(0.45, 0.06, 0.2), vec3(0.75, 0.12, 0.3), 1.0 - r);
	vec3 out_col = mix(vec3(0.35, 0.04, 0.45), vec3(0.7, 0.18, 0.6), r);
	vec3 col = mix(in_col, out_col, 0.5);
	COLOR = vec4(col, clamp(a, 0.0, 1.0) * 0.9);
}
