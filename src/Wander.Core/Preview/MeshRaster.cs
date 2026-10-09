using Wander.Core.Imaging;

namespace Wander.Core.Preview;

/// <summary>
/// A model drawn into a square of pixels, for its tile: the view the
/// preview pane opens on - from the front and a little above, Y up - lit
/// by the pane's three lights, flat-shaded, on transparency. Drawn here
/// rather than by WPF off screen: thumbnails are made on pool threads in
/// a layer without WPF, and a z-buffer over triangles is arithmetic a test
/// can reach.
///
/// <para>
/// The frame is the model's projected outline, not its bounding sphere:
/// the sphere leaves a long thin part small in the middle of its tile.
/// Edges are smoothed by drawing at twice the size and averaging.
/// </para>
/// </summary>
public static class MeshRaster {
    /// <summary>The pane's grey for a model that states no colour (<c>ModelBuilder</c>).</summary>
    private const float Grey = 0.76f;

    /// <summary>The reverse of a face, as the pane's back material darkens it.</summary>
    private const float BackShade = 0.72f;

    /// <summary>Lights of the pane (<c>Palette.xaml</c>): ambient, key, fill - grey levels.</summary>
    private const float Ambient = 0x40 / 255f;
    private const float Key = 0xC8 / 255f;
    private const float Fill = 0x50 / 255f;

    /// <summary>Samples per pixel along each side.</summary>
    private const int Supersample = 2;

    /// <summary>Room around the model inside its tile, a share of the side.</summary>
    private const double Margin = 0.06;

    /// <summary>The pane's camera: a field of 45 degrees, raised by this share of the radius.</summary>
    private const double FieldOfView = 45;
    private const double Raise = 0.55;

    // Directions the light travels, as the pane's DirectionalLight states them.
    private static readonly Vector _keyDirection = new Vector(-0.5, -1, -1).Unit();
    private static readonly Vector _fillDirection = new Vector(1, 0.5, 0.8).Unit();


    /// <summary>The model on a <paramref name="side"/>-pixel square, or null when it has nothing to draw.</summary>
    public static BgraImage? Render(MeshData mesh, int side) {
        if (side <= 0 || mesh.Bounds() is not { } box) {
            return null;
        }

        var centre = new Vector(box.CenterX, box.CenterY, box.CenterZ);
        double radius = Math.Max(Math.Sqrt(box.SizeX * (double)box.SizeX + box.SizeY * (double)box.SizeY + box.SizeZ * (double)box.SizeZ) / 2, 1e-6);
        double distance = radius / Math.Sin(FieldOfView / 2 * Math.PI / 180) * 1.05;
        var eye = centre + new Vector(0, radius * Raise, distance);
        var forward = (centre - eye).Unit();
        var right = forward.Cross(new Vector(0, 1, 0)).Unit();
        var up = right.Cross(forward);

        // Camera space, the projection on a unit focal plane. Floats: a
        // model at the triangle limit is six million corners.
        int count = mesh.VertexCount;
        var px = new float[count];
        var py = new float[count];
        var depth = new float[count];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < count; i++) {
            var p = new Vector(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]) - eye;
            double z = p.Dot(forward);
            if (!(z > 1e-9) || double.IsNaN(z)) {
                depth[i] = float.NaN;

                continue;
            }

