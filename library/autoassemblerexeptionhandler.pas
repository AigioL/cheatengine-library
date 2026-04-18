unit autoassemblerexeptionhandler;

{$mode delphi}

interface

uses
  windows, Classes, SysUtils;

type
  TAAExceptionInfo=record
    trylabel: string;
    exceptlabel: string;
  end;

  TAAExceptionInfoList=array of TAAExceptionInfo;

procedure InitializeAutoAssemblerExceptionHandler;
procedure AutoAssemblerExceptionHandlerRemoveExceptionRange(startaddress: ptruint);
procedure AutoAssemblerExceptionHandlerAddExceptionRange(tryaddress: ptruint; exceptionAddress: ptruint);
procedure AutoAssemblerExceptionHandlerApplyChanges;
function AutoAssemblerExceptionHandlerHasEntries: boolean;

implementation

uses CEFuncProc, autoassembler;

type
  TAAExceptionListEntry=record
    tryaddress: ptruint;
    exceptionaddress: ptruint;
  end;

  TAAExceptionList=array of TAAExceptionListEntry;

  TAAExceptionListEntry32=record
    tryaddress: dword;
    exceptionaddress: dword;
  end;

  TAAExceptionListEntry64=record
    tryaddress: qword;
    exceptionaddress: qword;
  end;

  TAAExceptionList32=array of TAAExceptionListEntry32;
  TAAExceptionList64=array of TAAExceptionListEntry64;

var
  pid: dword;
  signatureaddress: ptruint;
  setlistaddress: ptruint;
  listaddress: ptruint;
  exceptionlist: TAAExceptionList;

function AutoAssemblerExceptionHandlerHasEntries: boolean;
begin
  result:=(pid=CEFuncProc.ProcessID) and (length(exceptionlist)>0);
end;

procedure AutoAssemblerExceptionHandlerAddExceptionRange(tryaddress: ptruint; exceptionAddress: ptruint);
var i: integer;
begin
  i:=length(exceptionlist);
  setlength(exceptionlist, i+1);
  exceptionlist[i].tryaddress:=tryaddress;
  exceptionlist[i].exceptionaddress:=exceptionAddress;
end;

procedure AutoAssemblerExceptionHandlerRemoveExceptionRange(startaddress: ptruint);
var i,j: integer;
begin
  i:=0;
  while i<length(exceptionlist) do
  begin
    if exceptionlist[i].tryaddress=startaddress then
    begin
      for j:=i to length(exceptionlist)-2 do
        exceptionlist[j]:=exceptionlist[j+1];

      setlength(exceptionlist, length(exceptionlist)-1);
    end
    else
      inc(i);
  end;
end;

procedure AutoAssemblerExceptionHandlerApplyChanges;
var
  params32: record
    list: dword;
    listsize: integer;
  end;

  params64: record
    list: qword;
    listsize: integer;
  end;

  params: pointer;
  th: thandle;
  templist32: TAAExceptionList32;
  templist64: TAAExceptionList64;
  i: integer;
  oldlist: ptruint;
  x: ptruint;
  y: dword;
