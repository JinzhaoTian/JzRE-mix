$input v_normal

#include <bgfx_shader.sh>

void main()
{
    vec3 N = normalize(v_normal);
    vec3 L = normalize(vec3(0.4, 0.5, 1.0));
    float diff = max(dot(N, L), 0.0);
    vec3 base = vec3(0.82, 0.72, 0.60);
    vec3 color = base * (0.25 + 0.75 * diff);
    gl_FragColor = vec4(color, 1.0);
}
