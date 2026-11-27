using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TestMarketAssistant.Architecture;

/// <summary>
/// 样式优先级护栏：模板内命中类样式的元素，不得再用会被类样式盖掉的本地值写同一属性。
/// </summary>
/// <remarks>
/// <para>
/// 问题：<c>DataTemplate</c> 里的 <c>&lt;TextBlock Classes="caption" Foreground="{DynamicResource X}"/&gt;</c>
/// 编译、运行都不报错，但 <c>X</c> 实际没有生效——类样式（StyleTrigger 优先级）压过了模板内的
/// 本地 DynamicResource（Template 优先级）。字重与颜色悄悄退回类样式的取值，只有逐像素对比才看得出来。
/// </para>
/// <para>
/// 实测（Avalonia 12.1.1，元素带 <c>Classes</c> 且类样式设置了同一属性）：
/// </para>
/// <code>
/// 本地值写法                 ControlTemplate   DataTemplate
/// 字面量 / StaticResource    被类样式盖掉      本地值生效
/// DynamicResource            被类样式盖掉      被类样式盖掉
/// TemplateBinding            被类样式盖掉      —
/// Binding                    本地值生效        本地值生效
/// </code>
/// <para>
/// 两种模板互相嵌套时一律按 ControlTemplate 计（同样实测）。
/// </para>
/// <para>
/// 修法：在视图的 <c>UserControl.Styles</c> 里写具名类样式（如 <c>TextBlock.news-title</c>），
/// 元素写 <c>Classes="body-sm news-title"</c>——类与类之间按作用域远近、声明先后决定胜负，不再有优先级陷阱；
/// 本地值若与类样式取值相同，直接删除。详见 docs/design.md §3.2。
/// </para>
/// <para>
/// 匹配刻意保守：只看选择器最后一段、忽略祖先条件，不带伪类的类样式才参与。
/// 遇到误报同样改成具名类样式，不要放宽本测试。
/// </para>
/// </remarks>
[TestClass]
public sealed class TemplateStyleOverrideGuardTest
{
    private static readonly string[] GlobalRootNames = ["Styles", "ResourceDictionary", "Application"];

    private static readonly string[] BindingPrefixes = ["{Binding", "{CompiledBinding", "{ReflectionBinding"];

    /// <summary>选择器的一个复合段：[命名空间前缀|]类型.类名.类名:伪类#名称。</summary>
    private static readonly Regex CompoundSelector = new(
        @"^(?:[A-Za-z][A-Za-z0-9]*\|)?(?<type>[A-Za-z][A-Za-z0-9]*)?(?<classes>(?:\.[A-Za-z0-9_-]+)*)(?<pseudo>(?::[A-Za-z-]+(?:\([^)]*\))?)*)(?:#[A-Za-z0-9_]+)?$",
        RegexOptions.CultureInvariant);

    private const string GlobalCaptionStyle = """
        <Styles xmlns="https://github.com/avaloniaui">
            <Style Selector="TextBlock.caption">
                <Setter Property="Foreground" Value="Gray"/>
                <Setter Property="FontWeight" Value="Normal"/>
            </Style>
        </Styles>
        """;

    private const string DynamicCaption =
        """<TextBlock Classes="caption" Foreground="{DynamicResource TextBrush}"/>""";

    private enum TemplateKind
    {
        Data,
        Control,
    }

    /// <summary>
    /// 一条会按类命中元素的样式：最后一段选择器带类、不带伪类，且至少有一个 Setter。
    /// <paramref name="ScopeOwner"/> 为空表示全局样式，否则只作用于该元素的子树。
    /// </summary>
    private sealed record ClassStyle(
        string? ElementType,
        HashSet<string> Classes,
        HashSet<string> Properties,
        XElement? ScopeOwner,
        string Selector)
    {
        public bool Covers(XElement element, HashSet<string> elementClasses) =>
            (ElementType is null || ElementType == element.Name.LocalName)
            && Classes.IsSubsetOf(elementClasses)
            && (ScopeOwner is null || element.AncestorsAndSelf().Contains(ScopeOwner));
    }

    private sealed record Violation(
        string FileName,
        int Line,
        string Element,
        string Classes,
        string Property,
        string Value,
        string Selectors,
        TemplateKind Kind)
    {
        public override string ToString() =>
            $"{FileName}:{Line}  <{Element} Classes=\"{Classes}\"> {Property}=\"{Value}\"  被类样式 [{Selectors}] 盖掉（{Kind}Template）";
    }

