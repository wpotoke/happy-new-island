using System;
using System.Drawing;
#if PORTABLE
using Avalonia.Platform;
using SkiaSharp;
using Bitmap = Avalonia.Media.Imaging.WriteableBitmap;
#else
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
#endif
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace WinterIsland
{
    internal sealed class LeopardModel : IDisposable
    {
        private const float RasterScale = 1.38f;
        private const float ViewRise = .225f;
        public const float LogicalPixelsPerUnit = 116f;
        public const float ViewSin = ViewRise;
        public const float ViewLevel = .97436f;
        private const float InverseByte = 1f / 255;
        private static readonly Vector3 Key = Vector3.Normalize(new Vector3(.69f, .73f, -.29f));
        private static readonly Vector3 Fill = Vector3.Normalize(new Vector3(.32f, .55f, .73f));
        private static readonly Vector3 Rim = Vector3.Normalize(new Vector3(-.86f, .49f, .15f));
        private static readonly Vector3 Half = Vector3.Normalize(Key + new Vector3(ViewLevel, ViewRise, 0));

        private readonly Vertex[] vertices;
        private readonly FurStrand[] groom;
        private readonly ushort[] indices;
        private readonly Surface[] surface;
        private readonly Texture coat, relief;
        private readonly Projected[] projected;
        private readonly int[] snowRidges;
        private readonly LeopardSkeleton skeleton = new LeopardSkeleton();
        private readonly Matrix4x4[] skin = new Matrix4x4[LeopardSkeleton.Joints.Length];
        private readonly int width, height;
        private readonly float[] depth;
        private readonly int[] color;
        private readonly float rasterX, rasterY;
        private float subX, subY;

        private struct Vertex
        {
            public Vector3 Position, Normal, Tangent;
            public float U, V, Hand;
        }

        private struct Surface
        {
            public uint Bones, Weights;
        }

        private struct FurStrand
        {
            public Vector3 Root, Middle, Tip;
            public float Width;
            public Surface Binding;
            public uint Color;
        }

        private struct Projected
        {
            public float X, Y, Z, U, V, Lx, Ly, Lz, Ambient, Specular;
        }

        public Bitmap Frame { get; private set; }
        public Size LogicalSize { get; private set; }
        public float Ground { get; private set; }
        public float Pivot { get { return LogicalSize.Width / 2f; } }
        public float PixelsPerUnit { get; private set; }
        public PointF Muzzle { get; private set; }
        public PointF TailPoint { get; private set; }

        private LeopardModel(BinaryReader asset, float dpi)
        {
            uint version = asset.ReadUInt32();
            if (version != 0x334c4e48 && version != 0x344c4e48) throw new InvalidDataException("Leopard model header.");
            int vertexCount = asset.ReadInt32(), indexCount = asset.ReadInt32();
            int coatBytes = asset.ReadInt32(), reliefBytes = asset.ReadInt32();
            int strandCount = version == 0x344c4e48 ? asset.ReadInt32() : 0;
            if (vertexCount < 3 || vertexCount > 16000 || indexCount < 3 || indexCount > 75000 ||
                indexCount % 3 != 0 || coatBytes <= 0 || coatBytes > 2000000 || reliefBytes <= 0 || reliefBytes > 2000000 ||
                strandCount < 0 || strandCount > 8000)
                throw new InvalidDataException("Leopard model size.");

            vertices = new Vertex[vertexCount];
            snowRidges = new int[vertexCount];
            surface = new Surface[vertexCount];
            indices = new ushort[indexCount];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vertex
                {
                    Position = ReadVector(asset), Normal = ReadVector(asset),
                    U = asset.ReadSingle(), V = asset.ReadSingle(), Tangent = ReadVector(asset), Hand = asset.ReadSingle()
                };
            for (int i = 0; i < surface.Length; i++)
                surface[i].Bones = ReadJoints(asset);
            for (int i = 0; i < surface.Length; i++) surface[i].Weights = asset.ReadUInt32();
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = asset.ReadUInt16();
                if (indices[i] >= vertexCount) throw new InvalidDataException("Leopard triangle index.");
            }
            coat = new Texture(asset.ReadBytes(coatBytes));
            relief = new Texture(asset.ReadBytes(reliefBytes));
            groom = new FurStrand[strandCount];
            for (int i = 0; i < groom.Length; i++)
            {
                groom[i].Root = ReadVector(asset);
                groom[i].Middle = ReadVector(asset);
                groom[i].Tip = ReadVector(asset);
                groom[i].Width = asset.ReadSingle();
            }
            for (int i = 0; i < groom.Length; i++) groom[i].Binding.Bones = ReadJoints(asset);
            for (int i = 0; i < groom.Length; i++) groom[i].Binding.Weights = asset.ReadUInt32();
            for (int i = 0; i < groom.Length; i++) groom[i].Color = asset.ReadUInt32() | 0xff000000;

            LogicalSize = new Size((int)Math.Ceiling(190 * dpi), (int)Math.Ceiling(96 * dpi));
            width = (int)Math.Ceiling(LogicalSize.Width * RasterScale);
            height = (int)Math.Ceiling(LogicalSize.Height * RasterScale);
#if PORTABLE
            Frame = new Bitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
#else
            Frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
#endif
            rasterX = width / (float)LogicalSize.Width;
            rasterY = height / (float)LogicalSize.Height;
            Ground = 77 * dpi;
            PixelsPerUnit = LogicalPixelsPerUnit * dpi;
            projected = new Projected[vertexCount];
            depth = new float[width * height];
            color = new int[depth.Length];
        }

        public static LeopardModel Load(float dpi)
        {
            using (Stream stream = typeof(LeopardModel).Assembly.GetManifestResourceStream("WinterIsland.LeopardModel"))
            {
                if (stream == null) return null;
                using (var asset = new BinaryReader(stream)) return new LeopardModel(asset, dpi);
            }
        }

        private static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static uint ReadJoints(BinaryReader asset)
        {
            uint joints = asset.ReadUInt32();
            for (int j = 0; j < 32; j += 8)
                if (((joints >> j) & 255) >= LeopardSkeleton.Joints.Length)
                    throw new InvalidDataException("Leopard joint index.");
            return joints;
        }

        public void Render(LeopardPose pose, float offsetX, float offsetY)
        {
            subX = offsetX;
            subY = offsetY;
            skeleton.Sample(pose);
            var yaw = Matrix4x4.CreateFromQuaternion(skeleton.Yaw);
            for (int i = 0; i < skin.Length; i++)
            {
                var bone = skeleton.Bones[i];
                Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(bone.Rotation);
                matrix.Translation = bone.Position - Vector3.TransformNormal(LeopardSkeleton.Joints[i].Bind, matrix);
                skin[i] = matrix * yaw;
            }

            for (int i = 0; i < vertices.Length; i++) projected[i] = Project(vertices[i], surface[i]);
            Vector3 muzzle = Vector3.Transform(skeleton.Muzzle, skeleton.Yaw);
            Muzzle = new PointF(ScreenX(muzzle), ScreenY(muzzle));
            Vector3 tail = Vector3.Transform(skeleton.TailTip, skeleton.Yaw);
            TailPoint = new PointF(ScreenX(tail), ScreenY(tail));
            Array.Clear(depth, 0, depth.Length);
            Array.Clear(color, 0, color.Length);
            for (int i = 0; i < indices.Length; i += 3)
                PaintTriangle(projected[indices[i]], projected[indices[i + 1]], projected[indices[i + 2]]);
            for (int i = 0; i < groom.Length; i++)
                PaintFur(groom[i]);
            WriteFrame();
        }

        private Projected Project(Vertex vertex, Surface influences)
        {
            Vector3 position = Vector3.Zero, normal = Vector3.Zero, tangent = Vector3.Zero;
            uint joints = influences.Bones, weights = influences.Weights;
            while (weights != 0)
            {
                float amount = (weights & 255) * InverseByte;
                Matrix4x4 matrix = skin[joints & 255];
                position += Vector3.Transform(vertex.Position, matrix) * amount;
                normal += Vector3.TransformNormal(vertex.Normal, matrix) * amount;
                tangent += Vector3.TransformNormal(vertex.Tangent, matrix) * amount;
                weights >>= 8;
                joints >>= 8;
            }

            normal = Vector3.Normalize(normal);
            tangent = Vector3.Normalize(tangent - normal * Vector3.Dot(tangent, normal));
            Vector3 bitangent = Vector3.Cross(normal, tangent) * vertex.Hand;
            float glint = Math.Max(0, Vector3.Dot(normal, Half));
            glint *= glint;
            glint *= glint;
            glint *= glint;
            return new Projected
            {
                X = ScreenX(position) * rasterX,
                Y = ScreenY(position) * rasterY,
                Z = 2 + position.X * ViewLevel + position.Y * ViewRise,
                U = vertex.U * coat.Size, V = vertex.V * coat.Size,
                Lx = Vector3.Dot(tangent, Key), Ly = Vector3.Dot(bitangent, Key), Lz = Vector3.Dot(normal, Key),
                Ambient = .80f + .106f * Math.Max(0, Vector3.Dot(normal, Fill)) +
                    .075f * Math.Max(0, Vector3.Dot(normal, Rim)),
                Specular = glint * 4.1f
            };
        }

        private float ScreenX(Vector3 point) { return Pivot + subX + point.Z * PixelsPerUnit; }
        private float ScreenY(Vector3 point) { return Ground + subY - (point.Y * ViewLevel - point.X * ViewRise) * PixelsPerUnit; }

        private Vector3 HairPoint(Vector3 point)
        {
            return new Vector3(ScreenX(point) * rasterX, ScreenY(point) * rasterY,
                2 + point.X * ViewLevel + point.Y * ViewRise);
        }

        private void PaintFur(FurStrand strand)
        {
            Vector3 root = Vector3.Zero, middle = Vector3.Zero, tip = Vector3.Zero;
            uint joints = strand.Binding.Bones, weights = strand.Binding.Weights;
            while (weights != 0)
            {
                float amount = (weights & 255) * InverseByte;
                Matrix4x4 matrix = skin[joints & 255];
                root += Vector3.Transform(strand.Root, matrix) * amount;
                middle += Vector3.Transform(strand.Middle, matrix) * amount;
                tip += Vector3.Transform(strand.Tip, matrix) * amount;
                weights >>= 8;
                joints >>= 8;
            }
            Vector3 b = HairPoint(middle), c = HairPoint(tip);
            if (Buried(b) && Buried(c)) return;
            float radius = Math.Max(.18f, strand.Width * PixelsPerUnit * rasterX);
            PaintFiber(HairPoint(root), b, radius * 1.19f, radius * .74f, strand.Color, .56f);
            PaintFiber(b, c, radius * .72f, .10f, strand.Color, .73f);
        }

        private bool Buried(Vector3 point)
        {
            int x = (int)point.X, y = (int)point.Y;
            return x < 0 || x >= width || y < 0 || y >= height || depth[y * width + x] > point.Z + .018f;
        }

        private void PaintFiber(Vector3 a, Vector3 b, float baseRadius, float tipRadius, uint pigment, float opacity)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float squared = dx * dx + dy * dy;
            if (squared < .14f) return;
            float margin = baseRadius + .68f, inverse = 1 / squared;
            float taper = tipRadius - baseRadius, depthChange = b.Z - a.Z;
            int firstX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - margin));
            int lastX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + margin));
            int firstY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - margin));
            int lastY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + margin));
            for (int y = firstY; y <= lastY; y++)
            {
                float py = y + .5f - a.Y;
                int i = y * width + firstX;
                for (int x = firstX; x <= lastX; x++, i++)
                {
                    float px = x + .5f - a.X;
                    float at = Math.Max(0, Math.Min(1, (px * dx + py * dy) * inverse));
                    float ox = px - dx * at, oy = py - dy * at;
                    float edge = ox * ox + oy * oy;
                    float falloff = baseRadius + .63f + taper * at;
                    if (edge >= falloff * falloff) continue;
                    float behind = depth[i] - (a.Z + at * depthChange);
                    if (behind > .014f) continue;
                    float visible = 1 - LeopardSkeleton.Ease((behind - .001f) / .013f);
                    float coverage = Math.Min(1, falloff - (float)Math.Sqrt(edge)) * opacity * visible;
                    uint weight = (uint)(256 * coverage);
                    color[i] = unchecked((int)Lerp((uint)color[i], pigment, weight));
                }
            }
        }

        private void PaintTriangle(Projected a, Projected b, Projected c)
        {
            float abX = b.X - a.X, abY = b.Y - a.Y;
            float acX = c.X - a.X, acY = c.Y - a.Y;
            float area = abX * acY - abY * acX;
            if (area <= .018f) return;

            int x1 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            int y1 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            int x2 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y2 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            float inverse = 1 / area;
            float bx = acY * inverse, by = -acX * inverse;
            float cx = -abY * inverse, cy = abX * inverse;

            float u1 = b.U - a.U, u2 = c.U - a.U;
            float v1 = b.V - a.V, v2 = c.V - a.V;
            float footprint = Math.Max(Math.Max(Math.Abs(bx * u1 + cx * u2), Math.Abs(by * u1 + cy * u2)),
                Math.Max(Math.Abs(bx * v1 + cx * v2), Math.Abs(by * v1 + cy * v2)));
            int mip = footprint > 4.8f ? 2 : (footprint > 2.2f ? 1 : 0);
            int[] baseColor = coat.Layers[mip], fiber = relief.Layers[mip];
            bool whisker = a.U < 0 && b.U < 0 && c.U < 0;
            int size = coat.Size >> mip;
            float uvScale = 1f / (1 << mip);
            float dz1 = b.Z - a.Z, dz2 = c.Z - a.Z;
            float l1x = b.Lx - a.Lx, l2x = c.Lx - a.Lx;
            float l1y = b.Ly - a.Ly, l2y = c.Ly - a.Ly;
            float l1z = b.Lz - a.Lz, l2z = c.Lz - a.Lz;
            float sh1 = b.Ambient - a.Ambient, sh2 = c.Ambient - a.Ambient;
            float sp1 = b.Specular - a.Specular, sp2 = c.Specular - a.Specular;

            for (int y = y1; y <= y2; y++)
            {
                float py = y + .5f - a.Y, px = x1 + .5f - a.X;
                float wb = px * bx + py * by, wc = px * cx + py * cy;
                int i = y * width + x1;
                for (int x = x1; x <= x2; x++, i++, wb += bx, wc += cx)
                {
                    if (wb < -.0001f || wc < -.0001f || wb + wc > 1.0001f) continue;
                    float z = a.Z + wb * dz1 + wc * dz2;
                    if (z <= depth[i]) continue;
                    depth[i] = z;
                    float u = (a.U + wb * u1 + wc * u2) * uvScale - .5f;
                    float v = (a.V + wb * v1 + wc * v2) * uvScale - .5f;
                    uint rgb = 0xd2d5d8, nm = 0x8080ff;
                    if (!whisker) SampleTextures(baseColor, fiber, size, u, v, out rgb, out nm);
                    float tx = (((nm >> 16) & 255) * (2 * InverseByte) - 1) * .47f;
                    float ty = (((nm >> 8) & 255) * (2 * InverseByte) - 1) * .47f;
                    float tz = .53f + .47f * ((nm & 255) * (2 * InverseByte) - 1);
                    float incidence = (a.Lx + wb * l1x + wc * l2x) * tx +
                        (a.Ly + wb * l1y + wc * l2y) * ty + (a.Lz + wb * l1z + wc * l2z) * tz;
                    float direct = Math.Max(0, incidence);
                    float lit = a.Ambient + wb * sh1 + wc * sh2 + .265f * direct;
                    float shine = (a.Specular + wb * sp1 + wc * sp2) * (.43f + .57f * direct);
                    int red = (int)Math.Min(255, ((rgb >> 16) & 255) * lit + shine);
                    int green = (int)Math.Min(255, ((rgb >> 8) & 255) * (lit * 1.013f) + shine);
                    int blue = (int)Math.Min(255, (rgb & 255) * (lit * 1.041f) + shine);
                    color[i] = unchecked((int)0xff000000) | (red << 16) | (green << 8) | blue;
                }
            }
        }

        private static void SampleTextures(int[] coat, int[] relief, int size, float u, float v,
            out uint color, out uint normal)
        {
            u = Math.Max(0, Math.Min(size - 1.001f, u));
            v = Math.Max(0, Math.Min(size - 1.001f, v));
            int x = (int)u, y = (int)v;
            int i = y * size + x;
            uint dx = (uint)((u - x) * 256), dy = (uint)((v - y) * 256);
            color = Bilinear(coat, size, i, dx, dy);
            normal = Bilinear(relief, size, i, dx, dy);
        }

        private static uint Bilinear(int[] source, int size, int i, uint dx, uint dy)
        {
            uint top = Lerp((uint)source[i], (uint)source[i + 1], dx);
            uint bottom = Lerp((uint)source[i + size], (uint)source[i + size + 1], dx);
            return Lerp(top, bottom, dy);
        }

        private const uint Channel = 0x00ff00ff;

        private static uint Lerp(uint a, uint b, uint next)
        {
            uint old = 256 - next;
            uint rb = (((a & Channel) * old + (b & Channel) * next) >> 8) & Channel;
            uint ag = ((((a >> 8) & Channel) * old + ((b >> 8) & Channel) * next) >> 8) & Channel;
            return rb | (ag << 8);
        }

        private void WriteFrame()
        {
#if PORTABLE
            using (var frame = Frame.Lock())
            {
                if (frame.RowBytes == width * sizeof(int))
                    Marshal.Copy(color, 0, frame.Address, color.Length);
                else
                    for (int row = 0; row < height; row++)
                        Marshal.Copy(color, row * width, IntPtr.Add(frame.Address, row * frame.RowBytes), width);
            }
#else
            var frame = Frame.LockBits(new Rectangle(Point.Empty, Frame.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(color, 0, frame.Scan0, color.Length); }
            finally { Frame.UnlockBits(frame); }
#endif
        }

        public bool OpaqueAt(int x, int y)
        {
            return x >= 0 && y >= 0 && x < LogicalSize.Width && y < LogicalSize.Height &&
                (uint)color[(int)((y + .5f) * rasterY) * width + (int)((x + .5f) * rasterX)] >> 24 >= 126;
        }

        public int FindSnowAnchor(PointF point, float radius)
        {
            float x = point.X * rasterX, y = point.Y * rasterY;
            float best = radius * rasterX;
            best *= best;
            int hit = -1;
            for (int i = 0; i < projected.Length; i++)
            {
                Vertex site = vertices[i];
                if (site.U < 0 || site.Normal.Y < -.17f ||
                    (site.Position.Y < .116f && site.Position.Z > -.29f)) continue;
                Projected at = projected[i];
                float dx = x - at.X, dy = y - at.Y;
                float distance = dx * dx + dy * dy;
                if (distance >= best || SurfaceHidden(at)) continue;
                hit = i;
                best = distance;
            }
            return hit;
        }

        public bool SnowPoint(int index, out PointF point)
        {
            return SnowVisibility(index, out point) > .16f;
        }

        public float SnowSiteDistanceSquared(int first, int second)
        {
            return Vector3.DistanceSquared(vertices[first].Position, vertices[second].Position);
        }

        public int SnowRidge(int index)
        {
            if (snowRidges[index] != 0) return snowRidges[index] - 1;
            Vector3 contact = vertices[index].Position;
            bool tail = contact.Z < -.31f;
            float center = tail ? contact.X : contact.X * .13f;
            int best = index;
            float score = Single.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vertex site = vertices[i];
                if (site.U < 0 || site.Normal.Y < .42f) continue;
                Vector3 shift = site.Position - contact;
                if (Math.Abs(shift.Z) > .044f || Math.Abs(shift.X) > .139f ||
                    shift.Y < -.019f || shift.Y > (tail ? .086f : .127f)) continue;
                float choice = 1.48f * Math.Abs(shift.Z) + .7f * Math.Abs(site.Position.X - center) -
                    .54f * shift.Y - .028f * site.Normal.Y;
                if (choice >= score) continue;
                best = i;
                score = choice;
            }
            snowRidges[index] = best + 1;
            return best;
        }

        public float SnowVisibility(int index, out PointF point)
        {
            Projected at = projected[index];
            point = new PointF(at.X / rasterX, at.Y / rasterY);
            int x = (int)at.X, y = (int)at.Y;
            float sum = 0, weights = 0, peak = 0;
            for (int row = Math.Max(0, y - 1); row <= Math.Min(height - 1, y + 1); row++)
                for (int col = Math.Max(0, x - 1); col <= Math.Min(width - 1, x + 1); col++)
                {
                    float sample = depth[row * width + col];
                    if (sample <= 0) continue;
                    float visibility = 1 - LeopardSkeleton.Ease((sample - at.Z - .018f) / .047f);
                    float weight = (row == y ? 2 : 1) * (col == x ? 2 : 1);
                    sum += visibility * weight;
                    weights += weight;
                    peak = Math.Max(peak, visibility);
                }
            return weights > 0 ? .55f * (sum / weights) + .45f * peak : 0;
        }

        public float CoatCoverage(float x, float y)
        {
            float column = x * rasterX - .5f, row = y * rasterY - .5f;
            int cx = (int)Math.Floor(column), cy = (int)Math.Floor(row);
            float u = column - cx, v = row - cy;
            return (1 - v) * ((1 - u) * PixelOpacity(cx, cy) + u * PixelOpacity(cx + 1, cy)) +
                v * ((1 - u) * PixelOpacity(cx, cy + 1) + u * PixelOpacity(cx + 1, cy + 1));
        }

        private float PixelOpacity(int column, int row)
        {
            return column >= 0 && row >= 0 && column < width && row < height ?
                ((uint)color[row * width + column] >> 24) * InverseByte : 0;
        }

        private bool SurfaceHidden(Projected at)
        {
            int x = (int)at.X, y = (int)at.Y, i = y * width + x;
            return x < 0 || y < 0 || x >= width || y >= height ||
                depth[i] <= 0 || depth[i] > at.Z + .014f;
        }

        public void Dispose() { Frame.Dispose(); }

        private sealed class Texture
        {
            public readonly int Size;
            public readonly int[][] Layers = new int[3][];

            public Texture(byte[] compressed)
            {
#if PORTABLE
                using (var bitmap = SKBitmap.Decode(compressed))
                {
                    if (bitmap == null || bitmap.Width != 512 || bitmap.Height != 512)
                        throw new InvalidDataException("Leopard texture size.");
                    Size = bitmap.Width;
                    for (int i = 0; i < Layers.Length; i++)
                    {
                        int size = Size >> i;
                        using (var mip = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque))
                        {
                            if (!bitmap.ScalePixels(mip, SKFilterQuality.High)) throw new InvalidDataException("Leopard texture conversion.");
                            Layers[i] = new int[size * size];
                            Marshal.Copy(mip.GetPixels(), Layers[i], 0, size * size);
                        }
                    }
                }
#else
                using (var stream = new MemoryStream(compressed, false))
                using (var bitmap = new Bitmap(stream))
                {
                    if (bitmap.Width != 512 || bitmap.Height != 512) throw new InvalidDataException("Leopard texture size.");
                    Size = bitmap.Width;
                    for (int i = 0; i < Layers.Length; i++)
                    {
                        int size = Size >> i;
                        using (var mip = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                        {
                            using (var paint = Graphics.FromImage(mip))
                            {
                                paint.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                paint.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                paint.DrawImage(bitmap, new Rectangle(0, 0, size, size));
                            }
                            var area = mip.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            try
                            {
                                Layers[i] = new int[size * size];
                                Marshal.Copy(area.Scan0, Layers[i], 0, size * size);
                            }
                            finally { mip.UnlockBits(area); }
                        }
                    }
                }
#endif
            }
        }
    }
}