begin
  InitializeAutoAssemblerExceptionHandler;

  oldlist:=0;
  ReadProcessMemory(CEFuncProc.ProcessHandle, pointer(listaddress), @oldlist, sizeof(oldlist), x);

  if processhandler.is64Bit then
  begin
    setlength(templist64, length(exceptionlist));
    for i:=0 to length(exceptionlist)-1 do
    begin
      templist64[i].tryaddress:=exceptionlist[i].tryaddress;
      templist64[i].exceptionaddress:=exceptionlist[i].exceptionaddress;
    end;

    params64.listsize:=length(exceptionlist);
    if params64.listsize=0 then
      params64.list:=0
    else
    begin
      params64.list:=ptruint(VirtualAllocEx(CEFuncProc.ProcessHandle, nil, length(templist64)*sizeof(TAAExceptionListEntry64), MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE));
      WriteProcessMemory(CEFuncProc.ProcessHandle, pointer(params64.list), @templist64[0], length(templist64)*sizeof(TAAExceptionListEntry64), x);
    end;

    params:=VirtualAllocEx(CEFuncProc.ProcessHandle, nil, sizeof(params64), MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE);
    WriteProcessMemory(CEFuncProc.ProcessHandle, params, @params64, sizeof(params64), x);
  end
  else
  begin
    setlength(templist32, length(exceptionlist));
    for i:=0 to length(exceptionlist)-1 do
    begin
      templist32[i].tryaddress:=exceptionlist[i].tryaddress;
      templist32[i].exceptionaddress:=exceptionlist[i].exceptionaddress;
    end;

    params32.listsize:=length(exceptionlist);
    if params32.listsize=0 then
      params32.list:=0
    else
    begin
      params32.list:=ptruint(VirtualAllocEx(CEFuncProc.ProcessHandle, nil, length(templist32)*sizeof(TAAExceptionListEntry32), MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE));
      WriteProcessMemory(CEFuncProc.ProcessHandle, pointer(params32.list), @templist32[0], length(templist32)*sizeof(TAAExceptionListEntry32), x);
    end;

    params:=VirtualAllocEx(CEFuncProc.ProcessHandle, nil, sizeof(params32), MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE);
    WriteProcessMemory(CEFuncProc.ProcessHandle, params, @params32, sizeof(params32), x);
  end;

  th:=CreateRemoteThread(CEFuncProc.ProcessHandle, nil, 0, pointer(setlistaddress), params, 0, y);
  if WaitForSingleObject(th, 5000)<>WAIT_OBJECT_0 then
    raise exception.Create('Failure to set the exception list. Thread error');

  CloseHandle(th);

  if oldlist<>0 then
    VirtualFreeEx(CEFuncProc.ProcessHandle, pointer(oldlist), 0, MEM_RELEASE);

  VirtualFreeEx(CEFuncProc.ProcessHandle, params, 0, MEM_RELEASE);
end;

procedure InitializeAutoAssemblerExceptionHandler;
var
  init: TStringList;
  signature: PChar;
  x: ptruint;
  ehallocated: boolean;
  tempallocs: TCEAllocArray;
  i: integer;
