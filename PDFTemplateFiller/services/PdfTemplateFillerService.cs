using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfTemplateFiller.services;
using PDFTemplateFiller.models;

namespace PDFTemplateFiller.services
{
    /// <inheritdoc cref="IPdfTemplateFillerService"/>
    public sealed class PdfTemplateFillerService : IPdfTemplateFillerService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

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

            // Step 1: simple, single-run "{{key}}" text substitution.
            ContentStreamTextReplacer.ReplaceFields(document, request.Fields);

            // Step 1: locate "{{key}}" placeholders via text extraction and overlay the real
            // values on top. This is the default approach because it works regardless of how the
            // PDF producer (Word, LibreOffice, etc.) internally encoded the text - see the header
            // comment in PlaceholderOverlayReplacer.cs for the full reasoning.
            //PlaceholderOverlayReplacer.ReplaceFields(document, templatePdfBytes, request.Fields);

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
                return JsonSerializer.Deserialize<PdfFillRequest>(requestJson, JsonOptions)
                       ?? new PdfFillRequest();
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
