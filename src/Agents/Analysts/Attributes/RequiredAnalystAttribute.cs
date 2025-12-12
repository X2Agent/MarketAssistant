using System;

namespace MarketAssistant.Agents.Analysts.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RequiredAnalystAttribute : Attribute
{
    public RequiredAnalystAttribute()
    {
    }
}
