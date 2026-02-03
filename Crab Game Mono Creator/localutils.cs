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
        static public void FixFieldRefsInIl(
            AssemblyDefinition asm,
            JsonDocument crabgamemap)
        {
            var fieldMapCache = new Dictionary<string, Dictionary<string, string>>();
            var methodMapCache = new Dictionary<string, Dictionary<string, string>>();

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

                        // -----------------------------
                        // FIELD REFERENCES (map-only)
                        // -----------------------------
                        if (ins.Operand is FieldReference fr)
                        {
                            if (!TryBuildFieldRenameTable(
                                    fr.DeclaringType,
                                    crabgamemap,
                                    fieldMapCache,
                                    out var fmap))
                                continue;

                            if (fmap.TryGetValue(fr.Name, out var newName))
                            {
                                fr.Name = newName;
                                continue;
                            }

                            // mixed-state tolerance (kept from your original)
                            foreach (var kv in fmap)
                            {
                                if (kv.Value == fr.Name)
                                {
                                    fr.Name = kv.Value;
                                    break;
                                }
                            }

                            continue;
                        }

                        // -----------------------------
                        // PROPERTY ACCESSORS (map-only)
                        // Handles: get_X, set_X, IFoo.get_X, Ns.IFoo.set_X
                        // -----------------------------
                        if (ins.Operand is MethodReference mr0)
                        {
                            MethodReference mr = mr0;

                            // unwrap GenericInstanceMethod
                            if (mr is GenericInstanceMethod gim)
                                mr = gim.ElementMethod;

                            // never rename constructors
                            if (mr.Name == ".ctor" || mr.Name == ".cctor")
                                continue;

                            // Find accessor token anywhere in the name (explicit iface safe)
                            int getIdx = mr.Name.LastIndexOf("get_", StringComparison.Ordinal);
                            int setIdx = mr.Name.LastIndexOf("set_", StringComparison.Ordinal);

                            bool isGetter = getIdx >= 0;
                            bool isSetter = setIdx >= 0;

                            if (!isGetter && !isSetter)
                                continue;

                            // Choose which token we’re using (getter wins if both somehow appear)
                            int tokIdx;
                            string tok;
                            if (isGetter)
                            {
                                tokIdx = getIdx;
                                tok = "get_";
                            }
                            else
                            {
                                tokIdx = setIdx;
                                tok = "set_";
                            }

                            // Extract prefix and property name
                            // prefix: "" or "IFoo." or "Ns.IFoo."
                            string prefix = mr.Name.Substring(0, tokIdx);
                            string propName = mr.Name.Substring(tokIdx + 4);

                            // Build/get mapping table for this declaring type (map decides everything)
                            if (!TryBuildMethodRenameTable(
                                    mr.DeclaringType,
                                    crabgamemap,
                                    methodMapCache,
                                    out var mmap))
                                continue;

                            // Map keys are property names (not "get_"/"set_")
                            if (!mmap.TryGetValue(propName, out var newPropName))
                                continue;

                            mr.Name = prefix + tok + newPropName;
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
        public static void FixMethodRefsInIl(
            AssemblyDefinition asm,
            JsonDocument crabgamemap)
        {
            // Cache: typeKey -> (macMethodName -> mappedName)
            var methodMapCache = new Dictionary<string, Dictionary<string, string>>();

            // Cache: typeKey -> set of "methodName|paramCount" that exist on the resolved declaring type
            var methodExistsCache = new Dictionary<string, HashSet<string>>();

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
                        if (!map.TryGetValue(mr.Name, out var newName))
                            continue;

                        // typeKey used for both caches (keep it stable)
                        string typeKey = mr.DeclaringType.Scope.Name + "|" + mr.DeclaringType.FullName;

                        // build or fetch "exists" table for declaring type
                        if (!methodExistsCache.TryGetValue(typeKey, out var existsSet))
                        {
                            existsSet = new HashSet<string>(StringComparer.Ordinal);

                            TypeDefinition? td = null;
                            try { td = mr.DeclaringType.Resolve(); } catch { td = null; }

                            if (td != null)
                            {
                                foreach (var dm in td.Methods)
                                {
                                    existsSet.Add(dm.Name + "|" + dm.Parameters.Count);
                                }
                            }

                            methodExistsCache[typeKey] = existsSet;
                        }

                        // O(1) existence check
                        string sigKey = newName + "|" + mr.Parameters.Count;
                        if (!existsSet.Contains(sigKey))
                            continue;

                        // rename call target
                        mr.Name = newName;
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
