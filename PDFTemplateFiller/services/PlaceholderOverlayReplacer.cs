using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Content;
// Both PdfSharp and PdfPig define a "PdfDocument" class - alias PdfPig's to avoid ambiguity,
// since PdfSharp.Pdf.PdfDocument (brought in via "using PdfSharp.Pdf;" above) is the one used
// for every other PdfDocument reference in this file.
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace PDFTemplateFiller.services;

/// <summary>
/// Replaces "{{key}}" placeholders by locating their exact position on the page and drawing
/// over them - a whiteout rectangle to hide the original text, then the replacement value on
/// top - rather than trying to patch the PDF's raw text-drawing operators.
///
/// Why this exists instead of a literal content-stream text patch: PDF producers such as
/// Microsoft Word and LibreOffice embed a subset of the document's font and encode text as
/// hex glyph codes inside "TJ" operators (not literal, human-readable strings inside "Tj").
/// A regex looking for literal "{{key}}" text in the raw content stream will never match that
/// encoding, so it silently does nothing on templates produced by those tools. Locating text
/// via a proper PDF text-extraction library (which resolves glyph codes back to Unicode through
/// the document's embedded ToUnicode CMap) works regardless of how the producer encoded the
/// text, which is why this class uses PdfPig (Apache-2.0 licensed) purely for locating text,
/// while PDFsharp remains the library used for drawing.
///
/// Known limitations (validate against your own templates before relying on this in production):
///   - Uses PdfPig's default word extraction. This groups characters into words based on
///     spacing, so "{{CustomerName}}" (no internal spaces) is expected to come back as a single
///     word - confirmed against both a ReportLab-generated PDF and a real Word-exported PDF
///     during testing. If a specific PDF producer inserts unusual spacing that splits a
///     placeholder across two "words", this class will not find it; in that case, the
///     placeholder needs a small position adjustment in the source template, or a line-level
///     reconstruction step would need to be added here.
///   - The whiteout rectangle assumes a plain white page background behind the placeholder. If
///     your template has a colored or shaded background behind a placeholder, pass the correct
///     background color via <see cref="WhiteoutColor"/> instead of relying on the default.
///   - The replacement text is drawn with a standard PDFsharp font (Helvetica by default), sized
///     to approximately match the original text's height. It will not visually match a custom
///     embedded font exactly; set <see cref="ReplacementFontName"/> if you need a closer match
///     to a font you know is available to PDFsharp.
///   - This does not remove the original placeholder text from the document's underlying content
///     - it is only visually hidden beneath the whiteout rectangle. Text extraction/copy-paste
///     of the final PDF may still surface the original "{{key}}" text. If that matters for your
///     use case (e.g. compliance/redaction requirements), this approach is not sufficient and a
///     true content-stream rewrite (with all its font-encoding complications) would be needed
///     instead.
/// </summary>
public static class PlaceholderOverlayReplacer
{
    private static readonly Regex PlaceholderPattern = new(@"^\{\{(\w+)\}\}$", RegexOptions.Compiled);

    /// <summary>
    /// Font used to draw replacement values. Defaults to "Helvetica" (a PDFsharp base-14 font,
    /// always available with no embedding required).
    /// </summary>
    public static string ReplacementFontName { get; set; } = "Helvetica";

    /// <summary>
    /// Background color painted behind each replaced placeholder to hide the original text.
    /// Defaults to white; change this if your template's background is not plain white.
    /// </summary>
    public static XColor WhiteoutColor { get; set; } = XColors.White;

    /// <summary>
    /// Horizontal whiteout padding, as a fraction of the placeholder's font size, applied on
    /// each side. Scaling by font size (rather than a fixed point value) is what makes this
    /// work across templates with wildly different text sizes - e.g. an 11pt label next to a
    /// 36pt heading in the same document both get proportionally correct coverage.
    /// </summary>
    public static double HorizontalPaddingRatio { get; set; } = 0.15;

    /// <summary>
    /// Whiteout padding above the placeholder, as a fraction of font size. Kept modest so the
    /// whiteout does not eat into content sitting close above the placeholder (e.g. a table
    /// border directly over a cell value).
    /// </summary>
    public static double TopPaddingRatio { get; set; } = 0.12;

