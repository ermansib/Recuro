using System.Globalization;
using System.Text;

namespace Recuro.BuildingBlocks.Infrastructure.Documents;

/// <summary>
/// A small, dependency-free PDF 1.4 writer for text documents (offer letters, summaries). It uses the
/// standard Helvetica fonts every PDF reader has, so nothing is embedded and no font licence applies.
/// Text is WinAnsi: characters outside it are replaced with close ASCII equivalents.
/// </summary>
public static class SimplePdf
{
    private const int PageWidth = 595; // A4 in points
    private const int PageHeight = 842;
    private const int Margin = 56;
    private const int TitleSize = 15;
    private const int BodySize = 10;
    private const int Leading = 14;
    private const int WrapAt = 100;

    /// <summary>Renders a title and lines of body text. Long lines wrap; pages break as needed.</summary>
    public static byte[] Render(string title, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        var pages = Paginate(lines.SelectMany(Wrap).ToList());
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty, // pages tree, filled in below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };

        var kids = new List<int>();
        for (var i = 0; i < pages.Count; i++)
        {
            var content = PageContent(i == 0 ? title : null, pages[i], i + 1, pages.Count);
            objects.Add(Invariant($"<< /Length {Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream"));
            var contentId = objects.Count;
            objects.Add(Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
            kids.Add(objects.Count);
        }

        objects[1] = Invariant($"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(k => Invariant($"{k} 0 R")))}] /Count {kids.Count} >>");
        return Assemble(objects);
    }

    /// <summary>Maps text to what WinAnsi Helvetica can show.</summary>
    public static string ToWinAnsi(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '—' or '–' => "-",
                '₹' => "INR ",
                '•' or '●' or '·' => "*",
                '✓' => "OK",
                '→' => "->",
                '≤' => "<=",
                '≥' => ">=",
                '‘' or '’' => "'",
                '“' or '”' => "\"",
                '\t' => "    ",
                _ when c < 32 => string.Empty,
                _ when c > 255 => "?",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    private static readonly Encoding Latin1 = Encoding.Latin1;

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> Wrap(string line)
    {
        var text = ToWinAnsi(line ?? string.Empty);
        if (text.Length <= WrapAt)
        {
            yield return text;
            yield break;
        }

        var indent = new string(' ', text.Length - text.TrimStart().Length);
        var current = new StringBuilder(indent);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > indent.Length && current.Length + 1 + word.Length > WrapAt)
            {
                yield return current.ToString();
                current.Clear().Append(indent);
            }

            if (current.Length > indent.Length)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > indent.Length)
        {
            yield return current.ToString();
        }
    }

    private static List<List<string>> Paginate(List<string> lines)
    {
        var firstPage = (PageHeight - (2 * Margin) - (2 * Leading)) / Leading;
        var otherPages = (PageHeight - (2 * Margin)) / Leading;
        var pages = new List<List<string>> { lines.Take(firstPage).ToList() };
        for (var skip = firstPage; skip < lines.Count; skip += otherPages)
        {
            pages.Add(lines.Skip(skip).Take(otherPages).ToList());
        }

        return pages;
    }

    private static string PageContent(string? title, List<string> lines, int page, int pageCount)
    {
        var y = PageHeight - Margin;
        var content = new StringBuilder();
        if (title is not null)
        {
            content.Append(Invariant($"BT /F2 {TitleSize} Tf {Margin} {y} Td ({Escape(ToWinAnsi(title))}) Tj ET\n"));
            y -= 2 * Leading;
        }

        content.Append(Invariant($"BT /F1 {BodySize} Tf {Leading} TL {Margin} {y} Td\n"));
        foreach (var line in lines)
        {
            content.Append('(').Append(Escape(line)).Append(") Tj T*\n");
        }

        content.Append("ET\n");
        content.Append(Invariant($"BT /F1 8 Tf {Margin} {Margin / 2} Td (Page {page} of {pageCount}) Tj ET"));
        return content.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] Assemble(List<string> objects)
    {
        using var stream = new MemoryStream();
        void Write(string s)
        {
            var bytes = Latin1.GetBytes(s);
            stream.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write(Invariant($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"));
        }

        var xref = stream.Position;
        var table = new StringBuilder(Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            table.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        Write(table.ToString());
        Write(Invariant($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return stream.ToArray();
    }
}
