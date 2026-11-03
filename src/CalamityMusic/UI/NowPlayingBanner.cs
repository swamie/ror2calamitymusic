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

        private struct Palette
        {
            public Color Fill, Outline, Glow, Accent, Backdrop;
        }

        private sealed class TextLayer
        {
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public Material Material;
            public float Dilate, Softness;
            public TMP_MeshInfo[] Original;
        }

        private sealed class Particle
        {
            public RectTransform Rt;
            public RawImage Img;
            public bool Alive;
            public ParticleKind Kind;
            public Vector2 Pos, Vel;
            public float Age, Life, Size, Rot, Spin, Phase, Alpha, Stretch;
            public Color Col;
        }

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
        private readonly List<Particle> particles = new List<Particle>();
        private Texture2D texDot, texRing, texSparkle, texFlake, texSquare, texStreak, texPill;
        private readonly Dictionary<string, Texture2D> noteTextures = new Dictionary<string, Texture2D>();

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
        private float pulse, flash, beat, bassSlow, beatCooldown, spawnCarry;
        private float shineStart = -10f;
        private float glitchTimer = 1f, glitchLeft;
        private Vector2 glitchOffset;
        private float flickerValue = 1f, flickerTimer;
        private float slideDir = -1f;
        private BannerPosition appliedPosition = (BannerPosition)(-1);
        private float appliedOffsetX = float.NaN, appliedOffsetY = float.NaN;

        public void Init(Func<float> bass)
        {
            bassLevel = bass;
        }

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
            var s = Samples[sampleIndex++ % Samples.Length];
            Show(TrackInfo.FromFile("sample/" + s.Song + ".ogg", MusicLibrary.PackCalamity, s.Slot));
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

        private static TMP_FontAsset FindFont()
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

        private void Build()
        {
            var go = new GameObject("CalamityMusic_NowPlaying");
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvas.enabled = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            texPill = TextureFactory.SoftPill(256, 64);
            texDot = TextureFactory.SoftDot(32);
            texRing = TextureFactory.Ring(32);
            texSparkle = TextureFactory.Sparkle(48);
            texFlake = TextureFactory.Flake(48);
            texSquare = TextureFactory.Square(8);
            texStreak = TextureFactory.Streak(12, 48);

            root = NewRect("Root", go.transform);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            content = NewRect("Content", root);

            backRect = NewRect("Backdrop", content);
            back = AddImage(backRect, texPill);
            auraRect = NewRect("Aura", content);
            aura = AddImage(auraRect, texPill);
            fxRoot = NewRect("Particles", content);
            lineRect = NewRect("Underline", content);
            line = AddImage(lineRect, TextureFactory.Underline(256, 8));
            iconRect = NewRect("Icon", content);
            icon = AddImage(iconRect, null);

            textRoot = NewRect("Title", content);
            glowLayer = NewLayer("Glow", textRoot, TitleSize, 0.82f, 0.55f, 2f);
            outlineLayer = NewLayer("Outline", textRoot, TitleSize, 0.42f, 0.04f, 2f);
            faceLayer = NewLayer("Face", textRoot, TitleSize, 0.06f, 0f, 2f);

            subRoot = NewRect("Subtitle", content);
            subGlowLayer = NewLayer("Glow", subRoot, SubtitleSize, 0.7f, 0.6f, 7f);
            subFaceLayer = NewLayer("Face", subRoot, SubtitleSize, 0.1f, 0.02f, 7f);

            for (int i = 0; i < MaxParticles; i++)
            {
                var rt = NewRect("p", fxRoot);
                var img = AddImage(rt, texDot);
                img.enabled = false;
                particles.Add(new Particle { Rt = rt, Img = img });
            }

            ApplyFont(FindFont());
            ApplyPosition(true);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private static RawImage AddImage(RectTransform rt, Texture tex)
        {
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        private static TextLayer NewLayer(string name, RectTransform parent, float size, float dilate, float softness, float spacing)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.characterSpacing = spacing;
            t.richText = false; // song titles can contain '<'
            t.raycastTarget = false;
            t.extraPadding = true;
            return new TextLayer { Rect = rt, Text = t, Dilate = dilate, Softness = softness };
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
            foreach (var layer in Layers())
            {
                layer.Text.font = font;
                if (layer.Material) Destroy(layer.Material);
                layer.Material = new Material(font.material);
                var m = layer.Material;
                if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", Color.white); // tinted per layer by vertex colour
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
                if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", layer.Dilate);
                if (m.HasProperty("_OutlineSoftness")) m.SetFloat("_OutlineSoftness", layer.Softness);
                m.DisableKeyword("UNDERLAY_ON");
                m.DisableKeyword("GLOW_ON");
                layer.Text.fontSharedMaterial = m;
            }
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

        private static Color Hex(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        private Texture2D NoteFor(BannerStyle s)
        {
            if (!noteTextures.TryGetValue(s.Name, out var tex))
            {
                tex = TextureFactory.MusicNote(128, Hex(s.Fill, Color.white), Hex(s.Outline, Color.blue), Hex(s.Glow, Color.cyan));
                noteTextures[s.Name] = tex;
            }
            return tex;
        }

        // ------------------------------------------------------------------ showing a banner

        private void Begin(TrackInfo track, string hint)
        {
            currentHint = hint != null;
            style = currentHint ? BannerStyles.Default : BannerStyles.For(track);
            pal = new Palette
            {
                Fill = Hex(style.Fill, Color.white),
                Outline = Hex(style.Outline, Color.blue),
                Glow = Hex(style.Glow, Color.cyan),
                Accent = Hex(style.Accent, Color.white),
                Backdrop = Hex(style.Backdrop, Color.black),
            };
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

            SetLayerText(glowLayer, title, titleSize, pal.Glow);
            SetLayerText(outlineLayer, title, titleSize, pal.Outline);
            SetLayerText(faceLayer, title, titleSize, pal.Fill);
            string sub = hasSubtitle ? subtitle.ToUpperInvariant() : "";
            SetLayerText(subGlowLayer, sub, SubtitleSize, pal.Glow);
            SetLayerText(subFaceLayer, sub, SubtitleSize, Color.Lerp(pal.Fill, pal.Glow, 0.35f));

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

            icon.texture = NoteFor(style);
            back.color = new Color(pal.Backdrop.r, pal.Backdrop.g, pal.Backdrop.b, 0f);
            line.color = pal.Glow;

            canvas.enabled = true;
            foreach (var layer in Layers())
            {
                layer.Rect.anchoredPosition = Vector2.zero;
                if (!layer.Rect.gameObject.activeInHierarchy)
                {
                    layer.Original = null;
                    continue;
                }
                layer.Text.ForceMeshUpdate();
                layer.Original = layer.Text.textInfo.CopyMeshInfoVertexData();
            }

            foreach (var p in particles) Kill(p);
            holdTime = currentHint ? 7f : Mathf.Max(1f, ModConfig.NowPlayingSeconds.Value);
            phase = Phase.In;
            phaseTime = 0f;
            flash = currentHint ? 0.3f : 1f;
            shineStart = 0.35f; // the first light sweep runs as the letters land
            spawnCarry = 0f;
            if (!currentHint) Burst(10);
        }

        private static void SetLayerText(TextLayer layer, string text, float size, Color color)
        {
            layer.Text.text = text;
            layer.Text.fontSize = size;
            layer.Text.color = color;
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
                    alpha = EaseOutCubic(Mathf.Clamp01(t * 1.6f));
                    x = slideDir * 70f * (1f - EaseOutCubic(t));
                    scale = Mathf.LerpUnclamped(0.92f, 1f, EaseOutBack(t));
                    lineFill = EaseOutCubic(Mathf.Clamp01((phaseTime - 0.2f) / 0.55f));
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
                    float e = EaseInCubic(outT);
                    alpha = 1f - e;
                    x = -slideDir * 40f * e;
                    scale = 1f - 0.03f * e;
                    lineFill = 1f - EaseOutCubic(outT);
                    if (outT >= 1f)
                    {
                        phase = Phase.Hidden;
                        group.alpha = 0f;
                        canvas.enabled = false;
                        foreach (var p in particles) Kill(p);
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

            UpdateParticles(dt, effects);
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

        /// <summary>
        /// Per-letter effects, written straight into the mesh: letters pop in one after another,
        /// a band of light sweeps across, they bob to the bass, then float away on the way out.
        /// </summary>
        private void AnimateText(TextLayer layer, float delay, float outT, bool effects, float alphaScale, bool shineToGlow)
        {
            var info = layer.Text.textInfo;
            if (layer.Original == null || info == null) return;
            int count = info.characterCount;
            if (count == 0) return;

            float stagger = Mathf.Min(0.035f, 0.55f / Mathf.Max(1, count));
            float inTime = phase == Phase.In ? phaseTime - delay : 99f;
            float shineX = (phase == Phase.Out ? -1f : (phaseTime - shineStart) / 0.9f) * (width + 240f) - width / 2f - 120f;
            float time = Time.unscaledTime;

            for (int i = 0; i < count; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int m = ci.materialReferenceIndex, v = ci.vertexIndex;
                if (m >= layer.Original.Length || m >= info.meshInfo.Length) continue;
                var srcV = layer.Original[m].vertices;
                var srcC = layer.Original[m].colors32;
                var dstV = info.meshInfo[m].vertices;
                var dstC = info.meshInfo[m].colors32;
                if (srcV == null || dstV == null || v + 3 >= srcV.Length || v + 3 >= dstV.Length) continue;

                // Entrance: each letter fades and drops into place, slightly larger at first.
                float a = 1f, s = 1f, dy = 0f;
                if (effects)
                {
                    float k = Mathf.Clamp01((inTime - i * stagger) / 0.22f);
                    a = EaseOutCubic(k);
                    s = 1f + 0.55f * (1f - EaseOutBack(k));
                    dy = 14f * (1f - EaseOutCubic(k));

                    // Bob to the bass, as a wave running along the word.
                    dy += Mathf.Sin(time * 9f - i * 0.55f) * (1.2f * pulse + 4f * beat);

                    // Exit: letters fade from the start of the word and float upward.
                    if (phase == Phase.Out)
                    {
                        float ko = Mathf.Clamp01((outT * 1.6f) - i * stagger * 0.8f);
                        a *= 1f - ko;
                        dy += 18f * ko;
                    }
                }
                else if (phase == Phase.Out)
                {
                    a = 1f - outT;
                }

                Vector3 centre = (srcV[v] + srcV[v + 2]) * 0.5f;
                float shine = effects ? Mathf.Exp(-Mathf.Pow((centre.x - shineX) / 46f, 2f)) : 0f;
                Color tint = shineToGlow ? pal.Glow : Color.white;
                float tintAmount = shineToGlow ? shine * 0.9f : shine * 0.5f;
                byte alpha = (byte)Mathf.Clamp(a * alphaScale * 255f, 0f, 255f);
                for (int j = 0; j < 4; j++)
                {
                    var p = srcV[v + j];
                    dstV[v + j] = centre + (p - centre) * s + new Vector3(0f, dy, 0f);
                    Color32 c = srcC[v + j];
                    dstC[v + j] = new Color32(
                        (byte)Mathf.Lerp(c.r, tint.r * 255f, tintAmount),
                        (byte)Mathf.Lerp(c.g, tint.g * 255f, tintAmount),
                        (byte)Mathf.Lerp(c.b, tint.b * 255f, tintAmount),
                        (byte)(c.a * alpha / 255));
                }
            }
            layer.Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        // ------------------------------------------------------------------ particles

        private void UpdateParticles(float dt, bool effects)
        {
            if (effects && phase != Phase.Out)
            {
                float rate = style.Rate * intensity * (1f + 2f * pulse);
                spawnCarry += rate * dt;
                while (spawnCarry >= 1f)
                {
                    spawnCarry -= 1f;
                    Spawn(style.Particles);
                }
            }

            foreach (var p in particles)
            {
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    Kill(p);
                    continue;
                }
                float life = p.Age / p.Life;
                float env = Mathf.Clamp01(life / 0.15f) * Mathf.Clamp01((1f - life) / 0.4f);
                float a = p.Alpha * env;

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

        private void Burst(int n, ParticleKind? kind = null)
        {
            if (!ModConfig.NowPlayingEffects.Value) return;
            for (int i = 0; i < n; i++) Spawn(kind ?? style.Particles);
        }

        private Particle Free()
        {
            foreach (var p in particles)
                if (!p.Alive) return p;
            return null;
        }

        private float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private void Spawn(ParticleKind kind)
        {
            var p = Free();
            if (p == null) return;
            float hw = width / 2f, hh = height / 2f;
            p.Kind = kind;
            p.Age = 0f;
            p.Phase = R(0f, 6.28f);
            p.Rot = R(0f, 360f);
            p.Spin = 0f;
            p.Stretch = 1f;
            p.Col = Color.Lerp(pal.Glow, pal.Accent, R(0f, 1f));
            Texture tex = texDot;

            switch (kind)
            {
                case ParticleKind.Embers:
                    tex = texDot;
                    p.Pos = new Vector2(R(-hw - 20f, hw + 20f), R(-hh - 6f, -hh + 10f));
                    p.Vel = new Vector2(R(-12f, 12f), R(35f, 85f));
                    p.Size = R(3f, 8f);
                    p.Life = R(0.9f, 1.8f);
                    p.Alpha = R(0.7f, 1f);
                    break;
                case ParticleKind.Sparkles:
                    tex = texSparkle;
                    p.Pos = new Vector2(R(-hw - 30f, hw + 30f), R(-hh - 14f, hh + 14f));
                    p.Vel = new Vector2(R(-6f, 6f), R(4f, 16f));
                    p.Size = R(8f, 18f);
                    p.Spin = R(-40f, 40f);
                    p.Life = R(0.8f, 1.6f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                case ParticleKind.Bubbles:
                    tex = texRing;
                    p.Pos = new Vector2(R(-hw - 10f, hw + 10f), R(-hh - 10f, -hh + 6f));
                    p.Vel = new Vector2(0f, R(22f, 48f));
                    p.Size = R(6f, 15f);
                    p.Life = R(1.6f, 2.8f);
                    p.Alpha = R(0.5f, 0.9f);
                    p.Col = Color.Lerp(pal.Glow, Color.white, R(0.2f, 0.6f));
                    break;
                case ParticleKind.Snow:
                    tex = texFlake;
                    p.Pos = new Vector2(R(-hw - 20f, hw + 20f), R(hh, hh + 20f));
                    p.Vel = new Vector2(R(-8f, 8f), R(-42f, -20f));
                    p.Size = R(7f, 14f);
                    p.Spin = R(-60f, 60f);
                    p.Life = R(2f, 3.4f);
                    p.Alpha = R(0.6f, 1f);
                    p.Col = Color.Lerp(Color.white, pal.Glow, R(0f, 0.5f));
                    break;
                case ParticleKind.Stars:
                    if (rng.NextDouble() < 0.12)
                    {
                        // Shooting star.
                        p.Kind = ParticleKind.Sparks;
                        tex = texStreak;
                        p.Pos = new Vector2(R(-hw, hw), hh + R(0f, 20f));
                        p.Vel = new Vector2(R(160f, 260f) * (rng.NextDouble() < 0.5 ? -1f : 1f), R(-120f, -70f));
                        p.Size = R(5f, 8f);
                        p.Stretch = 3f;
                        p.Life = R(0.4f, 0.7f);
                        p.Alpha = 1f;
                        p.Col = pal.Accent;
                        break;
                    }
                    tex = texSparkle;
                    p.Pos = new Vector2(R(-hw - 40f, hw + 40f), R(-hh - 20f, hh + 20f));
                    p.Vel = new Vector2(R(-5f, 5f), R(-5f, 5f));
                    p.Size = R(6f, 16f);
                    p.Spin = R(-25f, 25f);
                    p.Life = R(1.2f, 2.4f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                case ParticleKind.Wisps:
                    tex = texDot;
                    p.Pos = new Vector2(R(-hw - 40f, hw + 40f), R(-hh, hh));
                    p.Vel = new Vector2(R(10f, 26f) * (rng.NextDouble() < 0.5 ? -1f : 1f), 0f);
                    p.Size = R(16f, 34f);
                    p.Life = R(2.2f, 3.6f);
                    p.Alpha = R(0.25f, 0.5f);
                    break;
                case ParticleKind.Drips:
                    tex = texStreak;
                    p.Pos = new Vector2(R(-hw + 20f, hw), R(-hh * 0.2f, hh * 0.1f));
                    p.Vel = new Vector2(R(-4f, 4f), R(-20f, -5f));
                    p.Size = R(4f, 7f);
                    p.Stretch = 1.2f;
                    p.Life = R(0.8f, 1.4f);
                    p.Alpha = R(0.7f, 1f);
                    break;
                case ParticleKind.Pixels:
                    tex = texSquare;
                    p.Pos = new Vector2(R(-hw - 30f, hw + 30f), R(-hh - 12f, hh + 12f));
                    p.Vel = new Vector2(R(-40f, 40f), 0f);
                    p.Size = R(3f, 6f);
                    p.Rot = 0f;
                    p.Life = R(0.4f, 1.1f);
                    p.Alpha = R(0.6f, 1f);
                    p.Col = rng.NextDouble() < 0.25 ? pal.Accent : pal.Glow;
                    break;
                case ParticleKind.Petals:
                    tex = texDot;
                    p.Pos = new Vector2(R(-hw - 20f, hw + 20f), R(hh, hh + 16f));
                    p.Vel = new Vector2(R(-10f, 10f), R(-30f, -14f));
                    p.Size = R(6f, 11f);
                    p.Stretch = R(1.5f, 2.1f);
                    p.Spin = R(-90f, 90f);
                    p.Life = R(2f, 3.2f);
                    p.Alpha = R(0.6f, 0.95f);
                    p.Col = Color.Lerp(pal.Accent, Color.white, R(0f, 0.5f));
                    break;
                case ParticleKind.Void:
                {
                    tex = texDot;
                    float ang = R(0f, 6.28f);
                    p.Pos = new Vector2(Mathf.Cos(ang) * (hw + R(50f, 110f)), Mathf.Sin(ang) * (hh + R(30f, 60f)));
                    p.Vel = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)) * R(20f, 50f); // a little swirl
                    p.Size = R(4f, 9f);
                    p.Life = R(1.4f, 2.4f);
                    p.Alpha = R(0.6f, 1f);
                    break;
                }
                case ParticleKind.Sparks:
                {
                    tex = texStreak;
                    float ang = R(0f, 6.28f);
                    p.Pos = new Vector2(R(-hw, hw) * 0.8f, R(-hh, hh) * 0.6f);
                    p.Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(140f, 300f);
                    p.Size = R(3f, 5f);
                    p.Stretch = 2.2f;
                    p.Life = R(0.25f, 0.5f);
                    p.Alpha = 1f;
                    p.Col = Color.Lerp(pal.Accent, pal.Glow, R(0f, 0.4f));
                    break;
                }
            }

            p.Img.texture = tex;
            p.Img.enabled = true;
            p.Alive = true;
            p.Rt.anchoredPosition = p.Pos;
            p.Img.color = new Color(p.Col.r, p.Col.g, p.Col.b, 0f);
        }

        private static void Kill(Particle p)
        {
            p.Alive = false;
            if (p.Img) p.Img.enabled = false;
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInCubic(float t) => t * t * t;

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private void Teardown()
        {
            if (glowLayer != null)
                foreach (var layer in Layers())
                    if (layer?.Material) Destroy(layer.Material);
            foreach (var tex in noteTextures.Values) if (tex) Destroy(tex);
            noteTextures.Clear();
            particles.Clear();
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
        }

        private void OnDestroy() => Teardown();
    }
}
