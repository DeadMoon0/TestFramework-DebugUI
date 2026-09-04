using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using TestFramework.DebugUI.Theme;
using Xunit;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// That every resource the code asks for by name is one the theme actually has.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of <see cref="ThemeKeys"/> the compiler cannot do. Naming the keys makes a typo
/// impossible; it does nothing about a key that was renamed or removed in <c>Theme.xaml</c>, which
/// still compiles and still fails at the moment the control is drawn — one missing brush, in one
/// panel, in a build nothing complained about.
/// </para>
/// <para>
/// Read out of the class by reflection rather than listed here, so a constant added tomorrow is
/// covered without anyone remembering to add it.
/// </para>
/// </remarks>
public class ThemeKeysTests
{
    [Fact]
    public void EveryKeyTheCodeNamesIsInTheTheme()
    {
        IReadOnlyList<FieldInfo> keys = Declared();

        Wpf.Run(() =>
        {
            List<string> missing = keys
                .Where(key => Application.Current.TryFindResource(key.GetValue(null)) is null)
                .Select(key => key.Name)
                .ToList();

            Assert.Empty(missing);
        });
    }

    /// <summary>
    /// That a constant is spelled exactly as the key it stands for.
    /// </summary>
    /// <remarks>
    /// The one rule that keeps the two files readable side by side, and the one that makes renaming a
    /// key in the theme force the same rename here. Without it a constant could carry any value at all
    /// and still resolve — to the wrong resource.
    /// </remarks>
    [Fact]
    public void EveryConstantIsSpelledExactlyAsItsKey()
    {
        List<string> mismatched = Declared()
            .Where(key => !string.Equals(key.Name, (string?)key.GetValue(null), System.StringComparison.Ordinal))
            .Select(key => $"{key.Name} = \"{key.GetValue(null)}\"")
            .ToList();

        Assert.Empty(mismatched);
    }

    /// <summary>
    /// Every constant the class declares.
    /// </summary>
    /// <remarks>
    /// Asserted to be non-empty, because a reflection query that quietly finds nothing is a test that
    /// quietly passes — and this whole class is one query.
    /// </remarks>
    private static IReadOnlyList<FieldInfo> Declared()
    {
        FieldInfo[] fields = typeof(ThemeKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .ToArray();

        Assert.NotEmpty(fields);

        return fields;
    }
}
