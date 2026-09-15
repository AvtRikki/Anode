using Silk.NET.OpenGL;

namespace Ecad.Rendering.OpenGL;

public enum GlslDialect
{
    /// <summary>OpenGL 3.3+ core (Linux, Windows WGL, macOS 4.1 core).</summary>
    Desktop330,

    /// <summary>OpenGL 3.2 core.</summary>
    Desktop150,

    /// <summary>OpenGL ES 3.0 (ANGLE on Windows).</summary>
    Es300,
}

/// <summary>
/// Shaders work in scene millimetres. Strokes and discs are analytic signed-distance shapes on instanced quads,
/// which gives round caps and one-pixel anti-aliasing without tessellation.
/// </summary>
internal static class GlShaders
{
    // Attribute locations shared by all programs.
    public const uint Corner = 0;
    public const uint Attr1 = 1;
    public const uint Attr2 = 2;
    public const uint Attr3 = 3;

    private const string Common = """
        uniform vec2 u_center;
        uniform vec2 u_scale;
        uniform vec2 u_halfViewport;
        uniform float u_devPxPerMm;

        vec4 toClip(vec2 p)
        {
            return vec4((p.x - u_center.x) * u_scale.x / u_halfViewport.x,
                        -(p.y - u_center.y) * u_scale.y / u_halfViewport.y, 0.0, 1.0);
        }
        """;

    public const string SegmentVertex = Common + """

        in vec2 a_corner;
        in vec2 a_a;
        in vec2 a_b;
        in float a_hw;
        out vec2 v_p;
        flat out vec2 v_a;
        flat out vec2 v_b;
        flat out float v_hw;

        void main()
        {
            float px = 1.0 / u_devPxPerMm;
            float hw = max(a_hw, 0.5 * px);
            vec2 d = a_b - a_a;
            float len = length(d);
            vec2 dir = len > 0.0 ? d / len : vec2(1.0, 0.0);
            vec2 nrm = vec2(-dir.y, dir.x);
            float r = hw + px;
            vec2 p = mix(a_a - dir * r, a_b + dir * r, a_corner.x) + nrm * (a_corner.y * r);
            v_p = p;
            v_a = a_a;
            v_b = a_b;
            v_hw = hw;
            gl_Position = toClip(p);
        }
        """;

    public const string SegmentFragment = """
        uniform vec4 u_color;
        uniform float u_devPxPerMm;
        in vec2 v_p;
        flat in vec2 v_a;
        flat in vec2 v_b;
        flat in float v_hw;
        out vec4 fragColor;

        void main()
        {
            vec2 pa = v_p - v_a;
            vec2 ba = v_b - v_a;
            float dd = dot(ba, ba);
            float h = dd > 0.0 ? clamp(dot(pa, ba) / dd, 0.0, 1.0) : 0.0;
            float dist = length(pa - ba * h) - v_hw;
            float alpha = clamp(0.5 - dist * u_devPxPerMm, 0.0, 1.0);
            if (alpha <= 0.0) discard;
            fragColor = vec4(u_color.rgb, u_color.a * alpha);
        }
        """;

    public const string CircleVertex = Common + """

        in vec2 a_corner;
        in vec2 a_c;
        in float a_r;
        out vec2 v_p;
        flat out vec2 v_c;
        flat out float v_r;

        void main()
        {
            float px = 1.0 / u_devPxPerMm;
            float r = max(a_r, 0.5 * px);
            float extent = r + px;
            vec2 p = a_c + vec2(a_corner.x * 2.0 - 1.0, a_corner.y) * extent;
            v_p = p;
            v_c = a_c;
            v_r = r;
            gl_Position = toClip(p);
        }
        """;

    public const string CircleFragment = """
        uniform vec4 u_color;
        uniform float u_devPxPerMm;
        in vec2 v_p;
        flat in vec2 v_c;
        flat in float v_r;
        out vec4 fragColor;

        void main()
        {
            float dist = length(v_p - v_c) - v_r;
            float alpha = clamp(0.5 - dist * u_devPxPerMm, 0.0, 1.0);
            if (alpha <= 0.0) discard;
            fragColor = vec4(u_color.rgb, u_color.a * alpha);
        }
        """;

    public const string FillVertex = Common + """

        in vec2 a_pos;

        void main()
        {
            gl_Position = toClip(a_pos);
        }
        """;

    public const string FillFragment = """
        uniform vec4 u_color;
        out vec4 fragColor;

        void main()
        {
            fragColor = u_color;
        }
        """;

    public static GlProgram Build(GL gl, GlslDialect dialect, string vertex, string fragment, params (uint Location, string Name)[] attributes)
    {
        string header = dialect switch
        {
            GlslDialect.Es300 => "#version 300 es\nprecision highp float;\n",
            GlslDialect.Desktop150 => "#version 150\n",
            _ => "#version 330 core\n",
        };

        uint vs = CompileShader(gl, ShaderType.VertexShader, header + vertex);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, header + fragment);
        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        foreach (var (location, name) in attributes)
        {
            gl.BindAttribLocation(program, location, name);
        }

        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        return new GlProgram(gl, program);
    }

    private static uint CompileShader(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }

        return shader;
    }
}

internal sealed class GlProgram(GL gl, uint handle) : IDisposable
{
    public uint Handle { get; } = handle;

    public int Center { get; } = gl.GetUniformLocation(handle, "u_center");

    public int Scale { get; } = gl.GetUniformLocation(handle, "u_scale");

    public int HalfViewport { get; } = gl.GetUniformLocation(handle, "u_halfViewport");

    public int DevPxPerMm { get; } = gl.GetUniformLocation(handle, "u_devPxPerMm");

    public int Color { get; } = gl.GetUniformLocation(handle, "u_color");

    public void Dispose() => gl.DeleteProgram(Handle);
}
