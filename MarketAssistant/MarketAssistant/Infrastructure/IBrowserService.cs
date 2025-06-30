namespace MarketAssistant.Infrastructure;

public interface IBrowserService
{
    (string Path, bool Found) CheckBrowser();
}
