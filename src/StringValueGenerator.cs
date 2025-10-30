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
        var extension = new StringBuilder();
        extension.AppendLine("#nullable enable");

        foreach (var e in inputs)
        {
            extension.AppendLine($"namespace {e.EnumNamespace}");
            extension.AppendLine("{");
            extension.AppendLine($"    /// <summary>");
            extension.AppendLine($"    /// </summary>");
            extension.AppendLine($"    internal static partial class EnumExtensions");
            extension.AppendLine("    {");


            extension.AppendLine($"        /// <summary>");
            extension.AppendLine($"        /// </summary>");
            extension.AppendLine($"        public static string GetStringValue(this {e.Name} @this) => @this switch");
            extension.AppendLine("        {");
            foreach (var att in e.Values)
            {
                extension.AppendLine($"            {e.Name}.{att.Key} => {att.Value},");
            }
            extension.AppendLine($"            _ => \"\"");
            extension.AppendLine("        };");

            extension.AppendLine($"        /// <summary>");
            extension.AppendLine($"        /// </summary>");
            extension.AppendLine($"        public static {e.Name} {e.Name}FromString(string? value) => value switch");
            extension.AppendLine("        {");
            foreach (var att in e.Values)
            {
                extension.AppendLine($"            {att.Value} => {e.Name}.{att.Key},");
            }
            extension.AppendLine($"            _ => {e.Name}.{e.Values.First().Key}");
            extension.AppendLine("        };");

            extension.AppendLine("    }"); // class end
            extension.AppendLine("}"); // namespace end
        }
        
        var str = extension.ToString();

        productionContext.AddSource($"EnumToString.g.cs", EnumToString(inputs));

        productionContext.AddSource($"EnumExtensions.g.cs", str);
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

    private static string EnumToString(IEnumerable<EnumEntry> enums)
    {
        var extension = new StringBuilder();
        extension.AppendLine("#nullable enable");
        foreach (var enum_namespace in enums.GroupBy(a => a.EnumNamespace))
        {
            extension.AppendLine($"namespace {enum_namespace.Key} {{");

            extension.AppendLine($"    /// <summary>");
            extension.AppendLine($"    /// </summary>");
            extension.AppendLine($"    internal static partial class EnumToStringGenerator");
            extension.AppendLine("    {");

            extension.AppendLine($"        /// <summary>");
            extension.AppendLine($"        /// </summary>");
            extension.AppendLine($"        public static string EnumToString(Enum en) => en switch ");
            extension.AppendLine("        {");

            foreach (var en in enum_namespace)
            {
                extension.AppendLine($"            {en.Name} {en.Name} => {en.Name}.GetStringValue(),");
            }

            extension.AppendLine("            _ => \"\"");

            extension.AppendLine("        };");
            extension.AppendLine("    }");
            extension.AppendLine("}");
        }

        return extension.ToString();
    }

    private static string ReadAttributesFile()
    {
        return @"
using System.Globalization;

#nullable enable

namespace Telani.SourceGenerator;
/// <summary>
/// This enum should be quickly convertible into a string.
/// </summary>
[global::Microsoft.CodeAnalysis.EmbeddedAttribute]
[AttributeUsage(AttributeTargets.Enum)]
internal sealed class StringValueGeneratorAttribute : Attribute
{

}

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
        if (node is not EnumDeclarationSyntax)
        {
            return false;
        }
        return true;
    }
}
