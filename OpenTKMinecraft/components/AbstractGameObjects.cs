﻿using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System;

using OpenTK.Graphics.OpenGL4;
using OpenTK;

namespace OpenTKMinecraft.Components
{
    public interface IUpdatable
    {
        void Update(double time, double delta);
    }

    public interface IRenderable
    {
        void Render(Camera camera);
    }

    public abstract class GameObject
        : IDisposable
        , IUpdatable
    {
        private static ulong _gameobjectcounter;
        private protected Matrix4 _projection;
        private protected Matrix4 _modelview;
        private protected Matrix4 _mnormal;

        public Renderable Model { get; private protected set; }
        public Vector4 Rotation { get; private protected set; }
        public Vector4 Direction { get; private protected set; }
        public Vector4 Position { get; private protected set; }
        public float Velocity { get; private protected set; }
        public Matrix4 ModelView => _modelview;
        public ulong ID { get; }



        public GameObject(Renderable model, Vector4 pos, Vector4 dir, Vector4 rot, float vel)
        {
            Model = model;
            Position = pos;
            Direction = dir;
            Rotation = rot;
            Velocity = vel;

            ID = _gameobjectcounter++;
        }

        public virtual void Update(double time, double delta) => Position += Direction * (Velocity * (float)delta);

        public virtual void Render(Camera camera)
        {
            Model.Program.Use();
            Model.Bind();

            _modelview = Matrix4.CreateRotationZ(Rotation.X)
                       * Matrix4.CreateRotationY(Rotation.Y)
                       * Matrix4.CreateRotationX(Rotation.Z)
                       * Matrix4.CreateTranslation(Position.X, Position.Y, Position.Z);
            _projection = camera.Projection;
            _mnormal = Matrix4.Transpose(Matrix4.Invert(_modelview));

            GL.UniformMatrix4(22, false, ref _mnormal);
            GL.UniformMatrix4(21, false, ref _modelview);
            GL.UniformMatrix4(20, false, ref _projection);

            Model.Render();
        }

        public void Dispose() => Model?.Dispose();
    }

    public abstract class Renderable
        : IDisposable
    {
        public ShaderProgram Program { get; internal protected set; }
        public int VertexArray { get; private protected set; }
        public int Buffer { get; private protected set; }
        public int VerticeCount { get; private protected set; }


        protected Renderable(ShaderProgram program, int vertexCount)
        {
            Program = program;
            VerticeCount = vertexCount;

            if (vertexCount > 0)
            {
                VertexArray = GL.GenVertexArray();
                Buffer = GL.GenBuffer();

                GL.BindVertexArray(VertexArray);
                GL.BindBuffer(BufferTarget.ArrayBuffer, Buffer);
            }
        }

        public virtual void Bind()
        {
            Program.Use();

            if (VerticeCount > 0)
                GL.BindVertexArray(VertexArray);
        }

        public virtual void Render() => Program.Use();

        protected virtual void InitBuffer()
        {
        }

        public void Dispose()
        {
            Dispose(true);

            // GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && (VerticeCount > 0))
            {
                GL.DeleteVertexArray(VertexArray);
                GL.DeleteBuffer(Buffer);
            }
        }
    }

