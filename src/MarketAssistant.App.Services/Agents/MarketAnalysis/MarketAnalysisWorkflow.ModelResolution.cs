using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// <see cref="MarketAnalysisWorkflow"/> 的模型解析部分：按分析师角色决定使用
/// 服务商默认模型还是角色专属模型。
/// </summary>
/// <remarks>
/// 独立成文件的理由：模型归属是「配置解析」职责，与编排（主文件）和
/// 进度跟踪（RunTracking）都不同；混在一起会让三者互相干扰。
/// </remarks>
public partial class MarketAnalysisWorkflow
{
    /// <summary>
    /// 解析单个分析师角色应使用的 Runtime：设置页为该角色单独分配了模型时创建独立 Runtime，
    /// 否则复用默认 Runtime。
    /// 分配不可用时（模型下线、协议未接入、缺少对应 API Key）降级回默认模型并记录告警，
    /// 不让单个角色的配置错误中断整轮分析。
    /// </summary>
    private ChatClientRuntime ResolveRuntimeForRole(Type analystType, ChatClientRuntime defaultRuntime)
    {
        var setting = _userSettingService.CurrentSetting;
        var assignedModelId = AnalystModelAssignment.GetAssignedModelId(
            setting,
            defaultRuntime.ProviderId,
            analystType.Name);

        if (assignedModelId.Length == 0 ||
            string.Equals(assignedModelId, defaultRuntime.ModelId, StringComparison.Ordinal))
        {
            return defaultRuntime;
        }

        try
        {
            var runtime = _chatClientFactory.CreateRuntime(assignedModelId);
            _logger.LogInformation(
                "分析师 {Analyst} 使用角色专属模型: {ModelId}",
                analystType.Name,
                runtime.ModelId);
            return runtime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "分析师 {Analyst} 的角色专属模型 {ModelId} 不可用，回退到默认模型 {DefaultModelId}",
                analystType.Name,
                assignedModelId,
                defaultRuntime.ModelId);
            return defaultRuntime;
        }
    }
}
