using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Anode.Render.OpenGl.Tests;

/// <summary>
/// Headless OpenGL 4.1 core context on macOS via CGL, so GPU rendering can be tested without a window or display.
/// </summary>
internal sealed unsafe partial class CglContext : IDisposable
{
    private const string OpenGLFramework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    private const int KCGLPFAColorSize = 8;
    private const int KCGLPFAAlphaSize = 11;
    private const int KCGLPFAAccelerated = 73;
    private const int KCGLPFAAllowOfflineRenderers = 96;
    private const int KCGLPFAOpenGLProfile = 99;
    private const int KCGLOGLPVersionGL4Core = 0x4100;

    private readonly nint _pixelFormat;
    private readonly nint _context;
    private readonly nint _library;

    private CglContext()
    {
        int* attributes = stackalloc int[]
        {
            KCGLPFAOpenGLProfile, KCGLOGLPVersionGL4Core,
            KCGLPFAAccelerated,
            KCGLPFAAllowOfflineRenderers,
            KCGLPFAColorSize, 24,
            KCGLPFAAlphaSize, 8,
            0,
        };

        Check(CGLChoosePixelFormat(attributes, out _pixelFormat, out _), "CGLChoosePixelFormat");
        Check(CGLCreateContext(_pixelFormat, 0, out _context), "CGLCreateContext");
        Check(CGLSetCurrentContext(_context), "CGLSetCurrentContext");

        _library = NativeLibrary.Load(OpenGLFramework);
        Gl = GL.GetApi(name => NativeLibrary.TryGetExport(_library, name, out var p) ? p : 0);
    }

    public GL Gl { get; }

    public string Version => Gl.GetStringS(StringName.Version) + " / " + Gl.GetStringS(StringName.Renderer);

    public static CglContext Create()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("CGL is macOS only.");
        }

        return new CglContext();
    }

    public void Dispose()
    {
        CGLSetCurrentContext(0);
        CGLDestroyContext(_context);
        CGLDestroyPixelFormat(_pixelFormat);
        NativeLibrary.Free(_library);
    }

    private static void Check(int error, string call)
    {
        if (error != 0)
        {
            throw new InvalidOperationException($"{call} failed with CGL error {error}.");
        }
    }

    [LibraryImport(OpenGLFramework)]
    private static partial int CGLChoosePixelFormat(int* attributes, out nint pixelFormat, out int count);

    [LibraryImport(OpenGLFramework)]
    private static partial int CGLCreateContext(nint pixelFormat, nint share, out nint context);

    [LibraryImport(OpenGLFramework)]
    private static partial int CGLSetCurrentContext(nint context);

    [LibraryImport(OpenGLFramework)]
    private static partial int CGLDestroyContext(nint context);

    [LibraryImport(OpenGLFramework)]
    private static partial int CGLDestroyPixelFormat(nint pixelFormat);
}

/// <summary>RGBA8 framebuffer object for offscreen rendering and readback.</summary>
internal sealed class OffscreenTarget : IDisposable
{
    private readonly GL _gl;
    private readonly uint _renderbuffer;

    public OffscreenTarget(GL gl, int width, int height)
    {
        _gl = gl;
        Width = width;
        Height = height;

        Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _renderbuffer = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _renderbuffer);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _renderbuffer);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        }
    }

    public uint Framebuffer { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Top-down RGBA rows.</summary>
    public byte[] ReadPixels()
    {
        var bottomUp = new byte[Width * Height * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, bottomUp.AsSpan());

        var topDown = new byte[bottomUp.Length];
        int row = Width * 4;
        for (int y = 0; y < Height; y++)
        {
            System.Buffer.BlockCopy(bottomUp, (Height - 1 - y) * row, topDown, y * row, row);
        }

        return topDown;
    }

    public void Dispose()
    {
        _gl.DeleteRenderbuffer(_renderbuffer);
        _gl.DeleteFramebuffer(Framebuffer);
    }
}
