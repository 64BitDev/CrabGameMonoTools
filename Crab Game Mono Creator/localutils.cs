using Mono.Cecil;
using System.Text;
using System.Text.Json;

namespace Crab_Game_Mono_Creator
{
    public static class LocalUtils
    {
        public static void FixTypeRefs(TypeDefinition t, JsonDocument crabgamemap, AssemblyDefinition macAsm)
        {
            FixTypeRef(t.BaseType, crabgamemap);

            foreach (var i in t.Interfaces)
                FixTypeRef(i.InterfaceType, crabgamemap);

            foreach (var f in t.Fields)
                FixTypeRef(f.FieldType, crabgamemap);

            foreach (var m in t.Methods)
            {
                FixTypeRef(m.ReturnType, crabgamemap);

                foreach (var p in m.Parameters)
                    FixTypeRef(p.ParameterType, crabgamemap);

                if (!m.HasBody)
                    continue;

                foreach (var v in m.Body.Variables)
                    FixTypeRef(v.VariableType, crabgamemap);

                foreach (var ins in m.Body.Instructions)
                {
                    switch (ins.Operand)
                    {
                        case TypeReference tr:
                            FixTypeRef(tr, crabgamemap);
                            break;

                        case MethodReference mr:
                            FixTypeRef(mr.DeclaringType, crabgamemap);
                            FixTypeRef(mr.ReturnType, crabgamemap);
                            foreach (var p in mr.Parameters)
                                FixTypeRef(p.ParameterType, crabgamemap);
                            break;

                        case FieldReference fr:
                            //try to change fieldRef
                            FixFieldRef(fr, crabgamemap);
                            FixTypeRef(fr.DeclaringType, crabgamemap);
                            FixTypeRef(fr.FieldType, crabgamemap);
                            
                            break;
                    }
                }
            }

            foreach (var n in t.NestedTypes)
                FixTypeRefs(n, crabgamemap, macAsm);
            foreach (var type in AsmUtils.GetAllTypeDefinitions(macAsm.MainModule))
            {
                foreach (var ca in type.CustomAttributes)
                {
                    FixTypeRef(ca.AttributeType, crabgamemap);

                    // ALSO fix constructor reference
                    if (ca.Constructor != null)
                    {
                        FixTypeRef(ca.Constructor.DeclaringType, crabgamemap);
                    }

                    // Fix attribute argument types
                    foreach (var arg in ca.ConstructorArguments)
                    {
                        if (arg.Value is TypeReference tr)
                            FixTypeRef(tr, crabgamemap);
                    }

                    foreach (var named in ca.Fields)
                    {
                        if (named.Argument.Value is TypeReference tr)
                            FixTypeRef(tr, crabgamemap);
                    }

                    foreach (var named in ca.Properties)
                    {
                        if (named.Argument.Value is TypeReference tr)
                            FixTypeRef(tr, crabgamemap);
                    }
                }
            }
        }
        static public void FixFieldRefsInIl(AssemblyDefinition asm, JsonDocument crabgamemap)
        {
            // Cache: (asmName + ":" + deopFullTypeKey) -> (macFieldName -> newFieldName)
            // You can replace the key with whatever your TryGetTypeObject is using internally.
            var fieldMapCache = new Dictionary<string, Dictionary<string, string>>();

            foreach (var t in AsmUtils.GetAllTypeDefinitions(asm.MainModule))
            {
                foreach (var m in t.Methods)
                {
                    if (!m.HasBody)
                        continue;

                    var il = m.Body.Instructions;
                    for (int i = 0; i < il.Count; i++)
                    {
                        var ins = il[i];
                        if (!(ins.Operand is FieldReference fr))
                            continue;

                        // Build/get mapping table for fr.DeclaringType
                        if (!TryBuildFieldRenameTable(fr.DeclaringType, crabgamemap, fieldMapCache, out var map))
                            continue;

                        // Mac DLL -> deopmacdll: operand name is expected to be Mac name
                        if (map.TryGetValue(fr.Name, out var newName))
                        {
                            fr.Name = newName;
                            continue;
                        }

                        // Fallback: if something already got partially renamed to FixedDeop,
                        // reverse lookup (slow, but you said that's ok for now).
                        // This lets you survive mixed states while debugging.
                        foreach (var kv in map)
                        {
                            if (kv.Value == fr.Name)
                            {
                                fr.Name = kv.Value; // already correct, keep it
                                break;
                            }
                        }
                    }
                }
            }
        }