    public class TexturedVertexSet
        : Renderable
    {
        internal readonly TextureSet _tex;

        public PrimitiveType PrimitiveType { set; get; }


        unsafe public TexturedVertexSet(Vertex[] vertices, PrimitiveType type, ShaderProgram program, params (string Path, TextureType Type)[] textures)
            : base(program, vertices.Length)
        {
            Vector3[] tangents = new Vector3[vertices.Length];
            Vector3[] bitangents = new Vector3[vertices.Length];

            if (type == PrimitiveType.Triangles)
            {
                int[] perm = { 1, 1, 2 };

                for (int i = 0; i < vertices.Length; i += 3)
                    for (int j = 0; j < 3; ++j)
                    {
                        Vector3 tang = Vector3.Normalize(vertices[i + perm[j]].position - vertices[i].position);

                        bitangents[i + j] = Vector3.Normalize(Vector3.Cross(vertices[i + j].normal, tang));
                        tangents[i + j] = Vector3.Normalize(Vector3.Cross(bitangents[i + j], vertices[i + j].normal));
                    }
            }
            // else if (type == PrimitiveType.Quads)
            //      for (int i = 0; i < vertices.Length; i += 4)
            //      {
            //          Vector3 tang1 = Vector3.Normalize(vertices[i + 1].position - vertices[i].position);
            //          Vector3 tang2 = Vector3.Normalize(vertices[i + 2].position - vertices[i + 3].position);
            //
            //          Vector3 bitang1 = Vector3.Normalize(Vector3.Cross(vertices[i].normal, tang1));
            //          Vector3 bitang2 = Vector3.Normalize(Vector3.Cross(vertices[i + 3].normal, tang2));
            //
            //          for (int j = 0; j < 4; ++j)
            //              (vertices[i + j].tangent, vertices[i + j].bitangent) = j < 2 ? (tang1, bitang1) : (tang2, bitang2);
            //      }
            else if (type != PrimitiveType.Quads)
                throw new NotImplementedException($"The primitive type '{type}' is currently not yet supported by the tangent space calculator.");

            GL.NamedBufferStorage(Buffer, sizeof(Vertex) * vertices.Length, vertices, BufferStorageFlags.MapWriteBit);
            // bind position
            GL.VertexArrayAttribBinding(VertexArray, 0, 0);
            GL.EnableVertexArrayAttrib(VertexArray, 0);
            GL.VertexArrayAttribFormat(VertexArray, 0, 3, VertexAttribType.Float, false, 0);
            // bind normal
            GL.VertexArrayAttribBinding(VertexArray, 1, 0);
            GL.EnableVertexArrayAttrib(VertexArray, 1);
            GL.VertexArrayAttribFormat(VertexArray, 1, 3, VertexAttribType.Float, false, 12);
            // bind color
            GL.VertexArrayAttribBinding(VertexArray, 2, 0);
            GL.EnableVertexArrayAttrib(VertexArray, 2);
            GL.VertexArrayAttribFormat(VertexArray, 2, 4, VertexAttribType.Float, false, 24);
            // bind tangent
            GL.VertexArrayAttribBinding(VertexArray, 3, 0);
            GL.EnableVertexArrayAttrib(VertexArray, 3);
            GL.VertexArrayAttribFormat(VertexArray, 3, 3, VertexAttribType.Float, false, 40);
            // bind bitangent
            GL.VertexArrayAttribBinding(VertexArray, 4, 0);
            GL.EnableVertexArrayAttrib(VertexArray, 4);
            GL.VertexArrayAttribFormat(VertexArray, 4, 3, VertexAttribType.Float, false, 52);
            GL.VertexArrayVertexBuffer(VertexArray, 0, Buffer, IntPtr.Zero, sizeof(Vertex));

            PrimitiveType = type;
            _tex = new TextureSet(program, textures);
        }

        public override void Bind()
        {
            base.Bind();

            _tex.Bind();
        }

        public override void Render()
        {
            if (VerticeCount > 0)
                GL.DrawArrays(PrimitiveType, 0, VerticeCount);
        }

        protected override void Dispose(bool disposing)
        {
            _tex.Dispose();

            base.Dispose(disposing);
        }
    }

