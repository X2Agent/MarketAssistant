# 自定义控件库

本目录包含 MarketAssistant.Avalonia 项目的自定义控件。

## 📁 推荐目录结构

```
Controls/
├── Common/              # 通用控件
│   ├── LoadingSpinner   # 加载动画组件
│   ├── CustomButton     # 自定义按钮
│   └── StatusIndicator  # 状态指示器
├── Stock/               # 股票相关控件
│   ├── StockCard        # 股票卡片
│   ├── PriceDisplay     # 价格显示
│   └── TrendChart       # 趋势图表
├── Chart/               # 图表控件
│   ├── KLineChart       # K线图
│   └── VolumeChart      # 成交量图
└── README.md           # 本文档
```

## 🎯 组件分类建议

### 通用控件 (Common/)
适合放置可复用的基础UI组件：
- 加载动画、进度条、按钮、输入框
- 对话框、弹出框、工具提示
- 导航组件、分页组件

### 业务特定控件 (Stock/, Chart/)
适合放置与业务逻辑紧密相关的组件：
- 股票卡片、价格显示、K线图
- 资讯卡片、分析报告展示
- 收藏列表、筛选面板

## 🛠️ 创建自定义组件的步骤

### 1. 选择合适的位置
根据组件功能选择对应的子目录，如果不存在则创建。

### 2. 创建组件文件
```bash
# 创建XAML文件
touch Controls/Common/MyControl.axaml
# 创建代码隐藏文件  
touch Controls/Common/MyControl.axaml.cs
```

### 3. 基本模板结构

**MyControl.axaml:**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MarketAssistant.Avalonia.Controls.Common.MyControl">
    <!-- 你的XAML内容 -->
</UserControl>
```

**MyControl.axaml.cs:**
```csharp
using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Avalonia.Controls.Common;

public partial class MyControl : UserControl
{
    public MyControl()
    {
        InitializeComponent();
    }
}
```

## 开发规范

### 命名约定
- 使用 PascalCase 命名控件类
- 属性使用 `Property` 后缀定义依赖属性
- XAML 文件使用 `.axaml` 扩展名
- 代码隐藏文件使用 `.axaml.cs` 扩展名

### 样式规范
- 使用动态资源引用主题颜色
- 遵循 4 的倍数间距规范 (4, 8, 12, 16)
- 支持亮色/暗色主题切换

### 文档要求
- 每个控件类添加 XML 文档注释
- 公共属性添加功能说明
- 复杂逻辑添加内联注释

## 添加新控件

1. 在相应分类目录下创建 `.axaml` 和 `.axaml.cs` 文件
2. 继承自 `UserControl` 或其他合适的基类
3. 使用 `StyledProperty` 定义可绑定属性
4. 在本文档中添加使用说明
5. 考虑添加单元测试

## 最佳实践

1. **属性设计**: 使用依赖属性支持数据绑定
2. **样式分离**: 将样式定义在控件内部或独立的样式文件中
3. **性能考虑**: 避免在属性变更时进行复杂计算
4. **可访问性**: 支持键盘导航和屏幕阅读器
5. **主题支持**: 使用动态资源确保主题兼容性