        static bool TryBuildFieldRenameTable(
            TypeReference declaringType,
            JsonDocument crabgamemap,
            Dictionary<string, Dictionary<string, string>> cache,
            out Dictionary<string, string> map)
        {
            map = null!;

            // Some operands will be GenericInstanceType etc.
            // We want the underlying element type for lookups.
            if (declaringType is GenericInstanceType git)
                declaringType = git.ElementType;

            JsonProperty? classType = null;

            foreach (var asm in crabgamemap.RootElement.EnumerateObject())
            {
                if (TryGetTypeObject(declaringType, asm.Name, "Mac", crabgamemap, out var tType))
                {
                    classType = tType;
                    break;
                }
            }

            if (classType is null)
                return false;

            // Cache key - use something stable. If your map has a stable "FixedDeop full name", use that.
            // If not, this key still works fine during a single run.
            string cacheKey = classType.Value.Name + ":" + declaringType.FullName;

            if (cache.TryGetValue(cacheKey, out map))
                return true;

            if (!classType.Value.Value.TryGetProperty("FieldMaps", out var fieldMaps))
                return false;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in fieldMaps.EnumerateObject())
            {
                var fieldObj = entry.Value;

                // "Mac" -> Program.MapToName
                if (!fieldObj.TryGetProperty("Mac", out var macNameEl))
                    continue;

                string? macName = macNameEl.GetString();
                if (string.IsNullOrEmpty(macName))
                    continue;

                if (!fieldObj.TryGetProperty(Program.MapToName, out var targetNameEl))
                    continue;

                string? targetName = targetNameEl.GetString();
                if (string.IsNullOrEmpty(targetName))
                    continue;

                // macFieldName -> newFieldName
                if (!dict.ContainsKey(macName))
                    dict.Add(macName, targetName);
            }

            cache[cacheKey] = dict;
            map = dict;
            return true;
        }

        static void FixFieldRef(FieldReference fr, JsonDocument crabgamemap)
        {
            Console.WriteLine($"[FIELD REF] {fr.DeclaringType.FullName}::{fr.Name}");

            JsonProperty? classType = null;

            // 1. Resolve owning class
            foreach (var asm in crabgamemap.RootElement.EnumerateObject())
            {
                if (TryGetTypeObject(
                        fr.DeclaringType,
                        asm.Name,
                        "Mac",
                        crabgamemap,
                        out var tType))
                {
                    classType = tType;
                    break;
                }
            }

            if (classType is null)
                return;

            // 2. Reverse lookup by FixedDeop name
            if (!classType.Value.Value.TryGetProperty("FieldMaps", out var fieldMaps))
                return;

            foreach (var fieldEntry in fieldMaps.EnumerateObject())
            {
                // fieldEntry.Name == slot key (AAAAAAAB)
                var fieldObj = fieldEntry.Value;

                if (!fieldObj.TryGetProperty("FixedDeop", out var fixedName))
                    continue;

                if (fixedName.GetString() != fr.Name)
                    continue;

                // 3. Apply platform-specific rename
                if (fieldObj.TryGetProperty(Program.MapToName, out var platformName))
                {
                    fr.Name = platformName.GetString()!;
                }

                return;
            }
        }

        static void FixTypeRef(TypeReference tr, JsonDocument crabgamemap)
        {
            if (tr == null)
                return;

            // HANDLE GENERICS FIRST
            if (tr is GenericInstanceType git)
            {
                foreach (var arg in git.GenericArguments)
                    FixTypeRef(arg, crabgamemap);
            }

            // unwrap ref / array / pointer
            while (tr is TypeSpecification spec)
                tr = spec.ElementType;

            if (tr.Scope is not AssemblyNameReference aref)
                return;

            if (!crabgamemap.RootElement.TryGetProperty(aref.Name, out var asm))
                return;

            string ns = string.IsNullOrWhiteSpace(tr.Namespace) ? "Global" : tr.Namespace;
            if (!asm.TryGetProperty(ns, out var nsObj))
                return;

            foreach (var mapped in nsObj.EnumerateObject())
            {
                var objMaps = mapped.Value.GetProperty("ObjectMaps");
                string mac = objMaps.GetProperty("Mac").GetString()!;
                string win = objMaps.GetProperty(Program.MapToName).GetString()!;

                if (tr.Name == mac)
                {
                    tr.Name = win;
                    return;
                }
            }
        }

        public static bool TryGetTypeObject(TypeDefinition t,string AsmName,string TypeNameDef,JsonDocument crabgamemap,out JsonProperty typeProperty)
        {
            if (crabgamemap.RootElement.TryGetProperty(AsmName, out var asm))
            {
                string nsName = string.IsNullOrWhiteSpace(t.Namespace)
                    ? "Global"
                    : t.Namespace;

                if (asm.TryGetProperty(nsName, out var ns))
                {
                    foreach (var mappedType in ns.EnumerateObject())
                    {
                        if (!mappedType.Value.TryGetProperty("ObjectMaps", out var objectMaps))
                            continue;

                        if (!objectMaps.TryGetProperty(TypeNameDef, out var mappedName))
                            continue;

                        if (mappedName.GetString() == t.Name)
                        {
                            typeProperty = mappedType;
                            return true;
                        }
                    }
                }
            }

            typeProperty = default;
            return false;
        }

        public static bool TryGetTypeObject(TypeReference t, string AsmName, string TypeNameDef, JsonDocument crabgamemap, out JsonProperty typeProperty)
        {
            if (crabgamemap.RootElement.TryGetProperty(AsmName, out var asm))
            {
                string nsName = string.IsNullOrWhiteSpace(t.Namespace)
                    ? "Global"
                    : t.Namespace;

                if (asm.TryGetProperty(nsName, out var ns))
                {
                    foreach (var mappedType in ns.EnumerateObject())
                    {
                        if (!mappedType.Value.TryGetProperty("ObjectMaps", out var objectMaps))
                            continue;

                        if (!objectMaps.TryGetProperty(TypeNameDef, out var mappedName))
                            continue;

                        if (mappedName.GetString() == t.Name)
                        {
                            typeProperty = mappedType;
                            return true;
                        }
                    }
                }
            }

            typeProperty = default;
            return false;
        }
    }
}
