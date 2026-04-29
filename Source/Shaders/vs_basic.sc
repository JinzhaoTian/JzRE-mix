$input a_position, a_normal
$output v_normal

#include <bgfx_shader.sh>

uniform mat4 u_mvp;
uniform mat4 u_normalMtx;

void main()
{
    gl_Position = mul(u_mvp, vec4(a_position, 1.0));
    v_normal = mul(u_normalMtx, vec4(a_normal, 0.0)).xyz;
}
