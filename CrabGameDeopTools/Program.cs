using Cpp2IL.Core;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using HarmonyLib;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
namespace CrabGameDeopTools
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Crab Game Assembly Mapping Tools v1.0.0");
            Console.WriteLine("By 64bitdev");
            switch (ConsoleUtils.SelectOptionFromArray("Tool:", "Create a extracted map using a mac build and a windows build", "Convert a extracted map to a Json encoded Crab Game Map also known as .jecbm"))
            {
                case 1:
                    CreateBasicCrabGameMacMonoToCrabGameWinMono();
                    break;
                case 2:
                    ConvertExtractedCrabGameDeopToAJsonEncodedCrabGameMap();
                    break;
            }

        }


        static void CreateBasicCrabGameMacMonoToCrabGameWinMono()
        {

            string CrabGameMacPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mac Directory:");
            string CrabGameWinPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Win Directory:");
            string OutputPath = ConsoleUtils.GetSafeStringFromConsole("Output Folder:");

            string ga = Path.Combine(CrabGameWinPath, "GameAssembly.dll");
            string meta = Path.Combine(
                CrabGameWinPath,
                "Crab Game_Data",
                "il2cpp_data",
                "Metadata",
                "global-metadata.dat"
            );

            if (!LibCpp2IlMain.LoadFromFile(ga, meta, new int[]{ 2020, 3, 21 }))
            {
                Console.WriteLine("ill2cpp Lib failed to process crab game please check your paths");
                return;
            }
            string CrabGameMacManagedDir = Path.Combine(
                CrabGameMacPath,
                "Contents",
                "Resources",
                "Data",
                "Managed"
                );

            var dummydlls = Cpp2IlApi.MakeDummyDLLs(); //this is were mostly everything happens
            Directory.CreateDirectory(OutputPath);
            using var AssemblyInfoStream = File.Create($"{OutputPath}\\CGMapInfo.json");
            using var AssemblyInfoWritor = new Utf8JsonWriter(AssemblyInfoStream, new JsonWriterOptions
            {
                Indented = true
            });

            AssemblyInfoWritor.WriteStartObject();
            AssemblyInfoWritor.WritePropertyName("IncludedObjectMaps");
            AssemblyInfoWritor.WriteStartArray();
            AssemblyInfoWritor.WriteStringValue("Mac");
            AssemblyInfoWritor.WriteStringValue("Windows");
            AssemblyInfoWritor.WriteEndArray();
            AssemblyInfoWritor.WriteEndObject();
            AssemblyInfoWritor.Flush();

            for (int i = 0; i < dummydlls.Count; i++)
            {
                if (dummydlls[i].Name.Name.StartsWith("Unity"))
                {
                    continue;
                }
                if (dummydlls[i].Name.Name.StartsWith("System"))
                {
                    continue;
                }
                if (dummydlls[i].Name.Name.StartsWith("mscorlib"))
                {
                    continue;
                }
                if (dummydlls[i].Name.Name.StartsWith("Mono"))
                {
                    continue;
                }
                if (File.Exists(Path.Combine(CrabGameMacManagedDir, dummydlls[i].Name.Name + ".dll")))
                {
                    try
                    {
                        var CrabGameMonoAsm = AssemblyDefinition.ReadAssembly(Path.Combine(CrabGameMacManagedDir, dummydlls[i].Name.Name + ".dll"));
                        CreateBasicCrabGameMacToWinDllMap(CrabGameMonoAsm, dummydlls[i], OutputPath);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"{dummydlls[i].Name.Name} success");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"{dummydlls[i].Name.Name} Failed");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
            }


        }
        static void CreateBasicCrabGameMacToWinDllMap(AssemblyDefinition MacDll, AssemblyDefinition WinDll, string OutputPath)
        {
            Directory.CreateDirectory(@$"{OutputPath}\{MacDll.Name.Name}");

            var macTop = MacDll.MainModule.Types;
            var winTop = WinDll.MainModule.Types;

            int topCount = Math.Min(macTop.Count, winTop.Count);

            for (int i = 0; i < topCount; i++)
            {
                MapTypeRecursive(macTop[i], winTop[i]);
            }

            void MapTypeRecursive(TypeDefinition macType, TypeDefinition winType)
            {
                WriteTypeMap(macType, winType);

                int nestedCount = Math.Min(macType.NestedTypes.Count, winType.NestedTypes.Count);
                for (int i = 0; i < nestedCount; i++)
                {
                    MapTypeRecursive(macType.NestedTypes[i], winType.NestedTypes[i]);
                }
            }

            void WriteTypeMap(TypeDefinition macType, TypeDefinition winType)
            {
                string objNamespace = string.IsNullOrEmpty(macType.Namespace) ? "Global" : macType.Namespace;
                string nsDir = Path.Combine(OutputPath, MacDll.Name.Name, objNamespace);
                Directory.CreateDirectory(nsDir);

                // Use FullName so nested types are unique, then sanitize for filename
                string safeName = MakeSafeFileName(macType.FullName) + ".json";
                string outFile = Path.Combine(nsDir, safeName);

                using var fs = File.Create(outFile);
                using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });

                w.WriteStartObject();
                w.WritePropertyName("ObjectMaps");
                w.WriteStartObject();
                w.WriteString("Mac", macType.Name);
                w.WriteString("Windows", winType.Name);
                w.WriteEndObject();
                w.WriteEndObject();
                w.Flush();
            }

            static string MakeSafeFileName(string s)
            {
                // Cecil nested type separator is "/" in FullName; make it stable and filesystem safe
                s = s.Replace('/', '+');

                foreach (char c in Path.GetInvalidFileNameChars())
                    s = s.Replace(c, '_');

                return s;
            }
        }

        static void ConvertExtractedCrabGameDeopToAJsonEncodedCrabGameMap()
        {
            string extractDir = ConsoleUtils.GetSafeStringFromConsole("Extracted Crab Game Map Dir:");
            string outPath = new DirectoryInfo(extractDir).Name + ".jecgm";

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
                        JECGMWritor.WritePropertyName(Path.GetFileNameWithoutExtension(type));
                        using (var doc = JsonDocument.Parse(File.ReadAllText(type)))
                        {
                            doc.RootElement.WriteTo(JECGMWritor);
                        }

                    }
                    JECGMWritor.WriteEndObject();

                }

                JECGMWritor.WriteEndObject();
            }
            JECGMWritor.WriteEndObject(); // root
            JECGMWritor.Flush();
        }


        static void ConvertExtractedCrabGameMapToABinaryEncodedCrabGameMap()
        {

        }
    }
}
