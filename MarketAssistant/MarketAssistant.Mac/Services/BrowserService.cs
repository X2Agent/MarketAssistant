using MarketAssistant.Infrastructure;

namespace MarketAssistant.Mac.Services
{
    internal class BrowserService : IBrowserService
    {
        /// <summary>
        /// 检查Mac系统上安装的浏览器
        /// </summary>
        /// <returns>浏览器路径和是否找到浏览器</returns>
        public (string Path, bool Found) CheckBrowser()
        {
            // 首先检查Chrome浏览器
            var (chromePath, chromeFound) = CheckChrome();
            if (chromeFound)
            {
                return (chromePath, true);
            }

            // 然后检查Safari浏览器
            var (safariPath, safariFound) = CheckSafari();
            if (safariFound)
            {
                return (safariPath, true);
            }

            return (string.Empty, false);
        }

        /// <summary>
        /// 检查Mac系统上安装的Chrome浏览器
        /// </summary>
        /// <returns>Chrome路径和是否找到Chrome</returns>
        private static (string Path, bool Found) CheckChrome()
        {
            try
            {
                // 检查Chrome在Mac上的标准安装路径
                var chromePath = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                if (File.Exists(chromePath))
                {
                    return (chromePath, true);
                }
            }
            catch
            {
                // 忽略文件访问错误
            }

            return (string.Empty, false);
        }

        /// <summary>
        /// 检查Mac系统上安装的Safari浏览器
        /// </summary>
        /// <returns>Safari路径和是否找到Safari</returns>
        private static (string Path, bool Found) CheckSafari()
        {
            try
            {
                // 检查Safari在Mac上的标准安装路径
                var safariPath = "/Applications/Safari.app/Contents/MacOS/Safari";
                if (File.Exists(safariPath))
                {
                    return (safariPath, true);
                }
            }
            catch
            {
                // 忽略文件访问错误
            }

            return (string.Empty, false);
        }
    }
}
