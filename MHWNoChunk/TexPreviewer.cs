using SharpGL;
using SharpGL.Version;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System;

namespace MHWNoChunk
{
    class TexPreviewer
    {
        const int MagicNumberTex = 0x00584554; // 54 45 58 00 | TEX 
        private int texType = 0x0;
        private int texHeight;
        private int texWidth;
        public OpenGL gl = new OpenGL();
        public GCHandle pixelsHandle;
        private bool failed = false;


        public TexPreviewer()
        {
            if (!gl.Create(OpenGLVersion.OpenGL4_2, RenderContextType.HiddenWindow, 1, 1, 32, null))
            {
                Console.Error.WriteLine("ERROR: Unable to initialize OpenGL");
                failed = true;
            }
        }

        //Learned from https://github.com/Qowyn/MHWTexToPng
        public Bitmap GetPic(byte[] texData)
        {
            if (failed) return null;
            gl.Flush();
            gl.RenderContextProvider.Dispose();
            gl.RenderContextProvider.Destroy();
            if (!gl.Create(OpenGLVersion.OpenGL4_2, RenderContextType.HiddenWindow, 1, 1, 32, null))
            {
                Console.Error.WriteLine("ERROR: Unable to initialize OpenGL");
                failed = true;
                return null;
            }
            MemoryStream texStream = new MemoryStream(texData);
            using (BinaryReader reader = new BinaryReader(texStream))
            {
                int magicNumber = reader.ReadInt32();

                reader.BaseStream.Position = 0x14;

                int mipMapCount = reader.ReadInt32();
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                texHeight = height;
                texWidth = width;

                reader.BaseStream.Position = 0x24;

                int type = reader.ReadInt32();

                reader.BaseStream.Position = 0xB8;

                long offset = reader.ReadInt64();
                int size;

                if (mipMapCount > 1)
                    size = (int)(reader.ReadInt64() - offset);
                else
                    size = (int)(texData.Length - offset);

                reader.BaseStream.Position = offset;

                uint internalFormat;

                switch (type)
                {
                    case 0x16:
                    case 0x17:
                        internalFormat = 0x83F1; // DXT1
                        break;
                    case 0x18:
                        internalFormat = 0x8DBB; // BC4U
                        break;
                    case 0x1A:
                        internalFormat = 0x8DBD; // BC5U
                        break;
                    case 0x1c:
                        internalFormat = 0x8E8F; // BC6H
                        break;
                    case 0x1d:
                    case 0x1e:
                    case 0x1f:
                        internalFormat = 0x8E8C; // BC7
                        break;
                    case 0x7:
                    case 0x9:
                        internalFormat = 0x57; // R8G8B8A8
                        break;
                    default:
                        internalFormat = 0;
                        break;
                }

                if (internalFormat == 0)
                {
                    texStream.Close();
                    reader.Close();
                    return null;
                }
                texType = type;
                if (internalFormat == 0x57)
                {
                    byte[] data = reader.ReadBytes(width * height * 4);
                    pixelsHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                    Bitmap texture = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, pixelsHandle.AddrOfPinnedObject());
                    texStream.Close();
                    reader.Close();
                    pixelsHandle.Free();
                    return texture;
                }
                else
                {
                    byte[] data = reader.ReadBytes(size);
                    GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                    gl.CompressedTexImage2D(OpenGL.GL_TEXTURE_2D, 0, internalFormat, width, height, 0, size, dataHandle.AddrOfPinnedObject());
                    dataHandle.Free();
                    int[] pixels = new int[width * height * 2];
                    pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    try { gl.GetTexImage(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, pixels); }
                    catch (Exception ex) { }
                    Bitmap texture = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, pixelsHandle.AddrOfPinnedObject());
                    texStream.Close();
                    reader.Close();
                    pixelsHandle.Free();
                    return texture;
                }
            }
        }
    }
}
