unit AddressChangeUnit;

{$mode objfpc}{$H+}

interface

uses
  Classes, SysUtils, symbolhandler, byteinterpreter, CEFuncProc, LazUTF8;

procedure IProcessAddress(address : WideString ; vartype : TVariableType ; showashexadecimal: Boolean;
  showAsSigned: boolean; bytesize: Integer; out res_address : WideString);stdcall;

implementation

procedure IProcessAddress(address : WideString ; vartype : TVariableType ; showashexadecimal: Boolean;
  showAsSigned: boolean; bytesize: Integer; out res_address : WideString);stdcall;
var a: PtrUInt;
  e: boolean;
  s: string;
begin
  //read the address and display the value it points to

  a:=symhandler.getAddressFromName(utf8toansi(UTF16ToUTF8(address)),false,e);
  if not e then
  begin
    //get the vartype and parse it
    s:=readAndParseAddress(a, vartype,nil,showashexadecimal, showAsSigned, bytesize);
    res_address:=UTF8ToUTF16(s);
  end
  else
    res_address:='???';
end;

end.

