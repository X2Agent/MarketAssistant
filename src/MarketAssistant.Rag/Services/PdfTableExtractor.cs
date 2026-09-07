using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF 表格检测与 Markdown 表格重建（internal 静态类，纯算法，无状态）。
/// </summary>
internal static class PdfTableExtractor
{
    private const int MinTableColumns = 2;

    private sealed record CellFragment(double Left, string Text);

    private sealed class LineCells
    {
        public StructuredLine Line { get; init; } = null!;
        public List<CellFragment> Cells { get; init; } = new();
    }

    internal static List<TableInfo> DetectTables(List<StructuredLine> lines)
    {
        var tables = new List<TableInfo>();
        if (lines.Count == 0) return tables;

        // 先为每一行基于词间距拆分潜在单元格
        var lineCellInfos = new List<LineCells>();
        for (int i = 0; i < lines.Count; i++)
        {
            lineCellInfos.Add(SplitLineIntoCells(lines[i]));
        }

        bool IsLikelyTableLine(LineCells lc)
        {
            if (lc.Cells.Count < MinTableColumns) return false;
            // 平均单元格长度（字符）
            var avgLen = lc.Cells.Average(c => c.Text.Length);
            if (avgLen > 60) return false; // 太长可能是段落
            // 含有数字或列数≥3 更倾向于表格
            bool hasDigit = lc.Cells.Any(c => c.Text.Any(char.IsDigit));
            return hasDigit || lc.Cells.Count >= 3;
        }

        int idx = 0;
        while (idx < lines.Count)
        {
            if (!IsLikelyTableLine(lineCellInfos[idx])) { idx++; continue; }

            int start = idx;
            int lastTableLine = idx;
            int gapAllowance = 1; // 允许夹1行非表格（多行单元格内容）
            int gaps = 0;
            var candidateLineCells = new List<(int index, LineCells cells)>();

            while (idx < lines.Count)
            {
                var lc = lineCellInfos[idx];
                if (IsLikelyTableLine(lc))
                {
                    candidateLineCells.Add((idx, lc));
                    lastTableLine = idx;
                    gaps = 0;
                    idx++;
                }
                else if (gaps < gapAllowance)
                {
                    // 可能是前一单元格的续行，先暂存（不直接作为表格解析行）
                    gaps++;
                    idx++;
                }
                else
                {
                    break;
                }
            }

            if (candidateLineCells.Count >= 2)
            {
                var tableLines = candidateLineCells.Select(c => lines[c.index]).ToList();
                var rowIndices = candidateLineCells.Select(c => c.index).ToList();
                tables.Add(new TableInfo
                {
                    Rows = tableLines,
                    StartIndex = rowIndices.Min(),
                    EndIndex = rowIndices.Max(),
                    ActualRowIndices = rowIndices
                });
            }
            else
            {
                // 不足以构成表格，回退一个位置继续
                idx = start + 1;
            }
        }

        return tables;
    }

    private static LineCells SplitLineIntoCells(StructuredLine line)
    {
        // 基于多个空格或明显的水平间隔（Left差值）来拆分
        var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cells = new List<CellFragment>();
        if (words.Length == 0)
        {
            return new LineCells { Line = line, Cells = cells };
        }

        // 由于我们没有逐词坐标（为保持侵入性最小，不修改上层结构），用多空格估计分列
        // 如果后续需要更精准，可在 StructuredLine 中保留 word 级坐标。
        var raw = line.Text;
        // 使用两个及以上空格或制表符作为列分隔符
        var split = Regex.Split(raw.Trim(), @"(\s{2,}|\t+)").Where(s => !string.IsNullOrWhiteSpace(s) && !Regex.IsMatch(s, @"^\s{2,}$")).ToList();
        if (split.Count <= 1)
        {
            // 回退：用单空格分但只取>2列的情况
            if (words.Length >= 3)
            {
                split = words.ToList();
            }
        }
        double currentLeft = line.LeftMargin;
        foreach (var s in split)
        {
            cells.Add(new CellFragment(currentLeft, PdfTextUtility.CleanText(s)));
            currentLeft += 50; // 人工递增，后面列对齐时只用相对顺序
        }
        return new LineCells { Line = line, Cells = cells };
    }

