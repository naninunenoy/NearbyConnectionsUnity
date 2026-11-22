using System;
using UnityEngine;
using UnityEngine.UI;

internal static class DemoUi
{
    private const int TouchHeight = 88;

    public static readonly Color Background = new Color32(13, 22, 36, 255);
    public static readonly Color Panel = new Color32(23, 37, 55, 255);
    public static readonly Color ButtonColor = new Color32(42, 64, 87, 255);
    public static readonly Color Accent = new Color32(51, 121, 111, 255);
    public static readonly Color Foreground = new Color32(242, 248, 252, 255);
    public static readonly Color Muted = new Color32(170, 194, 215, 255);

    public static RectTransform Rect(Transform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static void Fill(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }

    public static void Stack(RectTransform rect, int padding = 0)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = 12;
        layout.childForceExpandHeight = false;
    }

    public static RectTransform Row(Transform parent, string name)
    {
        var rect = Rect(parent, name);
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    public static Image Surface(RectTransform rect, Color color, bool interactive = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = interactive;
        return image;
    }

    public static Text Label(Transform parent, string text, int size = 26, bool heading = false)
    {
        var label = Rect(parent, "Label").gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = size;
        label.fontStyle = heading ? FontStyle.Bold : FontStyle.Normal;
        label.color = Foreground;
        label.supportRichText = false;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        return label;
    }

    public static Button Button(Transform parent, string title, Action onClick, bool primary = false)
    {
        var rect = Rect(parent, title);
        rect.gameObject.AddComponent<LayoutElement>().minHeight = TouchHeight;
        var background = Surface(rect, primary ? Accent : ButtonColor, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => onClick());
        var label = Label(rect, title, 24, true);
        label.alignment = TextAnchor.MiddleCenter;
        Fill(label.rectTransform, 6);
        return button;
    }

    public static InputField Input(Transform parent, string hint, int limit)
    {
        var rect = Rect(parent, "Message");
        rect.gameObject.AddComponent<LayoutElement>().minHeight = TouchHeight;
        var background = Surface(rect, Background, true);
        var input = rect.gameObject.AddComponent<InputField>();
        input.targetGraphic = background;
        input.characterLimit = limit;
        var value = Label(rect, "", 24);
        var placeholder = Label(rect, hint, 24);
        placeholder.color = Muted;
        Fill(value.rectTransform, 12);
        Fill(placeholder.rectTransform, 12);
        input.textComponent = value;
        input.placeholder = placeholder;
        return input;
    }

    public static RectTransform Scroll(Transform parent, string name, out ScrollRect scroll)
    {
        var rect = Rect(parent, name);
        scroll = rect.gameObject.AddComponent<ScrollRect>();
        var viewport = Rect(rect, "Viewport");
        Fill(viewport);
        Surface(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(viewport, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        Stack(content, 4);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    public static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var item = parent.GetChild(i);
            item.gameObject.SetActive(false);
            item.SetParent(null);
            UnityEngine.Object.Destroy(item.gameObject);
        }
    }
}
