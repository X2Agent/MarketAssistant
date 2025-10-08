# MarketAssistant.Avalonia 架构分层说明

本文档定义项目的分层架构原则，确保代码职责清晰、边界分明。

## 一、分层原则

### 1. Infrastructure（基础设施层）

**职责：** 核心技术框架、通用组件、系统配置

**包含内容：**
- `Configuration/` - 配置管理（Kernel、Plugin配置）
- `Core/` - 核心组件（异常处理、导航消息、文件系统抽象、视图定位）
- `Factories/` - 对象工厂（Kernel、Embedding、搜索服务）

**原则：**
- 不依赖具体业务逻辑
- 提供可复用的技术基础设施
- 对整个应用透明

---

### 2. Services（技术服务层）

**职责：** UI服务、平台特定服务、技术性基础服务

**包含内容：**
- `Browser/` - 浏览器服务（Playwright）
- `Dialog/` - 对话框服务（UI）
- `Navigation/` - 导航服务（UI）
- `Notification/` - 通知服务（UI）
- `Cache/` - 缓存服务（技术基础设施）
- `Settings/` - 设置持久化服务（技术基础设施）

**原则：**
- 提供技术性功能，不包含业务逻辑
- 服务于 Applications 层
- 平台相关或UI相关的技术实现

---

### 3. Applications（应用/业务层）

**职责：** 业务逻辑、领域服务、应用功能

**包含内容：**
- `Stocks/` - 股票业务逻辑（搜索、信息获取、收藏、K线、热门股）
- `Telegrams/` - 资讯业务逻辑（财经快讯）
- `News/` - 新闻更新业务逻辑
- `StockSelection/` - AI选股业务逻辑

**原则：**
- 包含核心业务逻辑
- 依赖 Infrastructure 和 Services
- 不包含技术基础设施实现（如缓存、持久化）

---

## 二、依赖关系

```
ViewModels
    ↓
Applications (业务层)
    ↓
Services (技术服务层)
    ↓
Infrastructure (基础设施层)
```

**规则：**
1. 上层可以依赖下层，下层不能依赖上层
2. Applications 可以调用 Services 和 Infrastructure
3. Services 只能依赖 Infrastructure
4. Infrastructure 不依赖任何其他层

---

## 三、常见误区与纠正

### ❌ 错误示例

1. **技术服务放在 Applications**
   - `Applications/Cache/AnalysisCacheService` ❌
   - 缓存是技术基础设施，应放在 `Services/Cache/`

2. **业务逻辑放在 Services**
   - `Services/Stock/HomeStockService` ❌
   - 股票业务逻辑应放在 `Applications/Stocks/`

3. **混合职责**
   - 一个服务既包含业务逻辑又包含持久化 ❌
   - 应分离：业务服务 + 技术服务

### ✅ 正确示例

1. **技术服务**
   - `Services/Cache/AnalysisCacheService` ✅
   - `Services/Settings/UserSettingService` ✅

2. **业务服务**
   - `Applications/Stocks/StockService` ✅
   - `Applications/Stocks/HomeStockService` ✅
   - `Applications/StockSelection/StockSelectionService` ✅

3. **基础设施**
   - `Infrastructure/Factories/KernelFactory` ✅
   - `Infrastructure/Configuration/KernelPluginConfig` ✅

---

## 四、命名空间规范

- Infrastructure: `MarketAssistant.Infrastructure.*`
- Services: `MarketAssistant.Services.*`
- Applications: `MarketAssistant.Applications.*`

---

## 五、重构检查清单

重构代码时，请检查：

- [ ] 服务的职责是否单一？
- [ ] 是否放在正确的分层目录？
- [ ] 命名空间是否符合规范？
- [ ] 依赖关系是否正确（不违反分层原则）？
- [ ] 是否有技术服务混入业务层？
- [ ] 是否有业务逻辑混入技术服务层？

---

## 六、本次重构内容

### 移动到 Services（技术服务）
- `Applications/Cache/` → `Services/Cache/`
- `Applications/Settings/` → `Services/Settings/`

### 移动到 Applications（业务逻辑）
- `Services/News/` → `Applications/News/`
- `Services/Stock/HomeStockService` → `Applications/Stocks/HomeStockService`
- `Services/StockSelection/` → `Applications/StockSelection/`

---

**最后更新：** 2025-10-06

