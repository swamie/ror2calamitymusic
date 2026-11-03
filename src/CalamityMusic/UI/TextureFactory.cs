using UnityEngine;

namespace CalamityMusic.UI
{
    /// <summary>Generates the banner's textures at runtime so the mod ships no image assets.</summary>
    internal static class TextureFactory
    {
        /// <summary>
        /// Two beamed eighth notes (♫) drawn from signed distance fields: white fill, blue outline,
        /// teal glow, matching the banner text.
        /// </summary>
        public static Texture2D MusicNote(int size, Color fill, Color outline, Color glow)
        {
            var tex = NewTexture(size, size);
            var px = new Color[size * size];
            const float shrink = 0.8f;          // leave room for the glow
            const float outlineWidth = 0.05f;
            const float glowWidth = 0.09f;
            float aa = 1.5f / size;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    var q = (p - new Vector2(0.5f, 0.5f)) / shrink + new Vector2(0.5f, 0.5f);
                    float d = NoteDistance(q) * shrink;

                    float fillA = Smooth(aa, -aa, d);
                    float outA = Smooth(aa, -aa, d - outlineWidth);
                    float outside = Mathf.Max(0f, d - outlineWidth);
                    float glowA = Mathf.Exp(-outside / (glowWidth * 0.4f)) * 0.9f;

                    Color c = new Color(glow.r, glow.g, glow.b, glowA);
                    c = Over(new Color(outline.r, outline.g, outline.b, outA), c);
                    c = Over(new Color(fill.r, fill.g, fill.b, fillA), c);
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        private static float NoteDistance(Vector2 p)
        {
            // Unit-square layout, y up.
            float head1 = Ellipse(p, new Vector2(0.30f, 0.24f), new Vector2(0.135f, 0.095f), -25f);
            float head2 = Ellipse(p, new Vector2(0.73f, 0.32f), new Vector2(0.135f, 0.095f), -25f);
            float stem1 = Box(p, new Vector2(0.405f, 0.53f), new Vector2(0.022f, 0.29f), 0f);
            float stem2 = Box(p, new Vector2(0.835f, 0.61f), new Vector2(0.022f, 0.29f), 0f);
            float beam = Box(p, new Vector2(0.62f, 0.80f), new Vector2(0.235f, 0.052f), 10f);
            return Mathf.Min(Mathf.Min(head1, head2), Mathf.Min(Mathf.Min(stem1, stem2), beam));
        }

        private static float Ellipse(Vector2 p, Vector2 c, Vector2 r, float degrees)
        {
            Vector2 q = Rotate(p - c, -degrees);
            float k = new Vector2(q.x / r.x, q.y / r.y).magnitude;
            return (k - 1f) * Mathf.Min(r.x, r.y);
        }

        private static float Box(Vector2 p, Vector2 c, Vector2 half, float degrees)
        {
            Vector2 q = Rotate(p - c, -degrees);
            Vector2 d = new Vector2(Mathf.Abs(q.x) - half.x, Mathf.Abs(q.y) - half.y);
            float outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }

        /// <summary>White stadium shape with a soft gaussian edge, tinted per use (backdrop, aura).</summary>
        public static Texture2D SoftPill(int width, int height)
        {
            var tex = NewTexture(width, height);
            var px = new Color[width * height];
            float aspect = (float)width / height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = ((x + 0.5f) / width - 0.5f) * 2f * aspect;
                    float v = ((y + 0.5f) / height - 0.5f) * 2f;
                    float halfLen = aspect - 1f;
                    float dx = Mathf.Max(0f, Mathf.Abs(u) - halfLen);
                    float dist = Mathf.Sqrt(dx * dx + v * v); // 0 at the spine, 1 at the edge
                    float a = Mathf.Exp(-dist * dist * 3.2f) * Smooth(1f, 0.85f, dist);
                    px[y * width + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Thin line that fades out toward both ends.</summary>
        public static Texture2D Underline(int width, int height)
        {
            var tex = NewTexture(width, height);
            var px = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / height - 0.5f) * 2f;
                for (int x = 0; x < width; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / width - 0.5f) * 2f;
                    float a = Smooth(1f, 0.55f, u) * Smooth(1f, 0.2f, v);
                    px[y * width + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// A lens flare line: a hot point at the left end that thins and fades out to the right.
        /// Mirrored with a negative x scale for the other side.
        /// </summary>
        public static Texture2D Flare(int width, int height)
        {
            var tex = NewTexture(width, height);
            var px = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / height - 0.5f) * 2f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float thickness = Mathf.Lerp(1f, 0.25f, u);         // thins out along its length
                    float line = Mathf.Exp(-Mathf.Pow(v / thickness, 2f) * 5f);
                    float along = Mathf.Pow(1f - u, 1.6f);
                    float core = Mathf.Exp(-u * 40f) * Mathf.Exp(-v * v * 2f) * 0.8f;
                    px[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(line * along + core));
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ particle sprites (white, tinted per use)

        public static Texture2D SoftDot(int size) => Radial(size, d => Mathf.Exp(-d * d * 4.5f));

        public static Texture2D Ring(int size) => Radial(size, d =>
        {
            float ring = Mathf.Exp(-Mathf.Pow((d - 0.72f) / 0.12f, 2f));
            float shine = d < 0.5f ? 0.12f * (1f - d / 0.5f) : 0f;
            return Mathf.Clamp01(ring + shine);
        });

        /// <summary>Four-point sparkle with a soft core.</summary>
        public static Texture2D Sparkle(int size)
        {
            return Shape(size, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float core = Mathf.Exp(-d * d * 18f);
                float arms = Mathf.Exp(-Mathf.Abs(x) * 26f) * Mathf.Exp(-Mathf.Abs(y) * 2.6f)
                           + Mathf.Exp(-Mathf.Abs(y) * 26f) * Mathf.Exp(-Mathf.Abs(x) * 2.6f);
                return Mathf.Clamp01(core + arms * 0.9f);
            });
        }

        /// <summary>Six-armed snowflake.</summary>
        public static Texture2D Flake(int size)
        {
            return Shape(size, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                if (d > 1f) return 0f;
                float a = Mathf.Atan2(y, x);
                float arm = Mathf.Abs(Mathf.Sin(a * 3f)); // 0 on the six arms
                float line = Mathf.Exp(-Mathf.Pow(arm * d * 9f, 2f)) * (1f - d);
                float twig = Mathf.Exp(-Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 3f + 0.9f)) * d * 12f, 2f)) * Mathf.Exp(-Mathf.Pow((d - 0.55f) * 6f, 2f));
                return Mathf.Clamp01(line * 1.4f + twig * 0.8f + Mathf.Exp(-d * d * 30f));
            });
        }

