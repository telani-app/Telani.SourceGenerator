using System.Globalization;

namespace Telani.SourceGenerator.Test;

[TestClass]
public static class AssemblySetup
{
    // The house convention, and load-bearing here: the generators run in this process, so a
    // non-invariant culture is what proves DateSourceGenerator formats the build date with
    // CultureInfo.InvariantCulture rather than picking up whatever the build machine uses.
    [AssemblyInitialize]
    public static void SetCulture(TestContext _)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
    }
}
