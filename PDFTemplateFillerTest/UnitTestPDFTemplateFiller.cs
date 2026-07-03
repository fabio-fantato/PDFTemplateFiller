using PDFTemplateFiller.actions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using PdfSharp.Fonts;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace PDFTemplateFillerTest
{
    public class UnitTestPDFTemplateFillerTest
    {
        private readonly PdfTemplateFillerActions _actionsPdfTemplateFiller;

        public UnitTestPDFTemplateFillerTest()
        {
            _actionsPdfTemplateFiller = new PdfTemplateFillerActions();
            // Ensure a simple font resolver is available in headless/test environments.
            // This resolver attempts to load Arial from the system fonts folder.
            try
            {
                GlobalFontSettings.FontResolver = new TestFontResolver();
            }
            catch
            {
                // If the environment doesn't support installing a resolver, tests that rely on
                // rendering may still fail; the try/catch prevents constructor failures.
            }
        }

        [Fact]
        public void Scenario1_FromExistingFiles_ReadTemplateAndJson()
        {
            // Try to locate an existing scenario folder with a PDF template and a JSON file
            string scenariosRoot = Path.Combine(Directory.GetCurrentDirectory(), "scenarios");
            string templatePath = null;
            string jsonPath = null;

            if (Directory.Exists(scenariosRoot))
            {
                // Find first PDF under scenarios
                var pdfFiles = Directory.GetFiles(scenariosRoot, "*.pdf", SearchOption.AllDirectories);
                if (pdfFiles.Length > 0) templatePath = pdfFiles[0];

                // Prefer common names
                string[] candidates = new[] { "scenario1_fields_only.json", "scenario1.json", "scenario1.json" };
                foreach (var name in candidates)
                {
                    var match = Directory.GetFiles(scenariosRoot, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null)
                    {
                        jsonPath = match;
                        break;
                    }
                }

                // Fallback: any json file
                if (jsonPath == null)
                {
                    var jsonFiles = Directory.GetFiles(scenariosRoot, "*.json", SearchOption.AllDirectories);
                    if (jsonFiles.Length > 0) jsonPath = jsonFiles[0];
                }
            }

            byte[] template = LoadTemplateBytes(templatePath);
            if (template == null || template.Length == 0)
            {
                // fallback to programmatically generated template
                template = BuildPdfWithTextRun("Invoice: {{InvoiceNumber}} - Name: {{CustomerName}}");
            }

            string requestJson = string.Empty;
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                requestJson = File.ReadAllText(jsonPath);
            }
            else
            {
                requestJson = JsonSerializer.Serialize(new { fields = new Dictionary<string, string> { { "InvoiceNumber", "INV-101" }, { "CustomerName", "FileFallback" } } });
            }

            _actionsPdfTemplateFiller.FillPdfTemplate(
                template,
                requestJson,
                out byte[] resultFile,
                out bool success,
                out string errorMessage);

            Assert.True(success, errorMessage);
            Assert.NotNull(resultFile);

            // save artifact
            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "test-output");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "scenario1_fromfiles_result.pdf");
                File.WriteAllBytes(outPath, resultFile);
            }
            catch { }
        }

        [Fact]
        public void Scenario2_FromExistingFiles_ReadTemplateAndJson()
        {
            string scenariosRoot = Path.Combine(Directory.GetCurrentDirectory(), "scenarios");
            string templatePath = null;
            string jsonPath = null;

            if (Directory.Exists(scenariosRoot))
            {
                var pdfFiles = Directory.GetFiles(scenariosRoot, "*.pdf", SearchOption.AllDirectories);
                if (pdfFiles.Length > 0) templatePath = pdfFiles[0];

                // Prefer scenario2 names
                string[] candidates = new[] { "scenario2_fields_and_table.json", "scenario2.json", "scenario2.json" };
                foreach (var name in candidates)
                {
                    var match = Directory.GetFiles(scenariosRoot, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null)
                    {
                        jsonPath = match;
                        break;
                    }
                }

                if (jsonPath == null)
                {
                    var jsonFiles = Directory.GetFiles(scenariosRoot, "*.json", SearchOption.AllDirectories);
                    if (jsonFiles.Length > 1) jsonPath = jsonFiles.Last();
                    else if (jsonFiles.Length == 1) jsonPath = jsonFiles[0];
                }
            }

            byte[] template = LoadTemplateBytes(templatePath);
            if (template == null || template.Length == 0)
            {
                template = BuildPdfWithTextRun("Invoice: {{InvoiceNumber}} - Name: {{CustomerName}}");
            }

            string requestJson = string.Empty;
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                requestJson = File.ReadAllText(jsonPath);
            }
            else
            {
                requestJson = JsonSerializer.Serialize(new { fields = new Dictionary<string, string> { { "InvoiceNumber", "INV-202" }, { "CustomerName", "FileFallback2" } }, tables = new object[] { } });
            }

            _actionsPdfTemplateFiller.FillPdfTemplate(
                template,
                requestJson,
                out byte[] resultFile,
                out bool success,
                out string errorMessage);

            Assert.True(success, errorMessage);
            Assert.NotNull(resultFile);

            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "test-output");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "scenario2_fromfiles_result.pdf");
                File.WriteAllBytes(outPath, resultFile);
            }
            catch { }
        }

        private sealed class TestFontResolver : IFontResolver
        {
            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            {
                // Map any requested family to the bundled DejaVuSans font (preferred) or system fallback.
                return new FontResolverInfo("DejaVuSans#" + (isBold ? "B" : "R") + (isItalic ? "I" : ""));
            }

            public byte[] GetFont(string faceName)
            {
                try
                {
                    // 1) Prefer bundled test font at ./test-resources/fonts/DejaVuSans.ttf (repo-relative)
                    string cwd = Directory.GetCurrentDirectory();
                    string candidate = Path.Combine(cwd, "test-resources", "fonts", "DejaVuSans.ttf");
                    if (File.Exists(candidate))
                    {
                        return File.ReadAllBytes(candidate);
                    }

                    // 2) Walk up a few parent directories to find the repository root in case tests run from bin folders
                    string dir = cwd;
                    for (int i = 0; i < 6; i++)
                    {
                        candidate = Path.Combine(dir, "test-resources", "fonts", "DejaVuSans.ttf");
                        if (File.Exists(candidate))
                        {
                            return File.ReadAllBytes(candidate);
                        }
                        var parent = Directory.GetParent(dir);
                        if (parent == null) break;
                        dir = parent.FullName;
                    }

                    // 3) Fallback to system fonts (e.g., arial.ttf on Windows)
                    string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                    string systemPath = Path.Combine(fonts, "arial.ttf");
                    if (File.Exists(systemPath))
                    {
                        return File.ReadAllBytes(systemPath);
                    }
                }
                catch
                {
                    // ignore and fall through
                }

                // Returning empty signals PDFsharp that the font couldn't be provided
                return Array.Empty<byte>();
            }
        }

        // Helper: builds a minimal valid PDF (single page) whose content stream contains
        // a single literal text run. This is generated as raw PDF bytes so tests avoid
        // depending on PdfSharp's font resolver when creating the template.
        private static byte[] BuildPdfWithTextRun(string text)
        {
            static string Escape(string s)
            {
                return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            }

            string header = "%PDF-1.4\n%âãÏÓ\n";

            string obj1 = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            string obj2 = "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n";
            string obj3 = "3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /MediaBox [0 0 612 792] /Contents 5 0 R >>\nendobj\n";
            string obj4 = "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n";

            string contentText = "BT\n/F1 12 Tf\n100 700 Td\n(" + Escape(text) + ") Tj\nET\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(contentText);
            string obj5 = "5 0 obj\n<< /Length " + contentBytes.Length + " >>\nstream\n" + contentText + "endstream\nendobj\n";

            var builder = new StringBuilder();
            builder.Append(header);
            long pos = Encoding.ASCII.GetByteCount(builder.ToString());

            var offsets = new List<long> { 0 }; // xref index 0

            // append objects and record offsets
            offsets.Add(pos);
            builder.Append(obj1);
            pos += Encoding.ASCII.GetByteCount(obj1);

            offsets.Add(pos);
            builder.Append(obj2);
            pos += Encoding.ASCII.GetByteCount(obj2);

            offsets.Add(pos);
            builder.Append(obj3);
            pos += Encoding.ASCII.GetByteCount(obj3);

            offsets.Add(pos);
            builder.Append(obj4);
            pos += Encoding.ASCII.GetByteCount(obj4);

            offsets.Add(pos);
            builder.Append(obj5);
            pos += Encoding.ASCII.GetByteCount(obj5);

            long xrefStart = pos;

            // xref
            builder.Append("xref\n0 6\n");
            builder.Append("0000000000 65535 f \n");
            for (int i = 1; i < offsets.Count; i++)
            {
                builder.Append(offsets[i].ToString("D10") + " 00000 n \n");
            }

            builder.Append("trailer\n<< /Size 6 /Root 1 0 R >>\n");
            builder.Append("startxref\n" + xrefStart + "\n%%EOF\n");

            return Encoding.ASCII.GetBytes(builder.ToString());
        }

        private static string LoadJson(string path)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
            // Try embedded resource fallback
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
                                              || (n.IndexOf("scenarios", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
            if (res != null)
            {
                using var s = asm.GetManifestResourceStream(res)!;
                using var r = new StreamReader(s, Encoding.UTF8);
                return r.ReadToEnd();
            }

            return string.Empty;
        }

        private static byte[] LoadTemplateBytes(string filePathCandidate)
        {
            if (!string.IsNullOrEmpty(filePathCandidate) && File.Exists(filePathCandidate))
            {
                return File.ReadAllBytes(filePathCandidate);
            }

            // Try embedded resource fallback
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames()
                         .FirstOrDefault(n => n.EndsWith(Path.GetFileName(filePathCandidate), StringComparison.OrdinalIgnoreCase)
                                              || (n.IndexOf("scenarios", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)));
            if (res != null)
            {
                using var s = asm.GetManifestResourceStream(res)!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            return Array.Empty<byte>();
        }

        [Fact]
        public void Scenario1_LoadTemplateAndFill_TextFieldsOnly()
        {
            // Scenario folder: tests look for PDFTemplateFillerTest/scenarios/invoice_template/
            string scenarioFolder = Path.Combine(Directory.GetCurrentDirectory(), "scenarios", "invoice_template");
            Directory.CreateDirectory(scenarioFolder);

            // Create a simple template containing text placeholders
            byte[] template = BuildPdfWithTextRun("Invoice: {{InvoiceNumber}} - Name: {{CustomerName}}");

            // scenario1.json — only text fields
            string scenario1Path = Path.Combine(scenarioFolder, "scenario1.json");
            if (!File.Exists(scenario1Path))
            {
                string json = JsonSerializer.Serialize(new
                {
                    fields = new Dictionary<string, string>
                    {
                        { "InvoiceNumber", "INV-001" },
                        { "CustomerName", "Acme Corp" }
                    }
                }, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(scenario1Path, json);
            }

            string requestJson = LoadJson(scenario1Path);

            _actionsPdfTemplateFiller.FillPdfTemplate(
                template,
                requestJson,
                out byte[] resultFile,
                out bool success,
                out string errorMessage);

            Assert.True(success, errorMessage);
            Assert.NotNull(resultFile);

            // Save result PDF for inspection
            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "test-output");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "scenario1_result.pdf");
                File.WriteAllBytes(outPath, resultFile);
            }
            catch
            {
                // Ignore write failures in test environment
            }
            // Ensure placeholders were replaced
            string resultText = Encoding.ASCII.GetString(resultFile);
            Assert.DoesNotContain("{{InvoiceNumber}}", resultText);
            Assert.DoesNotContain("{{CustomerName}}", resultText);
        }

        [Fact]
        public void Scenario2_LoadTemplateAndFill_TextAndTable()
        {
            string scenarioFolder = Path.Combine(Directory.GetCurrentDirectory(), "scenarios", "invoice_template");
            Directory.CreateDirectory(scenarioFolder);

            // Template with placeholders for text (table will be rendered by PdfTableRenderer)
            byte[] template = BuildPdfWithTextRun("Invoice: {{InvoiceNumber}} - Name: {{CustomerName}}");

            // scenario2.json — text fields + a simple table
            string scenario2Path = Path.Combine(scenarioFolder, "scenario2.json");
            if (!File.Exists(scenario2Path))
            {
                var scenario2 = new
                {
                    fields = new Dictionary<string, string>
                    {
                        { "InvoiceNumber", "INV-002" },
                        { "CustomerName", "Beta LLC" }
                    },
                    tables = new[]
                    {
                        new
                        {
                            name = "Items",
                            page = 1,
                            x = 50,
                            y = 500,
                            rowHeight = 20,
                            fontSize = 10,
                            columns = new[] { new { header = "Item", width = 300 }, new { header = "Qty", width = 50 } },
                            rows = new[] { new[] { "Widget", "2" }, new[] { "Gadget", "5" } }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(scenario2, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(scenario2Path, json);
            }

            string requestJson = LoadJson(scenario2Path);

            _actionsPdfTemplateFiller.FillPdfTemplate(
                template,
                requestJson,
                out byte[] resultFile,
                out bool success,
                out string errorMessage);

            Assert.True(success, errorMessage);
            Assert.NotNull(resultFile);

            // Save result PDF for inspection
            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "test-output");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "scenario2_result.pdf");
                File.WriteAllBytes(outPath, resultFile);
            }
            catch
            {
                // Ignore write failures in test environment
            }
            // Basic sanity: placeholders replaced
            string resultText = Encoding.ASCII.GetString(resultFile);
            Assert.DoesNotContain("{{InvoiceNumber}}", resultText);
            Assert.DoesNotContain("{{CustomerName}}", resultText);
        }
    }
}
