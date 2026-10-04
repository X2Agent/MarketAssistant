using System.Reflection;

namespace TestMarketAssistant.Architecture;

/// <summary>
/// 文件规模护栏：约束单个源文件的行数上限，防止「拆分完又长回去」。
/// </summary>
/// <remarks>
/// <para>
/// 动机：本仓库一度出现 9 个 500 行以上、22 个 400 行以上的文件，
/// 单文件承担过多职责后，任何改动都要通读全文才能判断影响面，
/// 评审与回归成本随行数线性上升。
/// </para>
/// <para>
/// <b>为什么需要这条测试：</b>拆分是一次性动作，若没有约束，数月后
/// 同样的巨文件会重新出现，届时既无历史也无动机再去治理。
/// 本测试把治理成果固化为可执行规则。
/// </para>
/// <para>
/// <b>例外机制：</b>确有必要的大文件（如自动生成的代码）可在
/// <see cref="AllowList"/> 中登记原因，而不是放宽全局阈值。
/// </para>
/// </remarks>
[TestClass]
public sealed class FileSizeGuardTest
{
    /// <summary>
    /// 单文件行数上限。取 600 而非 500：当前最大文件 957 行，
    /// 留出足够余量让文件自然演进，同时仍能拦住失控膨胀。
    /// </summary>
    private const int MaxLinesPerFile = 600;

    /// <summary>
    /// 仓库根目录。由测试程序集位置向上回溯定位，
    /// 避免依赖测试运行时的工作目录。
    /// </summary>
    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    /// <summary>已豁免文件：文件路径 → 豁免原因。新增豁免必须写明理由。</summary>
    private static readonly Dictionary<string, string> AllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        // 预留：若将来引入自动生成代码，在此登记并说明生成方式
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void SourceFiles_ShouldNotExceedMaxLineCount()
    {
        Assert.IsNotNull(RepositoryRoot, "未能定位仓库根目录");

        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            var lineCount = CountLines(file);
            if (lineCount <= MaxLinesPerFile)
            {
                continue;
            }

            var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');
            if (AllowList.TryGetValue(relative, out var reason))
            {
                continue;
            }

            violations.Add($"{relative}（{lineCount} 行，上限 {MaxLinesPerFile}）");
        }

        Assert.AreEqual(
            0,
            violations.Count,
            $"以下源文件超过 {MaxLinesPerFile} 行，请拆分职责或登记到 AllowList：\n  "
            + string.Join("\n  ", violations.OrderBy(v => v)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AllowList_ShouldNotContainStaleEntries()
    {
        // 已拆分/重命名/删除的文件必须从豁免表移除，
        // 否则豁免表会逐渐失真，最终形同虚设。
        var stale = AllowList.Keys
            .Select(relative => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Where(path => !File.Exists(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToList();

        Assert.AreEqual(
            0,
            stale.Count,
            "AllowList 中存在已不存在的文件，请清理：\n  " + string.Join("\n  ", stale));
    }

    /// <summary>枚举参与约束的源文件（排除生成物与 IDE 目录）。</summary>
    private static IEnumerable<string> EnumerateSourceFiles()
    {
        // 由本测试程序集定位：tests/bin/<cfg>/net10.0/ → 上溯至仓库根
        var srcRoot = Path.Combine(RepositoryRoot, "src");
        if (!Directory.Exists(srcRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');

            // 跳过构建产物与 IDE 缓存：它们不是源码
            if (normalized.Contains("/bin/", StringComparison.Ordinal) ||
                normalized.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static int CountLines(string file) => File.ReadLines(file).Count();

    /// <summary>
    /// 从程序集所在目录逐级上溯，找到同时含 <c>MarketAssistant.slnx</c> 的目录。
    /// </summary>
    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MarketAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        // 回退到当前目录，避免直接抛异常掩盖真实问题
        return Directory.GetCurrentDirectory();
    }
}
