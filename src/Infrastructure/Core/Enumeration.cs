using System.Reflection;

namespace MarketAssistant.Infrastructure.Core;

public class Enumeration : IComparable
{
    public string Name { get; private set; }

    public string Instructions { get; init; }

    protected Enumeration(string name, string instructions)
        => (Name, Instructions) = (name, instructions);

    public override string ToString() => Name;

    public static IEnumerable<T> GetAll<T>() where T : Enumeration =>
        typeof(T).GetFields(BindingFlags.Public |
                            BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                 .Select(f => f.GetValue(null))
                 .Cast<T>();

    public override bool Equals(object? obj)
    {
        if (obj is not Enumeration otherValue)
        {
            return false;
        }

        var typeMatches = GetType().Equals(obj.GetType());
        var valueMatches = Name.Equals(otherValue.Name);

        return typeMatches && valueMatches;
    }

    public int CompareTo(object? other)
    {
        if (other == null) return 1;
        if (other is not Enumeration) throw new ArgumentException("Object is not an Enumeration");
        return Name.CompareTo(((Enumeration)other).Name);
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}

