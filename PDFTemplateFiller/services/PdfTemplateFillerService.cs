using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFTemplateFiller.models;
using System.Linq;
using System.Text.Json;

namespace PDFTemplateFiller.services
{
    /// <inheritdoc cref="IPdfTemplateFillerService"/>
    public sealed class PdfTemplateFillerService : IPdfTemplateFillerService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        static PdfTemplateFillerService()
        {
            // PDFsharp has no OS font API to rely on in headless/Linux runtimes (like OutSystems
            // ODC's container) - without an IFontResolver registered, every "new XFont(...)" call
            // in PlaceholderOverlayReplacer/PdfTableRenderer throws, and that exception was
            // previously surfacing as a silent 0-byte output file. Registered once per process,
            // guarded so it never throws and never overwrites a resolver a host application (or a
            // future version of this library) may have already installed.
            try
            {
                GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
            }
            catch
            {
                // If installation fails for any reason, continue - FontHelper's own fallback
                // chain will still attempt to cope, and any resulting failure will be reported
                // through the normal success/errorMessage output rather than thrown here.
            }
        }

        public byte[] FillTemplate(byte[] templatePdfBytes, string requestJson)
        {
            if (templatePdfBytes is null || templatePdfBytes.Length == 0)
            {
                throw new ArgumentException("The PDF template binary is empty.", nameof(templatePdfBytes));
            }

            PdfFillRequest request = ParseRequest(requestJson);

            using var inputStream = new MemoryStream(templatePdfBytes);
            // PdfDocumentOpenMode.Modify is required so PDFsharp keeps the document structure
            // editable (as opposed to Import mode, which is meant for read-only page extraction).
            using PdfDocument document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

            // Step 1a: best-effort literal text-stream patch. Works for PDF producers that emit
            // plain literal text runs with non-subsetted fonts (e.g. hand-built PDFs, ReportLab
            // with base-14 fonts). Returns which keys it actually found and replaced.
            HashSet<string> fieldsReplacedLiterally = ContentStreamTextReplacer.ReplaceFields(document, request.Fields);

            // Step 1b: fallback for whatever the literal patch could not find - in particular,
            // PDFs from Word/LibreOffice, which encode text as hex glyph codes inside a subsetted
            // font rather than literal strings (see PlaceholderOverlayReplacer.cs for the full
            // explanation - confirmed empirically against real Word/LibreOffice output). Locates
            // the remaining "{{key}}" occurrences via text extraction and overlays the value.
            // Only the fields NOT already handled by Step 1a are passed in, so an already-correct
            // literal replacement is never re-drawn on top of itself.
            Dictionary<string, string> remainingFields = request.Fields
                .Where(kv => !fieldsReplacedLiterally.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            PlaceholderOverlayReplacer.ReplaceFields(document, templatePdfBytes, remainingFields, request.FieldAlignments);

            // Step 2: structured tables / multi-line blocks, drawn at explicit coordinates.
            foreach (PdfTableDefinition table in request.Tables)
            {
                PdfTableRenderer.RenderTable(document, table);
            }

            using var outputStream = new MemoryStream();
            document.Save(outputStream, closeStream: false);
            return outputStream.ToArray();
        }

        private static PdfFillRequest ParseRequest(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                // An empty request is valid - it simply means "return the template unchanged".
                return new PdfFillRequest();
            }

            try
            {
                PdfFillRequest request = JsonSerializer.Deserialize<PdfFillRequest>(requestJson, JsonOptions)
                       ?? new PdfFillRequest();

                // The model's "= new()" property initializers only apply when the JSON omits the
                // key entirely. If the caller sends an explicit "fields": null or "tables": null,
                // System.Text.Json overwrites the default with a real null, which would otherwise
                // throw a NullReferenceException deep inside the replacers/renderer. Normalize
                // here so an absent or explicitly-null section always behaves as "nothing to do".
                request.Fields ??= new Dictionary<string, string>();
                request.Tables ??= new List<PdfTableDefinition>();

                return request;
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(
                    "The provided JSON could not be parsed. Expected shape: " +
                    "{ \"fields\": { \"key\": \"value\" }, \"tables\": [ ... ] }.",
                    nameof(requestJson),
                    exception);
            }
        }
    }
}