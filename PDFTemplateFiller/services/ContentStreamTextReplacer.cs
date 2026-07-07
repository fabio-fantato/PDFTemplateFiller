using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;

namespace PDFTemplateFiller.services
{
    /// <summary>
    /// Replaces literal "{{key}}" tokens found inside a PDF's content streams.
    ///
    /// IMPORTANT (please read before relying on this in production):
    /// A PDF content stream does not contain "the text of the page" as a string - it contains a
    /// sequence of low-level drawing operators. Text is emitted via "Tj" (show one string) or "TJ"
    /// (show an array of strings interleaved with kerning adjustments), usually re-encoded through
    /// the font's character encoding. This class only handles the common, simple case where:
    ///   - the placeholder appears as a literal, human-readable string inside a single "(...) Tj"
    ///     operator (i.e. the PDF was generated with a simple, non-subsetted, non-kerned font,
    ///     which is typical for templates produced by Word/LibreOffice "Save as PDF" or basic
    ///     PDF-generation libraries), and
    ///   - the replacement text is not drastically longer than the placeholder (a much longer
    ///     replacement can visually overflow into neighboring content, since nothing here re-flows
    ///     the page).
    ///
    /// If your templates use "TJ" arrays, subsetted/embedded fonts with custom glyph encodings, or
    /// split the placeholder across runs, this will silently fail to find the match. In that case,
    /// use the coordinate-based overlay (<see cref="PdfTableRenderer"/>) for those specific fields
    /// instead of relying on text-stream patching.
    /// </summary>
    public static class ContentStreamTextReplacer
    {
        private static readonly Regex TjStringPattern = new(@"\(((?:[^()\\]|\\.)*)\)\s*Tj", RegexOptions.Compiled);

        /// <summary>
        /// Replaces every "{{key}}" occurrence found in simple Tj text runs across all pages of the document.
        /// </summary>
        /// <param name="document">An already-loaded PDFsharp document, opened with PdfDocumentOpenMode.Modify.</param>
        /// <param name="fields">Key/value pairs. Keys are matched without the surrounding "{{ }}". Null is treated as empty.</param>
        /// <returns>
        /// The subset of <paramref name="fields"/> keys that were actually found and replaced.
        /// Callers combining this with a fallback mechanism (e.g. PlaceholderOverlayReplacer)
        /// should only pass the remaining, non-returned keys to that fallback - otherwise a
        /// placeholder successfully handled here would be processed a second time.
        /// </returns>
        public static HashSet<string> ReplaceFields(PdfDocument document, Dictionary<string, string>? fields)
        {
            var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

            if (fields is null || fields.Count == 0)
            {
                return matchedKeys;
            }

            foreach (PdfPage page in document.Pages)
            {
                ReplaceFieldsOnPage(page, fields, matchedKeys);
            }

            return matchedKeys;
        }

        private static void ReplaceFieldsOnPage(PdfPage page, Dictionary<string, string> fields, HashSet<string> matchedKeys)
        {
            // A page can have a single content stream or an array of them; PDFsharp exposes this
            // as PdfPage.Contents, a collection of PdfDictionary entries each backed by a PdfStream.
            foreach (var contentDictionary in page.Contents)
            {
                PdfDictionary.PdfStream? stream = contentDictionary.Stream;
                if (stream is null)
                {
                    continue;
                }

                // Read and decompress the stream's bytes so we can regex-match the plain PDF
                // operators inside it, then write the modified bytes back the same way.
                stream.TryUncompress();
                byte[] decoded = stream.Value;
                string content = Encoding.Latin1.GetString(decoded);

                string updated = TjStringPattern.Replace(content, match =>
                {
                    string rawText = match.Groups[1].Value;
                    string decodedText = UnescapePdfString(rawText);
                    string replacedText = ReplacePlaceholders(decodedText, fields, matchedKeys);

                    if (ReferenceEquals(replacedText, decodedText))
                    {
                        // No placeholder found in this run - leave the original operator untouched
                        // to avoid unnecessary re-escaping/rewriting.
                        return match.Value;
                    }

                    string reEscaped = EscapePdfString(replacedText);
                    return $"({reEscaped}) Tj";
                });

                if (!ReferenceEquals(updated, content))
                {
                    byte[] newBytes = Encoding.Latin1.GetBytes(updated);
                    stream.Value = newBytes;
                    // Keep the dictionary's /Length entry consistent with the new (unfiltered)
                    // stream length, following the same pattern PDFsharp itself uses whenever it
                    // writes a stream's bytes directly (see PdfDictionary.PdfStream.Keys.Length).
                    contentDictionary.Elements.SetInteger(PdfDictionary.PdfStream.Keys.Length, stream.Value.Length);
                }
            }
        }

        private static string ReplacePlaceholders(string text, Dictionary<string, string> fields, HashSet<string> matchedKeys)
        {
            if (!text.Contains("{{", StringComparison.Ordinal))
            {
                return text;
            }

            string result = text;
            foreach (var (key, value) in fields)
            {
                string token = "{{" + key + "}}";
                if (result.Contains(token, StringComparison.Ordinal))
                {
                    // A null value (e.g. an explicit "key": null in the request JSON) is treated
                    // as an empty string rather than left as-is or thrown on - the placeholder is
                    // still considered "handled" so the overlay fallback does not also process it.
                    result = result.Replace(token, value ?? string.Empty, StringComparison.Ordinal);
                    matchedKeys.Add(key);
                }
            }

            return result;
        }

        /// <summary>
        /// Un-escapes PDF literal-string syntax: backslash escapes for parentheses, backslash, and
        /// common control characters as defined in ISO 32000-1, section 7.3.4.2.
        /// </summary>
        private static string UnescapePdfString(string raw)
        {
            var builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char currentChar = raw[i];
                if (currentChar == '\\' && i + 1 < raw.Length)
                {
                    char nextChar = raw[++i];
                    builder.Append(nextChar switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'b' => '\b',
                        'f' => '\f',
                        '(' => '(',
                        ')' => ')',
                        '\\' => '\\',
                        _ => nextChar
                    });
                }
                else
                {
                    builder.Append(currentChar);
                }
            }

            return builder.ToString();
        }

        private static string EscapePdfString(string text)
        {
            var builder = new StringBuilder(text.Length + 8);
            foreach (char currentChar in text)
            {
                switch (currentChar)
                {
                    case '(':
                    case ')':
                    case '\\':
                        builder.Append('\\').Append(currentChar);
                        break;
                    default:
                        builder.Append(currentChar);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}