    /// <summary>
    /// Whiteout padding below the placeholder, as a fraction of font size. Kept generous because
    /// glyph ink for characters like the placeholder's own "{" "}" braces - and descenders in
    /// general - commonly extend below what PdfPig's measured bounding box reports, especially
    /// for the subsetted/embedded fonts Word and LibreOffice produce.
    /// </summary>
    public static double BottomPaddingRatio { get; set; } = 0.30;

    public static void ReplaceFields(
        PdfDocument pdfSharpDocument,
        byte[] originalPdfBytes,
        Dictionary<string, string>? fields,
        Dictionary<string, string>? fieldAlignments = null)
    {
        if (fields is null || fields.Count == 0)
        {
            return;
        }

        fieldAlignments ??= new Dictionary<string, string>();

        using PdfPigDocument pdfPigDocument = PdfPigDocument.Open(originalPdfBytes);

        int pageNumber = 0;
        foreach (Page pigPage in pdfPigDocument.GetPages())
        {
            pageNumber++;
            if (pageNumber > pdfSharpDocument.PageCount)
            {
                break; // Safety guard - should not happen since both come from the same file.
            }

            PdfPage pdfSharpPage = pdfSharpDocument.Pages[pageNumber - 1];
            using XGraphics graphics = XGraphics.FromPdfPage(pdfSharpPage);
            var whiteoutBrush = new XSolidBrush(WhiteoutColor);
            var textBrush = XBrushes.Black;

            foreach (Word word in pigPage.GetWords())
            {
                Match match = PlaceholderPattern.Match(word.Text);
                if (!match.Success)
                {
                    continue;
                }

                string key = match.Groups[1].Value;
                if (!fields.TryGetValue(key, out string? replacementValue))
                {
                    continue; // Placeholder present in the template but not supplied in this request - leave it visible.
                }

                // A null value (e.g. an explicit "key": null in the request JSON) is treated as an
                // empty string - the whiteout still happens, just with nothing drawn on top.
                replacementValue ??= string.Empty;

                // Preserve the original placeholder's color and bold/italic style instead of
                // always drawing plain black text - a placeholder styled bold, italic, or in a
                // color (e.g. a colored date field, a bold total) was previously always replaced
                // with plain black text, silently losing that styling.
                //
                // NOTE: confirmed by your build - PdfPig's Letter.Font is obsolete in the version
                // you're on (0.1.15), replaced by Letter.FontDetails with the same IsBold/IsItalic
                // shape. Updated below; color extraction via Letter.Color.ToRGBValues() compiled
                // clean with no warning, so that part is confirmed correct as-is.
                Letter firstLetter = word.Letters[0];
                XBrush textBrush2 = textBrush;
                try
                {
                    var (r, g, b) = firstLetter.Color.ToRGBValues();
                    textBrush2 = new XSolidBrush(XColor.FromArgb(
                        (int)Math.Round(r * 255),
                        (int)Math.Round(g * 255),
                        (int)Math.Round(b * 255)));
                }
                catch
                {
                    // Fall back to plain black if color extraction fails for any reason - a wrong
                    // color is a cosmetic issue, not worth failing the whole placeholder over.
                }

                bool isBold = false;
                bool isItalic = false;
                try
                {
                    isBold = firstLetter.FontDetails?.IsBold ?? false;
                    isItalic = firstLetter.FontDetails?.IsItalic ?? false;
                }
                catch
                {
                    // Fall back to regular weight/style if this metadata is unavailable.
                }

                // PdfPig's bounding box uses PDF coordinate space (origin bottom-left, Y grows
                // upward). PDFsharp's XGraphics for a page uses the opposite convention (origin
                // top-left, Y grows downward), so the vertical coordinate is flipped here.
                double pageHeight = pdfSharpPage.Height.Point;
                double left = word.BoundingBox.Left;
                double top = pageHeight - word.BoundingBox.Top;
                double width = word.BoundingBox.Width;
                double height = word.BoundingBox.Height;

                // Use PdfPig's ACTUAL baseline coordinate for this text run, rather than
                // estimating it as a ratio of the bounding box height. The estimate broke down
                // specifically for placeholders: "{{CustomerName}}"'s curly braces extend below
                // the true baseline, inflating the measured bounding-box height, which threw off
                // any height-based baseline guess (confirmed against a real template: the
                // placeholder's own bounding-box bottom matched its neighboring label's true
                // baseline exactly, while a height*0.82 estimate landed about 3pt above it).
                // StartBaseLine is the literal coordinate PdfPig computed from the original text
                // rendering matrix - no estimation involved, so this generalizes correctly
                // regardless of which characters happen to be in a given placeholder.
                double baselineY = pageHeight - firstLetter.StartBaseLine.Y;

                // Use the ORIGINAL placeholder's actual font size (as specified in the PDF's Tf
                // operator, exposed per-letter by PdfPig) rather than deriving a size from the
                // bounding box height. The bounding box height reflects the tallest/lowest glyph
                // ink in that specific word - for a placeholder like "{{CustomerName}}" that is
                // dominated by the curly braces' glyph shape, not the font's actual point size,
                // which produced replacement text that was visibly too small compared to the
                // surrounding template text at the same nominal font size.
                //
                // CORRECTION (found via a real Google-Docs-style template, not yet verified
                // against a compiled build - please confirm and adjust if wrong): the raw value
                // from word.Letters[0].FontSize came out a consistent 96/72 = 1.3333x too large
                // compared to the placeholder's own measured height, on every placeholder tested
                // in that template. 96/72 is the exact ratio between CSS "pixels" (96 per inch)
                // and PDF "points" (72 per inch) - a very specific, non-coincidental number - so
                // this divides it back out. If your build renders text too SMALL after this
                // change, the bug was actually on the rendering side (PDFsharp/EmbeddedFontResolver)
                // rather than the value PdfPig reports, and this correction should be removed
                // instead of stacked on top of a different fix there.
                const double pxToPtCorrection = 72.0 / 96.0;
                double fontSize = word.Letters.Count > 0
                    ? word.Letters[0].FontSize * pxToPtCorrection
                    : Math.Max(6, height * 0.82); // Fallback if PdfPig ever returns an empty Letters list.

                // Padding is proportional to FONT SIZE, not to the measured word height - this is
                // what makes it generic across many different client templates instead of tuned
                // to one specific file. Font size is a stable, known quantity for any placeholder
                // in any template; the word's own bounding box height varies depending on which
                // characters happen to be present (confirmed visually: a placeholder dominated by
                // "{" "}" glyphs measured a height that did not match the surrounding text's true
                // size, and a whiteout padded off of THAT height either left slivers of the
                // original braces visible or, when made too generous, ate into content sitting
                // close above the placeholder such as a table border). Minimum floors keep this
                // sane for very small font sizes.
                double horizontalPadding = Math.Max(1.5, fontSize * HorizontalPaddingRatio);
                double topPadding = Math.Max(1.0, fontSize * TopPaddingRatio);
                double bottomPadding = Math.Max(2.0, fontSize * BottomPaddingRatio);
                var cellRect = new XRect(
                    left - horizontalPadding,
                    top - topPadding,
                    width + (horizontalPadding * 2),
                    height + topPadding + bottomPadding);
                graphics.DrawRectangle(whiteoutBrush, cellRect);

                // FontHelper.GetSafeXFont goes through the registered GlobalFontSettings.FontResolver
                // (see EmbeddedFontResolver.cs) instead of calling "new XFont(...)" directly - on a
                // headless/Linux runtime like OutSystems ODC, an unresolved XFont call throws, which
                // previously surfaced as a 0-byte output file (the exception was caught higher up).
                XFont replacementFont = FontHelper.GetSafeXFont(pdfSharpDocument, ReplacementFontName, fontSize, isBold, isItalic);

                // Alignment: default "Left" starts the replacement at the placeholder's own left
                // edge (previous, only behavior). "Right"/"Center" reposition a replacement value
                // of a different length so it still lines up the way the ORIGINAL placeholder did
                // - e.g. a right-aligned total amount whose replacement is shorter or longer than
                // "{{TotalAmount}}" still ends flush with the same right edge.
                string alignment = fieldAlignments.TryGetValue(key, out string? alignmentValue)
                    ? alignmentValue
                    : "Left";
                double drawX = left;
                if (!string.Equals(alignment, "Left", StringComparison.OrdinalIgnoreCase))
                {
                    double textWidth = graphics.MeasureString(replacementValue, replacementFont).Width;
                    double right = word.BoundingBox.Right;
                    drawX = alignment switch
                    {
                        _ when string.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase) => right - textWidth,
                        _ when string.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase) => left + (width / 2) - (textWidth / 2),
                        _ => left
                    };
                }

                graphics.DrawString(replacementValue, replacementFont, textBrush2, new XPoint(drawX, baselineY));
            }
        }
    }
}