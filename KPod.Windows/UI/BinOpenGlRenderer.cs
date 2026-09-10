using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using KPod.Core.Models;
using Silk.NET.OpenGL;

namespace KPod.Windows.UI;

internal sealed record BinRenderOptions(
    bool Textures, bool Wireframe, bool Grid, bool Smooth, bool Lighting,
    Vector3 LightPosition, Color Background);

/// <summary>One uploadable frame: a decoded model and the textures resolved for it.</summary>
internal sealed record BinFrameGeometry(BinModel Model, IReadOnlyList<LoadedBinTexture> Textures);

/// <summary>GPU renderer matching the controls and material behavior of JSPod's BIN preview.</summary>
internal sealed unsafe class BinOpenGlRenderer : IDisposable
{
    private readonly GL _gl;
    // One list of meshes per animation frame; an ordinary model is simply one frame.
    // Every frame is uploaded once so that stepping the animation costs nothing on the
    // GPU and never disturbs the grid or the camera.
    private readonly List<List<GpuMesh>> _frames = [];
    private int _frameIndex;
    private uint _program;
    private uint _gridVao;
    private uint _gridVbo;
    private int _gridVertices;
    private float _modelSize = 1;
    private float _yaw;
    private float _pitch = 0.32f;
    private float _distance = 3;
    private Vector3 _target;
    private Vector3 _modelCenter;

