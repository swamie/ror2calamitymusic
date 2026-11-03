using System;
using System.Collections.Generic;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CalamityMusic.UI
{
    /// <summary>A banner style's colours as Unity colours.</summary>
    internal struct Palette
    {
        public Color Fill, Outline, Glow, Accent, Backdrop;

        public static Palette From(BannerStyle s) => new Palette
        {
            Fill = Hex(s.Fill, Color.white),
            Outline = Hex(s.Outline, Color.blue),
            Glow = Hex(s.Glow, Color.cyan),
            Accent = Hex(s.Accent, Color.white),
            Backdrop = Hex(s.Backdrop, Color.black),
        };

        public static Color Hex(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }

    /// <summary>Textures shared by the banner and title cards, generated once.</summary>
    internal static class FxTextures
    {
        private static Texture2D dot, ring, sparkle, flake, square, streak, pill, underline, flare;
        private static readonly Dictionary<string, Texture2D> notes = new Dictionary<string, Texture2D>();

        public static Texture2D Dot => dot ? dot : dot = TextureFactory.SoftDot(32);
        public static Texture2D Ring => ring ? ring : ring = TextureFactory.Ring(32);
        public static Texture2D Sparkle => sparkle ? sparkle : sparkle = TextureFactory.Sparkle(48);
        public static Texture2D Flake => flake ? flake : flake = TextureFactory.Flake(48);
        public static Texture2D Square => square ? square : square = TextureFactory.Square(8);
        public static Texture2D Streak => streak ? streak : streak = TextureFactory.Streak(12, 48);
        public static Texture2D Pill => pill ? pill : pill = TextureFactory.SoftPill(256, 64);
        public static Texture2D Underline => underline ? underline : underline = TextureFactory.Underline(256, 8);
        public static Texture2D Flare => flare ? flare : flare = TextureFactory.Flare(256, 16);

        public static Texture2D Note(BannerStyle s)
        {
            if (!notes.TryGetValue(s.Name, out var tex) || !tex)
            {
                tex = TextureFactory.MusicNote(128, Palette.Hex(s.Fill, Color.white), Palette.Hex(s.Outline, Color.blue), Palette.Hex(s.Glow, Color.cyan));
                notes[s.Name] = tex;
            }
            return tex;
        }
    }

    /// <summary>Small UI helpers shared by the overlays.</summary>
    internal static class Ui
    {
        public static Canvas NewOverlayCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.enabled = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        public static RawImage AddImage(RectTransform rt, Texture tex)
        {
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        public static TMP_FontAsset FindFont()
        {
            if (HGTextMeshProUGUI.defaultLanguageFont) return HGTextMeshProUGUI.defaultLanguageFont;
            try
            {
                if (TMP_Settings.defaultFontAsset) return TMP_Settings.defaultFontAsset;
            }
            catch
            {
                // No TMP settings asset in this build.
            }
            var any = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            return any.Length > 0 ? any[0] : null;
        }

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float EaseInCubic(float t) => t * t * t;

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }

    /// <summary>
    /// One layer of text (glow, outline or face). Outline and glow are copies of the text with the
    /// glyphs dilated in the distance field, which only uses base SDF shader properties. Per-letter
    /// effects write straight into the cached mesh so TextMeshPro never rebuilds it mid-animation.
    /// </summary>
    internal sealed class TextLayer
    {
        public RectTransform Rect;
        public TextMeshProUGUI Text;
        public Material Material;
        public float Dilate, Softness;
        public TMP_MeshInfo[] Original;

        public struct CharFx
        {
            public float Alpha, Scale, ScaleX, OffsetX, OffsetY, TintAmount;
            public Color Tint;
            public static CharFx Identity => new CharFx { Alpha = 1f, Scale = 1f, ScaleX = 1f };
        }

        public static TextLayer Create(string name, RectTransform parent, float size, float dilate, float softness, float spacing,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Ui.NewRect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = alignment;
            t.characterSpacing = spacing;
            t.richText = false; // song titles can contain '<'
            t.raycastTarget = false;
            t.extraPadding = true;
            return new TextLayer { Rect = rt, Text = t, Dilate = dilate, Softness = softness };
        }

        public void ApplyFont(TMP_FontAsset font)
        {
            if (!font) return;
            Text.font = font;
            if (Material) UnityEngine.Object.Destroy(Material);
            Material = new Material(font.material);
            if (Material.HasProperty("_FaceColor")) Material.SetColor("_FaceColor", Color.white); // tinted by vertex colour
            if (Material.HasProperty("_OutlineWidth")) Material.SetFloat("_OutlineWidth", 0f);
            if (Material.HasProperty("_FaceDilate")) Material.SetFloat("_FaceDilate", Dilate);
            if (Material.HasProperty("_OutlineSoftness")) Material.SetFloat("_OutlineSoftness", Softness);
            Material.DisableKeyword("UNDERLAY_ON");
            Material.DisableKeyword("GLOW_ON");
            Text.fontSharedMaterial = Material;
        }

        public void Set(string text, float size, Color color)
        {
            Text.text = text;
            Text.fontSize = size;
            Text.color = color;
        }

        /// <summary>Builds the mesh now and remembers it as the base for per-letter effects.</summary>
        public void Cache()
        {
            Rect.anchoredPosition = Vector2.zero;
            if (!Rect.gameObject.activeInHierarchy)
            {
                Original = null;
                return;
            }
            Text.ForceMeshUpdate();
            Original = Text.textInfo.CopyMeshInfoVertexData();
        }

        /// <summary>Applies an effect to every visible letter; <paramref name="fx"/> gets the letter index and its centre.</summary>
        public void Apply(Func<int, Vector3, CharFx> fx)
        {
            var info = Text.textInfo;
            if (Original == null || info == null) return;
            int count = info.characterCount;
            for (int i = 0; i < count; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int m = ci.materialReferenceIndex, v = ci.vertexIndex;
                if (m >= Original.Length || m >= info.meshInfo.Length) continue;
                var srcV = Original[m].vertices;
                var srcC = Original[m].colors32;
                var dstV = info.meshInfo[m].vertices;
                var dstC = info.meshInfo[m].colors32;
                if (srcV == null || dstV == null || srcC == null || dstC == null || v + 3 >= srcV.Length || v + 3 >= dstV.Length) continue;

                Vector3 centre = (srcV[v] + srcV[v + 2]) * 0.5f;
                var f = fx(i, centre);
                byte alpha = (byte)Mathf.Clamp(f.Alpha * 255f, 0f, 255f);
                for (int j = 0; j < 4; j++)
                {
                    var d = srcV[v + j] - centre;
                    dstV[v + j] = centre + new Vector3(d.x * f.Scale * f.ScaleX + f.OffsetX, d.y * f.Scale + f.OffsetY, 0f);
                    Color32 c = srcC[v + j];
                    dstC[v + j] = new Color32(
                        (byte)Mathf.Lerp(c.r, f.Tint.r * 255f, f.TintAmount),
                        (byte)Mathf.Lerp(c.g, f.Tint.g * 255f, f.TintAmount),
                        (byte)Mathf.Lerp(c.b, f.Tint.b * 255f, f.TintAmount),
                        (byte)(c.a * alpha / 255));
                }
            }
            Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }
    }

    /// <summary>
    /// A pool of themed particles (embers, bubbles, snow, stars, void motes, sparks...) living in a
    /// RectTransform, positioned relative to its centre within a width x height area.
    /// </summary>
    internal sealed class ParticleField
    {
        private sealed class P
        {
            public RectTransform Rt;
            public RawImage Img;
            public bool Alive;
            public ParticleKind Kind;
            public Vector2 Pos, Vel;
            public float Age, Life, Size, Rot, Spin, Phase, Alpha, Stretch;
            public Color Col;
        }

        private readonly List<P> pool = new List<P>();
        private readonly System.Random rng = new System.Random();
        private float carry;

        public float Width = 200f, Height = 60f;
        public Palette Pal;
        public BannerStyle Style = BannerStyles.Default;
        public float Spread = 1f; // how far outside the area particles may start

        public ParticleField(RectTransform root, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var rt = Ui.NewRect("p", root);
                var img = Ui.AddImage(rt, FxTextures.Dot);
                img.enabled = false;
                pool.Add(new P { Rt = rt, Img = img });
            }
        }

        public void KillAll()
        {
            foreach (var p in pool) Kill(p);
            carry = 0f;
        }

        public void Burst(int n, ParticleKind? kind = null)
        {
            for (int i = 0; i < n; i++) Spawn(kind ?? Style.Particles);
        }

        public void Update(float dt, float spawnRate)
        {
            carry += spawnRate * dt;
            while (carry >= 1f)
            {
                carry -= 1f;
                Spawn(Style.Particles);
            }

            foreach (var p in pool)
            {
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    Kill(p);
                    continue;
                }
                float life = p.Age / p.Life;
                float a = p.Alpha * Mathf.Clamp01(life / 0.15f) * Mathf.Clamp01((1f - life) / 0.4f);

                switch (p.Kind)
                {
                    case ParticleKind.Embers:
                        p.Vel.x += Mathf.Sin(p.Phase + p.Age * 5f) * 30f * dt;
                        p.Vel.y += 20f * dt;
                        a *= 0.7f + 0.3f * Mathf.Sin(p.Phase + p.Age * 23f);
                        break;
                    case ParticleKind.Bubbles:
                        p.Vel.x = Mathf.Sin(p.Phase + p.Age * 3f) * 14f;
                        break;
                    case ParticleKind.Snow:
                    case ParticleKind.Petals:
                        p.Vel.x = Mathf.Sin(p.Phase + p.Age * 1.7f) * 22f - 6f;
                        break;
                    case ParticleKind.Sparkles:
                    case ParticleKind.Stars:
                        a *= 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(p.Phase + p.Age * 6f));
                        break;
                    case ParticleKind.Wisps:
                        p.Vel.y = Mathf.Sin(p.Phase + p.Age * 1.3f) * 12f;
                        a *= 0.55f + 0.45f * Mathf.Sin(p.Phase + p.Age * 2.1f);
                        break;
                    case ParticleKind.Drips:
                        p.Vel.y -= 160f * dt;
                        break;
                    case ParticleKind.Pixels:
                        a *= ((int)((p.Age + p.Phase) * 14f) % 3) == 0 ? 0.15f : 1f;
                        break;
                    case ParticleKind.Void:
                    {
                        Vector2 toCentre = -p.Pos;
                        float dist = toCentre.magnitude;
                        if (dist < 12f) { Kill(p); continue; }
                        p.Vel += toCentre / dist * 220f * dt;
                        p.Size = Mathf.Max(2f, p.Size - 6f * dt);
                        a *= Mathf.Clamp01(dist / 80f);
                        break;
                    }
                    case ParticleKind.Sparks:
                        p.Vel *= 1f - 3f * dt;
                        break;
                }

                p.Pos += p.Vel * dt;
                p.Rot += p.Spin * dt;
                p.Rt.anchoredPosition = p.Pos;
                if (p.Kind == ParticleKind.Sparks || p.Kind == ParticleKind.Drips)
                {
                    // Streaks point along their motion.
                    float ang = Mathf.Atan2(p.Vel.y, p.Vel.x) * Mathf.Rad2Deg - 90f;
                    p.Rt.localRotation = Quaternion.Euler(0f, 0f, ang);
                    float len = Mathf.Clamp(p.Vel.magnitude * 0.06f, 1f, 4f);
                    p.Rt.sizeDelta = new Vector2(p.Size * 0.5f, p.Size * p.Stretch * len);
                }
                else
                {
                    p.Rt.localRotation = Quaternion.Euler(0f, 0f, p.Rot);
                    p.Rt.sizeDelta = new Vector2(p.Size * p.Stretch, p.Size);
                }
                p.Img.color = new Color(p.Col.r, p.Col.g, p.Col.b, Mathf.Clamp01(a));
            }
        }

        private P Free()
        {
            foreach (var p in pool)
                if (!p.Alive) return p;
            return null;
        }

        private float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static void Kill(P p)
        {
            p.Alive = false;
            if (p.Img) p.Img.enabled = false;
        }

        public void Spawn(ParticleKind kind)
        {
            var p = Free();
            if (p == null) return;
            float hw = Width / 2f, hh = Height / 2f, sp = Spread;
            p.Kind = kind;
            p.Age = 0f;
            p.Phase = R(0f, 6.28f);
            p.Rot = R(0f, 360f);
            p.Spin = 0f;
            p.Stretch = 1f;
            p.Col = Color.Lerp(Pal.Glow, Pal.Accent, R(0f, 1f));
            Texture tex = FxTextures.Dot;

            switch (kind)
            {
                case ParticleKind.Embers:
                    p.Pos = new Vector2(R(-hw - 20f * sp, hw + 20f * sp), R(-hh - 6f, -hh + 10f));
                    p.Vel = new Vector2(R(-12f, 12f), R(35f, 85f));
                    p.Size = R(3f, 8f);
                    p.Life = R(0.9f, 1.8f);
                    p.Alpha = R(0.7f, 1f);
                    break;
                case ParticleKind.Sparkles:
                    tex = FxTextures.Sparkle;
                    p.Pos = new Vector2(R(-hw - 30f * sp, hw + 30f * sp), R(-hh - 14f * sp, hh + 14f * sp));
                    p.Vel = new Vector2(R(-6f, 6f), R(4f, 16f));
                    p.Size = R(8f, 18f);
                    p.Spin = R(-40f, 40f);
                    p.Life = R(0.8f, 1.6f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                case ParticleKind.Bubbles:
                    tex = FxTextures.Ring;
                    p.Pos = new Vector2(R(-hw - 10f * sp, hw + 10f * sp), R(-hh - 10f, -hh + 6f));
                    p.Vel = new Vector2(0f, R(22f, 48f));
                    p.Size = R(6f, 15f);
                    p.Life = R(1.6f, 2.8f);
                    p.Alpha = R(0.5f, 0.9f);
                    p.Col = Color.Lerp(Pal.Glow, Color.white, R(0.2f, 0.6f));
                    break;
                case ParticleKind.Snow:
                    tex = FxTextures.Flake;
                    p.Pos = new Vector2(R(-hw - 20f * sp, hw + 20f * sp), R(hh, hh + 20f));
                    p.Vel = new Vector2(R(-8f, 8f), R(-42f, -20f));
                    p.Size = R(7f, 14f);
                    p.Spin = R(-60f, 60f);
                    p.Life = R(2f, 3.4f);
                    p.Alpha = R(0.6f, 1f);
                    p.Col = Color.Lerp(Color.white, Pal.Glow, R(0f, 0.5f));
                    break;
                case ParticleKind.Stars:
                    if (rng.NextDouble() < 0.12)
                    {
                        // Shooting star.
                        p.Kind = ParticleKind.Sparks;
                        tex = FxTextures.Streak;
                        p.Pos = new Vector2(R(-hw, hw), hh + R(0f, 20f));
                        p.Vel = new Vector2(R(160f, 260f) * (rng.NextDouble() < 0.5 ? -1f : 1f), R(-120f, -70f));
                        p.Size = R(5f, 8f);
                        p.Stretch = 3f;
                        p.Life = R(0.4f, 0.7f);
                        p.Alpha = 1f;
                        p.Col = Pal.Accent;
                        break;
                    }
                    tex = FxTextures.Sparkle;
                    p.Pos = new Vector2(R(-hw - 40f * sp, hw + 40f * sp), R(-hh - 20f * sp, hh + 20f * sp));
                    p.Vel = new Vector2(R(-5f, 5f), R(-5f, 5f));
                    p.Size = R(6f, 16f);
                    p.Spin = R(-25f, 25f);
                    p.Life = R(1.2f, 2.4f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                case ParticleKind.Wisps:
                    p.Pos = new Vector2(R(-hw - 40f * sp, hw + 40f * sp), R(-hh, hh));
                    p.Vel = new Vector2(R(10f, 26f) * (rng.NextDouble() < 0.5 ? -1f : 1f), 0f);
                    p.Size = R(16f, 34f);
                    p.Life = R(2.2f, 3.6f);
                    p.Alpha = R(0.25f, 0.5f);
                    break;
                case ParticleKind.Drips:
                    tex = FxTextures.Streak;
                    p.Pos = new Vector2(R(-hw + 20f, hw), R(-hh * 0.2f, hh * 0.1f));
                    p.Vel = new Vector2(R(-4f, 4f), R(-20f, -5f));
                    p.Size = R(4f, 7f);
                    p.Stretch = 1.2f;
                    p.Life = R(0.8f, 1.4f);
                    p.Alpha = R(0.7f, 1f);
                    break;
                case ParticleKind.Pixels:
                    tex = FxTextures.Square;
                    p.Pos = new Vector2(R(-hw - 30f * sp, hw + 30f * sp), R(-hh - 12f * sp, hh + 12f * sp));
                    p.Vel = new Vector2(R(-40f, 40f), 0f);
                    p.Size = R(3f, 6f);
                    p.Rot = 0f;
                    p.Life = R(0.4f, 1.1f);
                    p.Alpha = R(0.6f, 1f);
                    p.Col = rng.NextDouble() < 0.25 ? Pal.Accent : Pal.Glow;
                    break;
                case ParticleKind.Petals:
                    p.Pos = new Vector2(R(-hw - 20f * sp, hw + 20f * sp), R(hh, hh + 16f));
                    p.Vel = new Vector2(R(-10f, 10f), R(-30f, -14f));
                    p.Size = R(6f, 11f);
                    p.Stretch = R(1.5f, 2.1f);
                    p.Spin = R(-90f, 90f);
                    p.Life = R(2f, 3.2f);
                    p.Alpha = R(0.6f, 0.95f);
                    p.Col = Color.Lerp(Pal.Accent, Color.white, R(0f, 0.5f));
                    break;
                case ParticleKind.Void:
                {
                    float ang = R(0f, 6.28f);
                    p.Pos = new Vector2(Mathf.Cos(ang) * (hw + R(50f, 110f) * sp), Mathf.Sin(ang) * (hh + R(30f, 60f) * sp));
                    p.Vel = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)) * R(20f, 50f); // a little swirl
                    p.Size = R(4f, 9f);
                    p.Life = R(1.4f, 2.4f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                }
                case ParticleKind.Sparks:
                {
                    tex = FxTextures.Streak;
                    float ang = R(0f, 6.28f);
                    p.Pos = new Vector2(R(-hw, hw) * 0.8f, R(-hh, hh) * 0.6f);
                    p.Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(140f, 300f) * sp;
                    p.Size = R(3f, 5f);
                    p.Stretch = 2.2f;
                    p.Life = R(0.25f, 0.5f);
                    p.Alpha = 1f;
                    p.Col = Color.Lerp(Pal.Accent, Pal.Glow, R(0f, 0.4f));
                    break;
                }
            }

            p.Img.texture = tex;
            p.Img.enabled = true;
            p.Alive = true;
            p.Rt.anchoredPosition = p.Pos;
            p.Img.color = new Color(p.Col.r, p.Col.g, p.Col.b, 0f);
        }
    }
}
