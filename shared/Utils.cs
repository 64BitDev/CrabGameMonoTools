using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
public static class ConsoleUtils
{
    static JsonDocument? jsonSettings;
    public static void SetupConsoleUtils()
    {
        if(File.Exists("JsonSettings.json"))
        {
            jsonSettings = JsonDocument.Parse(File.ReadAllText("JsonSettings.json"));
        }
    }
    public static int GetSafeIntFromConsole(string Name,string guid)
    {
        if(jsonSettings is not null)
        {
            if(jsonSettings.RootElement.TryGetProperty(guid, out var jsonvalue))
            {
                return jsonvalue.GetInt32();
            }
        }

        string? Input = string.Empty;
        while (true)
        {
            Console.Write(Name);
            Input = Console.ReadLine();
            if (int.TryParse(Input, out int value))
            {
                return value;
            }
        }

    }

    public static string GetSafeStringFromConsole(string Name, string guid)
    {
        if (jsonSettings is not null)
        {
            if (jsonSettings.RootElement.TryGetProperty(guid, out var jsonvalue))
            {
                return jsonvalue.GetString()!;
            }
        }
        string? Input = string.Empty;
        while (true)
        {
            Console.Write(Name);
            Input = Console.ReadLine();
            if (Input is not null)
            {
                return Input;
            }
        }
    }

    public static int SelectOptionFromArray(string Name,string guid, params string[] Options)
    {
        Console.WriteLine($"Select one of the following");
        for (int i = 0; i < Options.Length; i++)
        {
            Console.WriteLine($"{i + 1}. {Options[i]}");
        }
        return GetSafeIntFromConsole(Name,guid);

    }



}

public static class AsmUtils
{
    public static IEnumerable<TypeDefinition> GetAllTypeDefinitions(ModuleDefinition module)
    {
        foreach (var t in module.Types)
        {
            foreach (var tt in WalkType(t))
                yield return tt;
        }

        static IEnumerable<TypeDefinition> WalkType(TypeDefinition t)
        {
            yield return t;

            foreach (var n in t.NestedTypes)
            {
                foreach (var nn in WalkType(n))
                    yield return nn;
            }
        }
    }
}
