using System;
using System.IO;

namespace PDFTemplateFiller.services
{
    /// <summary>
    /// Production IFontResolver for headless/Linux runtimes (such as OutSystems ODC), where
    /// PDFsharp has no OS font API to fall back on. Without an IFontResolver installed, every
    /// "new XFont(...)" call throws - regardless of which family name is requested - which was
    /// the actual root cause behind PDF generation silently producing a 0-byte output file (the
    /// exception is caught by PdfTemplateFillerActions' outer try/catch, which sets resultFile to
    /// an empty array and reports the real reason only in errorMessage).
    ///
    /// This resolver maps ANY requested family/style to a single bundled DejaVu Sans font, since
    /// the goal here is reliable placeholder/table text rendering, not typographic fidelity to a
    /// specific requested family. DejaVu Sans is used because it is freely redistributable (see
    /// its license) and already bundled in this repository at
    /// PDFTemplateFiller/resources/fonts/DejaVuSans.ttf.
    ///
    /// NOTE: this was written and reasoned through without a .NET SDK or NuGet access available
    /// to actually compile/run it - the IFontResolver interface shape (ResolveTypeface/GetFont)
    /// matches the PDFsharp API as used by PDFTemplateFillerTest/UnitTestPDFTemplateFiller.cs's
    /// own TestFontResolver in this same repository, but please build and run the existing test
    /// suite locally before trusting this in production.
    /// </summary>
    public sealed class EmbeddedFontResolver : PdfSharp.Fonts.IFontResolver
    {
        private const string FaceName = "DejaVuSansEmbedded";
        private static readonly object SyncRoot = new();
        private static byte[]? _cachedFontBytes;

        public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            // A single embedded face is used for every request, regardless of the requested
            // family/bold/italic combination. If you later bundle separate bold/italic TTFs,
            // branch here and return a distinct face name for each, then extend GetFont to match.
            return new PdfSharp.Fonts.FontResolverInfo(FaceName);
        }

        public byte[] GetFont(string faceName)
        {
            lock (SyncRoot)
            {
                if (_cachedFontBytes is not null)
                {
                    return _cachedFontBytes;
                }

                string? fontPath = FontHelper.FindBundledDejaVuPath();
                if (fontPath is null)
                {
                    throw new InvalidOperationException(
                        "EmbeddedFontResolver could not locate the bundled DejaVuSans.ttf. " +
                        "Confirm resources/fonts/DejaVuSans.ttf is present and marked as an " +
                        "EmbeddedResource (or copied to the output directory) in the .csproj.");
                }

                _cachedFontBytes = File.ReadAllBytes(fontPath);
                return _cachedFontBytes;
            }
        }
    }
}