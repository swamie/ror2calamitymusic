using System;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CalamityMusic.UI
{
    /// <summary>
    /// The "♫ Artist - Title" banner. Styled after the Calamity/Infernum music display: white
    /// lettering with a thick royal-blue outline and a teal glow, plus a matching music-note icon.
    /// Slides and fades in, pulses with the bass while it is up, then slides out.
    ///
    /// The outline and glow are stacked copies of the text with their glyphs dilated (fattened) in
    /// the distance field. That only uses base SDF shader properties, so it renders even if the
    /// game build stripped TextMeshPro's optional outline/underlay shader variants.
    /// </summary>
    internal sealed class NowPlayingBanner : MonoBehaviour
    {
        private static readonly Color Fill = new Color32(0xF4, 0xF6, 0xFF, 0xFF);
        private static readonly Color Outline = new Color32(0x2A, 0x42, 0xBE, 0xFF);
        private static readonly Color Glow = new Color32(0x3C, 0xF0, 0xC8, 0xFF);

        private enum Phase { Hidden, In, Hold, Out }

        private const float InTime = 0.55f;
        private const float OutTime = 0.5f;
        private const float FontSize = 38f;
        private const float HintFontSize = 26f;
        private const float IconSize = 58f;
        private const float Gap = 12f;

        private sealed class TextLayer
        {
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public Material Material;
            public float Dilate, Softness;
            public Color Color;
        }

        private Func<float> bassLevel;
        private Canvas canvas;
        private RectTransform root, content, textRoot, iconRect, auraRect, backRect, lineRect;
        private CanvasGroup group;
        private TextLayer glowLayer, outlineLayer, faceLayer;
        private RawImage icon, aura, back, line;
        private TMP_FontAsset builtFont;

        private Phase phase = Phase.Hidden;
        private float phaseTime;
        private float holdTime;
        private string queuedText;
        private bool queuedHint;
        private bool currentHint;
        private float pulse;
        private float flash;
        private float slideDir = -1f;
        private BannerPosition appliedPosition = (BannerPosition)(-1);

        public void Init(Func<float> bass)
        {
            bassLevel = bass;
        }

        // ------------------------------------------------------------------ public API

        public void Show(string message) => Enqueue(message, false);

        public void ShowHint(string message) => Enqueue(message, true);

        private void Enqueue(string message, bool hint)
        {
            if (string.IsNullOrEmpty(message)) return;
            try
            {
                // Built on first use: the game's font isn't loaded yet when plugins start.
                if (canvas == null && !TryBuild()) return;
                if (phase == Phase.Hidden)
                {
                    Begin(message, hint);
                    return;
                }
                // Something is already showing: slide it out quickly, then show the new one.
                queuedText = message;
                queuedHint = hint;
                if (phase != Phase.Out)
                {
                    phase = Phase.Out;
                    phaseTime = OutTime * 0.5f;
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

            root = NewRect("Root", go.transform);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            content = NewRect("Content", root);

            var pill = TextureFactory.SoftPill(256, 64);
            backRect = NewRect("Backdrop", content);
            back = backRect.gameObject.AddComponent<RawImage>();
            back.texture = pill;
            back.color = new Color(0.02f, 0.03f, 0.08f, 0.55f);
            back.raycastTarget = false;

            auraRect = NewRect("Aura", content);
            aura = auraRect.gameObject.AddComponent<RawImage>();
            aura.texture = pill;
            aura.color = new Color(Glow.r, Glow.g, Glow.b, 0f);
            aura.raycastTarget = false;

            lineRect = NewRect("Underline", content);
            line = lineRect.gameObject.AddComponent<RawImage>();
            line.texture = TextureFactory.Underline(256, 8);
            line.color = Glow;
            line.raycastTarget = false;

            iconRect = NewRect("Icon", content);
            icon = iconRect.gameObject.AddComponent<RawImage>();
            icon.texture = TextureFactory.MusicNote(128, Fill, Outline, Glow);
            icon.raycastTarget = false;
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);

            textRoot = NewRect("Text", content);
            glowLayer = NewLayer("Glow", new Color(Glow.r, Glow.g, Glow.b, 0.85f), 0.82f, 0.55f);
            outlineLayer = NewLayer("Outline", Outline, 0.42f, 0.04f);
            faceLayer = NewLayer("Face", Fill, 0.06f, 0f);
            ApplyFont(FindFont());

            ApplyPosition(true);
        }

        private TextLayer NewLayer(string name, Color color, float dilate, float softness)
        {
            var rt = NewRect(name, textRoot);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = FontSize;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.characterSpacing = 2f;
            t.richText = false; // song titles can contain '<'
            t.raycastTarget = false;
            t.extraPadding = true;
            t.color = color;
            return new TextLayer { Rect = rt, Text = t, Color = color, Dilate = dilate, Softness = softness };
        }

        private void ApplyFont(TMP_FontAsset font)
        {
            if (!font) return;
            builtFont = font;
            foreach (var layer in new[] { glowLayer, outlineLayer, faceLayer })
            {
                layer.Text.font = font;
                if (layer.Material) Destroy(layer.Material);
                layer.Material = new Material(font.material);
                var m = layer.Material;
                if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", Color.white); // tinted per layer by vertex color
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
                if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", layer.Dilate);
                if (m.HasProperty("_OutlineSoftness")) m.SetFloat("_OutlineSoftness", layer.Softness);
                m.DisableKeyword("UNDERLAY_ON");
                m.DisableKeyword("GLOW_ON");
                layer.Text.fontSharedMaterial = m;
                layer.Text.UpdateMeshPadding();
            }
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

        private void ApplyPosition(bool force)
        {
            var pos = ModConfig.NowPlayingPosition.Value;
            if (!force && pos == appliedPosition) return;
            appliedPosition = pos;

            Vector2 anchor, offset;
            switch (pos)
            {
                case BannerPosition.TopLeft: anchor = new Vector2(0f, 1f); offset = new Vector2(48f, -170f); break;
                case BannerPosition.TopRight: anchor = new Vector2(1f, 1f); offset = new Vector2(-48f, -300f); break;
                case BannerPosition.MiddleLeft: anchor = new Vector2(0f, 0.5f); offset = new Vector2(48f, 140f); break;
                case BannerPosition.MiddleRight: anchor = new Vector2(1f, 0.5f); offset = new Vector2(-48f, 140f); break;
                case BannerPosition.BottomLeft: anchor = new Vector2(0f, 0f); offset = new Vector2(48f, 290f); break;
                case BannerPosition.BottomCenter: anchor = new Vector2(0.5f, 0f); offset = new Vector2(0f, 250f); break;
                case BannerPosition.BottomRight: anchor = new Vector2(1f, 0f); offset = new Vector2(-48f, 330f); break;
                default: anchor = new Vector2(0.5f, 1f); offset = new Vector2(0f, -205f); break;
            }
            root.anchorMin = root.anchorMax = anchor;
            root.pivot = anchor;
            root.anchoredPosition = offset;
            content.anchorMin = content.anchorMax = anchor;
            content.pivot = anchor;
            slideDir = anchor.x > 0.75f ? 1f : -1f;
        }

        private void Begin(string message, bool hint)
        {
            currentHint = hint;
            // The language (and so the font) may have changed since the banner was built.
            if (HGTextMeshProUGUI.defaultLanguageFont && builtFont != HGTextMeshProUGUI.defaultLanguageFont)
                ApplyFont(HGTextMeshProUGUI.defaultLanguageFont);

            float size = hint ? HintFontSize : FontSize;
            foreach (var layer in new[] { glowLayer, outlineLayer, faceLayer })
            {
                layer.Text.text = message;
                layer.Text.fontSize = size;
            }
            faceLayer.Text.ForceMeshUpdate();
            float textWidth = Mathf.Max(40f, faceLayer.Text.preferredWidth);
            float textHeight = size * 1.35f;
            float iconSize = hint ? 40f : IconSize;

            float width = iconSize + Gap + textWidth;
            float height = Mathf.Max(iconSize, textHeight);
            root.sizeDelta = content.sizeDelta = new Vector2(width, height);

            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = new Vector2(-width / 2f + iconSize / 2f, 2f);
            textRoot.sizeDelta = new Vector2(textWidth + 4f, textHeight);
            textRoot.anchoredPosition = new Vector2(-width / 2f + iconSize + Gap + textWidth / 2f, 0f);

            backRect.sizeDelta = new Vector2(width + 160f, height + 44f);
            auraRect.sizeDelta = new Vector2(width + 90f, height + 26f);
            lineRect.sizeDelta = new Vector2(width + 30f, 6f);
            lineRect.anchoredPosition = new Vector2(0f, -height / 2f - 6f);

            holdTime = hint ? 7f : Mathf.Max(1f, ModConfig.NowPlayingSeconds.Value);
            phase = Phase.In;
            phaseTime = 0f;
            flash = hint ? 0.3f : 1f;
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

            float dt = Time.unscaledDeltaTime;
            phaseTime += dt;
            float bass = ModConfig.NowPlayingPulse.Value && !currentHint ? (bassLevel?.Invoke() ?? 0f) : 0f;
            pulse = Mathf.Lerp(pulse, bass, 1f - Mathf.Exp(-dt * 18f));
            flash = Mathf.Max(0f, flash - dt * 1.6f);

            float alpha = 1f, x = 0f, scale = 1f, lineFill = 1f;
            switch (phase)
            {
                case Phase.In:
                {
                    float t = Mathf.Clamp01(phaseTime / InTime);
                    alpha = EaseOutCubic(Mathf.Clamp01(t * 1.5f));
                    x = slideDir * 80f * (1f - EaseOutCubic(t));
                    scale = Mathf.LerpUnclamped(0.9f, 1f, EaseOutBack(t));
                    lineFill = EaseOutCubic(Mathf.Clamp01((phaseTime - 0.15f) / 0.5f));
                    if (t >= 1f) { phase = Phase.Hold; phaseTime = 0f; }
                    break;
                }
                case Phase.Hold:
                    if (phaseTime >= holdTime) { phase = Phase.Out; phaseTime = 0f; }
                    break;
                case Phase.Out:
                {
                    float t = Mathf.Clamp01(phaseTime / OutTime);
                    float e = EaseInCubic(t);
                    alpha = 1f - e;
                    x = -slideDir * 50f * e;
                    scale = 1f - 0.04f * e;
                    lineFill = 1f - e;
                    if (t >= 1f)
                    {
                        phase = Phase.Hidden;
                        group.alpha = 0f;
                        canvas.enabled = false;
                        if (queuedText != null)
                        {
                            string q = queuedText;
                            queuedText = null;
                            Begin(q, queuedHint);
                        }
                        return;
                    }
                    break;
                }
            }

            group.alpha = alpha;
            content.anchoredPosition = new Vector2(x, 0f);
            content.localScale = Vector3.one * scale * (1f + 0.035f * pulse);
            root.localScale = Vector3.one * Mathf.Clamp(ModConfig.NowPlayingScale.Value / 100f, 0.3f, 3f);

            float glow = Mathf.Clamp01(0.22f + 0.45f * pulse + 0.6f * flash);
            aura.color = new Color(Glow.r, Glow.g, Glow.b, glow * 0.55f);
            line.color = new Color(Glow.r, Glow.g, Glow.b, 0.75f + 0.25f * pulse);
            lineRect.localScale = new Vector3(lineFill, 1f, 1f);
            iconRect.localScale = Vector3.one * (1f + 0.07f * pulse + 0.1f * flash);
            glowLayer.Text.color = new Color(Glow.r, Glow.g, Glow.b, Mathf.Clamp01(0.6f + 0.4f * pulse + flash));
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
            foreach (var layer in new[] { glowLayer, outlineLayer, faceLayer })
                if (layer?.Material) Destroy(layer.Material);
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
        }

        private void OnDestroy() => Teardown();
    }
}
