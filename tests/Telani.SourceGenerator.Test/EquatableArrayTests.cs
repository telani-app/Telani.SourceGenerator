using System.Collections.Immutable;
using System.Reflection;

namespace Telani.SourceGenerator.Test;

/// <summary>
/// EquatableArray&lt;T&gt; is internal, so these go through reflection rather than adding an
/// InternalsVisibleTo to the shipped analyzer assembly.
/// </summary>
[TestClass]
public sealed class EquatableArrayTests
{
    private static readonly Type OpenType =
        typeof(StringValueGenerator).Assembly.GetType("Telani.SourceGenerator.EquatableArray`1", throwOnError: true)!;

    private static object Create(params string[] items)
    {
        var closed = OpenType.MakeGenericType(typeof(string));
        var ctor = closed.GetConstructor([typeof(ImmutableArray<string>)])!;
        return ctor.Invoke([ImmutableArray.Create(items)]);
    }

    private static bool TypedEquals(object left, object right)
    {
        var closed = OpenType.MakeGenericType(typeof(string));
        return (bool)closed.GetMethod("Equals", [closed])!.Invoke(left, [right])!;
    }

    private static int HashOf(object value)
        => (int)OpenType.MakeGenericType(typeof(string)).GetMethod(nameof(GetHashCode), Type.EmptyTypes)!.Invoke(value, null)!;

    [TestMethod]
    public void Equal_contents_compare_equal()
        => Assert.IsTrue(TypedEquals(Create("a", "b"), Create("a", "b")));

    [TestMethod]
    public void Different_contents_compare_unequal()
        => Assert.IsFalse(TypedEquals(Create("a", "b"), Create("a", "c")));

    [TestMethod]
    public void Order_is_significant()
        => Assert.IsFalse(TypedEquals(Create("a", "b"), Create("b", "a")));

    [TestMethod]
    public void Equal_contents_hash_equally()
        => Assert.AreEqual(HashOf(Create("a", "b")), HashOf(Create("a", "b")));

    [TestMethod]
    public void An_empty_array_is_equal_to_another_empty_array()
        => Assert.IsTrue(TypedEquals(Create(), Create()));

    [TestMethod]
    public void The_default_comparer_uses_the_typed_path()
    {
        // This is the path Roslyn's node tables take, and the reason finding 5 has not bitten yet.
        var closed = OpenType.MakeGenericType(typeof(string));
        var comparer = typeof(EqualityComparer<>).MakeGenericType(closed)
            .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        var equals = comparer.GetType().GetMethod("Equals", [closed, closed])!;

        Assert.IsTrue((bool)equals.Invoke(comparer, [Create("a", "b"), Create("a", "b")])!);
    }

    // --- known bugs --------------------------------------------------------------------------

    [TestMethod]
    [Ignore("Audit finding 5: Equals(object) is 'obj is EquatableArray<T> array && Equals(this, array)'. Two arguments bind to the inherited STATIC object.Equals(object, object), which calls straight back into this override. WARNING: un-skipping this before the fix crashes the test runner with an uncatchable StackOverflowException - it cannot be caught or timed out. The fix is to call Equals(array).")]
    public void Equals_object_does_not_recurse()
    {
        var closed = OpenType.MakeGenericType(typeof(string));
        var equalsObject = closed.GetMethod("Equals", [typeof(object)])!;

        Assert.IsTrue((bool)equalsObject.Invoke(Create("a", "b"), [Create("a", "b")])!);
    }
}
