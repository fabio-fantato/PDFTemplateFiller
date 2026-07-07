using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PDFTemplateFiller.services
{
    /// <summary>
    /// Helper to obtain an XFont safely with a deterministic fallback sequence.
    /// Tries the requested family first, then a set of common families (including bundled
    /// DejaVu), and finally falls back to Courier to avoid throwing when fonts are missing.
    /// </summary>
    public static class FontHelper
    {
        private static readonly string[] PreferredFallbackFamilies = new[]
        {
            "DejaVu Sans",
            "Arial",
            "Liberation Sans",
            "Helvetica",
            "Times New Roman",
            "Courier"
        };

        public static XFont GetSafeXFont(PdfDocument? document, string preferredFamily, double size, bool isBold, bool isItalic = false)
        {
            XFontStyleEx style = (isBold, isItalic) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular
            };

            // 1) Try the requested family (preserve literal family string, e.g. "Helvetica-Bold")
            if (TryCreateFont(preferredFamily, size, style, out XFont font))
                return font;

            // 2) Try a set of sensible fallbacks (includes DejaVu if available on the system or bundled)
            foreach (var family in PreferredFallbackFamilies)
            {
                if (string.Equals(family, preferredFamily, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryCreateFont(family, size, style, out font))
                    return font;
            }

            // 3) Last resort: return Courier (very safe)
            return new XFont("Courier", size, style);
        }

        private static bool TryCreateFont(string family, double size, XFontStyleEx style, out XFont font)
        {
            try
            {
                // PDFsharp may throw if the family is not available. Wrap in try/catch.
                font = new XFont(family, size, style);
                return true;
            }
            catch
            {
                font = null!;
                return false;
            }
        }

        /// <summary>
        /// Attempts to find a bundled DejaVuSans.ttf embedded resource or on disk. Returns full path
        /// when found or null.
        public static string? FindBundledDejaVuPath()
        {
            // 1) Check common paths relative to application base
            var baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "resources", "fonts", "DejaVuSans.ttf"),
                Path.Combine(baseDir, "fonts", "DejaVuSans.ttf"),
                Path.Combine(baseDir, "test-resources", "fonts", "DejaVuSans.ttf")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            // 2) Check embedded resources in this assembly - this is the path that actually
            // matters in a deployed ODC environment, now that the .csproj marks the font as an
            // EmbeddedResource rather than a loose file copied to the output directory.
            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            var match = names.FirstOrDefault(n => n.EndsWith("DejaVuSans.ttf", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                // Extract to temp file so callers can load via file APIs if needed
                try
                {
                    using var stream = asm.GetManifestResourceStream(match);
                    if (stream != null)
                    {
                        var outPath = Path.Combine(Path.GetTempPath(), "DejaVuSans.embedded.ttf");
                        using var fs = File.Create(outPath);
                        stream.CopyTo(fs);
                        return outPath;
                    }
                }
                catch
                {
                    // ignore and continue
                }
            }

            // NOTE: a third fallback that downloaded the font from GitHub at runtime used to be
            // here. Removed - a network call inside font resolution is a reliability risk in
            // itself (can hang, be blocked by ODC's outbound network policy, or behave
            // non-deterministically), which works against the goal of tolerating missing/absent
            // inputs gracefully. The EmbeddedResource above is now the reliable source of truth;
            // if it is ever missing, GetFont's caller (EmbeddedFontResolver) throws a clear,
            // immediate error instead of silently depending on network availability.
            return null;
        }
    }
}