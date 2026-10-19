using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DropZone
{
    /// <summary>Helpers for building the uGUI interface in code with one consistent dark-arena look.</summary>
    public static class UiKit
    {
        public static Font Title, Bold, Black;
        public static Sprite Rounded, RoundedSmall, Circle, Star, Target, BombIcon, SwapIcon, Glow, Outline, CircleOutline;

        public static readonly Color TextColour = new Color(0.93f, 0.95f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.66f, 0.8f);
        public static readonly Color Ink = new Color(0.04f, 0.05f, 0.11f);
        public static readonly Color PanelColour = new Color(0.07f, 0.08f, 0.17f, 0.94f);
        public static readonly Color PanelLight = new Color(0.13f, 0.14f, 0.27f, 0.96f);
        public static readonly Color Edge = new Color(0.25f, 0.85f, 1f, 0.55f);
        public static readonly Color Cyan = new Color(0.2f, 0.88f, 1f);
        public static readonly Color Purple = new Color(0.62f, 0.4f, 1f);
        public static readonly Color Orange = new Color(1f, 0.58f, 0.16f);
        public static readonly Color Red = new Color(1f, 0.34f, 0.3f);
        public static readonly Color Green = new Color(0.35f, 0.95f, 0.55f);
        public static readonly Color Shade = new Color(0.01f, 0.01f, 0.05f, 0.7f);
        public static readonly Color ButtonColour = new Color(0.16f, 0.18f, 0.34f);

        public static void Init()
        {
            if (Title) return;
            Title = Resources.Load<Font>("Fonts/LilitaOne");
            Bold = Resources.Load<Font>("Fonts/NotoSansBold");
            Black = Resources.Load<Font>("Fonts/NotoSansBlack");
            Rounded = MakeRounded(64, 20);
            RoundedSmall = MakeRounded(32, 9);
            Circle = MakeRounded(64, 32);
            Outline = MakeRounded(64, 22, 5f);
            CircleOutline = MakeRounded(64, 32, 5f);
            Star = Icon(128, StarShape);
            Target = Icon(128, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Max(Band(r, 0.62f, 0.86f), Band(r, -1f, 0.3f)); });
            BombIcon = Icon(128, BombShape);
            SwapIcon = Icon(128, SwapShape);
            Glow = Icon(64, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r); }, soft: true);
        }

        // ---------- procedural sprites (coverage functions over -1..1, anti-aliased by supersampling) ----------

        static float Band(float v, float lo, float hi) => v >= lo && v <= hi ? 1f : 0f;

        static Sprite Icon(int size, Func<float, float, float> cover, bool soft = false)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    if (soft) a = cover((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    else
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                                a += cover((x + (sx + 0.5f) / 4f) / size * 2f - 1f, (y + (sy + 0.5f) / 4f) / size * 2f - 1f) / 16f;
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>Five-pointed star (player one's symbol).</summary>
        public static float StarShape(float x, float y)
        {
            float ang = Mathf.Atan2(x, y);
            float r = Mathf.Sqrt(x * x + y * y);
            float seg = Mathf.PI * 2f / 5f;
            float a = Mathf.Repeat(ang + seg * 0.5f, seg) - seg * 0.5f;   // angle from the nearest point
            // star edge: straight line between an outer point (R) and an inner corner (Ri)
            const float R = 0.92f, Ri = 0.4f;
            float half = seg * 0.5f;
            var p0 = new Vector2(0, R);
            var p1 = new Vector2(Mathf.Sin(half) * Ri, Mathf.Cos(half) * Ri);
            var q = new Vector2(Mathf.Abs(Mathf.Sin(a)) * r, Mathf.Cos(a) * r);
            // inside if on the inner side of the line p0->p1
            float cross = (p1.x - p0.x) * (q.y - p0.y) - (p1.y - p0.y) * (q.x - p0.x);
            return cross <= 0f ? 1f : 0f;
        }

        static float BombShape(float x, float y)
        {
            float body = (x + 0.1f) * (x + 0.1f) + (y + 0.15f) * (y + 0.15f) < 0.62f * 0.62f ? 1f : 0f;
            float cap = Mathf.Abs(x - 0.32f + (y - 0.42f) * 0.6f) < 0.12f && y > 0.28f && y < 0.56f ? 1f : 0f;
            float fuse = Mathf.Abs(y - (0.55f + 0.25f * Mathf.Sin((x - 0.35f) * 5f))) < 0.05f && x > 0.33f && x < 0.72f ? 1f : 0f;
            float spark = Mathf.Abs(x - 0.78f) + Mathf.Abs(y - 0.72f) < 0.16f ? 1f : 0f;
            float shine = (x + 0.33f) * (x + 0.33f) + (y - 0.05f) * (y - 0.05f) < 0.14f * 0.14f ? -1f : 0f;
            return Mathf.Clamp01(Mathf.Max(body + shine * body, Mathf.Max(cap, Mathf.Max(fuse, spark))));
        }

        static float SwapShape(float x, float y)
        {
            // two opposite arrows
            float Arrow(float px, float py, float dir)
            {
                float lx = (px) * dir;
                float shaft = lx > -0.75f && lx < 0.3f && Mathf.Abs(py) < 0.11f ? 1f : 0f;
                float head = lx >= 0.2f && lx < 0.75f && Mathf.Abs(py) < (0.75f - lx) * 0.75f ? 1f : 0f;
                return Mathf.Max(shaft, head);
            }
            return Mathf.Max(Arrow(x, y - 0.35f, 1f), Arrow(x, y + 0.35f, -1f));
        }

        static Sprite MakeRounded(int size, int radius, float outline = 0f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    if (outline > 0f)
                    {
                        // distance from the shape's edge, inwards
                        float edge = Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f));
                        float inside = d > 0f ? radius - d : edge;
                        a *= Mathf.Clamp01(outline - inside + 0.5f);
                    }
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        // ---------- layout helpers ----------

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offsetMin; r.offsetMax = offsetMax;
            return r;
        }

        public static RectTransform Center(this RectTransform r, Vector2 size, Vector2 pos = default)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            return r;
        }

        public static RectTransform Fill(this RectTransform r) => r.Place(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        public static Image Box(Transform parent, string name, Color colour, Sprite sprite = null)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite ? sprite : Rounded;
            img.type = sprite == null || sprite == Rounded || sprite == RoundedSmall || sprite == Outline ? Image.Type.Sliced : Image.Type.Simple;
            img.color = colour;
            return img;
        }

        /// <summary>A dark panel with a thin glowing edge.</summary>
        public static Image Panel(Transform parent, string name, Color? edge = null)
        {
            var border = Box(parent, name, edge ?? Edge);
            var inner = Box(border.transform, "Inner", Panel_);
            inner.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            inner.raycastTarget = false;
            return border;
        }
        static Color Panel_ => PanelColour;

        public static Image Icon(Transform parent, Sprite sprite, Color colour, float size)
        {
            var img = Box(parent, sprite ? sprite.name + " Icon" : "Icon", colour, sprite);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color colour, Font font = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font ? font : Bold;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.text = text;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Outline Outlined(this Text t, Color c, float d = 2f)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = c; o.effectDistance = new Vector2(d, -d);
            return o;
        }

        public static Button Button(Transform parent, string text, Color colour, UnityAction onClick, int fontSize = 34, Color? textColour = null)
        {
            var img = Box(parent, text + " Button", colour);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            cb.selectedColor = new Color(1.2f, 1.2f, 1.2f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            cb.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.55f);
            cb.colorMultiplier = 1.2f;
            b.colors = cb;
            var label = Label(img.transform, text, fontSize, textColour ?? (colour.grayscale > 0.5f ? Ink : TextColour), Black);
            label.rectTransform.Fill();
            b.onClick.AddListener(() => Sfx.Play(Sfx.Kind.Click, 0.5f));
            b.onClick.AddListener(onClick);
            // a bright outline shows which button keyboard / gamepad focus is on
            var focus = Box(img.transform, "Focus", Color.white, Outline);
            focus.type = Image.Type.Sliced;
            focus.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-7, -7), new Vector2(7, 7));
            focus.raycastTarget = false;
            focus.gameObject.AddComponent<FocusRing>().Target = b;
            return b;
        }

        public static Text LabelOf(Button b) => b.GetComponentInChildren<Text>();

        public static VerticalLayoutGroup Stack(RectTransform r, float spacing, int padding = 0, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlHeight = true; v.childControlWidth = true;
            v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return v;
        }

        public static HorizontalLayoutGroup Row(RectTransform r, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlHeight = true; h.childControlWidth = true;
            h.childForceExpandHeight = true; h.childForceExpandWidth = true;
            return h;
        }

        public static LayoutElement Size(this Component c, float height, float width = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (width > 0) { le.preferredWidth = width; le.minWidth = width; }
            return le;
        }

        public static Slider Slider(Transform parent, float value, Action<float> onChange)
        {
            var root = Rect("Slider", parent);
            var s = root.gameObject.AddComponent<Slider>();
            var bg = Box(root, "Track", new Color(0.2f, 0.22f, 0.38f));
            bg.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -7), new Vector2(0, 7));
            var fillArea = Rect("Fill Area", root).Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -7), new Vector2(0, 7));
            var fill = Box(fillArea, "Fill", Cyan);
            fill.rectTransform.Place(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleArea = Rect("Handle Area", root).Place(Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
            var handle = Box(handleArea, "Handle", TextColour, Circle);
            handle.rectTransform.sizeDelta = new Vector2(34, 34);
            var focus = Box(handle.transform, "Focus", Orange, CircleOutline);
            focus.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-8, -8), new Vector2(8, 8));
            focus.raycastTarget = false;
            focus.gameObject.AddComponent<FocusRing>().Target = s;
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1; s.value = value;
            s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }
    }
}