    public sealed class TextureSet
        : Renderable
    {
        private const int SZCNT = 4;

        private readonly Bitmap[] _imgs = new Bitmap[SZCNT * SZCNT];
        private int _size = int.MaxValue;

        public int TextureID { get; private set; }


        internal TextureSet(ShaderProgram program, params (string Path, TextureType Type)[] textures)
            : base(program, 0)
        {
            TextureID = -1;

            UpdateTexture(textures);
        }

        internal void UpdateTexture(params (string Path, TextureType Type)[] textures)
        {
            if (TextureID != -1)
                GL.DeleteTexture(TextureID);

            foreach ((string Path, TextureType Type) in textures)
                if ((Type >= 0) && ((int)Type < _imgs.Length))
                    try
                    {
                        if (Path is string s && Image.FromFile(s.Trim()) is Bitmap bmp)
                        {
                            if (bmp.Width != bmp.Height)
                                throw null;

                            _imgs[(int)Type] = bmp;
                            _size = Math.Min(_size, bmp.Width);
                        }
                    }
                    catch
                    {
                        throw new ArgumentException("All texture images must have a valid source path and be squared.", nameof(textures));
                    }

            if ((_size < 1) || (_size == int.MaxValue))
                _size = 16;

            TextureID = InitTexture();
        }

        public override void Bind()
        {
            GL.Uniform1(5, _size);
            GL.BindTexture(TextureTarget.Texture2D, TextureID);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                GL.DeleteTexture(TextureID);

            base.Dispose(disposing);
        }

        private unsafe int InitTexture()
        {
            int sz = _size * SZCNT;
            Bitmap bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float[] data = new float[sz * sz * 4];

            using (Graphics g = Graphics.FromImage(bmp))
                for (int y = 0; y < SZCNT; ++y)
                    for (int x = 0; x < SZCNT; ++x)
                        if (_imgs[(y * SZCNT) + x] is Bitmap b)
                            g.DrawImage(b, x * _size, y * _size, _size, _size);

            BitmapData dat = bmp.LockBits(new Rectangle(0, 0, sz, sz), ImageLockMode.ReadOnly, bmp.PixelFormat);
            byte* ptr = (byte*)dat.Scan0;
            int index = 0;

            for (int y = 0; y < sz; y++)
                for (int x = 0; x < sz; x++)
                {
                    //  ptr: BGRA
                    // data: RGBA
                    data[index + 0] = ptr[index + 2] / 255f;
                    data[index + 1] = ptr[index + 1] / 255f;
                    data[index + 2] = ptr[index + 0] / 255f;
                    data[index + 3] = ptr[index + 3] / 255f;

                    index += 4;
                }

            bmp.UnlockBits(dat);
            bmp.Dispose();
            bmp = null;

            GL.CreateTextures(TextureTarget.Texture2D, 1, out int tex);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TextureStorage2D(tex, 1, SizedInternalFormat.Rgba32f, sz, sz);
            GL.TextureSubImage2D(tex, 0, 0, 0, sz, sz, OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.Float, data);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, sz, sz, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.Float, data);
            GL.TexParameterI(TextureTarget.ProxyTexture2D, TextureParameterName.TextureWrapS, new[] { (int)TextureWrapMode.ClampToEdge });
            GL.TexParameterI(TextureTarget.ProxyTexture2D, TextureParameterName.TextureWrapT, new[] { (int)TextureWrapMode.ClampToEdge });
            GL.TexParameterI(TextureTarget.ProxyTexture2D, TextureParameterName.TextureMagFilter, new[] { (int)TextureMagFilter.Nearest });
            GL.TexParameterI(TextureTarget.ProxyTexture2D, TextureParameterName.TextureMinFilter, new[] { (int)TextureMinFilter.LinearMipmapLinear });

            return tex;
        }
    }

    public enum TextureType
        : byte
    {
        Diffuse                 = 0x0,
        AmbientOcclusion        = 0x1,
        Displacement            = 0x2,
        Glow                    = 0x3,
        Normal                  = 0x4,
        Gloss                   = 0x5,
        Specular                = 0x6,
        SubsurfaceScattering    = 0x7,
        Reflection              = 0x8,
        Parallax                = 0x9,
        Details                 = 0xa,
        Flow                    = 0xb,
    }
}