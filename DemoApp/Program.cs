using System.Diagnostics;
using System.Text.RegularExpressions;
using Telani.Data;
using Telani.Data.Settings;
using Telani.SourceGenerator;

namespace Telani.Data.Model
{

}

namespace DemoApp {

    // This DemoApp uses the generator as a ProjectReference that is not recommended and that is also the reason why this is a separate
    // package at all. However a ProjectReference is still the best way to develop here.

    [StringValueGenerator]
    public enum Planet
    {
        [StringValue("Merkur")]
        Mercury,
        [StringValue("Venus")]
        Venus,
        [StringValue("Erde")]
        Earth,
        [StringValue("Mars (de)")]
        Mars,
        [StringValue("Jupiter (de)")]
        Jupiter,
        [StringValue("Saturn (de)")]
        Saturn,
        [StringValue("Uranus (de)")]
        Uranus,
        [StringValue("Neptun (de)")]
        Neptune,
        [StringValue("This is not a planet")]
        MaxValue
    };

    [TelaniBuildDate]
    internal static partial class MyBuildDate
    {

    }

    public abstract class Route
    {
        public abstract string Path { get; }
        public abstract HttpMethod Method { get; }
        internal abstract Regex RequestRegex { get; }
    }

    [TelaniRoute("PUT", "/test/{id}")]
    public partial class MyRoute(string Test) : Route
    {
        public string Name => "MyRoute";

        public string Testing => Test;
    }

    public class Program
    {
        public static void Main(string[] args)
        {

            Console.WriteLine("Hello, World!");

            var buildDate = MyBuildDate.GetBuildDate();

            Console.WriteLine($"This executable was compiled on: {buildDate:d}");
            
            var myHome = (Planet)Random.Shared.Next((int)Planet.MaxValue);

            Console.WriteLine($"Hi, I am an extra terrestrial being from {myHome}, the German name is {myHome.GetStringValue()}");

            Console.WriteLine($"If a German person tells you something about {EnumToStringGenerator.EnumToString(Planet.Earth)}, they are talking about planet number: {(int)EnumExtensions.PlanetFromString("Erde") + 1}");


            IAppSettings settings = new AppSettings
            {
                TestProperty = "Test"
            };

            Debug.Assert(settings.TestNumberReadOnly == 17);

            AppSettings other_settings = new AppSettings
            {
                TestProperty = "New value"
            };

            (settings as AppSettings)!.UpdateFrom(other_settings);

            Debug.Assert(settings.TestProperty == "New value");


            Console.WriteLine(new MyRoute("Bla").RequestRegex.Matches("/bala").Count == 0);
            var matches = new MyRoute("Bla").RequestRegex.Matches("/test/123");
            foreach (var m in matches.FirstOrDefault()?.Groups?.Values?.Skip(1) ?? [])
            {
                Console.WriteLine(m);
            }
        }
    }
}

namespace Telani.Data.Settings
{
    [Telani.Data.AppSettings]
    public sealed partial class AppSettings : IAppSettings
    {
        /// <summary>
        /// This is a settings prop. Doc-Comments should be reflected in the interface.
        /// </summary>
        public string? TestProperty { get; set; }

        /// <summary>
        /// This prop is readonly.
        /// </summary>
        [SettingsReadOnly]
        public int TestNumberReadOnly { get; set; } = 17;

        /// <summary>
        /// This is not in the automatic interface.
        /// </summary>
        [SettingsIgnore]
        public bool RandomProperty { get; set; }

        /// <summary>
        /// This property is required, because in the original use-case extra info from the JSON file was stored here.
        /// </summary>
        [SettingsIgnore]
        public object? _extraStuff { get; set; }

        public void UpdateFrom(AppSettings other_settings)
        {
            // this is auto generated:
            Reload(other_settings);
        }

    }
}

