namespace CheatEngine.Library;

public static class AddressChangeUnit
{
    public static void ProcessAddress(string address, VariableType variableType, out string resolvedAddress, bool showAsHexadecimal = false, bool showAsSigned = false, int byteSize = 1)
    {
        var resolved = SymbolHandler.Default.GetAddressFromName(address, false, out var hasError);
        resolvedAddress = hasError
            ? "???"
            : ByteInterpreter.ReadAndParseAddress(resolved, variableType, null, showAsHexadecimal, showAsSigned, byteSize);
    }
}