    /// <summary>真实视图树：不得存在被类样式盖掉的模板本地值。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void AppAxaml_ShouldNotContainTemplateLocalValuesOverriddenByClassStyles()
    {
        var repositoryRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repositoryRoot.FullName, "MarketAssistant.slnx")))
        {
            repositoryRoot = repositoryRoot.Parent ?? throw new InvalidOperationException("未能定位仓库根目录（缺少 MarketAssistant.slnx）");
        }

        var sources = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot.FullName, "src", "MarketAssistant.App"), "*.axaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot.FullName, path).Replace('\\', '/'))
            .Where(name => !name.Contains("/bin/", StringComparison.Ordinal) && !name.Contains("/obj/", StringComparison.Ordinal))
            .Select(name => (Name: name, Xaml: File.ReadAllText(Path.Combine(repositoryRoot.FullName, name))))
            .ToList();

        // 找不到文件时"零违规"没有意义，必须让护栏自己先失败
        Assert.IsTrue(sources.Count > 0, "未找到任何 .axaml 文件");

        var violations = Scan(sources);

        Assert.AreEqual(
            0,
            violations.Count,
            "以下模板内的本地值会被同属性的类样式盖掉（编译与运行都不报错，但实际不生效）。\n"
            + "修法：在视图 UserControl.Styles 里写具名类样式并通过 Classes 引用；取值相同的本地值直接删除。详见 docs/design.md §3.2。\n  "
            + string.Join("\n  ", violations));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_DataTemplateLocalDynamicResource_ShouldBeReported()
    {
        var violations = Scan(
        [
            ("样式.axaml", GlobalCaptionStyle),
            ("视图.axaml", DataTemplateView(
                """<TextBlock Classes="caption" Foreground="{DynamicResource TextBrush}" FontWeight="{DynamicResource WeightBody}"/>""")),
        ]);

        CollectionAssert.AreEquivalent(
            new[] { "Foreground", "FontWeight" },
            violations.Select(violation => violation.Property).ToArray());
        Assert.IsTrue(violations.All(violation => violation.Kind == TemplateKind.Data));
        Assert.IsTrue(violations.All(violation => violation.FileName == "视图.axaml"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_DataTemplateLocalValuesThatWin_ShouldNotBeReported()
    {
        var violations = Scan(
        [
            ("样式.axaml", GlobalCaptionStyle),
            ("字面量.axaml", DataTemplateView("""<TextBlock Classes="caption" Foreground="Red"/>""")),
            ("静态资源.axaml", DataTemplateView("""<TextBlock Classes="caption" Foreground="{StaticResource TextBrush}"/>""")),
            ("绑定.axaml", DataTemplateView("""<TextBlock Classes="caption" Foreground="{Binding Tint}"/>""")),
        ]);

        Assert.AreEqual(0, violations.Count, string.Join("\n", violations));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_ControlTemplateLocalValues_ShouldBeReportedExceptBinding()
    {
        var violations = Scan(
        [
            ("样式.axaml", GlobalCaptionStyle),
            ("模板.axaml", ControlTemplateFile(
                """
                <StackPanel>
                    <TextBlock Classes="caption" Foreground="Red"/>
                    <TextBlock Classes="caption" Foreground="{StaticResource TextBrush}"/>
                    <TextBlock Classes="caption" Foreground="{DynamicResource TextBrush}"/>
                    <TextBlock Classes="caption" Foreground="{TemplateBinding Foreground}"/>
                    <TextBlock Classes="caption" Foreground="{Binding Tint, RelativeSource={RelativeSource TemplatedParent}}"/>
                </StackPanel>
                """)),
        ]);

        Assert.AreEqual(4, violations.Count, string.Join("\n", violations));
        Assert.IsTrue(violations.All(violation => violation.Kind == TemplateKind.Control));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_DataTemplateNestedInControlTemplate_ShouldFollowControlTemplateRule()
    {
        var violations = Scan(
        [
            ("样式.axaml", GlobalCaptionStyle),
            ("模板.axaml", ControlTemplateFile(
                """
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate><TextBlock Classes="caption" Foreground="Red"/></DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                """)),
        ]);

        Assert.AreEqual(1, violations.Count, string.Join("\n", violations));
        Assert.AreEqual(TemplateKind.Control, violations[0].Kind);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_LocalStyle_ShouldOnlyCoverItsOwnerSubtree()
    {
        const string localStyle = """<Style Selector="TextBlock.caption"><Setter Property="Foreground" Value="Gray"/></Style>""";

        var violations = Scan(
        [
            ("宿主内.axaml", $$"""
                <UserControl xmlns="https://github.com/avaloniaui">
                    <StackPanel>
                        <StackPanel.Styles>{{localStyle}}</StackPanel.Styles>
                        {{DataTemplateFragment(DynamicCaption)}}
                    </StackPanel>
                </UserControl>
                """),
            ("宿主外.axaml", $$"""
                <UserControl xmlns="https://github.com/avaloniaui">
                    <StackPanel><StackPanel.Styles>{{localStyle}}</StackPanel.Styles></StackPanel>
                    {{DataTemplateFragment(DynamicCaption)}}
                </UserControl>
                """),
            ("无样式.axaml", DataTemplateView(DynamicCaption)),
        ]);

        Assert.AreEqual(1, violations.Count, string.Join("\n", violations));
        Assert.AreEqual("宿主内.axaml", violations[0].FileName);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_SelectorVariants_ShouldMatchByLastCompoundClasses()
    {
        Assert.AreEqual(1, CountViolations("TextBlock.caption", "Foreground", DynamicCaption), "类型 + 类");
        Assert.AreEqual(1, CountViolations(".caption", "Foreground", DynamicCaption), "只有类");
        Assert.AreEqual(1, CountViolations("StackPanel > TextBlock.caption", "Foreground", DynamicCaption), "祖先条件被忽略，只看最后一段");
        Assert.AreEqual(1, CountViolations("Button.primary, TextBlock.caption", "Foreground", DynamicCaption), "逗号分隔的多个选择器");
        Assert.AreEqual(
            1,
            CountViolations(
                "local|Badge.caption",
                "Foreground",
                """<local:Badge Classes="caption" Foreground="{DynamicResource TextBrush}"/>"""),
            "带命名空间前缀的类型");
        Assert.AreEqual(
            1,
            CountViolations(
                "TextBlock.caption.strong",
                "Foreground",
                """<TextBlock Classes="caption strong" Foreground="{DynamicResource TextBrush}"/>"""),
            "多个类同时命中");
        Assert.AreEqual(
            1,
            CountViolations(
                "TextBlock.caption",
                "Foreground",
                """<TextBlock Classes.caption="{Binding IsDim}" Foreground="{DynamicResource TextBrush}"/>"""),
            "条件类 Classes.x 也算命中");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scan_UncoveredElements_ShouldNotBeReported()
    {
        Assert.AreEqual(0, CountViolations("TextBlock.caption:disabled", "Foreground", DynamicCaption), "带伪类的样式不参与");
        Assert.AreEqual(0, CountViolations("Button.caption", "Foreground", DynamicCaption), "类型不符");
        Assert.AreEqual(0, CountViolations("TextBlock.caption.strong", "Foreground", DynamicCaption), "元素缺少样式要求的类");
        Assert.AreEqual(0, CountViolations("TextBlock.caption", "FontSize", DynamicCaption), "样式没有设置该属性");
        Assert.AreEqual(
            0,
            CountViolations("TextBlock", "Foreground", """<TextBlock Foreground="{DynamicResource TextBrush}"/>"""),
            "纯类型样式优先级低于模板内本地值");

        var outsideTemplate = Scan(
        [
            ("视图.axaml", $$"""
                <UserControl xmlns="https://github.com/avaloniaui">
                    <UserControl.Styles>{{LocalStyle("TextBlock.caption", "Foreground")}}</UserControl.Styles>
                    {{DynamicCaption}}
                </UserControl>
                """),
        ]);
        Assert.AreEqual(0, outsideTemplate.Count, "模板之外的本地值本来就胜出");
    }

    /// <summary>
    /// 扫描一组 XAML：先汇总全部类样式，再逐元素检查模板内的本地值。
    /// </summary>
    private static List<Violation> Scan(IReadOnlyList<(string Name, string Xaml)> sources)
    {
        var documents = sources
            .Select(source => (source.Name, Document: XDocument.Parse(source.Xaml, LoadOptions.SetLineInfo)))
            .ToList();
        var styles = documents.SelectMany(document => CollectClassStyles(document.Document)).ToList();

        return documents
            .SelectMany(document => document.Document
                .Descendants()
                .SelectMany(element => FindViolations(document.Name, element, styles)))
            .OrderBy(violation => violation.FileName, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ToList();
    }

    /// <summary>
    /// 收集类样式。<c>Styles</c> / <c>ResourceDictionary</c> / <c>Application</c> 为根的文件视为全局样式，
    /// 其余文件里的样式只作用于所在 <c>*.Styles</c> 属性的宿主元素子树。
    /// </summary>
    private static IEnumerable<ClassStyle> CollectClassStyles(XDocument document)
    {
        var isGlobalFile = GlobalRootNames.Contains(document.Root!.Name.LocalName);

        foreach (var style in document.Descendants().Where(element => element.Name.LocalName == "Style"))
        {
            var selector = style.Attribute("Selector")?.Value;
            var properties = style.Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .Select(setter => setter.Attribute("Property")?.Value)
                .OfType<string>()
                .ToHashSet();
            if (selector is null || properties.Count == 0)
            {
                continue;
            }

            var owner = isGlobalFile
                ? null
                : style.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName.EndsWith(".Styles", StringComparison.Ordinal))?.Parent;
            if (!isGlobalFile && owner is null)
            {
                continue;
            }

            foreach (var alternative in selector.Split(','))
            {
                var lastCompound = alternative
                    .Replace("/template/", " ")
                    .Split([' ', '\t', '\r', '\n', '>'], StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault();
                var match = CompoundSelector.Match(lastCompound ?? string.Empty);
                if (!match.Success || match.Groups["classes"].Length == 0 || match.Groups["pseudo"].Length > 0)
                {
                    continue;
                }

                var elementType = match.Groups["type"].Success ? match.Groups["type"].Value : null;
                var classes = match.Groups["classes"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                yield return new ClassStyle(elementType, classes, properties, owner, selector);
            }
        }
    }

    /// <summary>找出一个元素上会被类样式盖掉的本地属性值；不在模板内、或没有 Classes 的元素直接跳过。</summary>
    private static IEnumerable<Violation> FindViolations(string fileName, XElement element, IReadOnlyList<ClassStyle> styles)
    {
        var kind = ResolveTemplateKind(element);
        var classes = ReadClasses(element);
        if (kind is null || classes.Count == 0)
        {
            yield break;
        }

        foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.Namespace == XNamespace.None))
        {
            var property = attribute.Name.LocalName;
            if (!LosesToClassStyle(kind.Value, attribute.Value))
            {
                continue;
            }

            var selectors = styles
                .Where(style => style.Properties.Contains(property) && style.Covers(element, classes))
                .Select(style => style.Selector)
                .Distinct()
                .ToList();
            if (selectors.Count > 0)
            {
                yield return new Violation(
                    fileName,
                    ((IXmlLineInfo)attribute).LineNumber,
                    element.Name.LocalName,
                    string.Join(' ', classes),
                    property,
                    attribute.Value,
                    string.Join(" | ", selectors),
                    kind.Value);
            }
        }
    }

    /// <summary>任一层祖先是 ControlTemplate 即按 ControlTemplate 计，否则有 DataTemplate 祖先按 DataTemplate 计。</summary>
    private static TemplateKind? ResolveTemplateKind(XElement element)
    {
        TemplateKind? kind = null;
        foreach (var ancestor in element.Ancestors())
        {
            switch (ancestor.Name.LocalName)
            {
                case "ControlTemplate":
                    return TemplateKind.Control;
                case "DataTemplate" or "TreeDataTemplate":
                    kind = TemplateKind.Data;
                    break;
            }
        }

        return kind;
    }

    private static HashSet<string> ReadClasses(XElement element)
    {
        var classes = new HashSet<string>(
            element.Attribute("Classes")?.Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries) ?? []);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.LocalName.StartsWith("Classes.", StringComparison.Ordinal))
            {
                classes.Add(attribute.Name.LocalName["Classes.".Length..]);
            }
        }

        return classes;
    }

    /// <summary>按上文实测表判断：该写法的本地值在此类模板里是否会输给类样式。</summary>
    private static bool LosesToClassStyle(TemplateKind kind, string value) => kind switch
    {
        TemplateKind.Data => value.StartsWith("{DynamicResource", StringComparison.Ordinal),
        _ => !BindingPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)),
    };

    private static string LocalStyle(string selector, string property) =>
        $"""<Style Selector="{selector}"><Setter Property="{property}" Value="Gray"/></Style>""";

    private static int CountViolations(string selector, string property, string element) =>
        Scan([("视图.axaml", DataTemplateView(element, LocalStyle(selector, property)))]).Count;

    private static string DataTemplateFragment(string element) =>
        $"<ItemsControl><ItemsControl.ItemTemplate><DataTemplate>{element}</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>";

    private static string DataTemplateView(string element, string localStyles = "") => $$"""
        <UserControl xmlns="https://github.com/avaloniaui" xmlns:local="using:Test">
            <UserControl.Styles>{{localStyles}}</UserControl.Styles>
            {{DataTemplateFragment(element)}}
        </UserControl>
        """;

    private static string ControlTemplateFile(string element) => $$"""
        <Styles xmlns="https://github.com/avaloniaui">
            <Style Selector="Button.card">
                <Setter Property="Template">
                    <ControlTemplate>{{element}}</ControlTemplate>
                </Setter>
            </Style>
        </Styles>
        """;
}
