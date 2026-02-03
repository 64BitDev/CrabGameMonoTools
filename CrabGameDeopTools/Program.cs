using Cpp2IL.Core;
using LibCpp2IL;
using Mono.Cecil;
using System.Text;
using System.Text.Json;
namespace CrabGameDeopTools
{
    internal class Program
    {
        static uint ArrayIntoMap = 0;
        static JsonDocument? DeopFile;
        static void Main(string[] args)
        {
            Console.WriteLine("Crab Game Assembly Mapping Tools v1.0.0");
            Console.WriteLine("By 64bitdev");
            ConsoleUtils.SetupConsoleUtils();
            while (true)
            {
                ArrayIntoMap = 0;

                switch (ConsoleUtils.SelectOptionFromArray("Tool:", "Tool", "Create a extracted map using a mac build and a windows build", "Convert a extracted map to a Json encoded Crab Game Map also known as .jecbm", "Add listing to new Map to a crab game map, this is for modifing maps"))
                {
                    case 1:
                        CreateBasicCrabGameMacMonoToCrabGameWinMono();
                        break;
                    case 2:
                        ConvertExtractedCrabGameDeopToAJsonEncodedCrabGameMap();
                        break;
                }
            }

        }

        static void CreateBasicCrabGameMacMonoToCrabGameWinMono()
        {

            string CrabGameMacPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mac Directory:", "CrabGameMacPath");
            string CrabGameWinPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Win Directory:", "CrabGameWinPath");
            string OutputPath = ConsoleUtils.GetSafeStringFromConsole("Output Folder:", "OutputPath");
            if (ConsoleUtils.GetSafeYesNoQuestion("Use Deop File:", "UseDeopFile"))
            {
                DeopFile = JsonDocument.Parse(File.ReadAllText(ConsoleUtils.GetSafeStringFromConsole("Deop File:", "DeopFilePath")));
            }

            string ga = Path.Combine(CrabGameWinPath, "GameAssembly.dll");
            string meta = Path.Combine(
                CrabGameWinPath,
                "Crab Game_Data",
                "il2cpp_data",
                "Metadata",
                "global-metadata.dat"
            );

            if (!LibCpp2IlMain.LoadFromFile(ga, meta, new int[] { 2020, 3, 21 }))
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
            if(Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, true);
            }
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
            AssemblyInfoWritor.WriteStringValue("FixedDeop");
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
                        throw;
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
                ArrayIntoMap++;
                string FixedDeop = string.Empty;
                if (!UsesOnlyAscii(winType.Name))
                {
                    FixedDeop = UIntToFixedString(ArrayIntoMap, System.Math.Min(Encoding.UTF8.GetByteCount(winType.Name), 8));

                }
                else
                {
                    FixedDeop = winType.Name;
                }
                if (DeopFile is not null)
                {
                    if (DeopFile.RootElement.TryGetProperty(WinDll.Name.Name, out var asm))
                    {
                        if (asm.TryGetProperty(winType.Namespace, out var ns))
                        {

                            if (ns.TryGetProperty(winType.FullName, out var cls))
                            {
                                FixedDeop = CutToByteLength(cls.GetProperty("FixedDeop").GetString()!, Encoding.UTF8.GetByteCount(winType.Name));
                            }
                        }
                    }
                }
                string safeName = MakeSafeFileName(FixedDeop) + ".json";
                string outFile = Path.Combine(nsDir, safeName);

