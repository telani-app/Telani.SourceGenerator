namespace Telani.SourceGenerator;

internal static class SourceWriterExtensions
{
    internal static void WriteEndBlock(this SourceWriter writer, bool addSemicolon = false)
    {
        if (addSemicolon)
        {
            writer.Indentation--;
            writer.WriteLine("};");
        }
        else
        {
            writer.Indentation--;
            writer.WriteLine('}');
        }   
    }

    internal static void WriteStartBlock(this SourceWriter writer)
    {
        writer.WriteLine('{');
        writer.Indentation++;
    }
}
