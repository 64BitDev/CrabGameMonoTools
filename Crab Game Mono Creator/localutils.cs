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
        public static void FixStringMethodRefsInIl(
    AssemblyDefinition asm,
    JsonDocument crabgamemap)
        {
            // Build a GLOBAL lookup: MacName -> FixedDeopName
            var globalMethodMap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var asmEntry in crabgamemap.RootElement.EnumerateObject())
            {
                foreach (var nsEntry in asmEntry.Value.EnumerateObject())
                {
                    foreach (var typeEntry in nsEntry.Value.EnumerateObject())
                    {
                        if (!typeEntry.Value.TryGetProperty("MethodMaps", out var methodMaps))
                            continue;

                        foreach (var m in methodMaps.EnumerateObject())
                        {
                            var obj = m.Value;

                            if (!obj.TryGetProperty("Mac", out var mac))
                                continue;

                            if (!obj.TryGetProperty(Program.MapToName, out var fixedDeop))
                                continue;

                            var macName = mac.GetString();
                            var newName = fixedDeop.GetString();

                            if (string.IsNullOrEmpty(macName) || string.IsNullOrEmpty(newName))
                                continue;

                            // Allow collisions — last one wins (you said this is OK)
                            globalMethodMap[macName] = newName;
                        }
                    }
                }
            }

            // Walk IL and patch ldstr
            foreach (var type in AsmUtils.GetAllTypeDefinitions(asm.MainModule))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var il = method.Body.Instructions;
                    for (int i = 0; i < il.Count; i++)
                    {
                        var ins = il[i];

                        if (ins.OpCode != Mono.Cecil.Cil.OpCodes.Ldstr)
                            continue;

                        if (ins.Operand is not string s)
                            continue;

                        if (globalMethodMap.TryGetValue(s, out var newName))
                        {
                            ins.Operand = newName;
                        }
                    }
                }
            }
        }
        public static void FixMethodRefsInIl(
    AssemblyDefinition asm,
    JsonDocument crabgamemap)
        {
            // Cache: typeKey -> (macMethodName -> mappedName)
            var methodMapCache = new Dictionary<string, Dictionary<string, string>>();

            foreach (var type in AsmUtils.GetAllTypeDefinitions(asm.MainModule))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var il = method.Body.Instructions;

                    for (int i = 0; i < il.Count; i++)
                    {
                        if (il[i].Operand is not MethodReference mr)
                            continue;

                        // unwrap GenericInstanceMethod
                        if (mr is GenericInstanceMethod gim)
                            mr = gim.ElementMethod;

                        // never rename constructors
                        if (mr.Name == ".ctor" || mr.Name == ".cctor")
                            continue;

                        // build or fetch rename table for declaring type
                        if (!TryBuildMethodRenameTable(
                                mr.DeclaringType,
                                crabgamemap,
                                methodMapCache,
                                out var map))
                            continue;

                        // rename ONLY if explicitly mapped
                        if (map.TryGetValue(mr.Name, out var newName))
                        {
                            mr.Name = newName;
                        }
                    }
                }
            }
        }

        static bool TryBuildMethodRenameTable(
    TypeReference declaringType,
    JsonDocument crabgamemap,
    Dictionary<string, Dictionary<string, string>> cache,
    out Dictionary<string, string> map)
        {
            map = null!;

            // unwrap generic instance types
            if (declaringType is GenericInstanceType git)
                declaringType = git.ElementType;

            JsonProperty? classType = null;

            foreach (var asm in crabgamemap.RootElement.EnumerateObject())
            {
                if (LocalUtils.TryGetTypeObject(
                        declaringType,
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
                return false;

            // stable cache key
            string cacheKey = classType.Value.Name + ":" + declaringType.FullName;

            if (cache.TryGetValue(cacheKey, out map))
                return true;

            if (!classType.Value.Value.TryGetProperty("MethodMaps", out var methodMaps))
                return false;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in methodMaps.EnumerateObject())
            {
                var obj = entry.Value;

                if (!obj.TryGetProperty("Mac", out var macEl))
                    continue;

                if (!obj.TryGetProperty(Program.MapToName, out var targetEl))
                    continue;

                string? macName = macEl.GetString();
                string? targetName = targetEl.GetString();

                if (string.IsNullOrEmpty(macName) || string.IsNullOrEmpty(targetName))
                    continue;

                // allow overloads: same name is OK
                if (!dict.ContainsKey(macName))
                    dict.Add(macName, targetName);
            }

            cache[cacheKey] = dict;
            map = dict;
            return true;
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
