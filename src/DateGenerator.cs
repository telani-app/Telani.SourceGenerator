using Microsoft.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Telani.SourceGenerator;

/// <summary>
/// A generator to add a class containing the build date to the output.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class DateSourceGenerator : IIncrementalGenerator
{

    private static StringBuilder GenerateSource(string dateString)
    {
        return new(@"
using System;
using System.Globalization;

namespace TelaniSourceGenerator
{
    internal static class BuildDate
    {
        internal static DateTime GetBuildDate() 
        {
            return DateTime.ParseExact(""" + dateString + @""", ""d"", CultureInfo.InvariantCulture);
        }
    }
}");
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var a = context.ParseOptionsProvider.Select(static (ab, cancel) => DateTime.UtcNow.ToString("d", CultureInfo.InvariantCulture));

        context.RegisterSourceOutput(a, static (productionContext, dateString) =>
        {
            productionContext.AddSource("TelaniSourceGeneratorBuildDate.g.cs", GenerateSource(dateString).ToString());
        });
    }
}
