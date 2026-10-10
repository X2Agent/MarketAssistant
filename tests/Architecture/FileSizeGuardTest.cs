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
    /// <remarks>
    /// <b>这张表是技术债台账，不是永久许可证。</b>
    /// 治理方式：每完成一次拆分，就从这里移除对应条目。
    /// 条目只应减少不应增加——若拆分新文件后又出现新的超限文件，
    /// 说明治理方向出了问题，应先复盘拆分策略而非登记豁免。
    /// <c>AllowList_ShouldNotContainStaleEntries</c> 会自动清理已删除文件的失效条目，
    /// 但它无法发现"重新引入"的情况，因此仍需人工把关方向。
    /// </remarks>
    private static readonly Dictionary<string, string> AllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/MarketAssistant.App/ViewModels/AssetPageViewModel.cs"] =
            "资产页聚合三个数据区块（K线/快照/盘口）的加载与状态机。"
            + "已完成一次职责梳理（逐笔区块随 A 股数据源修正一并移除），"
            + "确认区块间共享 _orderBookState 等状态字段，"
            + "直接切分会破坏状态机原子性；需先补单元测试再动。",

        ["src/MarketAssistant.App/ViewModels/AgentAnalysisViewModel.cs"] =
            "研判页编排 + 聊天侧边栏联动 + 进度平滑计时器。"
            + "进度计时器与流式增量渲染耦合较紧，待 M3 回测接入时一并重构。",

        ["src/MarketAssistant.App.Services/Agents/MarketAnalysis/MarketAnalysisWorkflow.cs"] =
            "已于本轮完成职责拆分（1086 → 768 行）：进度跟踪、模型解析、"
            + "分析师创建各自独立成 partial 文件。剩余部分为分析编排主体"
            + "（AnalyzeAsync + ExecuteWorkflowAsync），再切分会破坏编排可读性。",

        ["src/MarketAssistant.App.Services/Trading/TradeExecutor.cs"] =
            "下单执行链：条件单规格计算、风控校验、持仓更新、成交回报映射。"
            + "已有 3 个测试覆盖，但 Mock 按位置参数构造构造器，"
            + "改动构造签名会破坏 6 处 mock，需先解耦依赖再拆。",

        ["src/MarketAssistant.App.Services/Trading/MarketMonitor.cs"] =
            "策略调度 + 行情监控 + 告警触发的组合体，"
            + "与 StrategyEngine 存在双向依赖，需成对重构。",

        ["src/MarketAssistant.App/ViewModels/MCPConfigPageViewModel.cs"] =
            "MCP 配置编辑页，暂无单元测试。属低频管理页面，"
            + "拆分优先级低于交易与分析链路。",

        ["src/MarketAssistant.App.Services/Agents/MarketChatSession.cs"] =
            "会话编排 + 历史压缩 + Token 统计，已有 2 个测试覆盖，"
            + "待与记忆（M3 方向）一并演进时拆分。",

        ["src/MarketAssistant.App.Services/Applications/PriceAlert/PriceAlertService.cs"] =
            "价格告警规则 + 触发判定 + 数据库迁移，暂无单元测试。"
            + "与交易风控耦合，先补测试再拆。"
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void AllowList_ShouldNotExceedBudget()
    {
        // 递减预算：豁免表只能缩小。新增条目必须先偿还等量存量，
        // 否则"拆分 → 豁免 → 再拆分"会变成无限循环，阈值形同虚设。
        Assert.IsTrue(
            AllowList.Count <= AllowListBudget,
            $"豁免条目 {AllowList.Count} 条已超出预算 {AllowListBudget}。" +
            "请先拆分现有欠账文件并移除其豁免条目，再考虑新增。");
    }

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

    [TestMethod]
    [TestCategory("Unit")]
    public void AllowList_Entries_ShouldStillBeOversized()
    {
        // 反向检查：已降到阈值以内的文件必须移出豁免表。
        // 否则会积累"看不见的豁免"——文件明明合规却仍占着豁免名额，
        // 让人误以为债务更重，也让真正的新增超限混在其中不被察觉。
        var noLongerOversized = AllowList.Keys
            .Select(relative => (Relative: relative, Full: Path.Combine(
                RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            .Where(entry => File.Exists(entry.Full) && CountLines(entry.Full) <= MaxLinesPerFile)
            .Select(entry => $"{entry.Relative}（现 {CountLines(entry.Full)} 行，已不超限）")
            .ToList();

        Assert.AreEqual(
            0,
            noLongerOversized.Count,
            "以下文件已不超过阈值，请从 AllowList 移除以保持台账真实：\n  "
            + string.Join("\n  ", noLongerOversized));
    }

    /// <summary>
    /// 豁免数量上限（递减预算）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 治理采用递减预算而非"过半即可"：后者在当前状态下（8 个超限全部豁免）
    /// 会立刻失败，等于要求先清零再登记，而清零需要先豁免才能提交拆分成果——
    /// 陷入死锁。
    /// </para>
    /// <para>
    /// 此处登记当前存量 8 个作为起点，此后<b>只能减少不能增加</b>。
    /// 每拆掉一个文件就把数值改小一格；需要增加时必须同时在本文件注释中
    /// 说明新增原因并给出偿还计划，否则视为治理倒退。
    /// </para>
    /// <para>
    /// 配合 <see cref="AllowList_ShouldNotContainStaleEntries"/> 与
    /// <see cref="AllowList_Entries_ShouldStillBeOversized"/>，形成三重约束：
    /// 条目不能指向已删文件、不能指向已合规文件、总数不能超出预算。
    /// </para>
    /// </remarks>
    private const int AllowListBudget = 8;

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
