using MarketAssistant.Pages;

namespace MarketAssistant
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("analysis", typeof(AgentAnalysisPage));
            Routing.RegisterRoute("stock", typeof(StockPage));
            Routing.RegisterRoute("mcpconfig", typeof(MCPServerConfigPage));
        }
    }
}
