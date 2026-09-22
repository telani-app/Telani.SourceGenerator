using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Telani.SourceGenerator;


/// <summary>
/// This generator creates two mechanisms to convert enums to strings.
/// This works for enums that have the attribute StringValueGeneratorAttribute.
/// And all values of that enum must be annotated with a StringValueAttribute.
/// 
/// This Generator creates an extension method GetStringValue() on that enum.
/// Additionally a `string EnumToStringGenerator.EnumToString(Enum e)` method is 
/// created that calls the GetStringValue method on any enum handled by this generator
/// </summary>
[Generator(LanguageNames.CSharp)]
public class StringValueGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enums = context.SyntaxProvider.ForAttributeWithMetadataName("Telani.SourceGenerator.StringValueGeneratorAttribute",
            predicate: static (node, _) => IsSyntaxTargetForGenerationQuick(node),
            transform: static (syntaxContext, _) => ((EnumDeclarationSyntax)syntaxContext.TargetNode, syntaxContext.SemanticModel))
            .Select(static (a, _) => PrepareEnum(a.Item1, a.SemanticModel));

        context.RegisterSourceOutput(enums.Collect(), Execute);

        context.RegisterPostInitializationOutput(static c =>
        {
            c.AddEmbeddedAttributeDefinition();
            c.AddSource("Attributes.g.cs", ReadAttributesFile());

            c.AddSource("StringValueGeneratorAttribute.g.cs", 
                Helpers.ReadParameterlessAttributesFile("StringValueGeneratorAttribute", "This enum should be quickly convertible into a string.", AttributeTargets.Enum).ToSourceText());
        });
    }

    /// <summary>
    /// One enum member and the string it maps to, as written in the source.
    /// </summary>
    private readonly record struct EnumValueEntry(string MemberName, string StringValue);

    /// <summary>
    /// The members are kept in declaration order in an <see cref="EquatableArray{T}"/>. An
    /// ImmutableDictionary would be wrong here twice over: its enumeration order follows the
    /// randomized string hash codes, so every compiler process would emit the switch arms in a
    /// different order and pick a different fallback member, and it compares by reference, which
    /// is why this type needed a hand-written Equals. Declaration order makes the generated source
    /// reproducible and lets the record struct synthesize a correct Equals/GetHashCode pair.
    /// </summary>
    private readonly record struct EnumEntry(string Name, EquatableArray<EnumValueEntry> Values, string EnumNamespace);

    private static void Execute(SourceProductionContext productionContext, ImmutableArray<EnumEntry> inputs)
    {
        if (!inputs.Any())
        {
            return;
        }

        productionContext.AddSource($"EnumExtensions.g.cs", CreateEnumExtensions(inputs).ToSourceText());
        productionContext.AddSource($"EnumToString.g.cs", EnumToString(inputs).ToSourceText());
    }

    private static SourceWriter CreateEnumExtensions(ImmutableArray<EnumEntry> inputs)
    {
        var extension = new SourceWriter();
        extension.WriteLine("#nullable enable");
        extension.WriteLine();

        foreach (var e in inputs)
        {
            extension.WriteLine($"namespace {e.EnumNamespace}");
            extension.WriteStartBlock();
            extension.WriteLine($"/// <summary>");
            extension.WriteLine($"/// </summary>");
            extension.WriteLine($"internal static partial class EnumExtensions");
            extension.WriteStartBlock();

            extension.WriteLine($"/// <summary>");
            extension.WriteLine($"/// </summary>");
            extension.WriteLine($"public static string GetStringValue(this {e.Name} @this) => @this switch");
            extension.WriteStartBlock();
            foreach (var att in e.Values)
            {
                extension.WriteLine($"{e.Name}.{att.MemberName} => {att.StringValue},");
            }
            extension.WriteLine($"_ => \"\"");
            extension.WriteEndBlock(addSemicolon: true);

            extension.WriteLine();
            extension.WriteLine($"/// <summary>");
            extension.WriteLine($"/// </summary>");
            extension.WriteLine($"public static {e.Name} {e.Name}FromString(string? value) => value switch");
            extension.WriteStartBlock();
            foreach (var att in e.Values)
            {
                extension.WriteLine($"{att.StringValue} => {e.Name}.{att.MemberName},");
            }
            // An unrecognised string falls back to the first declared member. Any member has to be
            // picked, and the first declared one is the only choice that does not depend on the
            // order the members happen to be stored in.
            extension.WriteLine($"_ => {e.Name}.{e.Values.First().MemberName}");
            extension.WriteEndBlock(addSemicolon: true);

            extension.WriteEndBlock(); // class end
            extension.WriteEndBlock(); // end namespace
        }

        return extension;
    }

    private static EnumEntry PrepareEnum(EnumDeclarationSyntax i, SemanticModel semModel)
    {
        var enumName = i.Identifier.ValueText;
        var enumNamespace = semModel.GetDeclaredSymbol(i)?.ContainingNamespace?.ToDisplayString() ?? "";

        var values = ImmutableArray.CreateBuilder<EnumValueEntry>();

        foreach (var item in i.Members)
        {
            var enumValue = item.Identifier.ValueText;
            var enumStringValue = "";
            foreach (var attributeOnMember in item.AttributeLists)
            {
                foreach (var att in attributeOnMember.Attributes)
                {
                    var name = (att.Name as SimpleNameSyntax)?.Identifier.ValueText;
                    if (name == "StringValue")
                    {
                        var args = att.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax;
                        if (args is not null)
                        {
                            enumStringValue = args.Token.Text;
                        }
                    }
                }
            }
            values.Add(new EnumValueEntry(enumValue, enumStringValue));
        }
        return new EnumEntry(enumName, new EquatableArray<EnumValueEntry>(values.ToImmutable()), enumNamespace);
    }

    private static SourceWriter EnumToString(IEnumerable<EnumEntry> enums)
    {
        var extension = new SourceWriter();
        extension.WriteLine("#nullable enable");
        
        foreach (var enum_namespace in enums.GroupBy(a => a.EnumNamespace))
        {
            extension.WriteLine($"namespace {enum_namespace.Key}");
            extension.WriteStartBlock();
            extension.WriteLine($"/// <summary>");
            extension.WriteLine($"/// </summary>");
            extension.WriteLine($"internal static partial class EnumToStringGenerator");
            extension.WriteStartBlock();

            extension.WriteLine($"/// <summary>");
            extension.WriteLine($"/// </summary>");
            extension.WriteLine($"public static string EnumToString(Enum en) => en switch ");
            extension.WriteStartBlock();

            foreach (var en in enum_namespace)
            {
                extension.WriteLine($"{en.Name} {en.Name} => {en.Name}.GetStringValue(),");
            }

            extension.WriteLine("_ => \"\"");

            extension.WriteEndBlock(addSemicolon: true);
            extension.WriteEndBlock();
            extension.WriteEndBlock();
        }
        return extension;
    }

    private static string ReadAttributesFile()
    {
        return @"
using System.Globalization;

#nullable enable

namespace Telani.SourceGenerator;

/// <summary>
/// The string value that this enum value represents.
/// </summary>
[global::Microsoft.CodeAnalysis.EmbeddedAttribute]
[AttributeUsage(AttributeTargets.Field)]
internal sealed class StringValueAttribute : Attribute
{
    /// <summary>
    /// 
    /// </summary>
    public StringValueAttribute(string value) => Value = value;
    /// <summary>
    ///  The string that this enum value represents.
    /// </summary>
    public string Value { get; private set; }
}
";
    }

    private static bool IsSyntaxTargetForGenerationQuick(SyntaxNode node)
    {
        // This should not be necessary, because the attribute already has a usage restriction.
        if (node is not EnumDeclarationSyntax)
        {
            return false;
        }
        return true;
    }
}
