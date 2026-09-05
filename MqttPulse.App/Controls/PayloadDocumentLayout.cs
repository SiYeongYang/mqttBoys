using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MqttPulse.App.Controls;

internal static class PayloadDocumentLayout
{
    public static double MeasureWidth(Control viewer, string text, int prefixCharacters = 0)
    {
        var typeface = new Typeface(viewer.FontFamily, viewer.FontStyle, viewer.FontWeight, viewer.FontStretch);
        typeface.TryGetGlyphTypeface(out var glyphs);
        var space = GlyphWidth(' ');
        var prefix = prefixCharacters * space;
        var longest = prefix;
        var current = prefix;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                longest = Math.Max(longest, current);
                current = prefix;
            }
            else if (character == '\t')
            {
                current = (Math.Floor(current / (space * 8)) + 1) * space * 8;
            }
            else if (character != '\r')
            {
                current += GlyphWidth(character);
            }
        }

        return Math.Ceiling(Math.Max(longest, current)) + 24;

        double GlyphWidth(char character) => glyphs is not null
            && glyphs.CharacterToGlyphMap.TryGetValue(character, out var glyph)
                ? glyphs.AdvanceWidths[glyph] * viewer.FontSize
                : viewer.FontSize;
    }

    public static void ApplyWidth(RichTextBox viewer, double naturalWidth, bool wrap)
    {
        if (viewer.ActualWidth <= 0)
        {
            return;
        }

        var viewport = Math.Max(40, viewer.ActualWidth - viewer.BorderThickness.Left
            - viewer.BorderThickness.Right - viewer.Padding.Left - viewer.Padding.Right - 8);
        viewer.Document.PageWidth = wrap ? viewport : Math.Max(viewport, naturalWidth);
        viewer.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }
}
