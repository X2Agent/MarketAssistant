# 币安API鉴权集成指南

## 概述

本项目已集成币安API鉴权功能，使用**HMAC-SHA256签名方式**：
- ✅ 最简单易用，适合大多数场景
- ✅ 签名不区分大小写，易于调试
- ✅ 无需额外文件，仅需API Key和Secret Key
- ✅ 币安官方推荐的签名方式之一

## 快速开始

### 1. 获取API密钥

访问币安官网 [API管理页面](https://www.binance.com/zh-CN/support/faq/360002502072) 创建API密钥：

1. 登录币安账户
2. 进入 **账户管理 > API管理**
3. 创建新的API密钥（选择**HMAC_SHA256**类型）
4. 根据需求启用权限（读取信息、现货交易等）
5. **妥善保管API Key和Secret Key**，不要泄露给任何人

### 2. 配置鉴权服务

```csharp
var config = new BinanceAuthConfig
{
    ApiKey = "你的API_Key",
    SecretKey = "你的Secret_Key",
    RecvWindow = 5000 // 可选，默认5000毫秒
};
```

## 使用示例

### 初始化鉴权服务

```csharp
using MarketAssistant.Applications.Crypto;
using Microsoft.Extensions.DependencyInjection;

// 创建配置
var authConfig = new BinanceAuthConfig
{
    ApiKey = "你的API_Key",
    SecretKey = "你的Secret_Key",
    SignatureType = BinanceSignatureType.HMAC
};

// 注册服务
services.AddSingleton(authConfig);
services.AddSingleton<BinanceAuthService>();
services.AddSingleton<BinanceAccountService>();
```

### 获取账户信息

```csharp
var accountService = serviceProvider.GetRequiredService<BinanceAccountService>();

try 
{
    var accountInfo = await accountService.GetAccountInfoAsync();
    
    Console.WriteLine($"账户类型: {accountInfo.AccountType}");
    Console.WriteLine($"可交易: {accountInfo.CanTrade}");
    Console.WriteLine($"可提现: {accountInfo.CanWithdraw}");
    
    // 显示余额
    foreach (var balance in accountInfo.Balances.Where(b => decimal.Parse(b.Free) > 0))
    {
        Console.WriteLine($"{balance.Asset}: 可用 {balance.Free}, 冻结 {balance.Locked}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
}
```

### 下单示例

```csharp
try 
{
    // 市价买入
    var orderResponse = await accountService.PlaceOrderAsync(
        symbol: "BTCUSDT",
        side: "BUY",
        type: "MARKET",
        quantity: 0.001m
    );
    
    Console.WriteLine($"订单ID: {orderResponse.OrderId}");
    Console.WriteLine($"订单状态: {orderResponse.Status}");
    
    // 限价卖出
    var limitOrder = await accountService.PlaceOrderAsync(
        symbol: "BTCUSDT",
        side: "SELL",
        type: "LIMIT",
        quantity: 0.001m,
        price: 50000m
    );
}
catch (Exception ex)
{
    Console.WriteLine($"下单失败: {ex.Message}");
}
```

### 自定义鉴权API调用

如果需要调用其他鉴权接口，可以直接使用 `BinanceAuthService`：

```csharp
var authService = serviceProvider.GetRequiredService<BinanceAuthService>();
var httpClient = new HttpClient();

// 1. 准备查询参数
var queryParams = "symbol=BTCUSDT&limit=10";

// 2. 签名查询字符串（自动添加timestamp和signature）
var signedQuery = authService.SignQueryString(queryParams);

// 3. 构建请求
var url = $"https://api.binance.com/api/v3/myTrades?{signedQuery}";
var request = new HttpRequestMessage(HttpMethod.Get, url);

// 4. 添加鉴权Header
authService.AddAuthHeaders(request);

// 5. 发送请求
var response = await httpClient.SendAsync(request);
var content = await response.Content.ReadAsStringAsync();
```

## 签名原理

### HMAC-SHA256签名流程

1. **构建签名payload**：
   ```
   symbol=BTCUSDT&side=BUY&type=MARKET&quantity=0.001&timestamp=1499827319559
   ```

2. **使用Secret Key进行HMAC-SHA256签名**：
   ```csharp
   var signature = HMACSHA256(payload, secretKey).ToHex();
   ```

3. **添加签名参数**：
   ```
   symbol=BTCUSDT&side=BUY&type=MARKET&quantity=0.001&timestamp=1499827319559&signature=c8db5682...
   ```

4. **添加API Key到HTTP Header**：
   ```
   X-MBX-APIKEY: your-api-key
   ```

### 时间同步要求

- 所有鉴权请求必须包含 `timestamp` 参数（毫秒级Unix时间戳）
- 可选 `recvWindow` 参数指定请求有效期（默认5000毫秒，最大60000毫秒）
- 服务器会验证：`(serverTime - timestamp) <= recvWindow`
- 建议：确保本地时间与服务器时间同步，避免签名失败

## 安全最佳实践

### ⚠️ 重要提示

1. **永远不要在代码中硬编码API密钥**
2. **不要将API密钥提交到版本控制系统（Git）**
3. **使用环境变量或安全配置管理工具存储密钥**
4. **定期轮换API密钥**
5. **根据最小权限原则设置API权限**

### 推荐配置方式

#### 方式一：环境变量

```csharp
var config = new BinanceAuthConfig
{
    ApiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY") 
        ?? throw new InvalidOperationException("未配置BINANCE_API_KEY"),
    SecretKey = Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY") 
        ?? throw new InvalidOperationException("未配置BINANCE_SECRET_KEY"),
    SignatureType = BinanceSignatureType.HMAC
};
```

Windows设置环境变量：
```powershell
$env:BINANCE_API_KEY = "你的API_Key"
$env:BINANCE_SECRET_KEY = "你的Secret_Key"
```

#### 方式二：appsettings.json（开发环境）

```json
{
  "Binance": {
    "ApiKey": "你的API_Key",
    "SecretKey": "你的Secret_Key",
    "SignatureType": "HMAC"
  }
}
```

**生产环境使用 User Secrets 或 Azure Key Vault**

#### 方式三：用户设置（推荐）

在应用内提供设置页面，让用户自行配置API密钥，并使用加密存储。

## 常见问题

### Q1: 签名错误（-1022）

**原因**：签名不匹配或时间戳不正确

**解决方案**：
1. 检查API Key和Secret Key是否正确
2. 确保本地时间与服务器时间同步
3. 检查参数是否正确编码

### Q2: IP未被授权（-2015）

**原因**：API Key限制了IP访问

**解决方案**：
在币安API管理页面添加当前IP到白名单，或选择"不限制IP"（不推荐）

### Q3: 权限不足（-2014）

**原因**：API Key没有对应权限

**解决方案**：
在币安API管理页面启用所需权限（如"启用现货交易"）

### Q4: timestamp超出recvWindow（-1021）

**原因**：请求时间戳超出允许的时间窗口

**解决方案**：
1. 同步本地时间
2. 增加 `recvWindow` 值（不超过60000毫秒）

## 技术支持

- 币安API文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api
- 项目Issues：提交到GitHub仓库
- 安全问题：请发送邮件而非公开Issue

---

**最后提醒**：API密钥是您的资产安全钥匙，请务必妥善保管！
