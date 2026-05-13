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

    private readonly record struct EnumEntry(string Name, ImmutableDictionary<string, string> Values, string EnumNamespace)
    {
        public readonly bool Equals(EnumEntry other)
        {
            if (Name != other.Name)
            {
                return false;
            }
            if (EnumNamespace != other.EnumNamespace)
            {
                return false;
            }
            if (Values.Count != other.Values.Count)
            {
                return false;
            }
            foreach (var item in Values)
            {
                if (!other.Values.TryGetValue(item.Key, out var value) || value != item.Value)
                {
                    return false;
                }
            }
            return true;
        }

        public override readonly int GetHashCode() => HashCode.Combine(Name, Values, EnumNamespace);
    };

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
            extension.WriteLine($"namespace {e.EnumNamespace};");
            extension.WriteLine();
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
                extension.WriteLine($"{e.Name}.{att.Key} => {att.Value},");
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
                extension.WriteLine($"{att.Value} => {e.Name}.{att.Key},");
            }
            extension.WriteLine($"_ => {e.Name}.{e.Values.First().Key}");
            extension.WriteEndBlock(addSemicolon: true);

            extension.WriteEndBlock(); // class end
        }

        return extension;
    }

    private static EnumEntry PrepareEnum(EnumDeclarationSyntax i, SemanticModel semModel)
    {
        var enumName = i.Identifier.ValueText;
        var enumNamespace = semModel.GetDeclaredSymbol(i)?.ContainingNamespace?.ToDisplayString() ?? "";

        var values = new Dictionary<string, string>();

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
            values.Add(enumValue, enumStringValue);
        }
        return new EnumEntry(enumName, values.ToImmutableDictionary(), enumNamespace);
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
