# RichTextBlock 使用示例

## 基本用法

### 1. 自动格式检测（推荐）

最简单的用法，控件会自动识别内容格式：

```xml
<controls:RichTextBlock Text="{Binding MessageContent}" />
```

### 2. 手动指定格式

当你明确知道内容格式时，可以手动指定以提高性能：

```xml
<!-- Markdown格式 -->
<controls:RichTextBlock Text="{Binding MarkdownContent}" 
                       Format="Markdown" />

<!-- HTML格式 -->
<controls:RichTextBlock Text="{Binding HtmlContent}" 
                       Format="Html" />

<!-- 纯文本 -->
<controls:RichTextBlock Text="{Binding PlainText}" 
                       Format="PlainText" />
```

## ViewModel示例

```csharp
public class ChatViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _messageContent = string.Empty;
    
    // AI返回Markdown格式
    public void OnMarkdownResponse(string markdown)
    {
        MessageContent = markdown;
        // 例如: "## 分析结果\n\n**重要提示**：这是一条带格式的消息"
    }
    
    // AI返回HTML格式
    public void OnHtmlResponse(string html)
    {
        MessageContent = html;
        // 例如: "<h2>分析结果</h2><p><strong>重要提示</strong>：这是HTML格式</p>"
    }
    
    // 普通文本
    public void OnPlainTextResponse(string text)
    {
        MessageContent = text;
        // 例如: "这是一条普通文本消息"
    }
}
```

## 实际场景示例

### 场景1：聊天消息列表

```xml
<ListBox ItemsSource="{Binding Messages}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Border Padding="12,8"
                    Background="{DynamicResource CardBackgroundBrush}">
                <StackPanel>
                    <TextBlock Text="{Binding Sender}" 
                              FontWeight="Bold"/>
                    
                    <!-- 自动识别消息格式 -->
                    <controls:RichTextBlock Text="{Binding Content}" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### 场景2：分析报告显示

```xml
<ScrollViewer>
    <StackPanel Spacing="16">
        <TextBlock Text="分析报告" FontSize="24" FontWeight="Bold"/>
        
        <!-- 显示HTML格式的报告 -->
        <controls:RichTextBlock Text="{Binding HtmlReport}" 
                               Format="Html"
                               MinHeight="400"/>
    </StackPanel>
</ScrollViewer>
```

### 场景3：技术文档查看器

```xml
<Grid>
    <controls:RichTextBlock Text="{Binding DocumentContent}" 
                           Format="Markdown"/>
</Grid>
```

## 测试不同格式

### Markdown测试内容

```csharp
MessageContent = @"
# 股票分析报告

## 基本面分析

**市盈率**：15.2  
*市净率*：2.3

### 主要指标

1. 营收增长：**12.5%**
2. 净利润率：8.3%
3. ROE：~~10.1%~~ **12.8%**

```csharp
// 代码示例
decimal pe = 15.2m;
decimal pb = 2.3m;
```

> 重要提示：以上数据仅供参考

[查看完整报告](https://example.com/report)
";
```

### HTML测试内容

```csharp
MessageContent = @"
<html>
<head>
    <meta charset='utf-8'>
</head>
<body>
    <h1>股票分析报告</h1>
    <h2>基本面分析</h2>
    <p><strong>市盈率</strong>：15.2</p>
    <p><em>市净率</em>：2.3</p>
    
    <h3>主要指标</h3>
    <ul>
        <li>营收增长：<strong>12.5%</strong></li>
        <li>净利润率：8.3%</li>
        <li>ROE：<del>10.1%</del> <strong>12.8%</strong></li>
    </ul>
    
    <pre><code>
// 代码示例
decimal pe = 15.2m;
decimal pb = 2.3m;
    </code></pre>
    
    <blockquote>
        <p>重要提示：以上数据仅供参考</p>
    </blockquote>
    
    <p><a href='https://example.com/report'>查看完整报告</a></p>
</body>
</html>
";
```

### 纯文本测试内容

```csharp
MessageContent = @"股票分析报告

基本面分析
市盈率：15.2
市净率：2.3

主要指标：
- 营收增长：12.5%
- 净利润率：8.3%
- ROE：12.8%

重要提示：以上数据仅供参考";
```

## 性能优化建议

### 1. 明确格式类型

当你知道内容格式时，手动指定Format属性可以跳过格式检测，提高性能：

```xml
<controls:RichTextBlock Text="{Binding KnownMarkdown}" 
                       Format="Markdown" />
```

### 2. 大量短消息使用纯文本

对于简单的短消息，使用纯文本格式最高效：

```xml
<controls:RichTextBlock Text="{Binding SimpleMessage}" 
                       Format="PlainText" />
```

### 3. 避免频繁切换格式

如果内容格式频繁变化，控件需要不断切换渲染引擎，这会影响性能。

## 常见问题

### Q1：HTML内容不显示？
A：确保HTML内容是有效的。如果是HTML片段，控件会自动包装成完整文档。

### Q2：Markdown没有正确渲染？
A：检查Markdown语法是否正确，某些复杂的Markdown扩展语法可能不被支持。

### Q3：如何自定义HTML样式？
A：HTML片段会被自动包装并添加基础样式。要完全自定义，可以提供完整的HTML文档（包含样式）。

### Q4：性能如何？
A：
- 纯文本：最快
- Markdown：中等（首次渲染需要解析）
- HTML：较慢（需要WebView初始化）

### Q5：支持哪些Markdown扩展？
A：支持标准Markdown语法，具体取决于Markdown.Avalonia库的能力。

## 调试技巧

### 1. 查看检测到的格式

可以在代码中添加调试输出：

```csharp
var richTextBlock = this.FindControl<RichTextBlock>("MyRichText");
System.Diagnostics.Debug.WriteLine($"检测到的格式: {richTextBlock.Format}");
```

### 2. 测试格式检测

创建测试内容验证格式检测是否正确：

```csharp
// 应该被识别为HTML
var htmlTest = "<div>test</div><p>content</p>";

// 应该被识别为Markdown
var markdownTest = "# Title\n\n**bold**";

// 应该被识别为纯文本
var plainTest = "Simple text message";
```

## 总结

`RichTextBlock` 是一个强大且智能的富文本控件，适用于：
- ✅ 聊天应用（支持多种消息格式）
- ✅ 文档查看器
- ✅ 报告展示
- ✅ AI对话界面
- ✅ 技术文档显示

通过自动格式检测和多渲染引擎支持，它能够无缝处理各种富文本内容！


