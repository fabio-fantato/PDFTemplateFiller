namespace PDFTemplateFiller.services
{
    /// <summary>
    /// Fills a PDF template with real data: simple key/value substitution for "{{key}}"
    /// placeholders, plus optional structured tables drawn at explicit positions.
    /// </summary>
    public interface IPdfTemplateFillerService
    {
        /// <summary>
        /// Fills the given PDF template and returns the resulting PDF as a byte array.
        /// </summary>
        /// <param name="templatePdfBytes">The original, unmodified PDF template.</param>
        /// <param name="requestJson">
        /// The fill instructions as a JSON string. See <see cref="models.PdfFillRequest"/> for the expected shape.
        /// </param>
        /// <returns>The resulting PDF, as a byte array, ready to be returned to OutSystems as Binary Data.</returns>
        byte[] FillTemplate(byte[] templatePdfBytes, string requestJson);
    }
}
