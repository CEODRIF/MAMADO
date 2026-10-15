using System.IO.Compression;
using System.Text;
using AusbildungsWegInterview.Infrastructure.Documents;
using Xunit;

namespace AusbildungsWegInterview.Tests.Infrastructure.Documents;

/// <summary>
/// Document import: supported formats (TXT/PDF/DOCX), content sniffing
/// (extensions not trusted), failure categories (empty/corrupt/oversized/
/// no-text/unsupported), bounding, cancellation, and privacy (no disk writes).
/// </summary>
public class DocumentImporterTests : IDisposable
{
    private readonly string _dir;
    private readonly DocumentImporter _importer = new();

    public DocumentImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "awg-docimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    // ---------- supported formats ----------

    [Fact]
    public async Task Txt_ExtractsText()
    {
        var p = Write("cv.txt", "Max Mustermann\n10 years C# experience");
        var r = await _importer.ImportAsync(p);
        Assert.True(r.Success);
        Assert.Contains("Max Mustermann", r.Text!);
    }

    [Fact]
    public async Task Pdf_ExtractsText()
    {
        var p = WriteBytes("cv.pdf", PdfBuilder.Build("Hello CV Probe"));
        var r = await _importer.ImportAsync(p);
        Assert.True(r.Success);
        Assert.Contains("Hello CV Probe", r.Text!);
    }

    [Fact]
    public async Task Docx_ExtractsParagraphText()
    {
        var p = WriteBytes("jd.docx", DocxBuilder.Build("Senior Developer", "Requirements: C# and WPF"));
        var r = await _importer.ImportAsync(p);
        Assert.True(r.Success);
        Assert.Contains("Senior Developer", r.Text!);
        Assert.Contains("Requirements: C# and WPF", r.Text!);
        Assert.Contains('\n', r.Text!); // paragraphs stay separated
    }

    // ---------- content sniffing (extensions are not trusted alone) ----------

    [Fact]
    public async Task PdfContentWithTxtExtension_IsExtractedAsPdf()
    {
        var p = WriteBytes("fake.txt", PdfBuilder.Build("Sniffed PDF content"));
        var r = await _importer.ImportAsync(p);
        Assert.True(r.Success);
        Assert.Contains("Sniffed PDF content", r.Text!);
    }

    [Fact]
    public async Task UnsupportedContentAndExtension_IsRejected()
    {
        var p = WriteBytes("blob.bin", new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 });
        var r = await _importer.ImportAsync(p);
        Assert.Equal(DocumentImportError.Unsupported, r.Error);
    }

    // ---------- failure categories ----------

    [Fact]
    public async Task EmptyFile_IsCategorized()
    {
        var p = Write("empty.txt", "");
        var r = await _importer.ImportAsync(p);
        Assert.Equal(DocumentImportError.Empty, r.Error);
    }

    [Fact]
    public async Task CorruptPdf_IsCategorized()
    {
        var p = WriteBytes("bad.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\ngarbage garbage"));
        var r = await _importer.ImportAsync(p);
        Assert.Equal(DocumentImportError.Corrupt, r.Error);
    }

    [Fact]
    public async Task CorruptDocx_IsCategorized()
    {
        var p = WriteBytes("bad.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 });
        var r = await _importer.ImportAsync(p);
        Assert.True(r.Error is DocumentImportError.Unsupported or DocumentImportError.Corrupt, "got " + r.Error);
    }

    [Fact]
    public async Task ZipButNotDocx_IsUnsupported()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            zip.CreateEntry("readme.txt");
        var p = WriteBytes("notdocx.docx", ms.ToArray());
        var r = await _importer.ImportAsync(p);
        Assert.Equal(DocumentImportError.Unsupported, r.Error);
    }

    [Fact]
    public async Task OversizedFile_IsCategorized_BeforeParsing()
    {
        var p = Write("big.txt", new string('x', 500));
        var small = new DocumentImporter(maxFileBytes: 100);
        var r = await small.ImportAsync(p);
        Assert.Equal(DocumentImportError.Oversized, r.Error);
    }

    [Fact]
    public async Task ScannedLikePdf_WithoutText_IsCategorized()
    {
        // A structurally valid PDF with an empty content stream (what an
        // image-only/scanned page looks like to a text extractor).
        var p = WriteBytes("scan.pdf", PdfBuilder.Build(""));
        var r = await _importer.ImportAsync(p);
        Assert.Equal(DocumentImportError.NoText, r.Error);
    }

    [Fact]
    public async Task Extraction_IsBounded_TruncationFlagged()
    {
        var p = Write("long.txt", new string('a', 100));
        var bounded = new DocumentImporter(maxExtractedChars: 40);
        var r = await bounded.ImportAsync(p);
        Assert.True(r.Success);
        Assert.True(r.Truncated);
        Assert.Equal(40, r.Text!.Length);
    }

    [Fact]
    public async Task MissingFile_IsCategorized()
    {
        var r = await _importer.ImportAsync(Path.Combine(_dir, "nope.txt"));
        Assert.Equal(DocumentImportError.Corrupt, r.Error);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        var p = Write("cv.txt", "some text content here");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _importer.ImportAsync(p, cts.Token));
    }

    // ---------- privacy: nothing is written to disk ----------

    [Fact]
    public async Task Import_WritesNothingToDisk()
    {
        var p = Write("cv.txt", "content");
        var before = new HashSet<string>(Directory.GetFiles(_dir));
        await _importer.ImportAsync(p);
        var after = new HashSet<string>(Directory.GetFiles(_dir));
        Assert.Equal(before, after); // no temp/partial/extracted files
    }
}

/// <summary>
/// Builds a byte-accurate minimal single-page PDF (correct xref offsets) so
/// the tests exercise a REAL parse without any external PDF asset.
/// </summary>
internal static class PdfBuilder
{
    public static byte[] Build(string text)
    {
        var body = string.IsNullOrEmpty(text)
            ? string.Empty
            : "BT /F1 18 Tf 72 720 Td (" + text + ") Tj ET";

        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length " + body.Length + " >>\nstream\n" + body + "\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new long[6];
        for (int i = 0; i < objs.Length; i++)
        {
            offsets[i + 1] = sb.Length;
            sb.Append((i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n");
        }

        long xrefOffset = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
            sb.Append(offsets[i].ToString("D10") + " 00000 n \n");
        sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}

/// <summary>Builds a minimal but valid DOCX (ZIP with [Content_Types].xml + word/document.xml).</summary>
internal static class DocxBuilder
{
    public static byte[] Build(params string[] paragraphs)
    {
        var xml = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        foreach (var p in paragraphs)
            xml.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(XmlEscape(p)).Append("</w:t></w:r></w:p>");
        xml.Append("</w:body></w:document>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip.CreateEntry("[Content_Types].xml"),
                "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            WriteEntry(zip.CreateEntry("word/document.xml"), xml.ToString());
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchiveEntry entry, string content)
    {
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string XmlEscape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