        /// <summary>Crisp square pixel.</summary>
        public static Texture2D Square(int size)
        {
            return Shape(size, (x, y) => Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) < 0.8f ? 1f : 0f, false);
        }

        /// <summary>Vertical streak, bright in the middle, for sparks and drips.</summary>
        public static Texture2D Streak(int width, int height)
        {
            var tex = NewTexture(width, height);
            var px = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height * 2f - 1f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * 2f - 1f;
                    float a = Mathf.Exp(-u * u * 6f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 1.5f);
                    px[y * width + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D Radial(int size, System.Func<float, float> alphaAtDistance)
        {
            return Shape(size, (x, y) => alphaAtDistance(Mathf.Sqrt(x * x + y * y)));
        }

        /// <summary>Fills a white texture whose alpha comes from f(x, y) with x, y in [-1, 1].</summary>
        private static Texture2D Shape(int size, System.Func<float, float, float> f, bool smooth = true)
        {
            var tex = NewTexture(size, size);
            if (!smooth) tex.filterMode = FilterMode.Point;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(f(u, v)));
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
            };
        }

        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Straight-alpha "a over b".</summary>
        private static Color Over(Color a, Color b)
        {
            float outA = a.a + b.a * (1f - a.a);
            if (outA <= 1e-5f) return new Color(0, 0, 0, 0);
            float r = (a.r * a.a + b.r * b.a * (1f - a.a)) / outA;
            float g = (a.g * a.a + b.g * b.a * (1f - a.a)) / outA;
            float bl = (a.b * a.a + b.b * b.a * (1f - a.a)) / outA;
            return new Color(r, g, bl, outA);
        }
    }
}
