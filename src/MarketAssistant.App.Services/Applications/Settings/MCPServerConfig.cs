using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// MCP服务器配置类
/// </summary>
public class MCPServerConfig
{
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
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

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

    /// <summary>
    /// 获取传输选项字典
    /// </summary>
    /// <returns>传输选项字典</returns>
    public Dictionary<string, string> GetTransportOptions()
    {
        var options = new Dictionary<string, string>();

        if (TransportType == "stdio")
        {
            options["command"] = Command;
            options["arguments"] = Arguments;
        }
        else if (TransportType == "sse" || TransportType == "streamableHttp")
        {
            options["url"] = Command; // 对于SSE和StreamableHttp类型，Command字段存储URL
        }

        return options;
    }
}