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
