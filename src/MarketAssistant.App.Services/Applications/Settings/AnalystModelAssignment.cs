namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 分析师角色 → 模型分配的读写与解析。
/// 角色键统一采用分析师类型的类名（如 <c>FundamentalAnalystAgent</c>），
/// 与 <see cref="UserSetting.EnabledAnalystRoles"/>、设置页角色列表保持同一套约定。
/// </summary>
public static class AnalystModelAssignment
{
    /// <summary>
    /// 读取角色级模型分配；未分配或留空时返回空串（表示跟随默认模型）。
    /// </summary>
    public static string GetAssignedModelId(UserSetting setting, string providerId, string analystRoleKey)
    {
        if (setting is null || string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(analystRoleKey))
            return string.Empty;

        return setting.ProviderAnalystModelIds.TryGetValue(providerId, out var assignments) &&
               assignments.TryGetValue(analystRoleKey, out var modelId)
            ? modelId?.Trim() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// 写入角色级模型分配；<paramref name="modelId"/> 为空白时移除条目（跟随默认模型），
    /// 避免落盘大量空字符串并在缓存指纹中产生噪声。
    /// </summary>
    public static void SetAssignedModelId(UserSetting setting, string providerId, string analystRoleKey, string? modelId)
    {
        if (setting is null || string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(analystRoleKey))
            return;

        var normalized = modelId?.Trim() ?? string.Empty;

        if (normalized.Length == 0)
        {
            if (setting.ProviderAnalystModelIds.TryGetValue(providerId, out var existing))
            {
                existing.Remove(analystRoleKey);
                if (existing.Count == 0)
                    setting.ProviderAnalystModelIds.Remove(providerId);
            }

            return;
        }

        if (!setting.ProviderAnalystModelIds.TryGetValue(providerId, out var assignments))
        {
            assignments = new Dictionary<string, string>(StringComparer.Ordinal);
            setting.ProviderAnalystModelIds[providerId] = assignments;
        }

        assignments[analystRoleKey] = normalized;
    }

    /// <summary>
    /// 生成指定服务商下角色分配的稳定指纹（键排序），供分析缓存判定失效。
    /// 无任何有效分配时返回空串。
    /// </summary>
    public static string GetAssignmentFingerprint(UserSetting setting, string providerId)
    {
        if (setting is null ||
            string.IsNullOrWhiteSpace(providerId) ||
            !setting.ProviderAnalystModelIds.TryGetValue(providerId, out var assignments))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            assignments
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value.Trim()}"));
    }
}
