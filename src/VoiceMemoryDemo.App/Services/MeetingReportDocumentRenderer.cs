using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace VoiceMemoryDemo.App.Services;

public static partial class MeetingReportDocumentRenderer
{
    private static readonly Brush Primary = BrushFrom("#F1F3F6");
    private static readonly Brush Secondary = BrushFrom("#9AA2AE");
    private static readonly Brush Accent = BrushFrom("#D9A266");
    private static readonly Brush Border = BrushFrom("#2B3038");
    private static readonly Brush Surface = BrushFrom("#181C22");

    public static FlowDocument Create(string markdown)
    {
        var document = CreateBaseDocument();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length;)
        {
            var line = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (line == "---")
            {
                document.Blocks.Add(new Paragraph
                {
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 22, 0, 20)
                });
                index++;
                continue;
            }

            if (TryReadHeading(line, out var level, out var heading))
            {
                var paragraph = new Paragraph
                {
                    Foreground = level == 1 ? Primary : Accent,
                    FontSize = level switch { 1 => 25, 2 => 18, _ => 15 },
                    FontWeight = level <= 2 ? FontWeights.SemiBold : FontWeights.Medium,
                    Margin = level switch
                    {
                        1 => new Thickness(0, 0, 0, 20),
                        2 => new Thickness(0, 22, 0, 10),
                        _ => new Thickness(0, 15, 0, 7)
                    },
                    LineHeight = level == 1 ? 32 : 25
                };
                AddInlines(paragraph, heading);
                document.Blocks.Add(paragraph);
                index++;
                continue;
            }

            if (line.StartsWith('>'))
            {
                var quote = new Paragraph
                {
                    Foreground = Secondary,
                    Background = Surface,
                    BorderBrush = Accent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 16),
                    FontStyle = FontStyles.Italic
                };
                AddInlines(quote, line.TrimStart('>', ' '));
                document.Blocks.Add(quote);
                index++;
                continue;
            }

            if (IsTableStart(lines, index))
            {
                index = AddTable(document, lines, index);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                index = AddList(document, lines, index, ordered: false);
                continue;
            }

            if (OrderedListRegex().IsMatch(line))
            {
                index = AddList(document, lines, index, ordered: true);
                continue;
            }

            var body = new Paragraph
            {
                Foreground = Primary,
                FontSize = 13.5,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 11)
            };
            AddInlines(body, line);
            document.Blocks.Add(body);
            index++;
        }

        return document;
    }

    public static FlowDocument CreateMessage(string message)
    {
        var document = CreateBaseDocument();
        document.Blocks.Add(new Paragraph(new Run(message))
        {
            Foreground = Secondary,
            FontSize = 13,
            Margin = new Thickness(0)
        });
        return document;
    }

    private static FlowDocument CreateBaseDocument() => new()
    {
        FontFamily = UiLanguageService.TextFont,
        FontSize = 13.5,
        Foreground = Primary,
        Background = Brushes.Transparent,
        PagePadding = new Thickness(24, 20, 28, 24),
        ColumnWidth = double.PositiveInfinity,
        TextAlignment = TextAlignment.Left
    };

    private static int AddList(FlowDocument document, string[] lines, int start, bool ordered)
    {
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            MarkerOffset = 8,
            Margin = new Thickness(18, 0, 0, 12),
            Foreground = Primary
        };
        var index = start;
        while (index < lines.Length)
        {
            var value = lines[index].Trim();
            string? itemText;
            if (ordered)
            {
                var match = OrderedListRegex().Match(value);
                if (!match.Success) break;
                itemText = match.Groups[1].Value;
            }
            else
            {
                if (!value.StartsWith("- ", StringComparison.Ordinal)) break;
                itemText = value[2..].Trim();
            }

            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 5),
                LineHeight = 22,
                FontSize = 13.5
            };
            AddInlines(paragraph, itemText);
            list.ListItems.Add(new ListItem(paragraph));
            index++;
        }
        document.Blocks.Add(list);
        return index;
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index + 1 >= lines.Length) return false;
        var line = lines[index].Trim();
        var divider = lines[index + 1].Trim();
        return line.StartsWith('|') && line.EndsWith('|') && TableDividerRegex().IsMatch(divider);
    }

    private static int AddTable(FlowDocument document, string[] lines, int start)
    {
        var rows = new List<string[]>();
        var index = start;
        while (index < lines.Length)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith('|') || !line.EndsWith('|')) break;
            if (!TableDividerRegex().IsMatch(line))
            {
                rows.Add(line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray());
            }
            index++;
        }
        if (rows.Count == 0) return index;

        var columnCount = rows.Max(row => row.Length);
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 18)
        };
        for (var column = 0; column < columnCount; column++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new TableRow { Background = rowIndex == 0 ? Surface : Brushes.Transparent };
            for (var column = 0; column < columnCount; column++)
            {
                var cellText = column < rows[rowIndex].Length ? rows[rowIndex][column] : string.Empty;
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    FontSize = 12.5,
                    LineHeight = 20,
                    FontWeight = rowIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal
                };
                AddInlines(paragraph, cellText);
                row.Cells.Add(new TableCell(paragraph)
                {
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(10, 8, 10, 8)
                });
            }
            group.Rows.Add(row);
        }
        document.Blocks.Add(table);
        return index;
    }

    private static bool TryReadHeading(string line, out int level, out string heading)
    {
        level = 0;
        while (level < line.Length && line[level] == '#') level++;
        if (level is < 1 or > 6 || level >= line.Length || line[level] != ' ')
        {
            heading = string.Empty;
            return false;
        }
        heading = line[(level + 1)..].Trim();
        return true;
    }

    private static void AddInlines(Paragraph paragraph, string text)
    {
        var parts = text.Split("**", StringSplitOptions.None);
        for (var index = 0; index < parts.Length; index++)
        {
            if (string.IsNullOrEmpty(parts[index])) continue;
            Inline inline = new Run(parts[index]);
            if (index % 2 == 1) inline = new Bold(inline);
            paragraph.Inlines.Add(inline);
        }
    }

    private static Brush BrushFrom(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^\|?\s*:?-{3,}.*\|?$")]
    private static partial Regex TableDividerRegex();
}