                using var fs = File.Create(outFile);
                using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });

                w.WriteStartObject();
                w.WritePropertyName("ObjectMaps");
                w.WriteStartObject();
                w.WriteString("Mac", macType.Name);
                w.WriteString("Windows", winType.Name);

                w.WriteString("FixedDeop", FixedDeop);
                w.WriteEndObject();

                w.WritePropertyName("FieldMaps");
                w.WriteStartObject();
                foreach (var Field in macType.Fields)
                {
                    
                    if (!UsesOnlyAscii(Field.Name))
                    {
                        ArrayIntoMap++;
                        string DeopName = UIntToFixedString(ArrayIntoMap, 8);
                        w.WritePropertyName(DeopName);
                        w.WriteStartObject();
                        w.WriteString("Mac",Field.Name);
                        w.WriteString("FixedDeop",DeopName);
                        w.WriteEndObject();
                    }
                }
                foreach (var Property in macType.Properties)
                {
                    if (UsesOnlyAscii(Property.Name))
                        continue;

                    // -----------------------------
                    // INLINE: skip interface implementations
                    // -----------------------------
                    if (!macType.IsInterface)
                    {
                        var get = Property.GetMethod;
                        if (get != null && get.HasOverrides)
                        {
                            bool implementsInterface = false;

                            foreach (var ov in get.Overrides)
                            {
                                MethodDefinition? resolved = null;
                                try { resolved = ov.Resolve(); } catch { }

                                if (resolved?.DeclaringType?.IsInterface == true)
                                {
                                    implementsInterface = true;
                                    break;
                                }
                            }

                            if (implementsInterface)
                                continue; // interface owns this property
                        }
                    }

                    // -----------------------------
                    // BUILD MAP ENTRY
                    // -----------------------------
                    ArrayIntoMap++;
                    string DeopName = UIntToFixedString(ArrayIntoMap, 8);

                    w.WritePropertyName(DeopName);
                    w.WriteStartObject();
                    w.WriteString("Mac", Property.Name);
                    w.WriteString("FixedDeop", DeopName);
                    w.WriteEndObject();
                }

                w.WriteEndObject();
                w.WritePropertyName("MethodMaps");
                w.WriteStartObject();
                List<string> Names = new();
                foreach (var Method in macType.Methods)
                {
                    if (!UsesOnlyAscii(Method.Name))
                    {
                        ArrayIntoMap++;

                        if (Names.Contains(Method.Name))
                        {
                            continue;
                        }
                        Names.Add(Method.Name);
                        if (Method.IsVirtual)
                        {
                            //continue;
                        }
                        if (Method.HasOverrides)
                        {
                            continue;
                        }
                        string DeopName = UIntToFixedString(ArrayIntoMap, 8);
                        w.WritePropertyName(DeopName);
                        w.WriteStartObject();
                        w.WriteString("Mac", Method.Name);
                        w.WriteString("FixedDeop", DeopName);
                        w.WriteEndObject();
                    }
                }
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

        static string CutToByteLength(string input, int maxBytes)
        {
            if (maxBytes <= 0 || string.IsNullOrEmpty(input))
                return string.Empty;

            Encoding utf8 = Encoding.UTF8;
            byte[] bytes = utf8.GetBytes(input);

            if (bytes.Length <= maxBytes)
                return input;

            int cut = maxBytes;

            // Walk backwards until valid UTF-8 boundary
            while (cut > 0 && (bytes[cut] & 0b1100_0000) == 0b1000_0000)
                cut--;

            string result = utf8.GetString(bytes, 0, cut);

            // Print ONLY when truncation happens
            Console.WriteLine($"'{input}' was cut to '{result}' Please fix");

            return result;
        }

        static string UIntToFixedString(uint value, int totalWidth)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            const int baseN = 52;

            if (totalWidth <= 0)
                return string.Empty;

            char[] buffer = new char[totalWidth];

            uint v = value;

            // encode value into the available width (rollover-safe)
            for (int i = totalWidth - 1; i >= 0; i--)
            {
                buffer[i] = alphabet[(int)(v % baseN)];
                v /= baseN;
            }

            return new string(buffer);
        }
        static bool UsesOnlyAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 0x7F)
                    return false;
            }
            return true;
        }
        static void ConvertExtractedCrabGameDeopToAJsonEncodedCrabGameMap()
        {
            string extractDir = ConsoleUtils.GetSafeStringFromConsole("Extracted Crab Game Map Dir:", "OutputMapDir");
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
    }
}
