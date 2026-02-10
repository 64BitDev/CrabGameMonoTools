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

    public class WriteCBMap

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

}
