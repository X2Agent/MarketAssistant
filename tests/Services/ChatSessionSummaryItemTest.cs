using MarketAssistant.Services;
using MarketAssistant.ViewModels;

namespace TestMarketAssistant.Services;

/// <summary>
/// <see cref="ChatSessionSummaryItem"/> 展示字段契约测试。
/// </summary>
/// <remarks>
/// 历史会话面板直接绑定 <c>UpdatedAtText</c>：持久化写入侧是 UTC 往返格式，
/// 展示侧必须转本地时间并固定为 <c>MM-dd HH:mm</c>。此前该转换无测试锁定，
/// 而「UTC 原样展示」会让最近会话看起来早数小时（东八区早 8 小时）。
/// </remarks>
[TestClass]
public sealed class ChatSessionSummaryItemTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_ShouldPassThroughIdentityFields()
    {
        var createdAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 10, 9, 23, 30, 0, DateTimeKind.Utc);

        var item = new ChatSessionSummaryItem(new ChatSessionSummary
        {
            Id = "abc123",
            Title = "茅台估值讨论",
            StockCode = "sh600519",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        });

        Assert.AreEqual("abc123", item.Id);
        Assert.AreEqual("茅台估值讨论", item.Title);
        Assert.AreEqual("sh600519", item.StockCode);
        Assert.IsFalse(item.IsActive, "默认不应标记为当前会话");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdatedAtText_ShouldBeLocalTimeWithFixedShape()
    {
        var updatedAt = new DateTime(2026, 10, 9, 23, 30, 0, DateTimeKind.Utc);

        var item = new ChatSessionSummaryItem(new ChatSessionSummary
        {
            Id = "id",
            UpdatedAt = updatedAt
        });

        // 形状锁定：MM-dd HH:mm，防止回归成完整时间戳或 UTC 原样
        Assert.IsTrue(
            System.Text.RegularExpressions.Regex.IsMatch(item.UpdatedAtText, @"^\d{2}-\d{2} \d{2}:\d{2}$"),
            $"UpdatedAtText 形状应为 MM-dd HH:mm，实际为: {item.UpdatedAtText}");
        Assert.AreEqual(
            updatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            item.UpdatedAtText,
            "必须按本地时间展示，而非持久化的 UTC 值");
    }
}
