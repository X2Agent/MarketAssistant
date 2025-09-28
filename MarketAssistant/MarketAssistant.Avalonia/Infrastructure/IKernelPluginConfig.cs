using MarketAssistant.Agents;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Infrastructure;

public interface IKernelPluginConfig
{
    Kernel PluginConfig(Kernel kernel, AnalysisAgents analysisAgent);
}