    internal BinOpenGlRenderer(GL gl)
    {
        _gl = gl;
        _program = BuildProgram();
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Front);
    }

    internal void SetModel(BinModel model, IReadOnlyList<LoadedBinTexture> textures, bool smooth) =>
        SetFrames([new BinFrameGeometry(model, textures)], smooth);

    /// <summary>
    /// Uploads every animation frame at once and fits the grid and the camera to all of
    /// them together.
    ///
    /// <para>Fitting the union rather than each frame in turn is what stops the ground
    /// plane and the framing from jumping as a T-rex swings through its poses, and
    /// uploading once is what lets <see cref="SelectFrame"/> be a single assignment.</para>
    /// </summary>
    internal void SetFrames(IReadOnlyList<BinFrameGeometry> frames, bool smooth, bool resetView = true)
    {
        ClearMeshes();
        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        foreach (BinFrameGeometry frame in frames)
        {
            Dictionary<string, LoadedBinTexture> textureMap = [];
            foreach (LoadedBinTexture texture in frame.Textures)
            {
                textureMap[BinTextureResolver.Stem(texture.Name)] = texture;
            }

            /*
              .BIN is authored Z-up and is swapped into view space; a 4x4 Evolution .SMF is
              already Y-up and must be left alone. Its texture V likewise runs top-down,
              which is the opposite of what the .BIN upload flip assumes. The model states
              both, so neither is inferred here.
            */
            bool swapAxes = !string.Equals(frame.Model.UpAxis, "Y", StringComparison.Ordinal);
            bool flipTexture = !string.Equals(frame.Model.UvOrigin, "top-left", StringComparison.Ordinal);

            List<GpuMesh> meshes = [];
            foreach (BinMesh mesh in frame.Model.Meshes)
            {
                textureMap.TryGetValue(BinTextureResolver.Stem(mesh.TextureName), out LoadedBinTexture? texture);
                meshes.Add(UploadMesh(mesh, texture, smooth, swapAxes, flipTexture));
                for (int i = 0; i + 2 < mesh.Positions.Count; i += 3)
                {
                    Vector3 point = swapAxes
                        ? new Vector3(mesh.Positions[i], mesh.Positions[i + 2], -mesh.Positions[i + 1])
                        : new Vector3(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]);
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            }
            _frames.Add(meshes);
        }

        _frameIndex = Math.Min(_frameIndex, Math.Max(0, _frames.Count - 1));
        _modelCenter = minimum.X == float.MaxValue ? Vector3.Zero : (minimum + maximum) * 0.5f;
        _modelSize = minimum.X == float.MaxValue ? 1 : Math.Max(1, (maximum - minimum).Length());
        BuildGrid(_modelSize, _modelCenter.X, minimum.X == float.MaxValue ? 0 : minimum.Y, _modelCenter.Z);
        // A palette change re-uploads the same geometry, and throwing the view away
        // there is the same jolt as throwing it away between animation frames.
        if (resetView) ResetView();
    }

    /// <summary>Draws a different frame. No GPU work, and the view is left alone.</summary>
    internal void SelectFrame(int index)
    {
        if (_frames.Count == 0) return;
        _frameIndex = ((index % _frames.Count) + _frames.Count) % _frames.Count;
    }

    internal void SetSmoothing(bool smooth)
    {
        foreach (GpuMesh mesh in _frames.SelectMany(frame => frame))
        {
            SetTextureFilter(mesh.Diffuse, smooth);
            SetTextureFilter(mesh.Normal, smooth);
        }
    }

    internal void ResetView()
    {
        _yaw = 0; _pitch = 0.32f; _target = _modelCenter; _distance = _modelSize * 1.6f;
    }

    internal void Orbit(float dx, float dy)
    {
        _yaw += dx * 0.01f;
        _pitch = Math.Max(-1.45f, Math.Min(1.45f, _pitch + (dy * 0.01f)));
    }

    internal void Zoom(int wheelDelta) =>
        _distance = Math.Max(_modelSize * 0.08f, _distance * (wheelDelta > 0 ? 0.88f : 1.14f));

    internal void Strafe(float direction)
    {
        Vector3 right = new((float)Math.Cos(_yaw), 0, -(float)Math.Sin(_yaw));
        _target += right * (_distance * 0.04f * direction);
    }

    /// <summary>Vertical field of view, radians.</summary>
    private const float FieldOfView = 48f * ((float)Math.PI / 180f);

    /// <summary>
    /// The row-vector view-projection for a camera at <paramref name="eye"/> looking at
    /// <paramref name="target"/>. The projection is built here rather than taken from
    /// Matrix4x4.CreatePerspectiveFieldOfView because that one maps depth to the 0..1
    /// range Direct3D clips against; OpenGL clips against -1..1, so half the depth buffer
    /// would go unused and precision would be thrown away on models whose surfaces sit
    /// fractions of a unit apart.
    /// </summary>
    internal static Matrix4x4 ViewProjection(Vector3 eye, Vector3 target, float aspect, float near, float far)
    {
        float focal = 1f / (float)Math.Tan(FieldOfView / 2f);
        Matrix4x4 projection = new()
        {
            M11 = focal / aspect,
            M22 = focal,
            M33 = (far + near) / (near - far),
            M34 = -1,
            M43 = 2 * far * near / (near - far),
        };
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * projection;
    }

    internal void Render(int width, int height, BinRenderOptions options)
    {
        if (width < 1 || height < 1) return;
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(options.Background.R / 255f, options.Background.G / 255f, options.Background.B / 255f, 1);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        _gl.UseProgram(_program);

        float cosPitch = (float)Math.Cos(_pitch);
        Vector3 eye = _target + new Vector3(
            (float)Math.Sin(_yaw) * cosPitch * _distance,
            (float)Math.Sin(_pitch) * _distance,
            (float)Math.Cos(_yaw) * cosPitch * _distance);
        // Far tracks the camera instead of a fixed multiple of the model, so the depth
        // range stays tight enough for the thin coplanar plates these models are made of.
        float near = Math.Max(0.001f, _modelSize * 0.01f);
        float far = _distance + (_modelSize * 4f);
        SetMatrix("uViewProjection", ViewProjection(eye, _target, width / (float)height, near, far));
        SetVector("uCamera", eye);
        SetVector("uLight", options.LightPosition * _modelSize);
        SetInt("uLighting", options.Lighting ? 1 : 0);

        if (options.Grid && _gridVao != 0)
        {
            ConfigureOpaque();
            SetInt("uUseTexture", 0); SetInt("uUseNormal", 0); SetInt("uAlphaCutout", 0);
            SetVector("uTint", new Vector3(0.25f, 0.25f, 0.29f)); SetFloat("uOpacity", 1); SetFloat("uEmissive", 0);
            _gl.BindVertexArray(_gridVao); _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertices);
        }

        foreach (GpuMesh mesh in _frames.Count == 0 ? [] : _frames[_frameIndex])
        {
            if ((mesh.Source.Material?.Flags & 0x2000) != 0) DrawMesh(mesh, options, solidPass: true);
            DrawMesh(mesh, options, solidPass: false);
            if (options.Wireframe)
            {
                ConfigureOpaque();
                _gl.PolygonMode(TriangleFace.FrontAndBack, Silk.NET.OpenGL.PolygonMode.Line);
                SetInt("uUseTexture", 0); SetInt("uUseNormal", 0); SetInt("uAlphaCutout", 0);
                SetVector("uTint", new Vector3(1, 0.9f, 0)); SetFloat("uOpacity", 1); SetFloat("uEmissive", 1);
                _gl.BindVertexArray(mesh.Vao); _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.VertexCount);
                _gl.PolygonMode(TriangleFace.FrontAndBack, Silk.NET.OpenGL.PolygonMode.Fill);
            }
        }
        _gl.BindVertexArray(0);
    }

    private void DrawMesh(GpuMesh mesh, BinRenderOptions options, bool solidPass)
    {
        BinMaterial? material = mesh.Source.Material;
        uint flags = material?.Flags ?? 0;
        /*
          An .SMF states transparency two ways and its material flag is the weaker one: every
          stock Evo vegetation model writes that flag as 0 while naming a two-sample .TIF
          whose second sample is opacity. An alpha channel in the art is the material intent.
        */
        bool alphaTest = solidPass
            || (material is not null ? (flags & 0x0008) != 0 : mesh.Source.Transparent || mesh.HasTextureAlpha);
        bool blend = !solidPass && material is not null && (flags & 0x0004) != 0 && !alphaTest;
        bool additive = !solidPass && (flags & 0x0010) != 0;
        if (blend || additive)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, additive ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
        }
        else _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(solidPass || alphaTest || (flags & 0x0100) == 0);
        // .SMF sheets are single-sided quads meant to be seen from behind, so the decoder
        // marks them double-sided; .BIN relies on its material's TWOSIDED flag instead.
        if (mesh.Source.DoubleSided || (flags & 0x0080) != 0) _gl.Disable(EnableCap.CullFace);
        else _gl.Enable(EnableCap.CullFace);

        bool useTexture = options.Textures && mesh.Diffuse != 0;
        bool useNormal = useTexture && options.Lighting && mesh.Normal != 0;
        SetInt("uUseTexture", useTexture ? 1 : 0); SetInt("uUseNormal", useNormal ? 1 : 0);
        SetInt("uAlphaCutout", alphaTest ? 1 : 0);
        bool needsRawCutout = mesh.IsRaw && (mesh.Source.Transparent || (flags & (0x0004 | 0x0008 | 0x2000)) != 0);
        SetInt("uRawCutout", needsRawCutout ? 1 : 0);
        SetFloat("uAlphaReference", solidPass ? 0.5f : ((flags & 0x0800) != 0 ? (material?.AlphaReference ?? 128) / 255f : 0.5f));
        SetFloat("uOpacity", blend ? Clamp(material?.BaseAlpha ?? 1) : 1);
        SetFloat("uShininess", Math.Max(1, material?.SpecPower ?? 1));
        SetFloat("uEmissive", (flags & 0x0200) != 0 ? Clamp(material?.Emissive ?? 0) : 0);
        SetFloat("uNormalStrength", mesh.Source.Material2?.NormalStrength ?? 1);
        Vector3 tint = (flags & 0x0400) != 0
            ? new Vector3(material!.TintR, material.TintG, material.TintB)
            : useTexture ? Vector3.One : ColorVector(mesh.Source.Color);
        SetVector("uTint", tint);
        SetInt("uLighting", options.Lighting && (material is null || (flags & 1) != 0) ? 1 : 0);

        _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, mesh.Diffuse); SetInt("uDiffuse", 0);
        _gl.ActiveTexture(TextureUnit.Texture1); _gl.BindTexture(TextureTarget.Texture2D, mesh.Normal); SetInt("uNormalMap", 1);
        _gl.BindVertexArray(mesh.Vao); _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.VertexCount);
    }

    private void ConfigureOpaque()
    {
        _gl.Disable(EnableCap.Blend); _gl.Enable(EnableCap.CullFace); _gl.DepthMask(true);
    }

    private GpuMesh UploadMesh(BinMesh source, LoadedBinTexture? texture, bool smooth,
        bool swapAxes, bool flipTexture)
    {
        int vertices = source.Positions.Count / 3;
        float[] interleaved = new float[vertices * 8];
        for (int i = 0; i < vertices; i++)
        {
            int p = i * 3, o = i * 8, uv = i * 2;
            if (swapAxes)
            {
                interleaved[o] = source.Positions[p]; interleaved[o + 1] = source.Positions[p + 2]; interleaved[o + 2] = -source.Positions[p + 1];
                interleaved[o + 3] = source.Normals[p]; interleaved[o + 4] = source.Normals[p + 2]; interleaved[o + 5] = -source.Normals[p + 1];
            }
            else
            {
                interleaved[o] = source.Positions[p]; interleaved[o + 1] = source.Positions[p + 1]; interleaved[o + 2] = source.Positions[p + 2];
                interleaved[o + 3] = source.Normals[p]; interleaved[o + 4] = source.Normals[p + 1]; interleaved[o + 5] = source.Normals[p + 2];
            }
            interleaved[o + 6] = SnapUv(source.TextureCoordinates[uv], texture?.Diffuse?.Width);
            interleaved[o + 7] = SnapUv(source.TextureCoordinates[uv + 1], texture?.Diffuse?.Height);
        }
        uint vao = _gl.GenVertexArray(), vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* data = interleaved)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(interleaved.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        const uint stride = 8 * sizeof(float);
        _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2); _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        return new GpuMesh(source, vao, vbo, vertices,
            texture?.Diffuse is null ? 0 : UploadTexture(texture.Diffuse, smooth, flipTexture),
            texture?.Normal is null ? 0 : UploadTexture(texture.Normal, smooth, flipTexture),
            // A .RAW that has had an .OPA merged into it carries real alpha, so it must not
            // also be run through the colour-key cutout the MTM family needs.
            texture?.IsRaw == true && texture?.HasAlpha != true)
        {
            HasTextureAlpha = texture?.HasAlpha == true,
        };
    }

    private uint UploadTexture(Bitmap bitmap, bool smooth, bool flipY = true)
    {
        using Bitmap argb = bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb ? new Bitmap(bitmap) : bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        if (flipY) argb.RotateFlip(RotateFlipType.RotateNoneFlipY);
        BitmapData bits = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            uint id = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture2D, id);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)argb.Width, (uint)argb.Height, 0,
                Silk.NET.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, bits.Scan0.ToPointer());
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            SetTextureFilter(id, smooth);
            return id;
        }
        finally { argb.UnlockBits(bits); }
    }

    private void SetTextureFilter(uint id, bool smooth)
    {
        if (id == 0) return;
        _gl.BindTexture(TextureTarget.Texture2D, id);
        int filter = (int)(smooth ? TextureMinFilter.Linear : TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
    }

    private void BuildGrid(float size, float centerX, float height, float centerZ)
    {
        if (_gridVao != 0) { _gl.DeleteVertexArray(_gridVao); _gl.DeleteBuffer(_gridVbo); }
        int divisions = Math.Max(10, Math.Min(40, (int)Math.Ceiling(size * 2)));
        List<float> data = [];
        for (int i = 0; i <= divisions; i++)
        {
            float p = -size * 1.5f + ((size * 3) * i / divisions);
            AddGridVertex(data, centerX + p, height, centerZ - (size * 1.5f));
            AddGridVertex(data, centerX + p, height, centerZ + (size * 1.5f));
            AddGridVertex(data, centerX - (size * 1.5f), height, centerZ + p);
            AddGridVertex(data, centerX + (size * 1.5f), height, centerZ + p);
        }
        _gridVertices = data.Count / 8; _gridVao = _gl.GenVertexArray(); _gridVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_gridVao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        float[] array = data.ToArray();
        fixed (float* pointer = array) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(array.Length * sizeof(float)), pointer, BufferUsageARB.StaticDraw);
        const uint stride = 8 * sizeof(float);
        _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2); _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
    }

    private static void AddGridVertex(List<float> data, float x, float y, float z) => data.AddRange([x, y, z, 0, 1, 0, 0, 0]);
    private static float SnapUv(float value, int? size)
    {
        float clamped = Math.Max(0, Math.Min(1, value));
        if (size is null or <= 1) return clamped;
        float texel = ((float)Math.Floor(clamped * (size.Value - 1)) + 0.5f) / size.Value;
        return Math.Max(0.5f / size.Value, Math.Min(1 - (0.5f / size.Value), texel));
    }

    private uint BuildProgram()
    {
        uint vertex = Compile(ShaderType.VertexShader, VertexShader);
        uint fragment = Compile(ShaderType.FragmentShader, FragmentShader);
        uint program = _gl.CreateProgram(); _gl.AttachShader(program, vertex); _gl.AttachShader(program, fragment); _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        string log = _gl.GetProgramInfoLog(program);
        _gl.DeleteShader(vertex); _gl.DeleteShader(fragment);
        if (linked == 0) throw new NotSupportedException("Could not link the BIN preview shader: " + log);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type); _gl.ShaderSource(shader, source); _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0) throw new NotSupportedException("Could not compile the BIN preview shader: " + _gl.GetShaderInfoLog(shader));
        return shader;
    }

    private void SetInt(string name, int value) => _gl.Uniform1(_gl.GetUniformLocation(_program, name), value);
    private void SetFloat(string name, float value) => _gl.Uniform1(_gl.GetUniformLocation(_program, name), value);
    private void SetVector(string name, Vector3 value) => _gl.Uniform3(_gl.GetUniformLocation(_program, name), value.X, value.Y, value.Z);
    /// <summary>
    /// Uploads without transposing, which is what pairs a System.Numerics matrix with a
    /// GLSL one. System.Numerics is row-vector (p * M) and stores rows first; GLSL is
    /// column-vector (M * p) and reads columns first. Handing the same bytes over
    /// untransposed reinterprets rows as columns, and that reinterpretation is exactly
    /// the transpose the change of convention needs. Asking GL to transpose as well
    /// undoes it, and the shader then multiplies by the transpose of the view-projection:
    /// geometry smeared into long wedges radiating from the middle of the window.
    /// </summary>
    private void SetMatrix(string name, Matrix4x4 value)
    {
        float* pointer = &value.M11;
        _gl.UniformMatrix4(_gl.GetUniformLocation(_program, name), 1, false, pointer);
    }
    private static float Clamp(float value) => Math.Max(0, Math.Min(1, value));
    private static Vector3 ColorVector(int value) => new(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);

    private void ClearMeshes()
    {
        foreach (GpuMesh mesh in _frames.SelectMany(frame => frame))
        {
            _gl.DeleteVertexArray(mesh.Vao); _gl.DeleteBuffer(mesh.Vbo);
            if (mesh.Diffuse != 0) _gl.DeleteTexture(mesh.Diffuse);
            if (mesh.Normal != 0) _gl.DeleteTexture(mesh.Normal);
        }
        _frames.Clear();
    }

    public void Dispose()
    {
        ClearMeshes();
        if (_gridVao != 0) _gl.DeleteVertexArray(_gridVao);
        if (_gridVbo != 0) _gl.DeleteBuffer(_gridVbo);
        if (_program != 0) _gl.DeleteProgram(_program);
    }

    private sealed record GpuMesh(BinMesh Source, uint Vao, uint Vbo, int VertexCount, uint Diffuse, uint Normal, bool IsRaw)
    {
        /// <summary>Whether the resolved art carries its own alpha channel.</summary>
        public bool HasTextureAlpha { get; init; }
    }

    private const string VertexShader = @"#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
