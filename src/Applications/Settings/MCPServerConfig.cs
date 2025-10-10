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
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

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