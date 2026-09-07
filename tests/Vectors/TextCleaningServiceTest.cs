using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class TextCleaningServiceTest : BaseAgentTest
{
    private ITextCleaningService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();
        _service = _serviceProvider.GetRequiredService<ITextCleaningService>();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldRemoveExtraWhitespace()
    {
        // Arrange
        var input = "This   is  a   test  string";
        var expected = "This is a test string";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldRemoveLeadingAndTrailingWhitespace()
    {
        // Arrange
        var input = "   This is a test string   ";
        var expected = "This is a test string";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldHandleEmptyString()
    {
        // Arrange
        var input = "";
        var expected = "";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldHandleNullString()
    {
        // Arrange
        string? input = null;
        var expected = "";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldRemovePageNumbers()
    {
        // Arrange
        var input = "This is a test string. Page 1 of 10";
        var expected = "This is a test string.";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldRemoveUrls()
    {
        // Arrange
        var input = "This is a test string with a URL: https://example.com";
        var expected = "This is a test string with a URL:";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldNormalizeLineEndings()
    {
        // Arrange
        var input = "Line 1\r\nLine 2\rLine 3";
        var expected = "Line 1\nLine 2\nLine 3";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    // ---- P0-6 回归测试：金融数字不得被清洗规则吞掉或改写 ----

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldNotTouchFinancialNumbers()
    {
        // 10 位长数字曾被通用"电话号码"规则整段删除
        Assert.AreEqual("成交额1000000000元", _service.Clean("成交额1000000000元").Trim());
        // 8 位长数字曾被"重复字符折叠"规则缩水 10 万倍
        Assert.AreEqual("营收10000000元", _service.Clean("营收10000000元").Trim());
        // 千分位格式保持不变
        Assert.AreEqual("1,000,000", _service.Clean("1,000,000").Trim());
        // 中文叠词不是噪声，不得折叠
        Assert.IsTrue(_service.Clean("哈哈哈哈").Contains("哈哈哈哈"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clean_ShouldStillRemoveChinesePhoneNumber()
    {
        // 手机号语义（11 位 1[3-9] 开头）仍应被有损清洗移除
        var result = _service.Clean("咨询电话13800138000谢谢");
        Assert.IsFalse(result.Contains("13800138000"));
        Assert.IsTrue(result.Contains("咨询电话") && result.Contains("谢谢"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Normalize_ShouldPreserveAllContent()
    {
        // 摄取路径使用的 Normalize 必须完全无损
        var input = "成交额1000000000元\n咨询电话13800138000";
        var result = _service.Normalize(input);
        StringAssert.Contains(result, "1000000000");
        StringAssert.Contains(result, "13800138000");
    }
}
