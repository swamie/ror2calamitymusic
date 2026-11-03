using System;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CalamityMusic.UI
{
    /// <summary>
    /// Infernum-style boss introduction: the boss's name slams in from the centre of the screen,
    /// letters spreading out to their places, its title types in underneath between two lens
    /// flares, with a flash and a burst of the theme's particles. Coloured by the boss theme's
    /// banner style (embers for Mithrix/Calamitas, bubbles for the Vagrant, and so on).
    /// </summary>
    internal sealed class BossTitleCard : MonoBehaviour
    {
        private enum Phase { Hidden, In, Hold, Out }

        private const float InTime = 0.9f;
        private const float HoldTime = 2.4f;
        private const float OutTime = 0.8f;
        private const float NameSize = 66f;
        private const float SubSize = 22f;
        private const float MaxNameWidth = 1200f;
        private const int MaxParticles = 72;

        private struct Sample
        {
            public string Name, Subtitle, Slot;
            public Sample(string name, string subtitle, string slot) { Name = name; Subtitle = subtitle; Slot = slot; }
        }

        private static readonly Sample[] Samples =
        {
            new Sample("Mithrix", "King of Nothing", "SupremeCalamitas"),
            new Sample("Wandering Vagrant", "Gentle Protector", "LeviathanAnahita"),
            new Sample("Aurelionite", "Titanic Goldweaver", "Yharon"),
            new Sample("Solus Control Unit", "Corrupted AI", "ExoMechs"),
            new Sample("Stone Titan", "Crisis Vanguard", "Ravager"),
            new Sample("Boss Rush", "Tier 3", null),
        };

        private Func<float> bassLevel;
        private readonly System.Random rng = new System.Random();
        private int sampleIndex;

        private Canvas canvas;
        private RectTransform root, content, nameRoot, subRoot, bandRect, flashRect, streakRect, flareL, flareR, fxRoot;
        private CanvasGroup group;
        private TextLayer nameGlow, nameOutline, nameFace, subGlow, subFace;
        private RawImage band, flashImg, streak, flareLImg, flareRImg;
        private TMP_FontAsset builtFont;
        private ParticleField particles;

        private Phase phase = Phase.Hidden;
        private float phaseTime;
        private BannerStyle style = BannerStyles.Default;
        private Palette pal;
        private float intensity = 1f;
        private float nameW, subW, flareLen;
        private float pulse, flash, shake;
        private float glitchTimer = 1f, glitchLeft;
        private Vector2 glitchOffset;
        private float flickerValue = 1f, flickerTimer;

        // Per-letter pass parameters.
        private Func<int, Vector3, TextLayer.CharFx> nameFx, subFx;
        private float fxAlpha, fxStagger, fxCount;
        private bool fxEffects, fxTintGlow;

        public void Init(Func<float> bass)
        {
            bassLevel = bass;
            nameFx = NameFx;
            subFx = SubFx;
        }

        public bool Showing => phase != Phase.Hidden;

        /// <summary>Shows a title card. <paramref name="intensity"/> above 1 (Infernum songs, Boss Rush) adds more of everything.</summary>
        public void Show(string name, string subtitle, BannerStyle style, float intensity = 1f)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (canvas == null && !TryBuild()) return;
                Begin(name, subtitle, style ?? BannerStyles.Default, intensity);
            }
            catch (Exception e)
            {
                Log.Error("Boss title card failed: " + e);
                Hide();
            }
        }

        /// <summary>Settings-menu "Test Title Card".</summary>
        public void ShowSample()
        {
            var s = Samples[sampleIndex++ % Samples.Length];
            var st = s.Slot == null ? BannerStyles.BossRush : BannerStyles.ForSlot(s.Slot);
            Show(s.Name, s.Subtitle, st, s.Slot == null ? 1.4f : 1f);
        }

        // ------------------------------------------------------------------ construction

        private bool TryBuild()
        {
            try
            {
                Build();
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not build the boss title card: " + e);
                Teardown();
                return false;
            }
        }

        private void Build()
        {
            if (nameFx == null) { nameFx = NameFx; subFx = SubFx; }
            canvas = Ui.NewOverlayCanvas("CalamityMusic_BossTitle", 499);

            root = Ui.NewRect("Root", canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.69f); // upper middle, under the boss health bar
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            content = Ui.NewRect("Content", root);

            bandRect = Ui.NewRect("Band", content);
            band = Ui.AddImage(bandRect, FxTextures.Pill);
            flashRect = Ui.NewRect("Flash", content);
            flashImg = Ui.AddImage(flashRect, FxTextures.Pill);
            fxRoot = Ui.NewRect("Particles", content);
            streakRect = Ui.NewRect("Streak", content);
            streak = Ui.AddImage(streakRect, FxTextures.Underline);

            nameRoot = Ui.NewRect("Name", content);
            nameGlow = TextLayer.Create("Glow", nameRoot, NameSize, 0.8f, 0.6f, 6f, TextAlignmentOptions.Midline);
            nameOutline = TextLayer.Create("Outline", nameRoot, NameSize, 0.4f, 0.04f, 6f, TextAlignmentOptions.Midline);
            nameFace = TextLayer.Create("Face", nameRoot, NameSize, 0.05f, 0f, 6f, TextAlignmentOptions.Midline);

            subRoot = Ui.NewRect("Subtitle", content);
            subGlow = TextLayer.Create("Glow", subRoot, SubSize, 0.7f, 0.6f, 14f, TextAlignmentOptions.Midline);
            subFace = TextLayer.Create("Face", subRoot, SubSize, 0.1f, 0.02f, 14f, TextAlignmentOptions.Midline);

            flareL = Ui.NewRect("FlareL", content);
            flareLImg = Ui.AddImage(flareL, FxTextures.Flare);
            flareR = Ui.NewRect("FlareR", content);
            flareRImg = Ui.AddImage(flareR, FxTextures.Flare);
            // Pivot at the hot end so the flares grow outward from beside the subtitle.
            flareL.pivot = new Vector2(0f, 0.5f);
            flareR.pivot = new Vector2(0f, 0.5f);

            particles = new ParticleField(fxRoot, MaxParticles) { Spread = 2.2f };
            ApplyFont(Ui.FindFont());
        }

        private void ApplyFont(TMP_FontAsset font)
        {
            if (!font) return;
            builtFont = font;
            nameGlow.ApplyFont(font);
            nameOutline.ApplyFont(font);
            nameFace.ApplyFont(font);
            subGlow.ApplyFont(font);
            subFace.ApplyFont(font);
        }

        // ------------------------------------------------------------------ showing

        private void Begin(string name, string subtitle, BannerStyle st, float power)
        {
            style = st;
            pal = Palette.From(st);
            intensity = Mathf.Clamp(power, 0.5f, 2f);
            if (HGTextMeshProUGUI.defaultLanguageFont && builtFont != HGTextMeshProUGUI.defaultLanguageFont)
                ApplyFont(HGTextMeshProUGUI.defaultLanguageFont);

            string sub = string.IsNullOrEmpty(subtitle) ? "" : subtitle.ToUpperInvariant();
            float size = NameSize;
            nameFace.Text.fontSize = size;
            float natural = nameFace.Text.GetPreferredValues(name).x;
            if (natural > MaxNameWidth) size = Mathf.Max(34f, size * MaxNameWidth / natural);

            nameGlow.Set(name, size, pal.Glow);
            nameOutline.Set(name, size, pal.Outline);
            nameFace.Set(name, size, pal.Fill);
            subGlow.Set(sub, SubSize, pal.Glow);
            subFace.Set(sub, SubSize, Color.Lerp(pal.Fill, pal.Accent, 0.3f));

            nameW = Mathf.Max(60f, nameFace.Text.GetPreferredValues(name).x);
            subW = sub.Length > 0 ? subFace.Text.GetPreferredValues(sub).x : 0f;
            float nameH = size * 1.3f;
            float subY = -nameH / 2f - SubSize * 0.55f;

            nameRoot.sizeDelta = new Vector2(nameW + 80f, nameH);
            nameRoot.anchoredPosition = Vector2.zero;
            subRoot.sizeDelta = new Vector2(subW + 60f, SubSize * 1.6f);
            subRoot.anchoredPosition = new Vector2(0f, subY);
            subRoot.gameObject.SetActive(sub.Length > 0);

            float span = Mathf.Max(nameW, subW);
            bandRect.sizeDelta = new Vector2(span + 520f, nameH + SubSize * 2f + 70f);
            bandRect.anchoredPosition = new Vector2(0f, subY * 0.45f);
            flashRect.sizeDelta = new Vector2(nameW + 260f, nameH + 80f);
            streakRect.sizeDelta = new Vector2(nameW * 2.4f + 300f, 6f);
            streakRect.anchoredPosition = Vector2.zero;
            fxRoot.sizeDelta = new Vector2(nameW, nameH);

            flareLen = Mathf.Clamp(span * 0.45f + 120f, 180f, 420f);
            float flareGap = (sub.Length > 0 ? subW / 2f : 0f) + 22f;
            flareR.anchoredPosition = new Vector2(flareGap, subY);
            flareL.anchoredPosition = new Vector2(-flareGap, subY);
            flareL.localScale = new Vector3(-1f, 1f, 1f);
            flareR.localScale = Vector3.one;
            flareLImg.color = flareRImg.color = pal.Glow;
            streak.color = new Color(1f, 1f, 1f, 0f);

            canvas.enabled = true;
            nameGlow.Cache();
            nameOutline.Cache();
            nameFace.Cache();
            subGlow.Cache();
            subFace.Cache();

            particles.KillAll();
            particles.Width = nameW;
            particles.Height = nameH;
            particles.Pal = pal;
            particles.Style = style;

            phase = Phase.In;
            phaseTime = 0f;
            flash = 1f;
            shake = 1f;
            glitchLeft = 0f;
            if (ModConfig.NowPlayingEffects.Value)
            {
                particles.Burst(Mathf.RoundToInt(18 * intensity));
                particles.Burst(Mathf.RoundToInt(10 * intensity), ParticleKind.Sparks);
            }
        }

        private void Hide()
        {
            phase = Phase.Hidden;
            if (canvas) canvas.enabled = false;
            particles?.KillAll();
        }

        // ------------------------------------------------------------------ animation

        private void Update()
        {
            if (canvas == null || phase == Phase.Hidden) return;
            try
            {
                Animate();
            }
            catch (Exception e)
            {
                Log.Error("Boss title card failed: " + e);
                Hide();
            }
        }

        private void Animate()
        {
            bool hudOn = HUD.cvHudEnable == null || HUD.cvHudEnable.value;
            canvas.enabled = hudOn;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            phaseTime += dt;
            bool effects = ModConfig.NowPlayingEffects.Value;

            float bass = ModConfig.NowPlayingPulse.Value ? (bassLevel?.Invoke() ?? 0f) : 0f;
            pulse = Mathf.Lerp(pulse, bass, 1f - Mathf.Exp(-dt * 16f));
            flash = Mathf.Max(0f, flash - dt * 1.6f);
            shake = Mathf.Max(0f, shake - dt * 4f);

            float alpha = 1f, flares = 1f, outT = 0f;
            switch (phase)
            {
                case Phase.In:
                {
                    float t = Mathf.Clamp01(phaseTime / InTime);
                    alpha = Ui.EaseOutCubic(Mathf.Clamp01(t * 3f));
                    flares = Ui.EaseOutCubic(Mathf.Clamp01((phaseTime - 0.25f) / 0.6f));
                    if (phaseTime >= InTime) { phase = Phase.Hold; phaseTime = 0f; }
                    break;
                }
                case Phase.Hold:
                    if (phaseTime >= HoldTime) { phase = Phase.Out; phaseTime = 0f; }
                    break;
                case Phase.Out:
                    outT = Mathf.Clamp01(phaseTime / OutTime);
                    alpha = 1f - Ui.EaseInCubic(outT);
                    flares = 1f - Ui.EaseOutCubic(outT);
                    if (outT >= 1f)
                    {
                        Hide();
                        return;
                    }
                    break;
            }

            if (style.Flicker)
            {
                flickerTimer -= dt;
                if (flickerTimer <= 0f)
                {
                    flickerTimer = 0.05f + (float)rng.NextDouble() * 0.08f;
                    flickerValue = 0.8f + (float)rng.NextDouble() * 0.2f;
                }
            }
            else flickerValue = 1f;

            if (style.Glitch && effects && phase != Phase.Out)
            {
                glitchTimer -= dt;
                if (glitchTimer <= 0f)
                {
                    glitchTimer = 0.4f + (float)rng.NextDouble() * 1.2f;
                    glitchLeft = 0.06f + (float)rng.NextDouble() * 0.08f;
                    glitchOffset = new Vector2((rng.NextDouble() < 0.5 ? -1f : 1f) * (3f + (float)rng.NextDouble() * 6f),
                                               ((float)rng.NextDouble() - 0.5f) * 4f);
                }
            }
            glitchLeft -= dt;
            Vector2 split = glitchLeft > 0f ? glitchOffset : Vector2.zero;

            // Impact shake right as the name lands.
            Vector2 jitter = effects && shake > 0f
                ? new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * (14f * shake * shake * intensity)
                : Vector2.zero;
            group.alpha = alpha;
            content.anchoredPosition = jitter + (glitchLeft > 0f ? new Vector2(split.x * 0.4f, 0f) : Vector2.zero);
            content.localScale = Vector3.one * (1f + 0.025f * pulse);
            root.localScale = Vector3.one * Mathf.Clamp(ModConfig.NowPlayingScale.Value / 100f, 0.3f, 3f);

            float glow = Mathf.Clamp01((0.3f + 0.4f * pulse + 0.9f * flash) * flickerValue);
            band.color = new Color(pal.Backdrop.r, pal.Backdrop.g, pal.Backdrop.b, 0.55f);
            flashImg.color = new Color(pal.Glow.r, pal.Glow.g, pal.Glow.b, glow * 0.55f);
            flashRect.localScale = new Vector3(1f + 0.6f * flash, 1f + 0.2f * flash, 1f);
            streak.color = new Color(Mathf.Lerp(pal.Accent.r, 1f, 0.5f), Mathf.Lerp(pal.Accent.g, 1f, 0.5f), Mathf.Lerp(pal.Accent.b, 1f, 0.5f),
                effects ? flash * flash : 0f);
            streakRect.localScale = new Vector3(0.4f + 0.8f * (1f - flash), 1f, 1f);

            float flareAlpha = (0.75f + 0.25f * pulse) * flickerValue;
            flareL.sizeDelta = flareR.sizeDelta = new Vector2(flareLen * flares, 10f);
            flareLImg.color = flareRImg.color = new Color(pal.Glow.r, pal.Glow.g, pal.Glow.b, flareAlpha);

            nameOutline.Rect.anchoredPosition = split;
            nameGlow.Rect.anchoredPosition = -split;

            // Letters.
            fxEffects = effects;
            float glowAlpha = Mathf.Clamp01((0.6f + 0.35f * pulse + flash) * flickerValue);
            int nameCount = nameFace.Text.textInfo?.characterCount ?? 0;
            fxStagger = Mathf.Min(0.05f, 0.5f / Mathf.Max(1, nameCount));
            fxCount = nameCount;
            fxTintGlow = false;
            fxAlpha = 1f;
            nameFace.Apply(nameFx);
            fxTintGlow = true;
            nameOutline.Apply(nameFx);
            fxTintGlow = false;
            fxAlpha = glowAlpha;
            nameGlow.Apply(nameFx);

            if (subRoot.gameObject.activeSelf)
            {
                int subCount = subFace.Text.textInfo?.characterCount ?? 0;
                fxStagger = Mathf.Min(0.03f, 0.6f / Mathf.Max(1, subCount));
                fxCount = subCount;
                fxAlpha = 1f;
                subFace.Apply(subFx);
                fxAlpha = glowAlpha * 0.8f;
                subGlow.Apply(subFx);
            }

            float rate = effects && phase != Phase.Out ? style.Rate * 0.8f * intensity * (1f + 1.5f * pulse) : 0f;
            particles.Update(dt, rate);
        }

        /// <summary>Name letters: slam in from the centre outward, drift apart slowly, then spread and fade.</summary>
        private TextLayer.CharFx NameFx(int i, Vector3 centre)
        {
            var f = TextLayer.CharFx.Identity;
            float mid = (fxCount - 1) / 2f;
            float order = Mathf.Abs(i - mid); // centre letters first
            if (!fxEffects)
            {
                float plain = phase == Phase.In ? Mathf.Clamp01(phaseTime / 0.25f) : phase == Phase.Out ? 1f - Mathf.Clamp01(phaseTime / OutTime) : 1f;
                f.Alpha = fxAlpha * plain;
                return f;
            }

            float spread = 0f, a = 1f;
            switch (phase)
            {
                case Phase.In:
                {
                    float k = Mathf.Clamp01((phaseTime - order * fxStagger) / 0.4f);
                    float e = Ui.EaseOutCubic(k);
                    a = Mathf.Clamp01(k * 2.5f);
                    f.Scale = Mathf.LerpUnclamped(2.4f, 1f, Ui.EaseOutBack(k));
                    spread = -(1f - e) * 0.8f; // starts squeezed into the middle
                    f.TintAmount = (1f - k) * 0.9f;
                    break;
                }
                case Phase.Hold:
                    spread = 0.035f * Ui.EaseOutCubic(Mathf.Clamp01(phaseTime / HoldTime)); // slow cinematic drift
                    break;
                case Phase.Out:
                {
                    float o = Mathf.Clamp01(phaseTime / OutTime);
                    float e = Ui.EaseInCubic(o);
                    spread = 0.035f + 0.45f * e;
                    a = 1f - Mathf.Clamp01(o * 1.3f - (mid - order) / Mathf.Max(1f, fxCount) * 0.4f);
                    f.ScaleX = 1f + 0.8f * e;
                    break;
                }
            }
            f.OffsetX = centre.x * spread;
            f.Alpha = a * fxAlpha;
            f.Tint = fxTintGlow ? pal.Glow : Color.white;
            if (fxTintGlow) f.TintAmount = Mathf.Max(f.TintAmount, flash * 0.6f);
            return f;
        }

        /// <summary>Subtitle letters type in left to right after the name lands.</summary>
        private TextLayer.CharFx SubFx(int i, Vector3 centre)
        {
            var f = TextLayer.CharFx.Identity;
            float a = 1f;
            if (phase == Phase.In || (phase == Phase.Hold && phaseTime < 1f))
            {
                float t = (phase == Phase.In ? phaseTime : InTime + phaseTime) - 0.4f;
                float k = Mathf.Clamp01((t - i * fxStagger) / 0.12f);
                a = k;
                f.OffsetY = 6f * (1f - Ui.EaseOutCubic(k));
                f.Tint = Color.white;
                f.TintAmount = (1f - k) * 0.8f;
            }
            else if (phase == Phase.Out)
            {
                float o = Mathf.Clamp01(phaseTime / OutTime);
                a = 1f - Mathf.Clamp01(o * 1.6f);
                f.OffsetX = centre.x * 0.3f * Ui.EaseInCubic(o);
            }
            if (!fxEffects) a = phase == Phase.Out ? 1f - Mathf.Clamp01(phaseTime / OutTime) : 1f;
            f.Alpha = Mathf.Clamp01(a) * fxAlpha;
            return f;
        }

        private void Teardown()
        {
            foreach (var l in new[] { nameGlow, nameOutline, nameFace, subGlow, subFace })
                if (l?.Material) Destroy(l.Material);
            particles = null;
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
            phase = Phase.Hidden;
        }

        private void OnDestroy() => Teardown();
    }
}
