using System.IO.Compression;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;

namespace AusbildungsWegInterview.Infrastructure.Documents;

/// <summary>Machine-readable outcome codes for document import (UI localizes from these).</summary>
public enum DocumentImportError
{
    None,
    /// <summary>The file (by content) is not a supported format.</summary>
    Unsupported,
    /// <summary>The file is empty (0 bytes).</summary>
    Empty,
    /// <summary>The file could not be parsed (corrupt PDF/DOCX, unreadable text, ...).</summary>
    Corrupt,
    /// <summary>The file is larger than the configured maximum.</summary>
    Oversized,
    /// <summary>The file parsed but contains no extractable text (e.g. scanned/image PDF).</summary>
    NoText
}

/// <summary>Result of a document import attempt (text or a categorized error).</summary>
public sealed record DocumentImportResult(
    string? Text,
    DocumentImportError Error,
    bool Truncated = false,
    int? PageCount = null)
{
    public bool Success => Error == DocumentImportError.None && Text is not null;
}

/// <summary>
/// Imports CV / job-description documents as plain text, fully offline:
///  * PDF  — via PdfPig (MIT, pure C#);
///  * DOCX — via the built-in ZIP + XML reader (word/document.xml);
///  * TXT  — UTF-8 / Latin-1 fallback.
///
/// Security/robustness rules:
///  * Content is SNIFFED, the extension is not trusted alone: a PDF is detected
///    by its %PDF header even with a wrong extension, a DOCX by its ZIP layout,
///    and anything else is only accepted as plain text with a .txt extension.
///  * Files are never executed; their content is data, only.
///  * Bounded: max file size (checked first) and max extracted characters
///    (truncated, with a flag) keep the UI responsive.
///  * Nothing is written to disk and nothing is logged here.
/// </summary>
public sealed class DocumentImporter
{
    public const long DefaultMaxFileBytes = 10L * 1024 * 1024;      // 10 MB
    public const int DefaultMaxExtractedChars = 60_000;

    public long MaxFileBytes { get; }
    public int MaxExtractedChars { get; }

    public DocumentImporter(long? maxFileBytes = null, int? maxExtractedChars = null)
    {
        MaxFileBytes = maxFileBytes ?? DefaultMaxFileBytes;
        MaxExtractedChars = maxExtractedChars ?? DefaultMaxExtractedChars;
    }

    public async Task<DocumentImportResult> ImportAsync(string path, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Fail(DocumentImportError.Corrupt);

            long size = new FileInfo(path).Length;
            if (size == 0)
                return Fail(DocumentImportError.Empty);
            if (size > MaxFileBytes)
                return Fail(DocumentImportError.Oversized);

            await using var stream = File.OpenRead(path);
            using var header = new MemoryStream();
            int read = await CopyFirstAsync(stream, header, 4096, ct).ConfigureAwait(false);
            if (read == 0)
                return Fail(DocumentImportError.Empty);
            var head = header.ToArray();
            stream.Position = 0;

            // --- content sniffing (extensions are not trusted alone) ---
            if (ContainsPdfHeader(head))
                return ExtractPdf(stream, ct);

            if (IsZip(head))
            {
                try
                {
                    return ExtractDocx(stream, ct);
                }
                catch (InvalidDataException)
                {
                    return Fail(DocumentImportError.Unsupported); // a zip, but not a DOCX
                }
            }

            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".text", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
            {
                var text = await ReadTextAsync(stream, ct).ConfigureAwait(false);
                return FinalizeText(text);
            }

            return Fail(DocumentImportError.Unsupported);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Unreadable file / IO problem / parser crash: one honest category.
            return Fail(DocumentImportError.Corrupt);
        }
    }

    private DocumentImportResult Fail(DocumentImportError error)
        => new(null, error);

    private DocumentImportResult FinalizeText(string raw, int? pageCount = null)
    {
        var text = CollapseNewlines(raw);
        if (string.IsNullOrWhiteSpace(text))
            return Fail(DocumentImportError.NoText);
        bool truncated = text.Length > MaxExtractedChars;
        if (truncated)
            text = text[..MaxExtractedChars];
        return new DocumentImportResult(text, DocumentImportError.None, truncated, pageCount);
    }

    private DocumentImportResult ExtractPdf(Stream stream, CancellationToken ct)
    {
        try
        {
            using var doc = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            int pages = 0;
            foreach (var page in doc.GetPages())
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException("Document import was cancelled.", ct);
                pages++;
                sb.Append(page.Text);
                sb.Append('\n');
            }
            // Real page count from the PDF structure (shown on the document chip);
            // never inferred for the other formats.
            return FinalizeText(sb.ToString(), pages);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Parse failure, encrypted document, malformed structure: one honest category.
            return Fail(DocumentImportError.Corrupt);
        }
    }

    private DocumentImportResult ExtractDocx(Stream stream, CancellationToken ct)
    {
        // A DOCX is a ZIP archive; the body text lives in word/document.xml.
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.GetEntry("[Content_Types].xml") is null)
            throw new InvalidDataException("not a docx");

        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
            throw new InvalidDataException("not a docx (word/document.xml missing)");

        using var s = entry.Open();
        using var reader = XmlReader.Create(s);

        const string wordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var sb = new StringBuilder();
        while (reader.Read())
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("Document import was cancelled.", ct);

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.NamespaceURI == wordNs && reader.LocalName == "p")
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                }
                else if (reader.NamespaceURI == wordNs && reader.LocalName is "t" or "tab")
                {
                    if (reader.LocalName == "tab")
                        sb.Append(' ');
                    else
                    {
                        reader.Read();
                        if (reader.NodeType == XmlNodeType.Text)
                            sb.Append(reader.Value);
                    }
                }
            }
        }

        return FinalizeText(sb.ToString());
    }

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();

        // UTF-8 first (handles BOM), then Windows-1252/Latin-1 as the common
        // fallback for documents saved as "ANSI".
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(28591).GetString(bytes);
        }
    }

    private static async Task<int> CopyFirstAsync(Stream source, MemoryStream destination, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[maxBytes];
        int total = 0;
        int n;
        while (total < maxBytes && (n = await source.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
            total += n;
        destination.Write(buffer, 0, total);
        return total;
    }

    /// <summary>%PDF may sit a few bytes in (PDF allows a binary header before the marker).</summary>
    private static bool ContainsPdfHeader(byte[] head)
    {
        for (int i = 0; i + 4 <= head.Length; i++)
        {
            if (head[i] == (byte)'%' && head[i + 1] == (byte)'P' && head[i + 2] == (byte)'D' && head[i + 3] == (byte)'F')
                return true;
            if (i > 1024)
                break;
        }
        return false;
    }

    private static bool IsZip(byte[] head)
        => head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;

    private static string CollapseNewlines(string s)
    {
        // Normalize CRLF/CR, strip control chars, cap blank-line runs (keeps the
        // review box tidy without altering the actual words).
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(s.Length);
        int blanks = 0;
        foreach (var ch in s)
        {
            if (ch == '\n')
            {
                if (++blanks > 2) continue;
                sb.Append(ch);
            }
            else
            {
                blanks = 0;
                if (ch >= ' ' || ch == '\n' || ch == '\t')
                    sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
