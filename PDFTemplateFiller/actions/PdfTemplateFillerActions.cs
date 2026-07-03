using PDFTemplateFiller.interfaces;
using PDFTemplateFiller.services;

namespace PDFTemplateFiller.actions
{
    /// <summary>
    /// Implementation of the PDF template filler for OutSystems ODC.
    /// Delegates the actual PDF manipulation (JSON parsing, "{{key}}" text substitution, table
    /// rendering) to <see cref="PdfTemplateFillerService"/>, and wraps it with the
    /// out-parameter success/error pattern used across this library's actions.
    /// </summary>
    public class PdfTemplateFillerActions : IPdfTemplateFillerActions
    {
        public void FillPdfTemplate(
            byte[] templatePdf,
            string fillDataJson,
            out byte[] resultFile,
            out bool success,
            out string errorMessage)
        {
            resultFile = Array.Empty<byte>();
            success = false;
            errorMessage = string.Empty;

            try
            {
                var service = new PdfTemplateFillerService();
                resultFile = service.FillTemplate(templatePdf, fillDataJson);
                success = true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error filling PDF template: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
            }
        }
    }
}
