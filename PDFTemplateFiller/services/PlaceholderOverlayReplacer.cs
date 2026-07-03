using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Content;
// Both PdfSharp and PdfPig define a "PdfDocument" class - alias PdfPig's to avoid ambiguity,
// since PdfSharp.Pdf.PdfDocument (brought in via "using PdfSharp.Pdf;" above) is the one used
// for every other PdfDocument reference in this file.
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace PdfTemplateFiller.services;

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

    public static void ReplaceFields(PdfDocument pdfSharpDocument, byte[] originalPdfBytes, Dictionary<string, string> fields)
    {
        if (fields.Count == 0)
        {
            return;
        }

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
            var font = new XFont(ReplacementFontName, 10); // Font size is recalculated per match below.
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

                // PdfPig's bounding box uses PDF coordinate space (origin bottom-left, Y grows
                // upward). PDFsharp's XGraphics for a page uses the opposite convention (origin
                // top-left, Y grows downward), so the vertical coordinate is flipped here.
                double pageHeight = pdfSharpPage.Height.Point;
                double left = word.BoundingBox.Left;
                double top = pageHeight - word.BoundingBox.Top;
                double width = word.BoundingBox.Width;
                double height = word.BoundingBox.Height;

                var cellRect = new XRect(left - 1, top - 1, width + 2, height + 2);
                graphics.DrawRectangle(whiteoutBrush, cellRect);

                double fontSize = Math.Max(6, height * 0.82); // Empirical factor to approximately match cap-height to bounding-box height.
                var replacementFont = new XFont(ReplacementFontName, fontSize);
                graphics.DrawString(replacementValue, replacementFont, textBrush, new XPoint(left, top + height * 0.82));
            }
        }
    }
}
