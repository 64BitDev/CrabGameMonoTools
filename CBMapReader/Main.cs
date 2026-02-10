using System.Buffers;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CBMapReader
{

    public static class ReadCBMap
    {
        public static bool ReadCompressedJECGMV2(string JECGMFilePath, out CBMap map)
        {
            return ReadCompressedJECGMV2Text(File.ReadAllText(JECGMFilePath), out map);
        }

        public static bool ReadCompressedJECGMV2Text(string JECGMFileText,out CBMap map)
        {
            map = new CBMap();
            try
            {
                JsonDocument doc = JsonDocument.Parse(JECGMFileText);
                foreach(var DllMap in doc.RootElement.EnumerateObject())
                {
                    CBMapDll? DllMapClass = new CBMapDll();
                    map.DllMaps.Add(DllMap.Name, DllMapClass);

                    foreach (var NamespaceMap in DllMap.Value.EnumerateObject())
                    {
                        CBMapNamespace mapNamespace = new CBMapNamespace();
                        DllMapClass.Namespaces.Add(NamespaceMap.Name, mapNamespace);
                        foreach (var ClassMap in NamespaceMap.Value.EnumerateObject())
                        {
                            CBMapClass mapClass = new CBMapClass();
                            mapNamespace.Classes.Add(ClassMap.Name, mapClass);
                            var ObjectMap = ClassMap.Value.GetProperty("ObjectMaps");
                            foreach (var Objects in ObjectMap.EnumerateObject())
                            {
                                mapClass.ObjectMaps.Add(Objects.Name,Objects.Value.GetString()!);
                            }
                            var MethodMaps = ClassMap.Value.GetProperty("MethodMaps");
                            foreach (var Methods in MethodMaps.EnumerateObject())
                            {
                                CBMapMethod mapMethod = new CBMapMethod();
                                mapClass.Methods.Add(Methods.Name, mapMethod);
                                foreach (var MethodsMap in Methods.Value.EnumerateObject())
                                {
                                    mapMethod.ObjectMaps.Add(MethodsMap.Name, MethodsMap.Value.GetString()!);
                                }
                            }

                            var VarMaps = ClassMap.Value.GetProperty("FieldMaps");
                            foreach (var Var in VarMaps.EnumerateObject())
                            {
                                CBMapVar mapVar = new CBMapVar();
                                mapClass.Vars.Add(Var.Name, mapVar);
                                foreach (var VarMap in Var.Value.EnumerateObject())
                                {
                                    mapVar.ObjectMaps.Add(VarMap.Name, VarMap.Value.GetString()!);
                                }
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw;
                map.exceptionError = ex;
                return false;
            }
        }
    }



    public class CBMap
    {
        public Dictionary<string, CBMapDll> DllMaps = new();
        public Dictionary<string, object> Settings = new();
        public Exception? exceptionError; /// is used when a reader is supost to error out so you can print the error
    }


    public class CBMapDll
    {
        public Dictionary<string, CBMapNamespace> Namespaces = new();
        public Dictionary<string, object> Settings = new();
        internal Dictionary<string,Dictionary<string,CBMapClass>> MapKey = new();
        Dictionary<string, CBMapClass> GetLocalClassMap(bool AddToLookupTabe,string MapFromName)
        {
            
            if(MapKey.TryGetValue(MapFromName, out var TrueMap))
            {
                return TrueMap;
            }
            //create table
            TrueMap = new();

            foreach (var ns in Namespaces.Values)
            {
                foreach(var cls in ns.Classes)
                {
                    if (cls.Value.ObjectMaps.TryGetValue(MapFromName,out var MapKey))
                    {
                        TrueMap.Add(MapKey, cls.Value);
                    }
                }
            }

            return TrueMap;
        }
    }

    public class CBMapNamespace
    {
        public Dictionary<string, CBMapClass> Classes = new();
        public Dictionary<string, object> Settings = new();
    }

    public class CBMapClass
    {
        public Dictionary<string, string> ObjectMaps = new();
        public Dictionary<string, CBMapVar> Vars = new();
        public Dictionary<string, CBMapMethod> Methods = new();
        public Dictionary<string, object> Settings = new();
    }

    public class CBMapVar
    {
        public Dictionary<string, string> ObjectMaps = new();
        public Dictionary<string, object> Settings = new();
    }


    public class CBMapMethod
    {
        public Dictionary<string, string> ObjectMaps = new();
        public Dictionary<string, object> Settings = new();
    }



    public static class WriteCBMap
    {
        static void WriteCompressedJECGMV2(string FilePath,CBMap crabgamemap)
        {
            File.WriteAllText(FilePath, WriteCompressedJECGMV2Text(crabgamemap));
        }

        static string WriteCompressedJECGMV2Text(CBMap crabgamemap)
        {
            using MemoryStream fs = new MemoryStream();
            using Utf8JsonWriter writer = new Utf8JsonWriter(fs, new JsonWriterOptions
            {
                Indented = false
            });
            writer.WriteStartObject();
            foreach (var dll in crabgamemap.DllMaps)
            {
                writer.WritePropertyName(dll.Key);
                writer.WriteStartObject();
                foreach (var ns in dll.Value.Namespaces)
                {
                    writer.WritePropertyName(ns.Key);
                    writer.WriteStartObject();
                    foreach (var cls in ns.Value.Classes)
                    {
                        writer.WritePropertyName(cls.Key);
                        writer.WriteStartObject();

                        writer.WritePropertyName("ObjectMaps");
                        writer.WriteStartObject();
                        foreach (var objectMapName in cls.Value.ObjectMaps)
                        {
                            writer.WriteString(objectMapName.Key, objectMapName.Value);
                        }
                        writer.WriteEndObject();

                        writer.WritePropertyName("FieldMaps");
                        writer.WriteStartObject();
                        foreach (var Vars in cls.Value.Vars)
                        {
                            writer.WritePropertyName(Vars.Key);
                            writer.WriteStartObject();
                            foreach (var objectMapName in Vars.Value.ObjectMaps)
                            {
                                writer.WriteString(objectMapName.Key, objectMapName.Value);
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();


                        writer.WritePropertyName("MethodMaps");
                        writer.WriteStartObject();
                        foreach (var Methods in cls.Value.Methods)
                        {
                            writer.WritePropertyName(Methods.Key);
                            writer.WriteStartObject();
                            foreach (var objectMapName in Methods.Value.ObjectMaps)
                            {
                                writer.WriteString(objectMapName.Key, objectMapName.Value);
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();


                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            writer.Flush();
            return Encoding.UTF8.GetString(fs.ToArray());
        }
    }
}
