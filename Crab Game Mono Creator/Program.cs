using Mono.Cecil;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace Crab_Game_Mono_Creator
{
    internal class Program
    {
        static string CrabGameMacPath;
        static string CrabGameWinPath;
        static string OutputPath;
        const string MonoFilesDir = "MonoFiles";
        public const string MapToName = "FixedDeop";

        static void Main(string[] args)
        {
            Console.WriteLine("=== Crab Game Mono Creator ===");
            Console.WriteLine("Created by 64bitdev");
            ConsoleUtils.SetupConsoleUtils();
            
            CrabGameMacPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mac Directory:", "CrabGameMacPath");
            CrabGameWinPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Win Directory:", "CrabGameWinPath");
            OutputPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mono Output Directory:","OutputPath");

            //make sure the stuff we want exists there
            if (!Directory.Exists(OutputPath))
            {
                Console.WriteLine("You selected a directory that does not exist Creating folder");
                Directory.CreateDirectory(OutputPath);
            }


            //copy crab game files using generated code
            Console.WriteLine($"Copying Windows Crab Game Files");
            CopyCrabGameDir();

            //Copy MonoFiles
            Console.WriteLine($"Copying Copying Crab Game Mono Files");
            CopyDirectory(MonoFilesDir, OutputPath);


            string CrabGameMacManagedDir = Path.Combine(
            CrabGameMacPath,
            "Contents",
            "Resources",
            "Data",
            "Managed"
            );
            Console.WriteLine($"Starting to patch Managed Assemblys");
            Directory.CreateDirectory(Path.Combine(OutputPath, "Crab Game_Data", "Managed"));
            //preprossess json
            var crabgamemap = JsonDocument.Parse(File.ReadAllText("cgmonomap.jecgm"));
            //get all of the dlls that this maps
            foreach (var file in Directory.GetFiles(CrabGameMacManagedDir))
            {
                RewriteAsmWithMap(file,crabgamemap);
            }

            Console.WriteLine($"Done Writing Asms");

            Console.WriteLine($"Rewriting Unity Files");
            RewriteUnityFiles(crabgamemap);
            
        }


        static void RewriteUnityFiles(JsonDocument crabgamemap)
        {
            Console.WriteLine($"Creating List of assets to replace");
            List<ReplacePattern> CGMap = new();

            string CrabGameMonoDataDir = Path.Combine(
            OutputPath,
            "Crab Game_Data"
            );
            List<ReplacePattern> patterns = new List<ReplacePattern>();

            foreach (var dll in crabgamemap.RootElement.EnumerateObject())
            {
                foreach (var ns in dll.Value.EnumerateObject())
                {
                    foreach (var container in ns.Value.EnumerateObject())
                    {
                        foreach (var cls in container.Value.EnumerateObject())
                        {
                            if(cls.Value.TryGetProperty(MapToName,out var MapNameProperty))
                            {
                                string find = cls.Value.GetProperty("Windows").GetString()!;
                                string replace = MapNameProperty.GetString()!;

                                patterns.Add(new ReplacePattern
                                {
                                    Find = StringToUnityString(find),
                                    Replace = StringToUnityString(replace)
                                });
                            }
                        }
                    }
                }
            }
            Dictionary<byte, ReplacePattern[]> patternTable =
                patterns
                    .GroupBy(p => p.Find[0])
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(p => p.Find.Length).ToArray()
                    );

            foreach (var file in Directory.GetFiles(CrabGameMonoDataDir))
            {
                Console.WriteLine($"Doing File {file}");

                byte[] filebytes = File.ReadAllBytes(file);
                byte[] patched = ReplaceAll(filebytes, patternTable);
                File.WriteAllBytes(file, patched);
            }

        }

        struct ReplacePattern
        {
            public byte[] Find;
            public byte[] Replace;
        }

        static byte[] StringToUnityString(string value)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);

            byte[] result = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, result, 0, utf8.Length);
            result[result.Length - 1] = 0x00; // null terminato

            return result;
        }
        static byte[] ReplaceAll(byte[] data, Dictionary<byte, ReplacePattern[]> table)
        {
            List<byte> output = new List<byte>(data.Length);

            int i = 0;
            while (i < data.Length)
            {
                if (!table.TryGetValue(data[i], out var candidates))
                {
                    output.Add(data[i]);
                    i++;
                    continue;
                }

                bool matched = false;

                foreach (var p in candidates)
                {
                    if (i + p.Find.Length > data.Length)
                        continue;

                    bool ok = true;
                    for (int j = 1; j < p.Find.Length; j++)
                    {
                        if (data[i + j] != p.Find[j])
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                    {
                        output.AddRange(p.Replace);
                        i += p.Find.Length;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    output.Add(data[i]);
                    i++;
                }
            }

            return output.ToArray();
        }
        static void RewriteAsmWithMap(string file,JsonDocument crabgamemap)
        {

            var filename = Path.GetFileName(file);
            Console.WriteLine($"Trying to patch {filename}");
            if (filename.StartsWith("Unity") || filename.StartsWith("System") || filename.StartsWith("mscorlib") || filename.StartsWith("Mono"))
            {
                File.Copy(file, Path.Combine(OutputPath, "Crab Game_Data", "Managed", Path.GetFileName(file)), true);
                Console.WriteLine($"Skiped file {filename} because it was a unity file");
                return;
            }
            AssemblyDefinition macAsm = AssemblyDefinition.ReadAssembly(file);
            foreach (var type in AsmUtils.GetAllTypeDefinitions(macAsm.MainModule))
            {
                if (crabgamemap.RootElement.TryGetProperty(macAsm.Name.Name, out var asm))
                {
                    string nsName = string.IsNullOrWhiteSpace(type.Namespace) ? "Global" : type.Namespace;

                    if (asm.TryGetProperty(nsName, out var ns))
                    {
                        foreach (var mappedtype in ns.EnumerateObject())
                        {
                            string mac = mappedtype.Value.GetProperty("ObjectMaps").GetProperty("Mac").GetString()!;

                            if (mac == type.Name)
                            {
                                string Windows = mappedtype.Value.GetProperty("ObjectMaps").GetProperty(MapToName).GetString()!;
                                type.Name = Windows;
                                break;
                            }
                        }
                    }



                }
            }
            foreach (var t in AsmUtils.GetAllTypeDefinitions(macAsm.MainModule))
            {
                LocalUtils.FixExternalRefs(t,crabgamemap,macAsm);
            }


            macAsm.Write(Path.Combine(OutputPath, "Crab Game_Data", "Managed", Path.GetFileName(file)));
        }
        /// <summary>
        /// this is one of the functions of all time
        /// </summary>
        static void CopyCrabGameDir()
        {
            CopyCrabGameWindowsFileToGameDir(@"Crab Game.exe");
            CopyCrabGameWindowsFileToGameDir(@"UnityCrashHandler64.exe");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\app.info");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\boot.config");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\globalgamemanagers");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\globalgamemanagers.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level0");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level0.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level1");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level10");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level11");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level12");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level13");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level14");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level15");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level16");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level17");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level18");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level19");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level2");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level2.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level20");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level21");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level21.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level22");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level23");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level24");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level25");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level25.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level26");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level27");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level28");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level29");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level3");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level30");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level31");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level32");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level33");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level34");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level35");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level36");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level37");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level38");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level39");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level4");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level40");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level41");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level42");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level43");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level43.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level44");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level45");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level45.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level46");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level47");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level48");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level49");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level5");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level5.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level50");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level51");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level52");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level53");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level54");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level55");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level56");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level57");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level58");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level59");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level6");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level60");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level61");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level62");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level63");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level63.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level64");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level65");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level66");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level67");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level68");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level69");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level7");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level7.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level8");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\level9");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\resources.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\resources.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\resources.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\RuntimeInitializeOnLoads.json");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\ScriptingAssemblies.json");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets0.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets0.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets0.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets1.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets10.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets10.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets11.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets11.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets12.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets12.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets13.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets14.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets14.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets15.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets15.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets16.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets17.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets18.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets18.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets18.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets19.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets19.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets19.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets2.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets2.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets2.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets20.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets20.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets21.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets22.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets23.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets23.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets24.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets24.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets25.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets25.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets25.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets26.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets27.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets27.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets28.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets28.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets29.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets3.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets3.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets3.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets30.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets31.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets31.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets32.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets32.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets33.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets34.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets34.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets35.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets36.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets37.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets38.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets39.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets4.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets4.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets40.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets40.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets41.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets42.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets43.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets43.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets43.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets44.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets44.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets45.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets46.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets47.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets47.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets47.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets48.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets49.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets5.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets5.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets5.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets50.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets50.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets50.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets51.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets52.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets53.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets53.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets54.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets55.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets55.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets55.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets56.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets57.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets58.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets59.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets6.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets6.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets6.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets60.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets61.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets62.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets63.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets63.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets64.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets64.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets64.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets65.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets66.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets67.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets68.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets69.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets7.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets7.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets7.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets8.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets8.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets8.resource");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets9.assets");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\sharedassets9.assets.resS");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\Resources\unity default resources");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\Resources\unity_builtin_extra");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\Plugins\x86_64\AudioPluginDissonance.dll");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\Plugins\x86_64\opus.dll");
            CopyCrabGameWindowsFileToGameDir(@"Crab Game_Data\Plugins\x86_64\steam_api64_net.dll", @"Crab Game_Data\Plugins\x86_64\steam_api.dll");
        }


        static void CopyCrabGameWindowsFileToGameDir(string relativePath)
        {
            Console.WriteLine($"Copying Windows file {relativePath}");
            string sourceFile = Path.Combine(CrabGameWinPath, relativePath);
            string destFile = Path.Combine(OutputPath, relativePath);

            string? dir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.Copy(sourceFile, destFile, overwrite: true);
        }
        static void CopyCrabGameWindowsFileToGameDir(string relativePath, string newrelativePath)
        {
            Console.WriteLine($"Copying Windows file {relativePath} to {newrelativePath}");
            string sourceFile = Path.Combine(CrabGameWinPath, relativePath);
            string destFile = Path.Combine(OutputPath, newrelativePath);

            string? dir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.Copy(sourceFile, destFile, overwrite: true);
        }
        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}
