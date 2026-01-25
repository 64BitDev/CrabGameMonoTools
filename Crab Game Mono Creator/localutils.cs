using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crab_Game_Mono_Creator
{
    public static class LocalUtils
    {
        public static void FixExternalRefs(TypeDefinition t, JsonDocument crabgamemap,AssemblyDefinition macAsm)
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


        static void FixTypeRef(TypeReference tr,JsonDocument crabgamemap)
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
                string win = objMaps.GetProperty("Windows").GetString()!;

                if (tr.Name == mac)
                {
                    tr.Name = win;
                    return;
                }
            }
        }
    }
}
