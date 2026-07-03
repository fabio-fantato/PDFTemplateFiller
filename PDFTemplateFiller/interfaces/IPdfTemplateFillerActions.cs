using OutSystems.ExternalLibraries.SDK;

namespace PDFTemplateFiller.interfaces
{
    /// <summary>
    /// Fills a PDF template with real data for OutSystems ODC.
    ///
    /// Two mechanisms are combined in the implementation, because a single one cannot satisfy
    /// both "use {{key}} notation" and "support tables":
    /// - Simple fields: literal "{{key}}" tokens replaced directly inside the PDF's content
    ///   stream (fast, but only reliable for short, single-line, single-font-run placeholders).
    /// - Tables / multi-line blocks: drawn on top of the template at explicit page/X/Y
    ///   coordinates, with automatic pagination if a table overflows its starting page.
    ///
    /// See the "fillDataJson" parameter description below for the expected JSON shape.
    /// </summary>
    [OSInterface(
        Name = "PdfTemplateFiller",
        IconResourceName = "PDFTemplateFiller.Logo.png",
        Description = "Fills a PDF template with data: {{key}} text substitution plus optional tables drawn at explicit coordinates. Uses PDFsharp (MIT license)."
    )]
    public interface IPdfTemplateFillerActions
    {
        /// <summary>
        /// Fills a PDF template with the supplied field values and tables.
        /// </summary>
        /// <param name="templatePdf">The original PDF template (Binary Data).</param>
        /// <param name="fillDataJson">
        /// JSON text describing the fields/tables to fill in, e.g.:
        /// { "fields": { "CustomerName": "Jane Doe" },
        ///   "tables": [ { "name": "OrderItems", "page": 1, "x": 50, "y": 400, "width": 500,
        ///                 "columns": [ { "header": "Item", "width": 260 } ],
        ///                 "rows": [ [ "Widget A" ] ] } ] }
        /// See PDFTemplateFiller.models.PdfFillRequest for the full shape and field-by-field notes.
        /// </param>
        /// <param name="resultFile">The filled PDF (Binary Data)</param>
        /// <param name="success">True if generation succeeded</param>
        /// <param name="errorMessage">Error details if failed</param>
        [OSAction(
            Description = "Fill a PDF template with field substitutions and/or tables, and return the filled PDF.",
            ReturnName = "result"
        )]
        void FillPdfTemplate(
            byte[] templatePdf,
            string fillDataJson,
            out byte[] resultFile,
            out bool success,
            out string errorMessage);
    }
}