            px[i] = (float)(p.Dot(right) / z);
            py[i] = (float)(p.Dot(up) / z);
            depth[i] = (float)z;
            minX = Math.Min(minX, px[i]);
            maxX = Math.Max(maxX, px[i]);
            minY = Math.Min(minY, py[i]);
            maxY = Math.Max(maxY, py[i]);
        }
        if (minX > maxX) {
            return null;
        }

        int size = side * Supersample;
        double span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-12);
        double scale = size * (1 - 2 * Margin) / span;
        double offsetX = size / 2.0 - (minX + maxX) / 2 * scale;
        double offsetY = size / 2.0 + (minY + maxY) / 2 * scale;
        for (int i = 0; i < count; i++) {
            px[i] = (float)(offsetX + px[i] * scale);
            py[i] = (float)(offsetY - py[i] * scale);
        }

        var zbuffer = new float[size * size];
        Array.Fill(zbuffer, float.PositiveInfinity);
        var colour = new uint[size * size];
        foreach (var part in mesh.Parts) {
            var tint = part.Color ?? new MeshColor(Grey, Grey, Grey);
            for (int t = 0; t + 2 < part.Indices.Length; t += 3) {
                int a = part.Indices[t], b = part.Indices[t + 1], c = part.Indices[t + 2];
                if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count
                    || float.IsNaN(depth[a]) || float.IsNaN(depth[b]) || float.IsNaN(depth[c])) {
                    continue;
                }

                var pa = Point(mesh, a);
                var normal = (Point(mesh, b) - pa).Cross(Point(mesh, c) - pa);
                if (normal.Length() == 0) {
                    continue;
                }
                normal = normal.Unit();
                float shade = 1;
                if (normal.Dot(eye - pa) < 0) {
                    normal = -normal;
                    shade = BackShade;
                }

                double light = Ambient
                    + Key * Math.Max(0, -normal.Dot(_keyDirection))
                    + Fill * Math.Max(0, -normal.Dot(_fillDirection));
                uint pixel = Pack(tint, light * shade);
                Triangle(px, py, depth, a, b, c, size, zbuffer, colour, pixel);
            }
        }

        return Downsample(colour, size, side);
    }


    /// <summary>Fills one triangle where it is nearer than what is drawn - depth interpolated as 1/z, which is linear on screen.</summary>
    private static void Triangle(float[] px, float[] py, float[] depth, int a, int b, int c, int size, float[] zbuffer, uint[] colour, uint pixel) {
        double x0 = px[a], y0 = py[a], x1 = px[b], y1 = py[b], x2 = px[c], y2 = py[c];
        double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (Math.Abs(area) < 1e-12) {
            return;
        }

        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int right = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int bottom = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
        double w0 = 1.0 / depth[a], w1 = 1.0 / depth[b], w2 = 1.0 / depth[c];
        for (int y = top; y <= bottom; y++) {
            double sy = y + 0.5;
            for (int x = left; x <= right; x++) {
                double sx = x + 0.5;
                double l0 = ((x1 - sx) * (y2 - sy) - (x2 - sx) * (y1 - sy)) / area;
                double l1 = ((x2 - sx) * (y0 - sy) - (x0 - sx) * (y2 - sy)) / area;
                double l2 = 1 - l0 - l1;
                if (l0 < 0 || l1 < 0 || l2 < 0) {
                    continue;
                }

                float z = (float)(1 / (l0 * w0 + l1 * w1 + l2 * w2));
                int at = y * size + x;
                if (z < zbuffer[at]) {
                    zbuffer[at] = z;
                    colour[at] = pixel;
                }
            }
        }
    }


    /// <summary>Each <see cref="Supersample"/>-square of samples averaged into a pixel; uncovered samples are transparent.</summary>
    private static BgraImage Downsample(uint[] colour, int size, int side) {
        var pixels = new byte[side * side * 4];
        int samples = Supersample * Supersample;
        for (int y = 0; y < side; y++) {
            for (int x = 0; x < side; x++) {
                int r = 0, g = 0, b = 0, covered = 0;
                for (int dy = 0; dy < Supersample; dy++) {
                    for (int dx = 0; dx < Supersample; dx++) {
                        uint s = colour[(y * Supersample + dy) * size + x * Supersample + dx];
                        if (s == 0) {
                            continue;
                        }

                        b += (int)(s & 0xFF);
                        g += (int)((s >> 8) & 0xFF);
                        r += (int)((s >> 16) & 0xFF);
                        covered++;
                    }
                }
                if (covered == 0) {
                    continue;
                }

                int at = (y * side + x) * 4;
                pixels[at] = (byte)(b / covered);
                pixels[at + 1] = (byte)(g / covered);
                pixels[at + 2] = (byte)(r / covered);
                pixels[at + 3] = (byte)(255 * covered / samples);
            }
        }

        return new BgraImage(pixels, side, side, side * 4);
    }


    /// <summary>Opaque BGRA in one word; never zero, which means "nothing drawn".</summary>
    private static uint Pack(MeshColor tint, double light) {
        static uint Channel(float value, double light) {
            return (uint)Math.Clamp(Math.Round(value * light * 255), 0, 255);
        }

        return 0xFF000000u | (Channel(tint.R, light) << 16) | (Channel(tint.G, light) << 8) | Channel(tint.B, light);
    }


    private static Vector Point(MeshData mesh, int i) {
        return new Vector(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
    }


    private readonly record struct Vector(double X, double Y, double Z) {
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector operator -(Vector a) => new(-a.X, -a.Y, -a.Z);

        public double Dot(Vector o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vector Cross(Vector o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public double Length() => Math.Sqrt(Dot(this));

        public Vector Unit() {
            double length = Length();

            return length > 0 ? new Vector(X / length, Y / length, Z / length) : this;
        }
    }
}
