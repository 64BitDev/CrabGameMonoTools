using Mono.Cecil;
using System.Text;
using System.Text.Json;

namespace Crab_Game_Mono_Creator
{
    public static class LocalUtils
    {
        public static void FixExternalRefs(TypeDefinition t, JsonDocument crabgamemap, AssemblyDefinition macAsm)
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
                FixExternalRefs(n, crabgamemap, macAsm);
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
        static bool TryResolveMappedFieldName(
            JsonProperty classObject,
            string originalFieldName, // optional now, kept for compatibility
            string abiName,
            out string resolvedName)
        {
            if (classObject.Value.TryGetProperty("FieldMaps", out var fieldMaps))
            {
                foreach (var mappedField in fieldMaps.EnumerateObject())
                {

                    if (!mappedField.Value.TryGetProperty("AbiName", out var abiNameProp))
                        continue;

                    if (abiNameProp.GetString() != abiName)
                        continue;

                    if (mappedField.Value.TryGetProperty(Program.MapToName, out var mappedType))
                    {
                        resolvedName = mappedType.GetString()!;
                        return true;
                    }

                    // fallback: use the map key itself
                    resolvedName = mappedField.Name;
                    return true;
                }
            }

            resolvedName = string.Empty;
            return false;
        }
        /// <summary>
        /// For now we are not renaming vars normally; just use wrappers.
        /// </summary>
        static public void AddProxyPropertiesForType(TypeDefinition type, JsonProperty ClassObject)
        {
            var module = type.Module;

            // Local: make FieldType safe for identifier usage (fixes Client[] -> ClientArray, etc.)
            string NormalizeTypeName(TypeReference tr)
            {
                // unwrap arrays
                var arr = tr as ArrayType;
                if (arr != null)
                    return NormalizeTypeName(arr.ElementType) + "Array";

                // unwrap byref
                var byRef = tr as ByReferenceType;
                if (byRef != null)
                    return NormalizeTypeName(byRef.ElementType);

                // unwrap pointer
                var ptr = tr as PointerType;
                if (ptr != null)
                    return NormalizeTypeName(ptr.ElementType) + "Ptr";

                // base name (strip generic arity)
                string name = tr.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);

                // absolute safety: replace illegal identifier chars
                // (covers cases like [] & * < > + . , space, etc.)
                var sb = new StringBuilder(name.Length);
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if ((c >= 'a' && c <= 'z') ||
                        (c >= 'A' && c <= 'Z') ||
                        (c >= '0' && c <= '9') ||
                        c == '_')
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append('_');
                    }
                }
                return sb.ToString();
            }

            // -------------------------------
            // Build IL2CPP-style field names
            // -------------------------------
            var fieldNameMap = new Dictionary<FieldDefinition, string>();
            var groups = new Dictionary<string, List<FieldDefinition>>();

            foreach (var field in type.Fields)
            {
                if (field.IsStatic || field.IsLiteral)
                    continue;

                if (field.Name.Contains("BackingField"))
                    continue;

                string visibility =
                    field.IsPublic ? "Public" :
                    field.IsFamily ? "Protected" :
                    "Private";

                // group by signature (visibility + full field type)
                string key = visibility + "|" + field.FieldType.FullName;

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<FieldDefinition>();
                    groups[key] = list;
                }

                list.Add(field);
            }

            foreach (var kv in groups)
            {
                var list = kv.Value;

                // stable ordering
                list.Sort((a, b) => a.MetadataToken.RID.CompareTo(b.MetadataToken.RID));

                for (int i = 0; i < list.Count; i++)
                {
                    var field = list[i];

                    string visibility =
                        field.IsPublic ? "Public" :
                        field.IsFamily ? "Protected" :
                        "Private";

                    string typeNameSafe = NormalizeTypeName(field.FieldType);

                    string il2cppName =
                        "field_" +
                        visibility + "_" +
                        typeNameSafe + "_" +
                        i;

                    // mapped-name override (your resolver decides)
                    if (TryResolveMappedFieldName(
                            ClassObject,
                            field.Name,
                            il2cppName,
                            out string mappedName))
                    {
                        fieldNameMap[field] = mappedName;
                    }
                    else
                    {
                        fieldNameMap[field] = il2cppName;
                    }
                }
            }

            // -------------------------------
            // Emit proxy properties
            // -------------------------------
            foreach (var field in type.Fields)
            {
                if (!fieldNameMap.TryGetValue(field, out string propName))
                    continue;

                // ---- GET METHOD ----
                var getMethod = new MethodDefinition(
                    "get_" + propName,
                    MethodAttributes.Public |
                    MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName,
                    field.FieldType);

                var getIL = getMethod.Body.GetILProcessor();
                getIL.Append(getIL.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                getIL.Append(getIL.Create(Mono.Cecil.Cil.OpCodes.Ldfld, field));
                getIL.Append(getIL.Create(Mono.Cecil.Cil.OpCodes.Ret));

                // ---- SET METHOD ----
                var setMethod = new MethodDefinition(
                    "set_" + propName,
                    MethodAttributes.Public |
                    MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName,
                    module.TypeSystem.Void);

                setMethod.Parameters.Add(new ParameterDefinition(field.FieldType));

                var setIL = setMethod.Body.GetILProcessor();
                setIL.Append(setIL.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                setIL.Append(setIL.Create(Mono.Cecil.Cil.OpCodes.Ldarg_1));
                setIL.Append(setIL.Create(Mono.Cecil.Cil.OpCodes.Stfld, field));
                setIL.Append(setIL.Create(Mono.Cecil.Cil.OpCodes.Ret));

                // PROPERTY
                var prop = new PropertyDefinition(
                    propName,
                    PropertyAttributes.None,
                    field.FieldType)
                {
                    GetMethod = getMethod,
                    SetMethod = setMethod
                };

                type.Methods.Add(getMethod);
                type.Methods.Add(setMethod);
                type.Properties.Add(prop);
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
    }
}
