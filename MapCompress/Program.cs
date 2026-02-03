//a very simple console utill for compiling the folder you are in into an jecgm
using System.CommandLine;
using System.Text.Json;
namespace MapCompress
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int compresstypeloc = Array.IndexOf(args, "--compress");
            if(compresstypeloc == -1)
            {
                Console.WriteLine("No args added please select from the --compress modes --compress json");
                return;
            }
            string compresstype = args[compresstypeloc + 1];
            switch(compresstype)
            {
                case "json":
                    {
                        CompressToJecgm();
                        return;
                    }
                default:
                    Console.WriteLine($"decompress type '{compresstype}' was not found please use a vaild compress type");
                    return;
            }
        }


        static void CompressToJecgm()
        {

            string extractDir = AppContext.BaseDirectory;
            string outPath = new DirectoryInfo(extractDir).Name + ".jecgm";
            Console.WriteLine($"Creating {outPath}");
            using var JECGMStream = File.Create(outPath);
            using var JECGMWritor = new Utf8JsonWriter(JECGMStream, new JsonWriterOptions
            {
                Indented = false
            });

            JECGMWritor.WriteStartObject();

            foreach (var MapDll in Directory.GetDirectories(extractDir))
            {
                JECGMWritor.WritePropertyName(Path.GetFileName(MapDll));
                JECGMWritor.WriteStartObject(); //Asm
                foreach (var Namespace in Directory.GetDirectories(MapDll))
                {
                    JECGMWritor.WritePropertyName(Path.GetFileName(Namespace));
                    JECGMWritor.WriteStartObject(); //Asm
                    foreach (var type in Directory.GetFiles(Namespace))
                    {
                        try
                        {
                            JECGMWritor.WritePropertyName(Path.GetFileNameWithoutExtension(type));
                            using (var doc = JsonDocument.Parse(File.ReadAllText(type)))
                            {
                                doc.RootElement.WriteTo(JECGMWritor);
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.BackgroundColor = ConsoleColor.White;
                            Console.WriteLine(ex.ToString());
                            Console.WriteLine($"At File {type}");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.BackgroundColor = ConsoleColor.Black;
                        }
                    }
                    JECGMWritor.WriteEndObject();

                }

                JECGMWritor.WriteEndObject();
            }
            JECGMWritor.WriteEndObject(); // root
            JECGMWritor.Flush();
        }
    }
}
