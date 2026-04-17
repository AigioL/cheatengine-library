namespace CheatEngine.Library;

public enum BasicType
{
    NoType = 0,
    Void = 1,
    Char = 2,
    WChar = 3,
    Int = 6,
    UInt = 7,
    Float = 8,
    Bcd = 9,
    Bool = 10,
    Long = 13,
    ULong = 14,
    Int2 = 16,
    Currency = 25,
    Date = 26,
    Variant = 27,
    Complex = 28,
    Bit = 29,
    Bstr = 30,
    HResult = 31,
}

public enum SymTagEnum
{
    Null = 0,
    Exe,
    Compiland,
    CompilandDetails,
    CompilandEnv,
    Function,
    Block,
    Data,
    Annotation,
    Label,
    PublicSymbol,
    Udt,
    Enum,
    FunctionType,
    PointerType,
    ArrayType,
    BaseType,
    Typedef,
    BaseClass,
    Friend,
    FunctionArgType,
    FuncDebugStart,
    FuncDebugEnd,
    UsingNamespace,
    VTableShape,
    VTable,
    Custom,
    Thunk,
    CustomType,
    ManagedType,
    Dimension,
}

public static class CvConst
{
    public const int CvAllRegErr = 30000;
    public const int CvAllRegTeb = 30001;
    public const int CvAllRegParams = 30008;
    public const int CvAllRegLocals = 30009;
    public const int CvRegNone = 0;
    public const int CvRegEax = 17;
    public const int CvRegEcx = 18;
    public const int CvRegEdx = 19;
    public const int CvRegEbx = 20;
    public const int CvRegEsp = 21;
    public const int CvRegEbp = 22;
    public const int CvRegEsi = 23;
    public const int CvRegEdi = 24;
    public const int CvAmd64Rax = 328;
    public const int CvAmd64Rcx = 329;
    public const int CvAmd64Rdx = 330;
    public const int CvAmd64Rbx = 331;
    public const int CvAmd64Rsp = 332;
    public const int CvAmd64Rbp = 333;
    public const int CvAmd64Rsi = 334;
    public const int CvAmd64Rdi = 335;
    public const int CvAmd64R8 = 336;
    public const int CvAmd64R9 = 337;
    public const int CvAmd64R10 = 338;
    public const int CvAmd64R11 = 339;
    public const int CvAmd64R12 = 340;
    public const int CvAmd64R13 = 341;
    public const int CvAmd64R14 = 342;
    public const int CvAmd64R15 = 343;
}