    internal static void ProcessTable(TableInfo table, StringBuilder markdown)
    {
        if (table.Rows.Count == 0) return;

        markdown.AppendLine();

        // 重新分析表格结构 - 更智能的方法
        var tableData = AnalyzeAndRestructureTable(table.Rows);

        if (tableData.Count == 0)
        {
            // 如果无法识别为表格，作为普通段落处理
            foreach (var row in table.Rows)
            {
                markdown.AppendLine(PdfTextUtility.CleanText(row.Text));
            }
            markdown.AppendLine();
            return;
        }

        // 生成Markdown表格
        for (int i = 0; i < tableData.Count; i++)
        {
            var row = tableData[i];
            markdown.AppendLine($"| {string.Join(" | ", row)} |");

            // 在第一行后添加分隔行
            if (i == 0)
            {
                var separatorRow = "| " + string.Join(" | ", Enumerable.Repeat("---", row.Count)) + " |";
                markdown.AppendLine(separatorRow);
            }
        }

        markdown.AppendLine();
    }

    private static List<List<string>> AnalyzeAndRestructureTable(List<StructuredLine> rows)
    {
        // 通用：用 SplitLineIntoCells 重新对齐列，选择出现频率最高的列数
        var lineCells = rows.Select(SplitLineIntoCells).ToList();
        var columnCounts = lineCells.Where(lc => lc.Cells.Count >= MinTableColumns).Select(lc => lc.Cells.Count).ToList();
        if (!columnCounts.Any()) return new List<List<string>>();
        int targetColumns = columnCounts
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First().Key;

        var table = new List<List<string>>();

        foreach (var lc in lineCells)
        {
            if (lc.Cells.Count < 1) continue;
            if (lc.Cells.Count == targetColumns)
            {
                table.Add(lc.Cells.Select(c => c.Text).ToList());
            }
            else if (lc.Cells.Count > targetColumns)
            {
                // 合并多余列（从右向左合并最短文本）
                var cells = lc.Cells.Select(c => c.Text).ToList();
                while (cells.Count > targetColumns)
                {
                    // 找到两个最短相邻合并
                    int mergeIndex = 0;
                    int minLen = int.MaxValue;
                    for (int i = 0; i < cells.Count - 1; i++)
                    {
                        int lens = cells[i].Length + cells[i + 1].Length;
                        if (lens < minLen)
                        {
                            minLen = lens;
                            mergeIndex = i;
                        }
                    }
                    cells[mergeIndex] = (cells[mergeIndex] + " " + cells[mergeIndex + 1]).Trim();
                    cells.RemoveAt(mergeIndex + 1);
                }
                table.Add(cells);
            }
            else // 少于目标列，尝试用空列填充（多行单元格可能导致）
            {
                var cells = lc.Cells.Select(c => c.Text).ToList();
                while (cells.Count < targetColumns) cells.Add("");
                table.Add(cells);
            }
        }

        // 尝试识别表头：第一行如果所有列都是非数字且长度适中
        if (table.Count > 1)
        {
            bool firstRowHeader = table[0].Count(c => c.Any(char.IsLetter)) >= Math.Max(2, targetColumns - 1) &&
                                  table[0].Any(c => c.Contains("名称") || c.Contains("地区") || c.Contains("时间") || c.Contains("类") || c.Contains("种"));
            if (!firstRowHeader)
            {
                // 生成一个通用表头
                var header = new List<string>();
                for (int i = 0; i < targetColumns; i++) header.Add($"列{i + 1}");
                table.Insert(0, header);
            }
        }
        return table;
    }

    // 旧的特定案例处理逻辑已移除，保留最小必要工具函数
    internal static bool IsDataRow(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsDigit)) >= 2;
}
