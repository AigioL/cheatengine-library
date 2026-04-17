using System;
using System.Collections.Generic;
using System.Linq;

namespace CheatEngine.Library;

public delegate int ConversionRoutine(nint data);

public delegate void ReverseConversionRoutine(int value, nint output);

public enum CustomTypeType
{
    AutoAssembler,
    LuaScript,
    Plugin,
}

public sealed class CustomType
{
    private string _name = string.Empty;
    private string _functionTypeName = string.Empty;
    private string _script = string.Empty;

    public CustomType(string name, int byteSize, CustomTypeType customTypeType = CustomTypeType.Plugin)
    {
        ByteSize = byteSize;
        CustomTypeType = customTypeType;
        Name = name;
    }

    public int ByteSize { get; set; }

    public int PreferredAlignment { get; set; }

    public ConversionRoutine? Routine { get; set; }

    public ReverseConversionRoutine? ReverseRoutine { get; set; }

    public CustomTypeType CustomTypeType { get; private set; }

    public bool ScriptUsesFloat { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            CustomTypeHandler.EnsureUniqueName(this, value);
            _name = value;
        }
    }

    public string FunctionTypeName
    {
        get => _functionTypeName;
        set
        {
            CustomTypeHandler.EnsureUniqueFunctionTypeName(this, value);
            _functionTypeName = value;
        }
    }

    public string Script => _script;

    public void ConvertToData(float value, nint output)
    {
        ConvertFloatToData(value, output);
    }

    public float ConvertFromData(nint data)
    {
        return ConvertDataToFloat(data);
    }

    public void ConvertToData(int value, nint output)
    {
        ConvertIntegerToData(value, output);
    }

    public int ConvertFromDataAsInteger(nint data)
    {
        return ConvertDataToInteger(data);
    }

    public int ConvertDataToInteger(nint data)
    {
        if (Routine is null)
        {
            return 0;
        }

        var raw = Routine(data);
        if (!ScriptUsesFloat)
        {
            return raw;
        }

        var floatValue = BitConverter.Int32BitsToSingle(raw);
        return (int)Math.Truncate(floatValue);
    }

    public void ConvertIntegerToData(int value, nint output)
    {
        var payload = ScriptUsesFloat ? BitConverter.SingleToInt32Bits(value) : value;
        ReverseRoutine?.Invoke(payload, output);
    }

    public float ConvertDataToFloat(nint data)
    {
        if (Routine is null)
        {
            return 0;
        }

        var raw = Routine(data);
        return ScriptUsesFloat ? BitConverter.Int32BitsToSingle(raw) : raw;
    }

    public void ConvertFloatToData(float value, nint output)
    {
        var raw = ScriptUsesFloat ? BitConverter.SingleToInt32Bits(value) : (int)Math.Truncate(value);
        ReverseRoutine?.Invoke(raw, output);
    }

    public void SetScript(string script, bool luaScript = false)
    {
        _script = script ?? string.Empty;
        CustomTypeType = luaScript ? CustomTypeType.LuaScript : CustomTypeType.AutoAssembler;
    }

    public void Remove()
    {
        CustomTypeHandler.Remove(this);
    }

    public void ShowDebugInfo()
    {
        System.Diagnostics.Debug.WriteLine($"CustomType Name={Name}, FunctionTypeName={FunctionTypeName}, ByteSize={ByteSize}, Alignment={PreferredAlignment}, ScriptUsesFloat={ScriptUsesFloat}");
    }
}

public static class CustomTypeHandler
{
    private static readonly object SyncRoot = new();
    private static readonly List<CustomType> RegisteredCustomTypes = new();

    public static bool AllIncludesCustomType { get; set; }

    public static int MaxCustomTypeSize { get; private set; }

    public static IReadOnlyList<CustomType> CustomTypes
    {
        get
        {
            lock (SyncRoot)
            {
                return RegisteredCustomTypes.ToArray();
            }
        }
    }

    public static CustomType? GetCustomTypeFromName(string name)
    {
        lock (SyncRoot)
        {
            return RegisteredCustomTypes.FirstOrDefault(
                customType => string.Equals(customType.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void Register(CustomType customType)
    {
        ArgumentNullException.ThrowIfNull(customType);

        lock (SyncRoot)
        {
            EnsureUniqueName(customType, customType.Name);
            EnsureUniqueFunctionTypeName(customType, customType.FunctionTypeName);
            if (!RegisteredCustomTypes.Contains(customType))
            {
                RegisteredCustomTypes.Add(customType);
                MaxCustomTypeSize = Math.Max(MaxCustomTypeSize, customType.ByteSize);
            }
        }
    }

    public static bool Remove(CustomType customType)
    {
        ArgumentNullException.ThrowIfNull(customType);

        lock (SyncRoot)
        {
            var removed = RegisteredCustomTypes.Remove(customType);
            if (removed)
            {
                MaxCustomTypeSize = RegisteredCustomTypes.Count == 0 ? 0 : RegisteredCustomTypes.Max(type => type.ByteSize);
            }

            return removed;
        }
    }

    internal static void EnsureUniqueName(CustomType owner, string candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            throw new ArgumentException("Custom type name cannot be empty.", nameof(candidateName));
        }

        lock (SyncRoot)
        {
            var duplicate = RegisteredCustomTypes.FirstOrDefault(
                customType => !ReferenceEquals(customType, owner) && string.Equals(customType.Name, candidateName, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                throw new InvalidOperationException($"A custom type with name {candidateName} already exists.");
            }
        }
    }

    internal static void EnsureUniqueFunctionTypeName(CustomType owner, string candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return;
        }

        lock (SyncRoot)
        {
            var duplicate = RegisteredCustomTypes.FirstOrDefault(
                customType => !ReferenceEquals(customType, owner) && string.Equals(customType.FunctionTypeName, candidateName, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                throw new InvalidOperationException($"A custom function type with name {candidateName} already exists.");
            }
        }
    }
}