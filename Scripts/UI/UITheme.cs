using Godot;

namespace PeakShift.UI;

/// <summary>Shared styling constants and helpers for the modern minimalist UI.</summary>
public static class UITheme
{
    // ── Colors ──────────────────────────────────────────────────
    public static readonly Color TextPrimary = new(1f, 1f, 1f);
    public static readonly Color TextMuted = new(1f, 1f, 1f, 0.4f);
    public static readonly Color Accent = new(0f, 0.83f, 1f);
    public static readonly Color Overlay = new(0.04f, 0.05f, 0.08f, 0.88f);
    public static readonly Color Gold = new(1f, 0.84f, 0f);

    // ── Button styling ──────────────────────────────────────────

    public static void StyleButton(Button btn, bool primary = true)
    {
        var borderN = primary ? new Color(0f, 0.83f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.15f);
        var borderH = primary ? new Color(0f, 0.83f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
        var fontN = primary ? TextPrimary : TextMuted;
        var fontH = primary ? Accent : TextPrimary;

        btn.AddThemeStyleboxOverride("normal", MakeBox(new Color(0.10f, 0.12f, 0.16f, 0.85f), borderN));
        btn.AddThemeStyleboxOverride("hover", MakeBox(new Color(0f, 0.83f, 1f, 0.08f), borderH));
        btn.AddThemeStyleboxOverride("pressed", MakeBox(new Color(0f, 0.83f, 1f, 0.2f), Accent));
        btn.AddThemeStyleboxOverride("focus", MakeBox(Colors.Transparent, Colors.Transparent, bw: 0));
        btn.AddThemeFontSizeOverride("font_size", 24);
        btn.AddThemeColorOverride("font_color", fontN);
        btn.AddThemeColorOverride("font_hover_color", fontH);
        btn.AddThemeColorOverride("font_pressed_color", Accent);
        btn.CustomMinimumSize = new Vector2(280f, 56f);
    }

    private static StyleBoxFlat MakeBox(Color bg, Color border, int radius = 12, int bw = 2)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderWidthLeft = bw,
            BorderWidthTop = bw,
            BorderWidthRight = bw,
            BorderWidthBottom = bw,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomRight = radius,
            CornerRadiusBottomLeft = radius,
            ContentMarginLeft = 48f,
            ContentMarginTop = 14f,
            ContentMarginRight = 48f,
            ContentMarginBottom = 14f,
        };
    }
}
