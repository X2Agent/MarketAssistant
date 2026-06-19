# TradeMonitorView 页面与交易策略修复方案

> 基于 `TradeMonitorView.axaml` 及其底层交易引擎（`MarketMonitor` / `StrategyEngine` / `TradeExecutor` / `RiskManager` / `TradingDataService`）的全面评审，本文档列出所有不符合专业交易实践的问题，并给出具体修复方案。

修复优先级标记：
- **P0**：阻塞功能或导致数据失真，必须立即修复
- **P1**：影响交易安全或风控有效性
- **P2**：体验与专业性增强

---

## 一、UI 设计层修复

### 1.1 [P0] 补全人工确认 UI（当前会导致交易死锁）

**问题位置**：[TradeMonitorView.axaml](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App/Views/Pages/Trading/TradeMonitorView.axaml)

**根因**：[TradeMonitorViewModel.cs:29-36](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App/ViewModels/Trading/TradeMonitorViewModel.cs#L29-L36) 定义了完整的确认属性与命令，但 View 中完全未渲染。当 `RiskManager` 返回 `NeedsConfirmation` 时，`TradeExecutor` 通过 `ConfirmationCallback` 等待 `TaskCompletionSource`，用户无法操作 → 交易永久阻塞，且策略锁被占用导致后续触发全部跳过。

#### Bug A：确认回调参数顺序相反（必须与 UI 一同修复）

**问题位置**：
- 调用方 [TradeExecutor.cs:90-91](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradeExecutor.cs#L90-L91)
- 定义方 [TradeMonitorViewModel.cs:107-108](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App/ViewModels/Trading/TradeMonitorViewModel.cs#L107-L108)
- 委托签名 [TradeExecutor.cs:23-25](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradeExecutor.cs#L23-L25)

**根因**：调用与定义的 `price` / `quantity` 参数顺序相反。

| 位置 | 参数顺序 |
|------|---------|
| `TradeExecutor.ConfirmationCallback` 委托签名注释 | `(symbol, side, quantity, price, reason)` |
| `TradeExecutor` 实际调用 | `(symbol, side, quantity, currentPrice, reason)` |
| `TradeMonitorViewModel.OnTradeConfirmationRequestedAsync` 定义 | `(symbol, side, price, quantity, reason)` |

结果：ViewModel 接收到的 `price` 实际是数量，`quantity` 实际是价格。再叠加格式化差异（`ConfirmationPrice` 用 `F2`、`ConfirmationQuantity` 用 `F6`），UI 上"价格"栏会显示成 6 位小数的数量，"数量"栏会显示成 2 位小数的价格，极具误导性。

**修复方案**：统一参数顺序为 `(symbol, side, price, quantity, reason)`（与语义一致，price 在前更自然）。

1. 修正 `TradeExecutor.ConfirmationCallback` 委托签名与调用：

```csharp
/// <summary>
/// Human-in-the-Loop 确认回调。
/// 当风控返回 NeedsConfirmation 时，调用此回调等待用户确认。
/// 参数: (symbol, side, price, quantity, reason) → true=放行 false=拒绝。
/// 未设置时保持现有行为（直接拒绝）。
/// </summary>
public Func<string, OrderSide, decimal, decimal, string, Task<bool>>? ConfirmationCallback { get; set; }
```

```csharp
// 调用处修正参数顺序
var approved = await ConfirmationCallback(
    instrumentSymbol, side, currentPrice, quantity, riskCheck.Reason ?? "需人工确认");
```

2. `TradeMonitorViewModel.OnTradeConfirmationRequestedAsync` 签名保持不变（已是正确顺序）。

3. 全局检索所有 `ConfirmationCallback` 的赋值点，确保签名一致。当前仅 `TradeMonitorViewModel` 构造函数赋值一次，无其他调用方。

**验证**：修复后构造一笔需确认的交易，检查 UI 显示的价格与数量是否与实际下单参数一致。

#### 补全确认 UI

**修复方案**：在 `TradeMonitorView.axaml` 的 `ScrollViewer` 内最顶部插入确认对话框卡片：

```xml
<!-- Human-in-the-Loop 交易确认 -->
<controls:CardView Header="交易确认" IsVisible="{Binding HasPendingConfirmation}">
    <StackPanel Spacing="{StaticResource DefaultSpacing}">
        <TextBlock Text="以下交易等待您确认：" FontWeight="Bold"/>
        <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto">
            <TextBlock Grid.Row="0" Grid.Column="0" Text="交易对：" Margin="0,0,8,0"/>
            <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding ConfirmationSymbol}" FontWeight="Bold"/>
            <TextBlock Grid.Row="1" Grid.Column="0" Text="方向：" Margin="0,0,8,0"/>
            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding ConfirmationSide}"/>
            <TextBlock Grid.Row="2" Grid.Column="0" Text="价格：" Margin="0,0,8,0"/>
            <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding ConfirmationPrice}"/>
            <TextBlock Grid.Row="3" Grid.Column="0" Text="数量：" Margin="0,0,8,0"/>
            <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding ConfirmationQuantity}"/>
            <TextBlock Grid.Row="4" Grid.Column="0" Text="理由：" Margin="0,0,8,0" VerticalAlignment="Top"/>
            <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding ConfirmationReason}"
                       TextWrapping="Wrap" MaxWidth="400"/>
        </Grid>
        <StackPanel Orientation="Horizontal" Spacing="{StaticResource DefaultSpacing}">
            <Button Content="批准" Command="{Binding ApproveConfirmationCommand}"
                    Background="{DynamicResource SuccessBrush}" Foreground="White"/>
            <Button Content="拒绝" Command="{Binding RejectConfirmationCommand}"
                    Background="{DynamicResource DangerBrush}" Foreground="White"/>
        </StackPanel>
    </StackPanel>
</controls:CardView>
```

**额外建议**：在 `TradeMonitorViewModel` 中为确认操作增加超时（如 60 秒自动拒绝），避免用户离开后交易长时间挂起：

```csharp
private async Task<bool> OnTradeConfirmationRequestedAsync(
    string symbol, OrderSide side, decimal price, decimal quantity, string reason)
{
    // ... 设置属性 ...
    _confirmationTcs = new TaskCompletionSource<bool>();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    cts.Token.Register(() => _confirmationTcs.TrySetResult(false));

    return await _confirmationTcs.Task;
}
```

---

### 1.2 [P0] 增加持仓展示卡片

**问题**：交易员第一关注的信息缺失。`CryptoPortfolioService.GetCurrentPositionsAsync()` 已实现，但未在监控页展示。

**修复方案**：在 ViewModel 增加持仓集合，并在账户余额卡片后插入持仓卡片。

ViewModel 新增：

```csharp
public ObservableCollection<PositionInfo> Positions { get; } = [];

// 在 RefreshAsync 中加载
var positions = await _portfolioService.GetCurrentPositionsAsync();
Positions.Clear();
foreach (var p in positions.Where(p => p.Quantity > 0))
    Positions.Add(p);
```

View 新增卡片：

```xml
<controls:CardView Header="当前持仓">
    <StackPanel Spacing="{StaticResource TinySpacing}">
        <TextBlock Text="暂无持仓"
                   IsVisible="{Binding !Positions.Count}"
                   Foreground="{DynamicResource TextSecondaryBrush}"
                   HorizontalAlignment="Center"/>
        <ItemsControl ItemsSource="{Binding Positions}">
            <ItemsControl.ItemTemplate>
                <DataTemplate DataType="models:PositionInfo">
                    <Border Background="{DynamicResource CardBackgroundBrush}"
                            CornerRadius="{StaticResource TinyCornerRadius}"
                            Padding="{StaticResource SmallCardPadding}" Margin="0,0,0,2">
                        <Grid ColumnDefinitions="*,Auto,Auto,Auto">
                            <TextBlock Grid.Column="0" Text="{Binding Symbol}" FontWeight="Bold"/>
                            <TextBlock Grid.Column="1" Text="{Binding Quantity, StringFormat='{}{0:F6}'}"
                                       Foreground="{DynamicResource TextSecondaryBrush}"/>
                            <TextBlock Grid.Column="2" Text="{Binding EntryPrice, StringFormat='入场 {0:F2}'}"
                                       Foreground="{DynamicResource TextSecondaryBrush}"/>
                            <TextBlock Grid.Column="3" Text="{Binding UnrealizedPnl, StringFormat='{}{0:F2} USDT'}"
                                       Foreground="{Binding UnrealizedPnl, Converter={StaticResource PnlColorConverter}}"/>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</controls:CardView>
```

---

### 1.3 [P1] 增加活跃策略列表展示

**问题**：用户看不到当前运行的策略、触发条件、执行进度。

**修复方案**：ViewModel 增加 `ObservableCollection<TradingStrategy> ActiveStrategies`，在 `RefreshAsync` 中调用 `_dataService.GetStrategiesByStatusAsync(StrategyStatus.Active)` 加载。

View 新增卡片，展示：策略类型、Symbol、触发价、止损/止盈、执行次数/上限、状态。提供"暂停"/"删除"按钮。

---

### 1.4 [P1] 增加风控指标实时展示

**问题**：交易员无法感知离风控阈值多近。

**修复方案**：在监控状态卡片中增加进度条形式的风控指标：

- 今日亏损占比 / `MaxDailyLossPercent`
- 总仓位占比 / `MaxTotalPositionPercent`
- 今日交易次数 / `MaxDailyTrades`

使用 `ProgressBar` 接近阈值时变红，让交易员直观感知风险敞口。

---

### 1.5 [P2] 未完成订单增加撤单与详情

**问题**：当前仅文本拼接展示，缺少时间、订单类型、关联策略，且无撤单操作。

#### Bug B：DataTemplate 类型不匹配导致绑定静默失败

**问题位置**：[TradeMonitorView.axaml:118](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App/Views/Pages/Trading/TradeMonitorView.axaml#L118)

**根因**：DataTemplate 声明的类型与 ViewModel 集合元素类型不一致，且绑定了不存在的字段。

| 位置 | 类型/字段 |
|------|----------|
| View DataTemplate `DataType` | `crypto:BinanceOrderResponse`（Binance 专有响应模型） |
| ViewModel 集合类型 | `ObservableCollection<ExchangeOrderResult>`（统一抽象模型） |
| View 绑定字段 | `OrigQty`（Binance 专有字段） |
| `ExchangeOrderResult` 实际字段 | `RequestedQty` / `ExecutedQty` / `Price`（无 `OrigQty`） |

后果：Avalonia 在运行时找不到 `OrigQty` 属性，绑定静默失败，UI 上"数量"栏显示空值。`Price` 字段虽然存在，但 DataTemplate 类型不匹配可能导致整个模板的数据上下文类型推断异常。

**修复方案**：

1. 修正 DataTemplate 的 `DataType` 为正确的统一模型：

```xml
<DataTemplate DataType="abstractions:ExchangeOrderResult">
```

需要在 View 顶部增加命名空间引用：

```xml
xmlns:abstractions="using:MarketAssistant.Trading.Abstractions"
```

2. 修正字段绑定，移除 `OrigQty`，改用 `RequestedQty`：

```xml
<MultiBinding StringFormat="价格: {0} 数量: {1} 状态: {2}">
    <Binding Path="Price"/>
    <Binding Path="RequestedQty"/>
    <Binding Path="Status"/>
</MultiBinding>
```

3. 顺便移除不再需要的 `crypto` 命名空间引用（若 View 中无其他地方使用）。

**验证**：启动应用，确保 Binance API 已配置且有挂单时，订单卡片能正确显示价格、数量、状态。

#### 增强展示与撤单能力

**修复方案**：
- 展开为表格或更详细的卡片布局
- 增加撤单按钮（调用 `IExchangeClient.CancelOrderAsync`）
- 显示 `Time`、`Type`、关联 `StrategyId`

---

### 1.6 [P2] 增加实时价格展示

**问题**：监控中的标的无实时价格展示。

**修复方案**：`MarketMonitor` 已订阅 `BinanceWebSocketService.PriceUpdated` 事件，可在 ViewModel 中订阅同一事件维护一个 `Dictionary<string, decimal>` 实时价格表，在 UI 顶部以紧凑列表展示。

---

## 二、交易执行层修复

### 2.1 [P0] 修正 PnL 计算（当前盈亏统计完全失真）

**问题位置**：[TradeExecutor.cs:154-160](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradeExecutor.cs#L154-L160) 与 [TradingDataService.cs:318-338](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradingDataService.cs#L318-L338)

**根因**：`GetAverageEntryPriceAsync` 对该 symbol **所有历史买入记录**求加权平均，包括已经平仓的部分。导致：
- 先买 1 BTC @ 50000，卖 1 BTC @ 60000（已平仓）
- 再买 0.5 BTC @ 55000
- 代码算出成本 = (1×50000 + 0.5×55000)/(1+0.5) = 51666
- 实际成本应为 55000，PnL 虚增

**修复方案**：引入 FIFO 持仓追踪表，按时间顺序匹配买入与卖出。

新增 `positions` 表：

```sql
CREATE TABLE IF NOT EXISTS positions (
    id TEXT PRIMARY KEY,
    symbol TEXT NOT NULL,
    side INTEGER NOT NULL,          -- 0=Long, 1=Short
    quantity REAL NOT NULL,         -- 剩余未平仓数量
    entry_price REAL NOT NULL,      -- 开仓价
    opened_at TEXT NOT NULL,
    strategy_id TEXT,
    closed_quantity REAL DEFAULT 0  -- 已平仓数量
);
CREATE INDEX IF NOT EXISTS idx_positions_symbol ON positions(symbol);
CREATE INDEX IF NOT EXISTS idx_positions_open ON positions(symbol, quantity) WHERE quantity > 0;
```

在 `TradeExecutor.ExecuteApprovedOrderAsync` 中：

```csharp
// 买入：开新仓位行
if (side == OrderSide.Buy)
{
    await _dataService.OpenPositionAsync(new Position
    {
        Symbol = instrumentSymbol,
        Side = PositionSide.Long,
        Quantity = record.ExecutedQty,
        EntryPrice = record.ExecutedPrice,
        OpenedAt = DateTime.UtcNow,
        StrategyId = strategyId
    }, ct);
}
// 卖出：FIFO 匹配平仓，计算每笔已实现 PnL
else
{
    pnl = await _dataService.ClosePositionFifoAsync(
        instrumentSymbol, record.ExecutedQty, record.ExecutedPrice, ct);
}
```

`ClosePositionFifoAsync` 实现：

```csharp
public async Task<decimal> ClosePositionFifoAsync(
    string symbol, decimal closeQty, decimal closePrice, CancellationToken ct)
{
    decimal realizedPnl = 0;
    var remaining = closeQty;

    await using var conn = await OpenConnectionAsync(ct);
    await using var tx = await conn.BeginTransactionAsync(ct);

    // 按时间顺序取出未平仓的多头仓位
    await using var cmd = conn.CreateCommand();
    cmd.Transaction = (SqliteTransaction)tx;
    cmd.CommandText = """
        SELECT id, quantity, entry_price, closed_quantity
        FROM positions
        WHERE symbol = @symbol AND side = 0 AND quantity - closed_quantity > 0
        ORDER BY opened_at ASC
        """;
    cmd.Parameters.AddWithValue("@symbol", symbol);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    var toClose = new List<(string id, decimal available, decimal entryPrice)>();
    while (await reader.ReadAsync(ct))
    {
        var id = reader.GetString(0);
        var qty = (decimal)reader.GetDouble(1);
        var closed = (decimal)reader.GetDouble(2);
        var entry = (decimal)reader.GetDouble(3);
        toClose.Add((id, qty - closed, entry));
    }

    foreach (var (id, available, entry) in toClose)
    {
        if (remaining <= 0) break;
        var closeThis = Math.Min(remaining, available);
        realizedPnl += (closePrice - entry) * closeThis;

        await using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = (SqliteTransaction)tx;
        updateCmd.CommandText = """
            UPDATE positions SET closed_quantity = closed_quantity + @close
            WHERE id = @id
            """;
        updateCmd.Parameters.AddWithValue("@close", (double)closeThis);
        updateCmd.Parameters.AddWithValue("@id", id);
        await updateCmd.ExecuteNonQueryAsync(ct);

        remaining -= closeThis;
    }

    await tx.CommitAsync(ct);
    return realizedPnl;
}
```

---

### 2.2 [P0] 修正手续费填充（当前始终为 0）

**问题位置**：[TradeExecutor.cs:135-149](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradeExecutor.cs#L135-L149)

**根因**：构造 `TradeRecord` 时从未从交易所响应中读取 `Commission`，始终为默认值 0。导致日统计 `TotalCommission` 永远是 0，风控无法基于真实手续费评估。

**修复方案**：

1. 扩展 `IExchangeClient` 的 `ExchangeOrderResult` DTO，增加 `FillCommission` 与 `FillCommissionAsset` 字段。

2. `BinanceExchangeClient` 在解析订单响应时填充手续费（Binance 的 `POST /api/v3/order` 响应中的 `fills` 数组包含每笔成交的 `commission` 和 `commissionAsset`）：

```csharp
// 汇总所有 fills 的手续费
decimal totalCommission = 0;
string? commissionAsset = null;
if (response.RootElement.TryGetProperty("fills", out var fills))
{
    foreach (var fill in fills.EnumerateArray())
    {
        totalCommission += fill.GetProperty("commission").GetDecimal();
        commissionAsset ??= fill.GetProperty("commissionAsset").GetString();
    }
}
```

3. `TradeExecutor` 构造 `TradeRecord` 时填充：

```csharp
Commission = response.FillCommission,
CommissionAsset = response.FillCommissionAsset ?? string.Empty,
```

---

### 2.3 [P1] 支持限价单与滑点保护（当前全市价单）

**问题位置**：[TradeExecutor.cs:54](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradeExecutor.cs#L54)

**根因**：策略触发交易时硬编码 `OrderType.Market`，加密市场滑点可能吃掉利润，尤其网格交易高频场景。

**修复方案**：

1. 在 `TradingStrategy` 增加 `OrderType` 字段，默认 `Market`，用户可配置为 `Limit`。

2. 限价单时基于当前价计算限价：
   - 买入：`limitPrice = currentPrice * (1 + slippageTolerance)`（愿意接受的最大滑点）
   - 卖出：`limitPrice = currentPrice * (1 - slippageTolerance)`

3. 在 `TradingStrategy.CustomParams` 中支持 `slippageTolerance` 参数（默认 0.3%）。

4. `TradeExecutor.ExecuteTradeAsync` 传递 `limitPrice`：

```csharp
decimal? limitPrice = null;
if (strategy.OrderType == OrderType.Limit)
{
    var slippage = 0.003m; // 0.3%
    limitPrice = strategy.Side == OrderSide.Buy
        ? currentPrice * (1 + slippage)
        : currentPrice * (1 - slippage);
}

var result = await ExecuteOrderAsync(
    strategy.Symbol, strategy.Side, strategy.OrderType, strategy.Quantity,
    currentPrice, limitPrice: limitPrice, strategyId: strategy.Id,
    aiReasoning: aiReasoning, ct: ct);
```

5. 限价单增加超时撤单逻辑（如 30 秒未成交则撤单），避免挂单长期占用。

---

### 2.4 [P2] 增加订单超时与重试机制

**问题**：当前下单失败直接返回，无重试，网络抖动可能导致错过交易机会。

**修复方案**：使用 Polly 或手写重试逻辑，对网络异常重试 3 次，指数退避（1s/2s/4s）。对交易所业务错误（如余额不足）不重试。

---

## 三、策略引擎层修复

### 3.1 [P0] 追踪止损状态持久化（当前重启丢失）

**问题位置**：[StrategyEngine.cs:15](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/StrategyEngine.cs#L15)

**根因**：`_peakPrices` 是内存字典，应用重启后峰值/谷值丢失，追踪止损会重新激活并可能立即触发错误信号。

**修复方案**：将峰值/谷值持久化到 `strategies` 表的新字段 `trailing_peak_price`。

1. 数据库迁移：

```sql
ALTER TABLE strategies ADD COLUMN trailing_peak_price REAL;
```

2. `TradingStrategy` 模型增加：

```csharp
public decimal? TrailingPeakPrice { get; set; }
```

3. `StrategyEngine.EvaluateTrailingStop` 改为读写持久化字段：

```csharp
private async Task<bool> EvaluateTrailingStopAsync(
    TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
{
    // ... 解析参数 ...

    var peak = strategy.TrailingPeakPrice ?? currentPrice;
    if (strategy.Side == OrderSide.Sell)
    {
        if (!strategy.TrailingPeakPrice.HasValue && currentPrice < activationPrice)
            return false;

        peak = Math.Max(peak, currentPrice);
        var trailPrice = peak * (1 - trailingPercent / 100);

        // 持久化更新峰值
        if (peak != strategy.TrailingPeakPrice)
        {
            strategy.TrailingPeakPrice = peak;
            await _dataService.UpdateStrategyTrailingPeakAsync(strategy.Id, peak, ct);
        }

        return currentPrice <= trailPrice;
    }
    // ... Sell 侧对称逻辑 ...
}
```

4. `TradingDataService` 增加 `UpdateStrategyTrailingPeakAsync` 方法。

---

### 3.2 [P0] 网格交易增加破网止损

**问题位置**：[StrategyEngine.cs:201-202](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/StrategyEngine.cs#L201-L202)

**根因**：价格超出网格上下界时直接 `return false`，已建立的网格仓位无人管理。价格跌破下界后，多头仓位持续亏损却无止损。

**修复方案**：在 `GridTradingParams` 增加可选的 `StopLossPrice`，价格突破边界时触发止损平仓。

```csharp
public class GridTradingParams
{
    // ... 现有字段 ...

    /// <summary>
    /// 破网止损价（可选）。价格跌破此值时清仓所有网格多头仓位。
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 破网止盈价（可选）。价格涨破此值时清仓所有网格空头仓位。
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }
}
```

`EvaluateGridTrading` 修改：

```csharp
// 价格超出网格边界时检查破网止损
if (currentPrice < gridParams.LowerPrice)
{
    if (gridParams.StopLossPrice.HasValue && currentPrice <= gridParams.StopLossPrice.Value)
    {
        effectiveSide = OrderSide.Sell;
        effectiveQty = gridParams.QuantityPerGrid * gridParams.GridCount; // 清仓
        _logger.LogWarning("网格破网止损触发: {StrategyId} 价格 {Price} <= {StopLoss}",
            strategy.Id, currentPrice, gridParams.StopLossPrice);
        return true;
    }
    return false;
}
if (currentPrice > gridParams.UpperPrice)
{
    if (gridParams.TakeProfitPrice.HasValue && currentPrice >= gridParams.TakeProfitPrice.Value)
    {
        effectiveSide = OrderSide.Buy;
        effectiveQty = gridParams.QuantityPerGrid * gridParams.GridCount;
        return true;
    }
    return false;
}
```

---

### 3.3 [P1] DCA "低价加倍"改为基于估值分位

**问题位置**：[StrategyEngine.cs:270-275](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/StrategyEngine.cs#L270-L275)

**根因**：低于阈值双倍买入是"越跌越买"，在趋势性下跌中会快速耗尽资金并深度套牢。

**修复方案**：将 `DoubleBuyBelowPrice` 改为基于相对均线偏离度或 RSI 超卖信号。

方案 A（均线偏离度）：增加 `MaPeriod` 与 `MaDeviationThreshold` 参数，当价格低于 N 周期均线超过 X% 时加倍买入。需要 `StrategyEngine` 注入 `BinanceMarketDataService` 获取历史 K 线。

方案 B（简化版，推荐先实现）：保留 `DoubleBuyBelowPrice` 但增加两个约束：
1. **加倍冷却期**：连续加倍之间至少间隔 N 小时，防止瀑布式下跌中连续加倍。
2. **加倍次数上限**：每个定投周期内最多加倍 K 次。

```csharp
public class DCAParams
{
    // ... 现有字段 ...

    /// <summary>
    /// 加倍冷却期（秒）。两次加倍之间至少间隔此时间，默认 24 小时。
    /// </summary>
    public int DoubleBuyCooldownSeconds { get; set; } = 86400;

    /// <summary>
    /// 加倍次数上限（每个定投周期内）。
    /// </summary>
    public int MaxDoubleBuyCount { get; set; } = 3;
}
```

`EvaluateDCA` 增加冷却检查（需要记录上次加倍时间，可复用 `LastTriggeredAt` 或新增字段）。

---

### 3.4 [P1] AISignal 策略增加风险预算约束

**问题位置**：[MarketMonitor.cs:315-331](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/MarketMonitor.cs#L315-L331)

**根因**：AI prompt 只提供价格、仓位、分析报告，无明确风险预算约束，AI 无法根据置信度调整仓位。

**修复方案**：

1. 在 prompt 中明确风险预算：

```csharp
var maxPositionPercent = strategy.MaxPositionPercent ?? 20m;
var prompt = $"""
    分析交易标的 {strategy.Symbol}，当前价格 {currentPrice}。

    ## 风险预算（必须严格遵守）
    - 本次交易后该 symbol 总仓位不得超过账户总值的 {maxPositionPercent:F1}%
    - 当前已持仓: {positionSummary}
    - 今日已实现亏损: {todayStats.TotalPnl:F2} USDT
    - 今日剩余交易次数: {remainingTrades}

    ## 策略配置
    {strategy.CustomParams ?? "无"}
    风险边界: {stopLossInfo} | {takeProfitInfo}

    ## 决策要求
    请输出结构化决策：
    1. 决策: BUY / SELL / HOLD
    2. 置信度: 0-100
    3. 建议仓位（基于置信度，0-100% 风险预算）: X%
    4. 入场逻辑: ...
    5. 退出计划（止损/止盈具体价位）: ...
    6. 风险因素: ...

    如果置信度低于 60，建议 HOLD。
    """;
```

2. 让 AI 通过工具调用 `PlaceOrder` 时传入动态计算的 quantity，而非固定 `strategy.Quantity`。需要扩展 `PlaceOrder` 工具签名支持自定义数量。

3. 增加 AI 决策日志持久化，记录每次决策的置信度、建议仓位、实际执行结果，便于后续绩效分析。

---

## 四、风控层修复

### 4.1 [P1] 收紧默认风控配置

**问题位置**：[TradingModels.cs:81-90](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.Trading/TradingModels.cs#L81-L90)

**根因**：默认值偏宽松，`MaxDailyLossPercent = 10%` 对专业日内交易员过高。

**修复方案**：

```csharp
public class RiskConfig
{
    public decimal MaxSingleOrderPercent { get; set; } = 3;       // 5% → 3%
    public decimal MaxDailyLossPercent { get; set; } = 5;         // 10% → 5%
    public decimal MaxTotalPositionPercent { get; set; } = 70;    // 80% → 70%
    public int MaxDailyTrades { get; set; } = 15;                 // 20 → 15
    public decimal MinOrderAmount { get; set; } = 10;
    public bool RequireConfirmation { get; set; } = true;         // 默认开启
    public decimal ConfirmationThreshold { get; set; } = 500;     // 1000 → 500

    /// <summary>
    /// 最大回撤熔断（新增）。累计回撤超过此百分比时停止所有交易。
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = 20;

    /// <summary>
    /// 单 symbol 最大仓位占比（新增）。
    /// </summary>
    public decimal MaxSinglePositionPercent { get; set; } = 30;

    /// <summary>
    /// 相关性风险检查（新增）。高相关币种总仓位合并计算。
    /// </summary>
    public bool EnableCorrelationCheck { get; set; } = false;
}
```

---

### 4.2 [P1] 增加最大回撤熔断

**问题**：仅有日级止损，连续亏损多日无保护。

**修复方案**：

1. 新增 `account_snapshots` 表，每日记录账户总值：

```sql
CREATE TABLE IF NOT EXISTS account_snapshots (
    date TEXT PRIMARY KEY,
    total_value_usdt REAL NOT NULL,
    snapshot_at TEXT NOT NULL
);
```

2. `RiskManager.ValidateOrderAsync` 增加回撤检查：

```csharp
// 计算从历史最高点的回撤
var peakValue = await _dataService.GetPeakAccountValueAsync(ct);
if (peakValue > 0)
{
    var drawdownPercent = (peakValue - totalUSDT) / peakValue * 100;
    if (drawdownPercent >= config.MaxDrawdownPercent)
    {
        return RiskCheckResult.Reject(
            $"账户回撤 {drawdownPercent:F1}% 已达熔断阈值 {config.MaxDrawdownPercent}%，停止交易");
    }
}
```

3. 每日定时（如 UTC 00:00）记录账户快照。

---

### 4.3 [P1] 增加单 symbol 仓位上限

**问题**：当前只有总仓位上限，无单 symbol 上限，可能满仓单一币种。

**修复方案**：`RiskManager` 增加检查：

```csharp
if (config.MaxSinglePositionPercent > 0 && side == OrderSide.Buy)
{
    var symbolValue = portfolioSummary.Assets
        .Where(a => !a.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
        .Where(a => instrumentSymbol.StartsWith(a.Asset, StringComparison.OrdinalIgnoreCase))
        .Sum(a => a.ValueUSDT);
    var symbolPercent = (symbolValue + orderValueUSDT) / totalUSDT * 100;
    if (symbolPercent > config.MaxSinglePositionPercent)
        return RiskCheckResult.Reject(
            $"单标的仓位将达 {symbolPercent:F1}%，超过限额 {config.MaxSinglePositionPercent}%");
}
```

---

### 4.4 [P2] 增加相关性风险检查

**问题**：同时满仓多个高相关币种（如 BTC/BCH/BSV）等于变相加杠杆。

**修复方案**：维护一个币种相关性矩阵（可配置或基于历史价格计算），将高相关币种（相关系数 > 0.8）的仓位合并计算。

实现较复杂，建议作为 P2 后续迭代。初期可提供手动分组配置：

```csharp
public class CorrelationGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = [];
    public decimal MaxGroupPositionPercent { get; set; } = 50;
}
```

---

## 五、持久化与状态一致性修复

### 5.1 [P1] 网格交易状态原子持久化（已部分实现，需完善）

**问题位置**：[MarketMonitor.cs:246-248](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/MarketMonitor.cs#L246-L248)

**现状**：`TradeExecutor.ExecuteTradeAsync` 已通过 `UpdateStrategyTriggeredWithParamsAsync` 原子更新计数和参数，但 `StrategyEngine.EvaluateGridTrading` 在评估阶段也会修改 `strategy.CustomParams`（更新 `LastTriggeredIndex`），此修改未持久化。若评估后交易失败，`LastTriggeredIndex` 已变但未保存，下次评估会重复触发。

**修复方案**：将 `LastTriggeredIndex` 的更新延迟到交易成功后。`EvaluateGridTrading` 只返回触发决策，不修改 `CustomParams`；`TradeExecutor` 成功后再更新。

```csharp
// EvaluateGridTrading 改为只读评估
private bool EvaluateGridTrading(TradingStrategy strategy, decimal currentPrice,
    out OrderSide effectiveSide, out decimal effectiveQty,
    out int newIndex)  // 新增 out 参数
{
    // ... 解析参数 ...
    newIndex = gridParams.LastTriggeredIndex; // 默认不变

    if (currentIndex == gridParams.LastTriggeredIndex)
        return false;

    effectiveSide = currentIndex < gridParams.LastTriggeredIndex ? OrderSide.Buy : OrderSide.Sell;
    effectiveQty = gridParams.QuantityPerGrid;
    newIndex = currentIndex; // 仅返回新索引，不修改 strategy.CustomParams

    return true;
}
```

`TradeExecutor.ExecuteTradeAsync` 成功后更新：

```csharp
if (result.Success && pendingNewIndex.HasValue)
{
    var paramsJson = JsonSerializer.Serialize(gridParams with { LastTriggeredIndex = pendingNewIndex.Value });
    await _dataService.UpdateStrategyTriggeredWithParamsAsync(strategy.Id, paramsJson, ct);
}
```

---

### 5.2 [P2] 日统计按用户本地时区切分

**问题位置**：[TradingDataService.cs:274](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Trading/TradingDataService.cs#L274)

**根因**：`DateTime.UtcNow.ToString("yyyy-MM-dd")` 按 UTC 切分，与用户本地交易日可能错位（亚洲用户 UTC 16:00 后实际是次日）。

**修复方案**：增加用户可配置的时区设置，默认使用本地时区：

```csharp
private static string GetTodayDateString()
{
    // 优先使用用户配置时区，回退到本地时区
    return DateTime.Now.ToString("yyyy-MM-dd");
}
```

`GetTodayStatsAsync` 与 `UpdateDailyStatsAsync` 统一使用此方法。

---

## 六、专业能力增强（P2）

### 6.1 纸面交易模式

**目标**：先验证策略再上真金白银。

**方案**：`TradeExecutor` 增加配置 `PaperTradingMode`，开启时不调用交易所 API，而是模拟成交（按当前价 + 随机滑点），记录到独立的 `paper_trade_records` 表。UI 增加切换开关。

### 6.2 回测能力

**目标**：策略上线前用历史数据验证。

**方案**：新增 `BacktestEngine`，输入策略配置 + 历史K线数据，模拟执行并输出绩效报告（胜率、盈亏比、最大回撤、夏普比率）。复用 `StrategyEngine` 的评估逻辑，但替换实时价格为历史价格迭代。

### 6.3 绩效分析

**目标**：交易员需要持续复盘。

**方案**：新增 `PerformanceAnalyzer`，基于 `trade_records` 计算关键指标：

```csharp
public class PerformanceMetrics
{
    public decimal TotalReturn { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }      // 总盈利 / 总亏损
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public int TotalTrades { get; set; }
    public decimal AverageHoldTime { get; set; }
}
```

UI 增加"绩效分析"页面，展示指标与资金曲线图。

### 6.4 交易日志与复盘

**目标**：每笔交易记录决策理由与情绪标签。

**方案**：`TradeRecord` 增加 `ReviewNote` 字段，UI 在交易历史中支持添加复盘备注。AISignal 策略自动填充 `AIReasoning`，其他策略支持手动添加。

---

## 七、修复优先级与依赖关系

```
P0（必须立即修复，阻塞功能或数据失真）
├── 1.1 人工确认 UI + Bug A 参数顺序修正（独立，两者必须一同修复）
├── 2.1 PnL 计算（依赖 5.1 positions 表）
├── 2.2 手续费填充（依赖 IExchangeClient DTO 扩展）
├── 3.1 追踪止损持久化（独立）
└── 3.2 网格破网止损（独立）

P1（影响交易安全）
├── 1.2 持仓展示（依赖 2.1 持仓数据）
├── 1.3 活跃策略列表（独立）
├── 1.4 风控指标展示（依赖 4.1/4.2/4.3）
├── 2.3 限价单支持（独立）
├── 3.3 DCA 加倍约束（独立）
├── 3.4 AISignal 风险预算（独立）
├── 4.1 收紧风控配置（独立）
├── 4.2 最大回撤熔断（依赖 account_snapshots 表）
├── 4.3 单 symbol 仓位上限（独立）
└── 5.1 网格状态原子持久化（独立）

P2（专业性增强）
├── 1.5 未完成订单详情与撤单 + Bug B DataTemplate 类型修正（独立，Bug B 可单独先修）
├── 1.6 实时价格展示
├── 2.4 订单重试机制
├── 4.4 相关性风险检查
├── 5.2 日统计本地时区
├── 6.1 纸面交易模式
├── 6.2 回测能力
├── 6.3 绩效分析
└── 6.4 交易日志与复盘
```

> **特别说明**：
> - **Bug A** 虽归类于 1.1，但本质是执行层与 ViewModel 的契约不一致，修复时必须同时改 `TradeExecutor`（调用方）与确认 UI（消费方），缺一不可。
> - **Bug B** 虽归类于 P2，但修复成本极低（改 3 处绑定），建议与 1.5 一同处理，或作为独立 hotfix 先行修复，避免挂单页面长期显示空值。

---

## 八、验证策略

每项修复完成后，按以下方式验证：

| 修复类型 | 验证方式 |
|---------|---------|
| UI 修复（1.x） | `dotnet build MarketAssistant.slnx -c Debug` + 手动启动应用验证交互 |
| 执行层修复（2.x） | 单元测试：构造历史交易记录，验证 PnL 计算与手续费填充 |
| 策略修复（3.x） | 单元测试：模拟价格序列，验证触发逻辑与持久化 |
| 风控修复（4.x） | 单元测试：构造边界场景，验证风控拒绝/通过 |
| 持久化修复（5.x） | 集成测试：重启应用后验证状态恢复 |

所有代码改动需确保 `dotnet build MarketAssistant.slnx -c Debug` 通过。

---

## 九、迁移与兼容性

1. **数据库迁移**：新增字段（`trailing_peak_price`、`order_type`）和新增表（`positions`、`account_snapshots`）使用 `CREATE TABLE IF NOT EXISTS` 与 `ALTER TABLE ADD COLUMN`（SQLite 支持，已存在列会报错需 try-catch 忽略）。

2. **配置迁移**：`RiskConfig` 新增字段有默认值，旧配置反序列化时自动填充默认值。

3. **策略参数迁移**：`GridTradingParams` 新增 `StopLossPrice`/`TakeProfitPrice` 为可空，旧策略配置无需修改。

4. **灰度发布**：建议先在纸面交易模式（6.1）下验证所有修复，再切换到真实交易。
