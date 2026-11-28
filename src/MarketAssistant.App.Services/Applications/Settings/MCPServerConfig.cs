using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// MCP服务器配置类。
/// 仅 <see cref="IsEnabled"/> 参与变更通知——MCP 配置页的服务器列表行用开关直接切换启用状态，
/// 需要它把变化推回 UI；其余属性只在编辑表单里读写，走普通的「保存」提交，无需通知。
/// </summary>
public class MCPServerConfig : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 服务器ID，用于唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 服务器名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 服务器描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 服务器类型：stdio(标准输入/输出)、sse(服务器发送事件) 或 streamableHttp(流式HTTP)
    /// </summary>
    public string TransportType { get; set; } = "stdio";

    /// <summary>
    /// 命令或URL，根据TransportType类型决定
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// 命令参数，用于stdio类型
    /// </summary>
    public string Arguments { get; set; } = "";

    /// <summary>
    /// 环境变量，用于stdio类型
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = [];

    /// <summary>
    /// 是否启用。列表行的开关直接绑定它并即时落库，因此需要变更通知。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    /// <summary>
    /// 工具分组标签，用于工具的选择性暴露
    /// 例如: "search", "finance", "code"
    /// </summary>
    public string Category { get; set; } = "general";

    /// <summary>
    /// 允许的工具名称列表（白名单）。为空表示不暴露任何工具；
    /// 只有显式设置 <see cref="AllowAllTools"/> = true 才会加载该服务器全部工具。
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// 是否允许加载全部工具。默认 false；开启后忽略 <see cref="AllowedTools"/> 白名单。
    /// 属于高危选项，仅应在明确信任该服务器时启用。
    /// </summary>
    public bool AllowAllTools { get; set; } = false;

    /// <summary>
    /// 工具白名单机制的当前配置版本号。
    /// </summary>
    public const int CurrentToolsSchemaVersion = 1;

    /// <summary>
    /// 配置结构版本。默认 0 表示旧版本保存的配置（JSON 无此字段或未经新版本 UI 确认），
    /// 由 UI 提示用户重新勾选工具白名单后升级为 <see cref="CurrentToolsSchemaVersion"/> 并落盘。
    /// </summary>
    public int ToolsSchemaVersion { get; set; }

}