# Avalonia 自定义控件

## RichTextBlock - 智能富文本控件 ⭐推荐

支持多种格式（HTML、Markdown、纯文本）自动识别和渲染的智能富文本控件。

### 功能特性

- **🔍 自动格式检测**：智能识别HTML、Markdown和纯文本
- **🎨 多渲染引擎**：
  - HTML → WebView控件渲染
  - Markdown → Markdown.Avalonia渲染
  - 纯文本 → TextBlock渲染
- **🚀 性能优化**：按需加载渲染引擎，避免资源浪费
- **💡 易于使用**：一个属性绑定，自动处理所有格式

### 使用方法

#### 在XAML中使用

```xml
<UserControl xmlns:controls="using:MarketAssistant.Avalonia.Controls">
    <!-- 自动检测格式（推荐） -->
    <controls:RichTextBlock Text="{Binding Content}" />
    
    <!-- 手动指定格式 -->
    <controls:RichTextBlock Text="{Binding Content}" 
                           Format="Markdown" />
</UserControl>
```

#### 支持的格式示例

**Markdown格式：**
```markdown
# 标题

**粗体文本** 和 *斜体文本*

- 列表项 1
- 列表项 2

`内联代码`

\```csharp
// 代码块
Console.WriteLine("Hello");
\```

[链接](https://example.com)
```

**HTML格式：**
```html
<h1>标题</h1>
<p>这是一段<strong>HTML</strong>内容</p>
<ul>
  <li>列表项 1</li>
  <li>列表项 2</li>
</ul>
```

**纯文本：**
```
普通文本内容
会自动换行
```

### 格式检测规则

RichTextBlock会根据以下规则自动检测内容格式：

1. **HTML检测**：
   - 以 `<!DOCTYPE` 或 `<html` 开始
   - 包含多个HTML结构标签（如 `<div>`, `<p>`, `<span>` 等）
   - 包含HTML实体（如 `&nbsp;`, `&#160;` 等）

2. **Markdown检测**：
   - 包含标题语法 `# ## ###`
   - 包含粗体/斜体 `**text**` `*text*`
   - 包含列表语法 `- item` 或 `1. item`
   - 包含代码块 ``` 或内联代码 \`code\`
   - 包含链接语法 `[text](url)`

3. **纯文本**：不匹配以上任何规则的内容

### Format属性说明

`Format` 属性可以手动指定渲染格式：

- `ContentFormat.Auto`（默认）：自动检测
- `ContentFormat.Html`：强制使用HTML渲染
- `ContentFormat.Markdown`：强制使用Markdown渲染
- `ContentFormat.PlainText`：强制使用纯文本渲染

### 在ChatSidebarView中的应用

ChatSidebarView已经集成了RichTextBlock，可以自动处理：
- AI返回的Markdown格式内容
- AI返回的HTML格式内容  
- 普通文本消息
- 代码片段和技术文档
- 格式化的分析报告

### 技术细节

- 基于 `Markdown.Avalonia` 库（v11.0.3-a1）用于Markdown渲染
- 基于 `WebViewControl-Avalonia` 库（v3.120.10）用于HTML渲染
- 使用延迟加载策略，只在需要时创建对应的渲染引擎
- 支持UI线程安全的异步更新
- 自动清理不再使用的渲染资源

### 注意事项

1. **HTML渲染限制**：WebView需要额外的初始化时间，首次渲染HTML可能有轻微延迟
2. **性能考虑**：对于大量短文本，使用纯文本格式性能最佳
3. **格式切换**：当内容格式改变时，控件会自动切换渲染引擎
4. **样式定制**：HTML内容会自动包装在带有基础样式的完整文档中