uniform mat4 uViewProjection;
out vec3 vPosition; out vec3 vNormal; out vec2 vUv;
void main(){ vPosition=aPosition; vNormal=normalize(aNormal); vUv=aUv; gl_Position=uViewProjection*vec4(aPosition,1.0); }";

    private const string FragmentShader = @"#version 330 core
in vec3 vPosition; in vec3 vNormal; in vec2 vUv; out vec4 outputColor;
uniform sampler2D uDiffuse; uniform sampler2D uNormalMap;
uniform int uUseTexture, uUseNormal, uLighting, uAlphaCutout, uRawCutout;
uniform float uAlphaReference, uOpacity, uShininess, uEmissive, uNormalStrength;
uniform vec3 uTint, uCamera, uLight;
void main(){
 vec4 texel=uUseTexture!=0?texture(uDiffuse,vUv):vec4(1.0);
 if(uRawCutout!=0 && all(lessThan(texel.rgb,vec3(0.001)))) discard;
 if(uAlphaCutout!=0 && texel.a<uAlphaReference) discard;
 vec3 n=normalize(vNormal);
 if(uUseNormal!=0){ vec3 q1=dFdx(vPosition),q2=dFdy(vPosition); vec2 st1=dFdx(vUv),st2=dFdy(vUv);
   vec3 t=normalize(q1*st2.t-q2*st1.t); vec3 b=normalize(-q1*st2.s+q2*st1.s);
   vec3 map=texture(uNormalMap,vUv).rgb*2.0-1.0; map.xy*=vec2(uNormalStrength,-uNormalStrength); n=normalize(mat3(t,b,n)*map); }
 vec3 base=texel.rgb*uTint; float light=1.0;
 if(uLighting!=0){ vec3 l=normalize(uLight-vPosition); vec3 v=normalize(uCamera-vPosition); vec3 h=normalize(l+v);
   light=0.8+max(dot(n,l),0.0)*0.75+pow(max(dot(n,h),0.0),max(1.0,uShininess))*0.35; }
 vec3 color=base*light+base*uEmissive; outputColor=vec4(color,texel.a*uOpacity);
}";
}
