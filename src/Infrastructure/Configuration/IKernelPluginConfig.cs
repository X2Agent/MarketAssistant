using MarketAssistant.Agents;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Infrastructure.Configuration;

public interface IKernelPluginConfig
{
    Kernel PluginConfig(Kernel kernel, AnalysisAgent agent);
}

