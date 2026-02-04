using Mono.Cecil;
using System.Text.Json;
using Mono.Cecil.Cil;
namespace Crab_Game_Mono_Creator
{
    internal class Program
    {
        static string CrabGameMacPath;
        static string CrabGameWinPath;
        static string OutputPath;
        const string MonoFilesDir = "MonoFiles";
        public static string MapToName = "FixedDeop";
        const string DefaultCGMapDownloadLoc = "https://raw.githubusercontent.com/64BitDev/CrabGameMappings/refs/heads/builds/CrabGameMappings_V2Compatable.jecgm";
        static bool IsVanilla = false;
        static void Main(string[] args)
        {
            Console.WriteLine("=== Crab Game Mono Creator ===");
            Console.WriteLine("Created by 64bitdev");
            ConsoleUtils.SetupConsoleUtils();
            JsonDocument crabgamemap = null;
            if(args.Contains("--vanilla"))
            {
                IsVanilla = true;
            }
            
                //see if we have cgmonomap.jecgm
                if (!File.Exists("cgmonomap.jecgm"))
                {
                    int TypeOfDownload = ConsoleUtils.SelectOptionFromArray("We could not find cgmonomap.jecgm please provide a replacment using one of the following:", "automaticlydownloadmap", "Download from 64BitDev/CrabGameMappings ", "Use Local File");
                    switch (TypeOfDownload)
                    {
                        case 1:
                            {
                                Console.WriteLine("Downloading from " + DefaultCGMapDownloadLoc);
                                using var http = new HttpClient();
                                http.DefaultRequestHeaders.UserAgent.ParseAdd("CGMapClient");

                                using var response = http
                                    .GetAsync(DefaultCGMapDownloadLoc, HttpCompletionOption.ResponseHeadersRead)
                                    .GetAwaiter().GetResult();

                                response.EnsureSuccessStatusCode();

                                using var stream = response.Content
                                    .ReadAsStreamAsync()
                                    .GetAwaiter().GetResult();

                                using var file = File.Create("cgmonomap.jecgm");
                                stream.CopyTo(file);
                                file.Close();
                                crabgamemap = JsonDocument.Parse(File.ReadAllText("cgmonomap.jecgm"));
                                break;
                            }
                        case 2:
                            crabgamemap = JsonDocument.Parse(File.ReadAllText(ConsoleUtils.GetSafeStringFromConsole("cgmonomap dir:", "CrabGameMonoMapPath")));
                            break;
                    }
                }
                else
                {
                    crabgamemap = JsonDocument.Parse(File.ReadAllText("cgmonomap.jecgm"));
                }

            CrabGameMacPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mac Directory:", "CrabGameMacPath");
            if (Directory.Exists(Path.Combine(CrabGameMacPath, "Crab Game.app")))
            {
                CrabGameMacPath = Path.Combine(CrabGameMacPath, "Crab Game.app");
            }
            CrabGameWinPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Win Directory:", "CrabGameWinPath");
            OutputPath = ConsoleUtils.GetSafeStringFromConsole("Crab Game Mono Output Directory:", "OutputPath");

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
            Console.WriteLine($"Patching Managed Asm");
            Directory.CreateDirectory(Path.Combine(OutputPath, "Crab Game_Data", "Managed"));
            //get all of the dlls that this maps
            foreach (var file in Directory.GetFiles(CrabGameMacManagedDir))
            {
#if RELEASE
                try
                
                {
#endif
                RewriteAsmWithMap(file, crabgamemap);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{Path.GetFileName(file)} success");
                Console.ForegroundColor = ConsoleColor.White;
#if RELEASE
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{Path.GetFileName(file)} failed with error");
                    Console.WriteLine(e.ToString());
                    Console.ForegroundColor = ConsoleColor.White;
                }
#endif

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
                    foreach (var cls in ns.Value.EnumerateObject())
                    {
                        JsonElement objectMap = cls.Value.GetProperty("ObjectMaps");
                        if (objectMap.TryGetProperty(MapToName, out var MapNameProperty))
                        {
                            byte[] find = StringToUnityString(objectMap.GetProperty("Windows").GetString()!);
                            byte[] replace = StringToUnityString(MapNameProperty.GetString()!, find.Length);

                            patterns.Add(new ReplacePattern
                            {
                                Find = find,
                                Replace = replace
                            });
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

        static byte[] StringToUnityString(string value, int leng)
        {
            if (leng <= 0)
                throw new ArgumentOutOfRangeException(nameof(leng));

            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            byte[] result = new byte[leng];

            int copyLen = Math.Min(utf8.Length, leng - 1); // leave room for null
            Buffer.BlockCopy(utf8, 0, result, 0, copyLen);

            result[copyLen] = 0x00; // null terminator (always valid)

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
        static bool TryGetBool(JsonProperty JsonPro,string Name)
        {
            if(!JsonPro.Value.TryGetProperty(Name,out var BoolVal))
            {
                return false;
            }
            return BoolVal.GetBoolean();
        }
        static void RewriteAsmWithMap(string file, JsonDocument crabgamemap)
        {
            
            var filename = Path.GetFileName(file);
            Console.WriteLine($"Started Patching {filename}");
            if (filename.StartsWith("Unity") || filename.StartsWith("System") || filename.StartsWith("mscorlib") || filename.StartsWith("Mono"))
            {
                File.Copy(file, Path.Combine(OutputPath, "Crab Game_Data", "Managed", Path.GetFileName(file)), true);
                Console.WriteLine($"Skiped file {filename} because it was a unity file");
                return;
            }
            AssemblyDefinition macAsm = AssemblyDefinition.ReadAssembly(file);
            LocalUtils.FixFieldRefsInIl(macAsm,crabgamemap);
            LocalUtils.FixMethodRefsInIl(macAsm, crabgamemap);
            var TypesInMacMainModule = AsmUtils.GetAllTypeDefinitions(macAsm.MainModule);
            foreach (var type in TypesInMacMainModule)
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
            foreach (var t in TypesInMacMainModule)
            {
                if(LocalUtils.TryGetTypeObject(t,macAsm.Name.Name,MapToName,crabgamemap,out var mod))
                {
                    RewriteFields(t, mod);
                    RewriteMethods(t, mod,crabgamemap);
                }
                
                LocalUtils.FixTypeRefs(t, crabgamemap, macAsm);
            }
            if(!IsVanilla)
            {
                foreach (var t in TypesInMacMainModule)
                {
                    AddPublicFieldWrappers(t);
                }
            }
            macAsm.Write(Path.Combine(OutputPath, "Crab Game_Data", "Managed", Path.GetFileName(file)));
        }
        public static void AddPublicFieldWrappers(TypeDefinition type)
        {
            var module = type.Module;

            // avoid duplicate properties
            HashSet<string> existingProps = new HashSet<string>(
                type.Properties.Select(p => p.Name),
                StringComparer.Ordinal);

            foreach (var field in type.Fields)
            {
                // skip already-public fields
                if (field.IsPublic)
                    continue;

                string propName = field.Name + "_W"; // change suffix if you want

                if (existingProps.Contains(propName))
                    continue;

                // -----------------------------
                // GETTER
                // -----------------------------
                var getter = new MethodDefinition(
                    "get_" + propName,
                    MethodAttributes.Public |
                    MethodAttributes.SpecialName |
                    MethodAttributes.HideBySig,
                    field.FieldType
                );

                getter.Body = new MethodBody(getter);
                var ilGet = getter.Body.GetILProcessor();

                if (!field.IsStatic)
                    ilGet.Append(ilGet.Create(OpCodes.Ldarg_0));

                ilGet.Append(ilGet.Create(
                    field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld,
                    field));

                ilGet.Append(ilGet.Create(OpCodes.Ret));

                type.Methods.Add(getter);

                // -----------------------------
                // SETTER (skip readonly)
                // -----------------------------
                MethodDefinition? setter = null;

                if (!field.IsInitOnly)
                {
                    setter = new MethodDefinition(
                        "set_" + propName,
                        MethodAttributes.Public |
                        MethodAttributes.SpecialName |
                        MethodAttributes.HideBySig,
                        module.TypeSystem.Void
                    );

                    setter.Parameters.Add(
                        new ParameterDefinition("value",
                            ParameterAttributes.None,
                            field.FieldType));

                    setter.Body = new MethodBody(setter);
                    var ilSet = setter.Body.GetILProcessor();

                    if (!field.IsStatic)
                        ilSet.Append(ilSet.Create(OpCodes.Ldarg_0));

                    ilSet.Append(ilSet.Create(OpCodes.Ldarg_1));

                    ilSet.Append(ilSet.Create(
                        field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld,
                        field));

                    ilSet.Append(ilSet.Create(OpCodes.Ret));

                    type.Methods.Add(setter);
                }

                // -----------------------------
                // PROPERTY METADATA
                // -----------------------------
                var prop = new PropertyDefinition(
                    propName,
                    PropertyAttributes.None,
                    field.FieldType);

                prop.GetMethod = getter;
                if (setter != null)
                    prop.SetMethod = setter;

                type.Properties.Add(prop);
                existingProps.Add(propName);
            }
        }
        public static void RewriteFields(TypeDefinition type, JsonProperty mappedtype)
        {
            if(!mappedtype.Value.TryGetProperty("FieldMaps",out var FieldTable))
            {
                return;
            }
            Dictionary<string, string> mapField = new();
            foreach(var MappedField in FieldTable.EnumerateObject())
            {
                mapField.Add(MappedField.Value.GetProperty("Mac").GetString()!, MappedField.Value.GetProperty(MapToName).GetString()!);
            }           
            foreach (var Field in type.Fields)
            {
               if(mapField.TryGetValue(Field.Name,out var outname))
                {
                    
                    Field.Name  = outname;
                }
            }

            foreach (var Property in type.Properties)
            {
                if (mapField.TryGetValue(Property.Name, out var outname))
                {

                    Property.Name = outname;
                }
            }

        }

        public struct MethodMapInfo
        {
            public MethodMapInfo(string newMethodName, string?[] methodNameTable)
            {
                NewMethodName = newMethodName;
                MethodNameTable = methodNameTable;
            }

            public string NewMethodName { get; }
            public string?[] MethodNameTable { get; }
        }

        public static void RewriteMethods(
            TypeDefinition type,
            JsonProperty mappedtype,
            JsonDocument crabgamemap)
        {
            if (!mappedtype.Value.TryGetProperty("MethodMaps", out var MethodMaps))
                return;

            // Build rename table once
            Dictionary<string, MethodMapInfo> mapMethod = new();
            foreach (var m in MethodMaps.EnumerateObject())
            {
                
                string?[] MethodNameMap = null;
                if(m.Value.TryGetProperty("VarMap",out var aaaa))
                {
                    MethodNameMap = aaaa.EnumerateArray().Select(e => e.GetString()).ToArray();
                    Console.WriteLine("aaaaaaa");
                }
                
                MethodMapInfo methodMap = new(m.Value.GetProperty(MapToName).GetString()!,MethodNameMap);

                mapMethod[m.Value.GetProperty("Mac").GetString()!] = methodMap;
                    ;
            }

            var module = type.Module;

            foreach (var method in type.Methods.ToArray())
            {
                if (method.Parameters.Count > 0 && !IsVanilla)
                {
                    if (!StringUtils.UsesOnlyAscii(method.Parameters[0].Name))
                    {
                        byte argcount = 0;
                        foreach (var param in method.Parameters)
                        {
                            param.Name = $"arg{argcount}";
                            argcount++;
                        }
                    }
                }
                if(!IsVanilla)
                {
                    method.Attributes =
    (method.Attributes & ~MethodAttributes.MemberAccessMask)
    | MethodAttributes.Public;
                }
                // Cannot generate bodies for these
                if (method.IsAbstract || method.IsPInvokeImpl)
                    continue;

                // CRITICAL: never wrap open generics (AOT / gshared crash)
                if (method.HasGenericParameters ||
                    method.DeclaringType.HasGenericParameters)
                    continue;

                // Must be explicitly mapped
                if (!mapMethod.TryGetValue(method.Name, out var newName))
                    continue;

                if (newName.MethodNameTable is not null)
                {
                    int count = Math.Min(method.Parameters.Count, newName.MethodNameTable.Length);

                    for (int i = 0; i < count; i++)
                    {
                        method.Parameters[i].Name = newName.MethodNameTable[i];
                    }
                }
                // Already renamed -> do nothing
                if (method.Name == newName.NewMethodName)
                    continue;

                string oldName = method.Name;

                // Rename original method
                method.Name = newName.NewMethodName;
   
                // Create alias wrapper under old name
                var wrapper = new MethodDefinition(
                    oldName,
                    method.Attributes & ~(
                        MethodAttributes.Abstract |
                        MethodAttributes.PInvokeImpl
                    ),
                    method.ReturnType
                );

                // Copy parameters
                foreach (var p in method.Parameters)
                    wrapper.Parameters.Add(
                        new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

                // Force body
                wrapper.Body = new MethodBody(wrapper);
                wrapper.Body.InitLocals = true;

                var il = wrapper.Body.GetILProcessor();

                int argIndex = 0;

                // Load `this` for instance methods
                if (!method.IsStatic)
                {
                    il.Append(il.Create(OpCodes.Ldarg_0));
                    argIndex = 1;
                }

                // Load parameters
                for (int i = 0; i < wrapper.Parameters.Count; i++)
                {
                    il.Append(il.Create(OpCodes.Ldarg, argIndex));
                    argIndex++;
                }

                // IMPORTANT: always use CALL (avoid virtual recursion)
                il.Append(il.Create(
                    OpCodes.Call,
                    module.ImportReference(method)
                ));

                il.Append(il.Create(OpCodes.Ret));

                type.Methods.Add(wrapper);
            }
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
