using System;
using System.Collections.Generic;
using CalamityMusic.Library;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CalamityMusic.UI
{
    /// <summary>
    /// The "♫ Artist - Title" banner, styled after the Calamity/Infernum music display and themed
    /// per song: each boss or area gets its own colours and effects (embers for Calamitas, bubbles
    /// for the ocean, a void pull for the Devourer, glitches for the Exo Mechs, lightning for the
    /// Storm Weaver...). Letters pop in one by one, a light sweeps across them, everything pulses
    /// and bumps with the bass, and the boss/area name sits underneath.
    ///
    /// The outline and glow are stacked copies of the text with their glyphs dilated in the
    /// distance field, so they render even if the game build stripped TextMeshPro's optional
    /// outline/underlay shader variants. All per-letter animation is done on vertex data, which
    /// never triggers a mesh rebuild.
    /// </summary>
    internal sealed class NowPlayingBanner : MonoBehaviour
    {
        private enum Phase { Hidden, In, Hold, Out }

        private const float InTime = 0.6f;
        private const float OutTime = 0.6f;
        private const float TitleSize = 36f;
        private const float SubtitleSize = 17f;
        private const float HintSize = 25f;
        private const float IconSize = 62f;
        private const float Gap = 14f;
        private const int MaxParticles = 64;
        private const float MaxTitleWidth = 720f; // longer titles shrink to fit

        private struct Sample
        {
            public string Song, Slot;
            public Sample(string song, string slot) { Song = song; Slot = slot; }
        }

        private static readonly Sample[] Samples =
        {
            new Sample("DM DOKURO - Stained, Brutal Calamity", "SupremeCalamitas"),
            new Sample("DM DOKURO - UNIVERSAL COLLAPSE", "DevourerOfGods"),
            new Sample("DM DOKURO - Roar of the Jungle Dragon", "Yharon"),
            new Sample("PinpinNeon - Inferior Fabrications", "ExoMechs"),
            new Sample("DM DOKURO - Servants of the Scourge (Type W)", "StormWeaver"),
            new Sample("DM DOKURO - Forbidden Lullaby", "LeviathanAnahita"),
            new Sample("DM DOKURO - Toxic Wisdom", "OldDuke"),
            new Sample("DM DOKURO - Pest of the Cosmos", "AstrumDeus"),
            new Sample("DM DOKURO - Feral Amalgamation", "Polterghast"),
            new Sample("DM DOKURO - Antarctic Reinforcement", "Cryogen"),
            new Sample("DM DOKURO - Open Frenzy", "Ravager"),
            new Sample("DM DOKURO - Infestation", "Crabulon"),
            new Sample("ENNWAY! - Edenic Whispers", "Biome_EternalGarden"),
            new Sample("Moonburn - nowhere now here", "Biome_Rift"),
            new Sample("DM DOKURO - Threats of the Ocean Floor", "PrimordialWyrm"),
            new Sample("ENNWAY! - PARADISE PARASITE", "AvatarOfEmptiness"),
        };

        private Func<float> bassLevel;
        private readonly System.Random rng = new System.Random();
        private int sampleIndex;

        // UI objects
        private Canvas canvas;
        private RectTransform root, content, textRoot, subRoot, iconRect, auraRect, backRect, lineRect, fxRoot;
        private CanvasGroup group;
        private TextLayer glowLayer, outlineLayer, faceLayer, subGlowLayer, subFaceLayer;
        private RawImage icon, aura, back, line;
        private TMP_FontAsset builtFont;
        private ParticleField particles;

        // Current banner
        private Phase phase = Phase.Hidden;
        private float phaseTime;
        private float holdTime;
        private bool currentHint;
        private bool hasSubtitle;
        private BannerStyle style = BannerStyles.Default;
        private Palette pal;
        private float intensity = 1f;
        private float width, height;
        private TrackInfo queuedTrack;
        private string queuedHint;
        private bool queued;

        // Live effect state
        private float pulse, flash, beat, bassSlow, beatCooldown;
        private float shineStart = -10f;
        private float glitchTimer = 1f, glitchLeft;
        private Vector2 glitchOffset;
        private float flickerValue = 1f, flickerTimer;
        private float slideDir = -1f;
        private BannerPosition appliedPosition = (BannerPosition)(-1);
        private float appliedOffsetX = float.NaN, appliedOffsetY = float.NaN;

        // Per-letter pass parameters (fields so the effect delegate is allocated once).
        private Func<int, Vector3, TextLayer.CharFx> letterFx;
        private float fxDelay, fxOutT, fxAlpha, fxStagger, fxInTime, fxShineX, fxTime;
        private bool fxEffects, fxShineToGlow;

        public void Init(Func<float> bass)
        {
            bassLevel = bass;
            letterFx = LetterFx;
        }

        /// <summary>True while a banner is on screen or about to be.</summary>
        public bool Busy => phase != Phase.Hidden;

        // ------------------------------------------------------------------ public API

        /// <summary>Shows the banner for a song; the style comes from the song's boss/area folder.</summary>
        public void Show(TrackInfo track)
        {
            if (track == null) return;
            Enqueue(track, null);
        }

        public void ShowHint(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Enqueue(null, message);
        }

        /// <summary>Settings-menu "Test Banner": shows the next style each press.</summary>
        public void ShowSample()
        {
            Show(NextSample());
        }

        /// <summary>The next sample song, cycling through every style.</summary>
        public TrackInfo NextSample()
        {
            var s = Samples[sampleIndex++ % Samples.Length];
            return TrackInfo.FromFile("sample/" + s.Song + ".ogg", MusicLibrary.PackCalamity, s.Slot);
        }

        private void Enqueue(TrackInfo track, string hint)
        {
            try
            {
                // Built on first use: the game's font isn't loaded yet when plugins start.
                if (canvas == null && !TryBuild()) return;
                if (phase == Phase.Hidden)
                {
                    Begin(track, hint);
                    return;
                }
                // Something is already showing: let it leave quickly, then show the new one.
                queuedTrack = track;
                queuedHint = hint;
                queued = true;
                if (phase != Phase.Out)
                {
                    phase = Phase.Out;
                    phaseTime = OutTime * 0.4f;
                }
            }
            catch (Exception e)
            {
                Log.Error("Now Playing banner failed: " + e);
                phase = Phase.Hidden;
            }
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
                Log.Error("Could not build the Now Playing banner: " + e);
                Teardown();
                return false;
            }
        }

        private void Build()
        {
            if (letterFx == null) letterFx = LetterFx;
            canvas = Ui.NewOverlayCanvas("CalamityMusic_NowPlaying", 500);

            root = Ui.NewRect("Root", canvas.transform);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            content = Ui.NewRect("Content", root);

            backRect = Ui.NewRect("Backdrop", content);
            back = Ui.AddImage(backRect, FxTextures.Pill);
            auraRect = Ui.NewRect("Aura", content);
            aura = Ui.AddImage(auraRect, FxTextures.Pill);
            fxRoot = Ui.NewRect("Particles", content);
            lineRect = Ui.NewRect("Underline", content);
            line = Ui.AddImage(lineRect, FxTextures.Underline);
            iconRect = Ui.NewRect("Icon", content);
            icon = Ui.AddImage(iconRect, null);

            textRoot = Ui.NewRect("Title", content);
            glowLayer = TextLayer.Create("Glow", textRoot, TitleSize, 0.82f, 0.55f, 2f);
            outlineLayer = TextLayer.Create("Outline", textRoot, TitleSize, 0.42f, 0.04f, 2f);
            faceLayer = TextLayer.Create("Face", textRoot, TitleSize, 0.06f, 0f, 2f);

            subRoot = Ui.NewRect("Subtitle", content);
            subGlowLayer = TextLayer.Create("Glow", subRoot, SubtitleSize, 0.7f, 0.6f, 7f);
            subFaceLayer = TextLayer.Create("Face", subRoot, SubtitleSize, 0.1f, 0.02f, 7f);

            particles = new ParticleField(fxRoot, MaxParticles);

            ApplyFont(Ui.FindFont());
            ApplyPosition(true);
        }

        private IEnumerable<TextLayer> Layers()
        {
            yield return glowLayer;
            yield return outlineLayer;
            yield return faceLayer;
            yield return subGlowLayer;
            yield return subFaceLayer;
        }

        private void ApplyFont(TMP_FontAsset font)
        {
            if (!font) return;
            builtFont = font;
            foreach (var layer in Layers()) layer.ApplyFont(font);
        }

        private void ApplyPosition(bool force)
        {
            var pos = ModConfig.NowPlayingPosition.Value;
            float ox = ModConfig.NowPlayingOffsetX.Value, oy = ModConfig.NowPlayingOffsetY.Value;
            if (!force && pos == appliedPosition && ox == appliedOffsetX && oy == appliedOffsetY) return;
            appliedPosition = pos;
            appliedOffsetX = ox;
            appliedOffsetY = oy;

            Vector2 anchor, offset;
            switch (pos)
            {
                case BannerPosition.TopLeft: anchor = new Vector2(0f, 1f); offset = new Vector2(44f, -170f); break;
                case BannerPosition.TopCenter: anchor = new Vector2(0.5f, 1f); offset = new Vector2(0f, -205f); break;
                case BannerPosition.TopRight: anchor = new Vector2(1f, 1f); offset = new Vector2(-44f, -300f); break;
                case BannerPosition.MiddleLeft: anchor = new Vector2(0f, 0.5f); offset = new Vector2(44f, 140f); break;
                case BannerPosition.MiddleRight: anchor = new Vector2(1f, 0.5f); offset = new Vector2(-44f, 140f); break;
                case BannerPosition.BottomCenter: anchor = new Vector2(0.5f, 0f); offset = new Vector2(0f, 250f); break;
                case BannerPosition.BottomRight: anchor = new Vector2(1f, 0f); offset = new Vector2(-44f, 330f); break;
                // Bottom left, just above the health bar: the abilities live in the bottom right.
                default: anchor = new Vector2(0f, 0f); offset = new Vector2(44f, 170f); break;
            }
            root.anchorMin = root.anchorMax = anchor;
            root.pivot = anchor;
            root.anchoredPosition = offset + new Vector2(ox, oy);
            content.anchorMin = content.anchorMax = anchor;
            content.pivot = anchor;
            slideDir = anchor.x > 0.75f ? 1f : -1f;
        }

        // ------------------------------------------------------------------ showing a banner

        private void Begin(TrackInfo track, string hint)
        {
            currentHint = hint != null;
            style = currentHint ? BannerStyles.Default : BannerStyles.For(track);
            pal = Palette.From(style);
            intensity = currentHint ? 0.35f
                : track != null && track.Pack.Equals(MusicLibrary.PackInfernum, StringComparison.OrdinalIgnoreCase) ? 1.45f : 1f;

            if (HGTextMeshProUGUI.defaultLanguageFont && builtFont != HGTextMeshProUGUI.defaultLanguageFont)
                ApplyFont(HGTextMeshProUGUI.defaultLanguageFont); // the language changed since we built

            string title = currentHint ? hint : track.Display;
            string subtitle = currentHint || !ModConfig.NowPlayingThemeName.Value ? null : BannerStyles.ThemeName(track);
            hasSubtitle = !string.IsNullOrEmpty(subtitle);
            float titleSize = currentHint ? HintSize : TitleSize;
            float iconSize = currentHint ? 42f : IconSize;

            // Long titles ("Servants of the Scourge (Type W)" plus artist) shrink instead of running off screen.
            faceLayer.Text.fontSize = titleSize;
            float natural = faceLayer.Text.GetPreferredValues(title).x;
            if (natural > MaxTitleWidth) titleSize = Mathf.Max(22f, titleSize * MaxTitleWidth / natural);

            glowLayer.Set(title, titleSize, pal.Glow);
            outlineLayer.Set(title, titleSize, pal.Outline);
            faceLayer.Set(title, titleSize, pal.Fill);
            string sub = hasSubtitle ? subtitle.ToUpperInvariant() : "";
            subGlowLayer.Set(sub, SubtitleSize, pal.Glow);
            subFaceLayer.Set(sub, SubtitleSize, Color.Lerp(pal.Fill, pal.Glow, 0.35f));

            // Measure without building the mesh, lay everything out, then build and cache the meshes.
            float titleW = Mathf.Max(40f, faceLayer.Text.GetPreferredValues(title).x);
            float subW = hasSubtitle ? subFaceLayer.Text.GetPreferredValues(sub).x : 0f;
            float titleH = titleSize * 1.3f;
            float subH = hasSubtitle ? SubtitleSize * 1.6f : 0f;
            float textW = Mathf.Max(titleW, subW);
            width = iconSize + Gap + textW;
            height = Mathf.Max(iconSize, titleH + subH);
            root.sizeDelta = content.sizeDelta = new Vector2(width, height);

            float left = -width / 2f;
            float textX = left + iconSize + Gap + textW / 2f;
            float titleY = hasSubtitle ? height / 2f - titleH / 2f : 0f;
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = new Vector2(left + iconSize / 2f, 0f);
            textRoot.sizeDelta = new Vector2(textW + 6f, titleH);
            textRoot.anchoredPosition = new Vector2(textX, titleY);
            subRoot.sizeDelta = new Vector2(textW + 6f, Mathf.Max(1f, subH));
            subRoot.anchoredPosition = new Vector2(textX, titleY - titleH / 2f - subH / 2f + 2f);
            subRoot.gameObject.SetActive(hasSubtitle);

            backRect.sizeDelta = new Vector2(width + 170f, height + 50f);
            auraRect.sizeDelta = new Vector2(width + 100f, height + 30f);
            fxRoot.sizeDelta = new Vector2(width, height);
            lineRect.sizeDelta = new Vector2(textW + 24f, 5f);
            lineRect.anchoredPosition = new Vector2(textX, titleY - titleH / 2f + 1f);

            icon.texture = FxTextures.Note(style);
            back.color = new Color(pal.Backdrop.r, pal.Backdrop.g, pal.Backdrop.b, 0f);
            line.color = pal.Glow;

            canvas.enabled = true;
            foreach (var layer in Layers()) layer.Cache();

            particles.KillAll();
            particles.Width = width;
            particles.Height = height;
            particles.Pal = pal;
            particles.Style = style;
            holdTime = currentHint ? 7f : Mathf.Max(1f, ModConfig.NowPlayingSeconds.Value);
            phase = Phase.In;
            phaseTime = 0f;
            flash = currentHint ? 0.3f : 1f;
            shineStart = 0.35f; // the first light sweep runs as the letters land
            if (!currentHint) Burst(10);
        }

        // ------------------------------------------------------------------ animation

        private void Update()
        {
            if (canvas == null) return;
            try
            {
                Animate();
            }
            catch (Exception e)
            {
                Log.Error("Now Playing banner failed: " + e);
                phase = Phase.Hidden;
                canvas.enabled = false;
            }
        }

        private void Animate()
        {
            ApplyPosition(false);
            bool hudOn = HUD.cvHudEnable == null || HUD.cvHudEnable.value;
            canvas.enabled = hudOn && phase != Phase.Hidden;
            if (phase == Phase.Hidden) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            phaseTime += dt;
            bool effects = ModConfig.NowPlayingEffects.Value;

            // Bass analysis: smoothed level, plus "beats" when it jumps above its slow average.
            float bass = !currentHint && ModConfig.NowPlayingPulse.Value ? (bassLevel?.Invoke() ?? 0f) : 0f;
            pulse = Mathf.Lerp(pulse, bass, 1f - Mathf.Exp(-dt * 18f));
            bassSlow = Mathf.Lerp(bassSlow, bass, 1f - Mathf.Exp(-dt * 2.5f));
            beatCooldown -= dt;
            if (bass - bassSlow > 0.16f && beatCooldown <= 0f && phase != Phase.Out)
            {
                beatCooldown = 0.22f;
                OnBeat(effects);
            }
            beat = Mathf.Max(0f, beat - dt * 3.5f);
            flash = Mathf.Max(0f, flash - dt * 1.8f);

            float alpha = 1f, x = 0f, scale = 1f, lineFill = 1f, outT = 0f;
            switch (phase)
            {
                case Phase.In:
                {
                    float t = Mathf.Clamp01(phaseTime / InTime);
                    alpha = Ui.EaseOutCubic(Mathf.Clamp01(t * 1.6f));
                    x = slideDir * 70f * (1f - Ui.EaseOutCubic(t));
                    scale = Mathf.LerpUnclamped(0.92f, 1f, Ui.EaseOutBack(t));
                    lineFill = Ui.EaseOutCubic(Mathf.Clamp01((phaseTime - 0.2f) / 0.55f));
                    if (phaseTime >= InTime + 0.4f) { phase = Phase.Hold; phaseTime = 0f; }
                    break;
                }
                case Phase.Hold:
                    if (phaseTime - shineStart > 5f) shineStart = phaseTime; // a gentle sweep every few seconds
                    if (phaseTime >= holdTime) { phase = Phase.Out; phaseTime = 0f; }
                    break;
                case Phase.Out:
                {
                    outT = Mathf.Clamp01(phaseTime / OutTime);
                    float e = Ui.EaseInCubic(outT);
                    alpha = 1f - e;
                    x = -slideDir * 40f * e;
                    scale = 1f - 0.03f * e;
                    lineFill = 1f - Ui.EaseOutCubic(outT);
                    if (outT >= 1f)
                    {
                        phase = Phase.Hidden;
                        group.alpha = 0f;
                        canvas.enabled = false;
                        particles.KillAll();
                        if (queued)
                        {
                            queued = false;
                            Begin(queuedTrack, queuedHint);
                        }
                        return;
                    }
                    break;
                }
            }

            // Fire flicker and glitch timers.
            if (style.Flicker)
            {
                flickerTimer -= dt;
                if (flickerTimer <= 0f)
                {
                    flickerTimer = 0.05f + (float)rng.NextDouble() * 0.08f;
                    flickerValue = 0.82f + (float)rng.NextDouble() * 0.18f;
                }
            }
            else flickerValue = 1f;

            if (style.Glitch && effects && phase != Phase.Out)
            {
                glitchTimer -= dt;
                if (glitchTimer <= 0f)
                {
                    glitchTimer = 0.7f + (float)rng.NextDouble() * 2.2f;
                    StartGlitch();
                }
            }
            glitchLeft -= dt;
            Vector2 split = glitchLeft > 0f ? glitchOffset : Vector2.zero;

            group.alpha = alpha;
            content.anchoredPosition = new Vector2(x, 0f) + (glitchLeft > 0f ? new Vector2(glitchOffset.x * 0.3f, 0f) : Vector2.zero);
            content.localScale = Vector3.one * scale * (1f + 0.03f * pulse + 0.02f * beat);
            root.localScale = Vector3.one * Mathf.Clamp(ModConfig.NowPlayingScale.Value / 100f, 0.3f, 3f);

            // Glow, aura and backdrop react to the bass, lightning and fire flicker.
            float glow = Mathf.Clamp01((0.25f + 0.45f * pulse + 0.35f * beat + 0.6f * flash) * flickerValue);
            aura.color = new Color(pal.Glow.r, pal.Glow.g, pal.Glow.b, glow * 0.5f);
            var backCol = Color.Lerp(pal.Backdrop, pal.Glow, flash * (style.Lightning ? 0.45f : 0.12f));
            back.color = new Color(backCol.r, backCol.g, backCol.b, 0.6f);
            line.color = new Color(pal.Glow.r, pal.Glow.g, pal.Glow.b, 0.7f + 0.3f * pulse);
            lineRect.localScale = new Vector3(lineFill, 1f, 1f);
            iconRect.localScale = Vector3.one * (1f + 0.08f * pulse + 0.12f * beat + 0.1f * flash);
            iconRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 2.2f) * 4f + beat * 8f * slideDir);
            outlineLayer.Rect.anchoredPosition = split;
            glowLayer.Rect.anchoredPosition = -split;

            float glowAlpha = Mathf.Clamp01((0.55f + 0.35f * pulse + 0.3f * beat + flash) * flickerValue);
            AnimateText(faceLayer, 0f, outT, effects, 1f, false);
            AnimateText(outlineLayer, 0f, outT, effects, 1f, true);
            AnimateText(glowLayer, 0f, outT, effects, glowAlpha, false);
            if (hasSubtitle)
            {
                AnimateText(subFaceLayer, 0.3f, outT, effects, 1f, false);
                AnimateText(subGlowLayer, 0.3f, outT, effects, glowAlpha * 0.8f, false);
            }

            float rate = effects && phase != Phase.Out ? style.Rate * intensity * (1f + 2f * pulse) : 0f;
            particles.Update(dt, rate);
        }

        private void OnBeat(bool effects)
        {
            beat = 1f;
            if (!effects) return;
            if (style.Lightning && rng.NextDouble() < 0.55)
            {
                flash = 1f;
                Burst(8, ParticleKind.Sparks);
            }
            else
            {
                Burst(style.Particles == ParticleKind.Wisps ? 1 : 4);
            }
            if (style.Glitch && rng.NextDouble() < 0.35) StartGlitch();
        }

        private void StartGlitch()
        {
            glitchLeft = 0.07f + (float)rng.NextDouble() * 0.06f;
            glitchOffset = new Vector2(((float)rng.NextDouble() < 0.5f ? -1f : 1f) * (2f + (float)rng.NextDouble() * 4f),
                                       ((float)rng.NextDouble() - 0.5f) * 3f);
        }

        private void Burst(int n, ParticleKind? kind = null)
        {
            if (!ModConfig.NowPlayingEffects.Value) return;
            particles.Burst(n, kind);
        }

        /// <summary>
        /// Per-letter effects, written straight into the mesh: letters pop in one after another,
        /// a band of light sweeps across, they bob to the bass, then float away on the way out.
        /// </summary>
        private void AnimateText(TextLayer layer, float delay, float outT, bool effects, float alphaScale, bool shineToGlow)
        {
            int count = layer.Text.textInfo?.characterCount ?? 0;
            if (count == 0) return;
            fxDelay = delay;
            fxOutT = outT;
            fxEffects = effects;
            fxAlpha = alphaScale;
            fxShineToGlow = shineToGlow;
            fxStagger = Mathf.Min(0.035f, 0.55f / Mathf.Max(1, count));
            fxInTime = phase == Phase.In ? phaseTime - delay : 99f;
            fxShineX = (phase == Phase.Out ? -1f : (phaseTime - shineStart) / 0.9f) * (width + 240f) - width / 2f - 120f;
            fxTime = Time.unscaledTime;
            layer.Apply(letterFx);
        }

        private TextLayer.CharFx LetterFx(int i, Vector3 centre)
        {
            var f = TextLayer.CharFx.Identity;
            float a = 1f, dy = 0f;
            if (fxEffects)
            {
                // Entrance: each letter fades and drops into place, slightly larger at first.
                float k = Mathf.Clamp01((fxInTime - i * fxStagger) / 0.22f);
                a = Ui.EaseOutCubic(k);
                f.Scale = 1f + 0.55f * (1f - Ui.EaseOutBack(k));
                dy = 14f * (1f - Ui.EaseOutCubic(k));

                // Bob to the bass, as a wave running along the word.
                dy += Mathf.Sin(fxTime * 9f - i * 0.55f) * (1.2f * pulse + 4f * beat);

                // Exit: letters fade from the start of the word and float upward.
                if (phase == Phase.Out)
                {
                    float ko = Mathf.Clamp01((fxOutT * 1.6f) - i * fxStagger * 0.8f);
                    a *= 1f - ko;
                    dy += 18f * ko;
                }
            }
            else if (phase == Phase.Out)
            {
                a = 1f - fxOutT;
            }

            float shine = fxEffects ? Mathf.Exp(-Mathf.Pow((centre.x - fxShineX) / 46f, 2f)) : 0f;
            f.Tint = fxShineToGlow ? pal.Glow : Color.white;
            f.TintAmount = fxShineToGlow ? shine * 0.9f : shine * 0.5f;
            f.Alpha = a * fxAlpha;
            f.OffsetY = dy;
            return f;
        }

        private void Teardown()
        {
            if (glowLayer != null)
                foreach (var layer in Layers())
                    if (layer?.Material) Destroy(layer.Material);
            particles = null;
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
        }

        private void OnDestroy() => Teardown();
    }
}