begin
  ehallocated:=false;

  if (pid=CEFuncProc.ProcessID) and (signatureaddress<>0) then
  begin
    getmem(signature,5);
    try
      if ReadProcessMemory(CEFuncProc.ProcessHandle, pointer(signatureaddress), signature, 4, x) then
      begin
        signature[4]:=#0;
        if signature='AAEH' then
          ehallocated:=true;
      end;
    finally
      freemem(signature);
    end;
  end;

  if ehallocated then
    exit;

  setlength(exceptionlist,0);
  setlength(tempallocs,0);

  init:=TStringList.Create;
  try
    init.Add('alloc(Signature,4)');
    init.Add('alloc(MREW,8)');
    init.Add('alloc(vehid,8)');
    init.Add('alloc(List,8)');
    init.Add('alloc(ListSize,8)');
    init.Add('alloc(ExceptionHandler,1024)');
    init.Add('alloc(SetList,1024)');
    init.Add('alloc(registereh,128)');
    init.Add('label(next)');
    init.Add('label(nomatch)');
    init.Add('label(match)');
    init.Add('label(ExceptionHandler_exit)');
    init.Add('');
    init.Add('Signature:');
    init.Add('db ''AAEH''');
    init.Add('');
    init.Add('MREW:');
    init.Add('dq 0');
    init.Add('');
    init.Add('List:');
    init.Add('dq 0');
    init.Add('');
    init.Add('ListSize:');
    init.Add('dd 0');
    init.Add('');
    init.Add('SetList:');
    if processhandler.is64Bit then
    begin
      init.Add('sub rsp,28');
      init.Add('mov [rsp+30],rcx');
      init.Add('mov rcx,MREW');
      init.Add('call AcquireSRWLockExclusive');
      init.Add('mov rax,[rsp+30]');
      init.Add('mov r8,[rax]');
      init.Add('mov r9,[rax+8]');
      init.Add('mov [List],r8');
      init.Add('mov [ListSize],r9');
      init.Add('mov rcx,MREW');
      init.Add('call ReleaseSRWLockExclusive');
      init.Add('add rsp,28');
      init.Add('ret');
    end
    else
    begin
      init.Add('push ebp');
      init.Add('mov ebp,esp');
      init.Add('push MREW');
      init.Add('call AcquireSRWLockExclusive');
      init.Add('mov eax,[ebp+8]');
      init.Add('mov ebx,[eax]');
      init.Add('mov ecx,[eax+4]');
      init.Add('mov [List],ebx');
      init.Add('mov [ListSize],ecx');
      init.Add('push MREW');
      init.Add('call ReleaseSRWLockExclusive');
      init.Add('pop ebp');
      init.Add('ret 4');
    end;

    init.Add('');
    init.Add('ExceptionHandler:');
    if processhandler.is64Bit then
    begin
      init.Add('sub rsp,28');
      init.Add('mov [rsp+30],rcx');
      init.Add('mov rcx,MREW');
      init.Add('call AcquireSRWLockShared');
      init.Add('cmp [List],0');
      init.Add('je ExceptionHandler_exit');
      init.Add('mov rax,[rsp+30]');
      init.Add('mov rax,[rax+8]');
      init.Add('lea rax,[rax+f8]');
      init.Add('mov r8,[List]');
      init.Add('mov rcx,[ListSize]');
      init.Add('next:');
      init.Add('mov r9,[r8]');
      init.Add('mov r10,[r8+8]');
      init.Add('cmp [rax],r9');
      init.Add('jb nomatch');
      init.Add('cmp [rax],r10');
      init.Add('jb match');
      init.Add('nomatch:');
      init.Add('add r8,10');
      init.Add('loop next');
      init.Add('xor rax,rax');
      init.Add('jmp ExceptionHandler_exit');
      init.Add('match:');
      init.Add('mov [rax],r10');
      init.Add('mov eax,ffffffff');
      init.Add('ExceptionHandler_exit:');
      init.Add('mov [rsp+30],rax');
      init.Add('mov rcx,MREW');
      init.Add('call ReleaseSRWLockShared');
      init.Add('mov rax,[rsp+30]');
      init.Add('add rsp,28');
      init.Add('ret');
    end
    else
    begin
      init.Add('push ebp');
      init.Add('mov ebp,esp');
      init.Add('push ebx');
      init.Add('push ecx');
      init.Add('push edx');
      init.Add('push esi');
      init.Add('push MREW');
      init.Add('call AcquireSRWLockShared');
      init.Add('cmp [List],0');
      init.Add('je ExceptionHandler_exit');
      init.Add('mov eax,[ebp+8]');
      init.Add('mov eax,[eax+4]');
      init.Add('lea eax,[eax+b8]');
      init.Add('mov esi,[List]');
      init.Add('mov ecx,[ListSize]');
      init.Add('next:');
      init.Add('mov ebx,[esi]');
      init.Add('mov edx,[esi+4]');
      init.Add('cmp [eax],ebx');
      init.Add('jb nomatch');
      init.Add('cmp [eax],edx');
      init.Add('jb match');
      init.Add('nomatch:');
      init.Add('add esi,8');
      init.Add('loop next');
      init.Add('mov eax,0');
      init.Add('jmp ExceptionHandler_exit');
      init.Add('match:');
      init.Add('mov [eax],edx');
      init.Add('mov eax,ffffffff');
      init.Add('ExceptionHandler_exit:');
      init.Add('push eax');
      init.Add('push MREW');
      init.Add('call ReleaseSRWLockShared');
      init.Add('pop eax');
      init.Add('pop esi');
      init.Add('pop edx');
      init.Add('pop ecx');
      init.Add('pop ebx');
      init.Add('pop ebp');
      init.Add('ret 4');
    end;

    init.Add('');
    init.Add('registereh:');
    if processhandler.is64Bit then
    begin
      init.Add('sub rsp,28');
      init.Add('mov rcx,1');
      init.Add('mov rdx,ExceptionHandler');
      init.Add('call AddVectoredExceptionHandler');
      init.Add('mov [vehid],rax');
      init.Add('add rsp,28');
      init.Add('ret');
    end
    else
    begin
      init.Add('push ExceptionHandler');
      init.Add('push 1');
      init.Add('call AddVectoredExceptionHandler');
      init.Add('mov [vehid],eax');
      init.Add('ret 4');
    end;

    init.Add('');
    init.Add('createthreadandwait(registereh)');

    if not autoassemble(init, false, true, false, false, tempallocs) then
      raise exception.Create('Failure to assemble exception handler');

    for i:=0 to length(tempallocs)-1 do
    begin
      if lowercase(tempallocs[i].varname)='signature' then
        signatureaddress:=tempallocs[i].address
      else
      if lowercase(tempallocs[i].varname)='list' then
        listaddress:=tempallocs[i].address
      else
      if lowercase(tempallocs[i].varname)='setlist' then
        setlistaddress:=tempallocs[i].address;
    end;

    pid:=CEFuncProc.ProcessID;
  finally
    init.Free;
  end;
end;

end.