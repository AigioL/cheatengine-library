unit autoassembler;

{$MODE Delphi}

interface

uses jwawindows, windows, Assemblerunit, classes, LCLIntf,symbolhandler,
     sysutils,dialogs,controls, CEFuncProc, NewKernelHandler,
     ProcessHandlerUnit;



function getenableanddisablepos(code:tstrings;var enablepos,disablepos: integer): boolean;
function autoassemble(code: tstrings;popupmessages: boolean):boolean; overload;
function autoassemble(code: Tstrings; popupmessages,enable,syntaxcheckonly, targetself: boolean):boolean; overload;
function autoassemble(code: Tstrings; popupmessages,enable,syntaxcheckonly, targetself: boolean;var CEAllocarray: TCEAllocArray; registeredsymbols: tstringlist=nil; exceptions: PCEExceptionListArray=nil): boolean; overload;

implementation

uses StrUtils, memscan, autoassemblerexeptionhandler;

resourcestring
  rsForwardJumpWithNoLabelDefined = 'Forward jump with no label defined';
  rsThereIsCodeDefinedWithoutSpecifyingTheAddressItBel = 'There is code defined without specifying the address it belongs to';
  rsIsNotAValidBytestring = '%s is not a valid bytestring';
  rsTheBytesAtAreNotWhatWasExpected = 'The bytes at %s are not what was expected';
  rsTheMemoryAtCanNotBeRead = 'The memory at +%s can not be read';
  rsWrongSyntaxASSERTAddress1122335566 = 'Wrong syntax. ASSERT(address,11 22 33 ** 55 66)';
  rsIsNotAValidSize = '%s is not a valid size';
  rsWrongSyntaxGLOBALALLOCNameSize = 'Wrong syntax. GLOBALALLOC(name,size)';
  rsCouldNotBeFound = '%s could not be found';
  rsWrongSyntaxIncludeFilenameCea = 'Wrong syntax. Include(filename.cea)';
  rsWrongSyntaxCreateThreadAddress = 'Wrong syntax. CreateThread(address)';
  rsCouldNotBeInjected = '%s could not be injected';
  rsWrongSyntaxLoadLibraryFilename = 'Wrong syntax. LoadLibrary(filename)';
  rsWrongSyntaxLuaCall = 'Wrong Syntax. LuaCall(luacommand)';
  rsInvalidAddressForReadMem = 'Invalid address for ReadMem';
  rsInvalidSizeForReadMem = 'Invalid size for ReadMem';
  rsTheMemoryAtCouldNotBeFullyRead = 'The memory at %s could not be fully read';
  rsWrongSyntaxReadMemAddressSize = 'Wrong syntax. ReadMem(address,size)';
  rsTheFileDoesNotExist = 'The file %s does not exist';
  rsWrongSyntaxLoadBinaryAddressFilename = 'Wrong syntax. LoadBinary(address,filename)';
  rsWrongSyntaxReAssemble = 'Wrong syntax. Reassemble(address)';
  rsMissingExcept = 'Missing {$EXCEPT} for {$TRY} at line %d';
  rsSyntaxError = 'Syntax error';
  rsTheArrayOfByteCouldNotBeFound = 'The array of byte ''%s'' could not be found';
  rsAATheArrayOfByteNamed = 'The array of byte named %s could not be found';
  rsAAErrorWhileSacnningForAobs = 'Error while scanning for AOB''s : ';
  rsAAError = 'Error: ';
  rsAAModuleNotFound = 'module not found:';
  rsWrongSyntaxAOBSCANName11223355 = 'Wrong syntax. AOBSCAN(name,11 22 33 ** 55)';
  rsWrongSyntaxAOBSCANMODULEName11223355 = 'Wrong syntax. AOBSCANMODULE(name, module, 11 22 33 ** 55)';
  rsWrongSyntaxAOBSCANREGION = 'Wrong syntax. AOBSCANREGION(name, startaddress, stopaddress, 11 22 33 ** 55)';

  rsDefineAlreadyDefined = 'Define %s already defined';
  rsWrongSyntaxDEFINENameWhatever = 'Wrong syntax. DEFINE(name,whatever)';
  rsSyntaxErrorFullAccessAddressSize = 'Syntax error. FullAccess(address,size)';
  rsIsNotAValidIdentifier = '%s is not a valid identifier';
  rsIsBeingRedeclared = '%s is being redeclared';
  rsLabelIsBeingDefinedMoreThanOnce = 'label %s is being defined more than once';
  rsLabelIsNotDefinedInTheScript = 'label %s is not defined in the script';
  rsTheIdentifierHasAlreadyBeenDeclared = 'The identifier %s has already been declared';
  rsWrongSyntaxALLOCIdentifierSizeinbytes = 'Wrong syntax. ALLOC(identifier,sizeinbytes)';
  rsNeedToUseKernelmodeReadWriteprocessmemory = 'You need to use kernelmode read/writeprocessmemory if you want to use KALLOC';
  rsSorryButWithoutTheDriverKALLOCWillNotFunction = 'Sorry, but without the driver KALLOC will not function';
  rsWrongSyntaxKallocIdentifierSizeinbytes = 'Wrong syntax. kalloc(identifier,sizeinbytes)';
  rsThisAddressSpecifierIsNotValid = 'This address specifier is not valid';
  rsThisInstructionCanTBeCompiled = 'This instruction can''t be compiled';
  rsErrorInLine = 'Error in line %s (%s) :%s';
  rsWasSupposedToBeAddedToTheSymbollistButItIsnTDeclar = '%s was supposed to be added to the symbollist, but it isn''t declared';
  rsTheAddressInCreatethreadIsNotValid = 'The address in createthread(%s) is not valid';
  rsTheAddressInCreatethreadAndWaitIsNotValid = 'The address in createthreadandwait(%s) is not valid';
  rsTheAddressInLoadbinaryIsNotValid = 'The address in loadbinary(%s,%s) is not valid';
  rsThisCodeCanBeInjectedAreYouSure = 'This code can be injected. Are you sure?';
  rsFailureToAllocateMemory = 'Failure to allocate memory';
  rsNotAllInstructionsCouldBeInjected = 'Not all instructions could be injected';
  rsTheFollowingKernelAddressesWhereAllocated = 'The following kernel addresses where allocated';
  rsTheCodeInjectionWasSuccessfull = 'The code injection was successfull';
  rsCouldNotDecodeInstructionForReassemble = 'Could not decode the instruction at %s for Reassemble';
  rsReassembleTargetOutOfRange = 'Reassemble(%s) target is out of range';
  rsYouCanOnlyHaveOneEnableSection = 'You can only have one enable section';
  rsYouCanOnlyHaveOneDisableSection = 'You can only have one disable section';
  rsYouHavnTSpecifiedAEnableSection = 'You havn''t specified a enable section';
  rsYouHavnTSpecifiedADisableSection = 'You havn''t specified a disable section';
  rsWrongSyntaxSHAREDALLOCNameSize = 'Wrong syntax. SHAREDALLOC(name,size)';



procedure tokenize(input: string; tokens: tstringlist);
var i: integer;
    a: integer;
    inquote: boolean;
    inquote2: boolean;
begin

  tokens.clear;
  inquote:=false;
  inquote2:=false;
  a:=-1;
  for i:=1 to length(input) do
  begin
    if inquote and (input[i]<>'''') then continue;
    if inquote2 and (input[i]<>'"') then continue;

    case input[i] of
      'a'..'z','A'..'Z','0'..'9','.', '_','#','@', #128..#255: if a=-1 then a:=i;
      else
      begin
        if input[i]='''' then
        begin
          if inquote then
          begin
            if a<>-1 then
              tokens.AddObject(copy(input,a,i-a),tobject(a));

            a:=-1;
            inquote:=false;
          end
          else
          begin
            inquote:=true;
            a:=i;
          end;

          continue;
        end;

        if input[i]='"' then
        begin
          if inquote2 then
          begin
            if a<>-1 then
              tokens.AddObject(copy(input,a,i-a),tobject(a));

            a:=-1;
            inquote2:=false;
          end
          else
          begin
            inquote2:=true;
            a:=i;
          end;

          continue;
        end;

        if a<>-1 then
          tokens.AddObject(copy(input,a,i-a),tobject(a));
        a:=-1;
      end;
    end;
  end;

  if a<>-1 then
    tokens.AddObject(copy(input,a,length(input)),tobject(a));
end;

function tokencheck(input,token:string):boolean;
var tokens: tstringlist;
    i: integer;
begin
  tokens:=tstringlist.Create;
  try
    tokenize(input,tokens);
    result:=false;

    for i:=0 to tokens.Count-1 do
      if tokens[i]=token then
      begin
        result:=true;
        break;
      end;
  finally
    tokens.free;
  end;
end;

function replacetoken(input: string;token:string;replacewith:string):string;
var tokens: tstringlist;
    i,j: integer;
begin
  result:=input;
  tokens:=tstringlist.Create;
  try
    tokenize(result,tokens);
    for i:=tokens.Count-1 downto 0 do
      if tokens[i]=token then
      begin
        j:=integer(tokens.Objects[i]);
        result:=copy(result,1,j-1)+replacewith+copy(result,j+length(token),length(result));
      end;

  finally
    tokens.free;
  end;
end;

procedure tokenizeStruct(input: string; tokens: tstringlist);
//a version of tokenize using strutils to split up the strings. (I don't want to replace it yet since it is slightly different, probably better, but let's be safe for now till it's fully tested)
var delims: TSysCharSet;
    i: integer;
begin
  delims:=[' '];

  tokens.clear;
  for i:=1 to WordCount(input, delims) do
    tokens.add(ExtractWord(i, input, delims));

end;

procedure replaceStructWithDefines(code: Tstrings; linenr: integer);
{
parses the structure starting at the given line number.
removes the structure definition from the code
writes a define(xxxx,xxxxx) inplace starting from the linenumber

precondition: linenr is valid and code[linenr] is indeed a STRUCT name line

Structure format:
struct name
  db ?  db ? db ? db ?
  db ? ? ?
  dw ?
  dd ?
  dq ?
  resb 4 resb 4
  resw 4
  resd 2
  resq 1
elementname1:
  db ?
  resw 1
  dw ?
elementname2: dw ?

endstruct

Res is an alternate to the usual "d* ?" but is specifically for no fill  (Res stands for reserve)
Usage res* #  where # is the number of copies.  This is better suited for strings which can not be defined with db ?

when done the name.elementname1 and just elementname1 will be defined at the spot the structure was placed. Subsequent structure definitions can override these defines. Elements may NOT have reserved words as they will get replaced. (like assembler commands)
}


var
  currentOffset: integer;
  structname: string;
  elementname: string;
  i,j,k: integer;
  tokens: Tstringlist;

  elements: tstringlist;
  starttoken: integer;

  bytesize: integer;
  endfound: boolean;
  lastlinenr: integer;
  procedure structError(reason: string='');
  var error: string;
  begin
    error:='Error in the structure definition of '+structname+' at line '+inttostr(lastlinenr+1);
    if reason<>'' then
      error:=error+' :'+reason
    else
      error:=error+'.';

    raise exception.create(error);
  end;

begin
  lastlinenr:=linenr;
  endfound:=false;
  structname:=trim(copy(code[linenr], 7, length(code[linenr])));

  currentOffset:=0;
  tokens:=tstringlist.create;
  elements:=tstringlist.create;

  for i:=linenr+1 to code.count-1 do
  begin
    lastlinenr:=i;
    tokenizeStruct(code[i], tokens);

    j:=0;

    if tokens.count>0 then
    begin
      //first check if it's a label definition
      if tokens[0][length(tokens[0])]=':' then
      begin
        elementname:=copy(tokens[0], 1, Length(tokens[0])-1);
        if GetOpcodesIndex(elementname)<>-1 then
          structError(elementname+' is a reserved word');

        elements.AddObject(elementname, tobject(currentOffset));

        j:=1;
      end;



      //then check if it's the end of the struct
      if (uppercase(tokens[0])='ENDSTRUCT') or (uppercase(tokens[0])='ENDS') then
      begin
        endfound:=true;
        break; //all done
      end;
    end;


    //if it's neither a label or structure end then it's a size defining token

    while j < tokens.count do
    begin
      tokens[j]:=uppercase(tokens[j]);

      case tokens[j][1] of
        'R':
        begin
          //could be res*
          if (length(tokens[j])=4) and (copy(tokens[j],1,3)='RES') then
          begin
            case tokens[j][4] of
              'B': bytesize:=1;
              'W': bytesize:=2;
              'D': bytesize:=4;
              'Q': bytesize:=8;
              else
                StructError;
            end;
            //now get the count
            inc(j);
            if j>=tokens.count then
              structError;

            try
              inc(currentOffset, bytesize*StrToInt(tokens[j]));
            except
              structerror;
            end;
          end else structerror;
        end;

        'D':
        begin
          //could be d* ?
          if length(tokens[j])=2 then
          begin
            case tokens[j][2] of
              'B': bytesize:=1;
              'W': bytesize:=2;
              'D': bytesize:=4;
              'Q': bytesize:=8;
              else
                StructError;
            end;

            inc(j);
            if j>=tokens.count then
              structError;

            inc(currentOffset, bytesize);

            //check if there are more ?'s after this (in case of dw ? ? ?)
            while j<tokens.count-1 do
            begin
              if tokens[j+1]='?' then  //check from the spot in front
              begin
                inc(currentOffset, bytesize);
                inc(j);
              end
              else
                break; //nope
            end;

          end else structerror;
        end;

        else
          structError('No idea what '+tokens[j]+' is'); //we already dealth with labels, so this is wrong
      end;


      inc(j); //next token
    end;


  end;

  if endfound=false then
    structerror('No end found');

  //the elements have been filled in, delete the structure (between linenr and lastlinenr) and inject define(element,offset) and define(structname.element,offset)
  for i:=lastlinenr downto linenr do
    code.Delete(i);

  for i:=0 to elements.count-1 do
  begin
    code.Insert(linenr,'define('+elements[i]+','+inttohex(ptruint(elements.objects[i]),1)+')');
    code.Insert(linenr,'define('+structname+'.'+elements[i]+','+inttohex(ptruint(elements.objects[i]),1)+')');
  end;


  code.insert(linenr, 'define('+structname+'_size,'+inttohex(currentOffset,1)+')');



  tokens.Free;
  elements.free;



end;


procedure getPotentialLabels(code: Tstrings; labels: TStrings);
var
  i: integer;
  currentline: string;
  a: int64;
begin
  for i:=0 to code.count-1 do
  begin
    currentline:=trim(code[i]);
    if (currentline<>'') and (currentline[length(currentline)]=':') then
    begin
      if (pos('+', currentline)=0) and (pos('.', currentline)=0) then
      begin
        currentline:=copy(currentline,1,length(currentline)-1);
        if trystrtoint64('$'+currentline, a)=false then
          labels.Add(currentline);
      end;
    end;
  end;
end;


procedure removecomments(code: tstrings);
var i,j: integer;
    currentline: string;
    instring: boolean;
    incomment: boolean;
    bracecomment: boolean;
begin
  //remove comments
  incomment:=false;
  bracecomment:=false;
  for i:=0 to code.count-1 do
  begin
    currentline:=code[i];
    instring:=false;
    
    for j:=1 to length(currentline) do
    begin
      if incomment then
      begin
        //inside a comment, remove everything till a } is encountered
        if (bracecomment and (currentline[j]='}') and (processhandler.SystemArchitecture<>archArm)) or
           ((not bracecomment) and (currentline[j]='*') and (j<length(currentline)) and (currentline[j+1]='/')) then
        begin
          incomment:=false; //and continue parsing the code...

          if not bracecomment then
            currentline[j+1]:=' ';
        end;

        currentline[j]:=' ';
      end
      else
      begin
        if currentline[j]='''' then instring:=not instring;
        if currentline[j]=#9 then currentline[j]:=' '; //tabs are basicly comments 

        if not instring then
        begin
          //not inside a string, so comment markers need to be dealt with
          if (currentline[j]='/') and (j<length(currentline)) and (currentline[j+1]='/') then //- comment (only the rest of the line)
          begin
            //cut off till the end of the line (and might as well jump out now)
            currentline:=copy(currentline,1,j-1);
            break;
          end;

          if ((currentline[j]='{') and (processhandler.SystemArchitecture<>archArm)) or
             ((currentline[j]='/') and (j<length(currentline)) and (currentline[j+1]='*')) then
          begin
            incomment:=true;
            bracecomment:=currentline[j]='{';
            currentline[j]:=' '; //replace from here till the first } with spaces, this goes on for multiple lines
          end;
        end;
      end;
    end;

    code[i]:=trim(currentline);
  end;

end;


procedure unlabeledlabels(code: tstrings);
var i,j: integer;
    lastseenlabel: integer;
    labels: array of string; //sorted in order of definition
    currentline: string;

begin
  //unlabeled label support
  //For those reading the source, PLEASE , try not to code scripts like that
  //the scripts you make look like crap, and are hard to read. (like using goto in a c app)
  //this is just to make one guy happy

  setlength(labels,0);
  i:=0;
  while i<code.count do
  begin
    currentline:=code[i];

    if length(currentline)>1 then
    begin
      if currentline='@@:' then
      begin
        currentline:='RandomLabel'+chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 chr(ord('A')+random(26))+
                 ':';
        code[i]:=currentline;

        code.Insert(0,'label('+copy(currentline,1,length(currentline)-1)+')');
        inc(i);

        setlength(labels,length(labels)+1);
        labels[length(labels)-1]:=copy(currentline,1,length(currentline)-1);
      end else
      if currentline[length(currentline)]=':' then
      begin
        setlength(labels,length(labels)+1);
        labels[length(labels)-1]:=copy(currentline,1,length(currentline)-1);
      end;
    end;

    inc(i);
  end;

  //all label definitions have been filled in
  //now change @F (forward) and @B (back) to the labels in front and behind
  lastseenlabel:=-1;
  for i:=0 to code.Count-1 do
  begin
    currentline:=code[i];
    if length(currentline)>1 then
    begin
      if currentline[length(currentline)]=':' then
      begin
        //find this in the array
        currentline:=copy(currentline,1,length(currentline)-1);
        for j:=(lastseenlabel+1) to length(labels)-1 do  //lastseenlabel+1 since it is ordered in definition
        begin
          if uppercase(currentline)=uppercase(labels[j]) then
          begin
            lastseenlabel:=j;
            break;
          end;
        end;
        currentline:=currentline+':';
        //lastseenlabel is now updated to the current pos
      end else
      if pos('@f',lowercase(currentline))>0 then  //forward
      begin
        //forward label, so labels[lastseenlabel+1]
        if lastseenlabel+1 >= length(labels) then
          raise exception.Create(rsForwardJumpWithNoLabelDefined);

        currentline:=replacetoken(currentline,'@f',labels[lastseenlabel+1]);
        currentline:=replacetoken(currentline,'@F',labels[lastseenlabel+1]);
      end else
      if pos('@b',lowercase(currentline))>0 then //back
      begin
        //forward label, so labels[lastseenlabel]
        if lastseenlabel=-1 then
          raise exception.Create(rsThereIsCodeDefinedWithoutSpecifyingTheAddressItBel);

        currentline:=replacetoken(currentline,'@b',labels[lastseenlabel]);
        currentline:=replacetoken(currentline,'@B',labels[lastseenlabel]);
      end;
    end;
    code[i]:=currentline;

  end;
end;


procedure splitparameters(const s: string; list: TStrings; separators: TSysCharSet=[',']);
var i: integer;
begin
  list.Clear;
  ExtractStrings(separators, [' ',#9,#13,#10], pchar(s), list);

  i:=0;
  while i<list.Count do
  begin
    list[i]:=trim(list[i]);
    if list[i]='' then
      list.Delete(i)
    else
      inc(i);
  end;
end;

procedure parseTryExcept(code: tstrings; var exceptionlist: TAAExceptionInfoList);
var
  i,j: integer;
  trynr: integer;
  trylist: array of record
    linenr: integer;
    trynr: integer;
    hasexcept: boolean;
    trylabel: string;
    exceptlabel: string;
  end;
  found: boolean;
begin
  trynr:=0;
  setlength(trylist,0);

  for i:=0 to code.Count-1 do
  begin
    if uppercase(TrimRight(code[i]))='{$TRY}' then
    begin
      inc(trynr);

      j:=length(trylist);
      setlength(trylist, j+1);
      trylist[j].trynr:=trynr;
      trylist[j].hasexcept:=false;
      trylist[j].linenr:=ptruint(code.Objects[i]);
      trylist[j].trylabel:='tryoperation_'+IntToStr(trynr);
      code[i]:=trylist[j].trylabel+':';
    end;

    if uppercase(TrimRight(code[i]))='{$EXCEPT}' then
    begin
      found:=false;
      for j:=length(trylist)-1 downto 0 do
      begin
        if not trylist[j].hasexcept then
        begin
          trylist[j].hasexcept:=true;
          trylist[j].exceptlabel:='tryoperation'+IntToStr(trylist[j].trynr)+'_except';
          code[i]:=trylist[j].exceptlabel+':';
          found:=true;
          break;
        end;
      end;

      if not found then
        raise exception.Create(Format('Found an {$EXCEPT} at line %d with no matching {$TRY}', [ptruint(code.Objects[i])]));
    end;
  end;

  setlength(exceptionlist, length(trylist));
  for i:=0 to length(trylist)-1 do
  begin
    code.Insert(0,'label('+trylist[i].trylabel+')');
    code.Insert(0,'label('+trylist[i].exceptlabel+')');
    exceptionlist[i].trylabel:=trylist[i].trylabel;
    exceptionlist[i].exceptlabel:=trylist[i].exceptlabel;

    if not trylist[i].hasexcept then
      raise exception.Create(Format(rsMissingExcept, [trylist[i].linenr]));
  end;
end;

function getaobscanstopaddress: ptrUint;
begin
  if processhandler.is64Bit then
    result:=high(ptrUint)
  else
    result:=$ffffffff;
end;

function aobscans(code: tstrings; syntaxcheckonly: boolean): boolean;
type
  TAOBEntry = record
    name: string;
    aobstring: string;
    linenumber: integer;
  end;

var i,j,m,a,b,c,d,e: integer;
    currentline: string;
    s1,s2,s3,s4: string;
    aobscanmodules: array of record
      name: string;
      entries: array of TAOBEntry;
      minaddress, maxaddress: ptrUint;
      protection: string;
      memscan: TMemScan;
    end;
    mi: TModuleInfo;
    startaddress, stopaddress, testptr: ptrUint;
    cpucount: integer;
    threads: integer;
    aobstrings: string;
    results: TAddresses;
    error: boolean;
    errorstring: string;

  procedure finished(f: integer);
  var
    i: integer;
    aoblist: string;
  begin
    setlength(results,0);
    if length(aobscanmodules[f].entries)=1 then
    begin
      if aobscanmodules[f].memscan.GetOnlyOneResult(testptr) then
      begin
        setlength(results,1);
        results[0]:=testptr;

        if not InRangeX(results[0], aobscanmodules[f].minaddress, aobscanmodules[f].maxaddress) then
          raise exception.Create('Invalid result for aob region scan');
      end;
    end
    else
      aobscanmodules[f].memscan.GetOnlyOneResults(results);

    if length(results)=length(aobscanmodules[f].entries) then
    begin
      for i:=0 to length(aobscanmodules[f].entries)-1 do
      begin
        if results[i]=0 then
        begin
          error:=true;
          errorstring:=format(rsAATheArrayOfByteNamed, [aobscanmodules[f].entries[i].name]);
        end
        else
          code[aobscanmodules[f].entries[i].linenumber]:='DEFINE('+aobscanmodules[f].entries[i].name+', '+IntToHex(results[i],8)+')';
      end;
    end
    else
    begin
      error:=true;
      aoblist:='';
      for i:=0 to length(aobscanmodules[f].entries)-1 do
        aoblist:=aoblist+aobscanmodules[f].entries[i].name+#13#10;

      if aobscanmodules[f].memscan.GetErrorString<>'' then
        errorstring:=rsAAErrorWhileSacnningForAobs+aoblist+#13#10+rsAAError+aobscanmodules[f].memscan.GetErrorString
      else
        errorstring:=rsAAErrorWhileSacnningForAobs+aoblist+#13#10+rsAAError+'Not all results found';
    end;

    aobscanmodules[f].memscan.Free;
    aobscanmodules[f].memscan:=nil;
    dec(threads);
  end;
begin
  result:=false;
  error:=false;
  setlength(aobscanmodules,0);
  cpucount:=GetCPUCount;
  if cpucount<1 then
    cpucount:=1;
  threads:=0;

  for i:=0 to code.Count-1 do
  begin
    currentline:=trim(code[i]);

    if uppercase(copy(currentline,1,10))='AOBSCANEX(' then
    begin
      a:=pos('(',currentline);
      b:=pos(',',currentline);
      c:=pos(')',currentline);
      if (a<=0) or (b<=0) or (c<=0) then
        raise exception.Create(rsWrongSyntaxAOBSCANName11223355);

      s1:=trim(copy(currentline,a+1,b-a-1));
      s2:=trim(copy(currentline,b+1,c-b-1));

      if not syntaxcheckonly then
      begin
        m:=-1;
        for j:=0 to length(aobscanmodules)-1 do
          if aobscanmodules[j].name=' ' then
          begin
            m:=j;
            break;
          end;

        if m=-1 then
        begin
          setlength(aobscanmodules, length(aobscanmodules)+1);
          m:=length(aobscanmodules)-1;
          aobscanmodules[m].name:=' ';
          aobscanmodules[m].minaddress:=0;
          aobscanmodules[m].maxaddress:=getaobscanstopaddress;
          aobscanmodules[m].protection:='*C*W+X';
          setlength(aobscanmodules[m].entries,0);
        end;

        j:=length(aobscanmodules[m].entries);
        setlength(aobscanmodules[m].entries, j+1);
        aobscanmodules[m].entries[j].name:=s1;
        aobscanmodules[m].entries[j].aobstring:=s2;
        aobscanmodules[m].entries[j].linenumber:=i;
      end
      else
        code[i]:='DEFINE('+s1+', 00000000)';

      continue;
    end
    else
    if uppercase(copy(currentline,1,8))='AOBSCAN(' then
    begin
      a:=pos('(',currentline);
      b:=pos(',',currentline);
      c:=pos(')',currentline);
      if (a<=0) or (b<=0) or (c<=0) then
        raise exception.Create(rsWrongSyntaxAOBSCANName11223355);

      s1:=trim(copy(currentline,a+1,b-a-1));
      s2:=trim(copy(currentline,b+1,c-b-1));

      if not syntaxcheckonly then
      begin
        m:=-1;
        for j:=0 to length(aobscanmodules)-1 do
          if aobscanmodules[j].name='' then
          begin
            m:=j;
            break;
          end;

        if m=-1 then
        begin
          setlength(aobscanmodules, length(aobscanmodules)+1);
          m:=length(aobscanmodules)-1;
          aobscanmodules[m].name:='';
          aobscanmodules[m].minaddress:=0;
          aobscanmodules[m].maxaddress:=getaobscanstopaddress;
          aobscanmodules[m].protection:='';
          setlength(aobscanmodules[m].entries,0);
        end;

        j:=length(aobscanmodules[m].entries);
        setlength(aobscanmodules[m].entries, j+1);
        aobscanmodules[m].entries[j].name:=s1;
        aobscanmodules[m].entries[j].aobstring:=s2;
        aobscanmodules[m].entries[j].linenumber:=i;
      end
      else
        code[i]:='DEFINE('+s1+', 00000000)';

      continue;
    end
    else
    if uppercase(copy(currentline,1,14))='AOBSCANMODULE(' then
    begin
      a:=pos('(',currentline);
      b:=pos(',',currentline);
      c:=PosEx(',',currentline,b+1);
      d:=pos(')',currentline);
      if d<=a then
        raise exception.Create(rsWrongSyntaxAOBSCANMODULEName11223355);

      if (a<=0) or (b<=0) or (c<=0) then
        raise exception.Create(rsWrongSyntaxAOBSCANMODULEName11223355);

      s1:=trim(copy(currentline,a+1,b-a-1));
      s2:=trim(copy(currentline,b+1,c-b-1));
      s3:=trim(copy(currentline,c+1,d-c-1));

      if not syntaxcheckonly then
      begin
        m:=-1;
        for j:=0 to length(aobscanmodules)-1 do
          if aobscanmodules[j].name=uppercase(s2) then
          begin
            m:=j;
            break;
          end;

        if m=-1 then
        begin
          setlength(aobscanmodules, length(aobscanmodules)+1);
          m:=length(aobscanmodules)-1;
          aobscanmodules[m].name:=uppercase(s2);
          aobscanmodules[m].protection:='';

          if symhandler.getmodulebyname(s2, mi) then
          begin
            aobscanmodules[m].minaddress:=mi.baseaddress;
            aobscanmodules[m].maxaddress:=mi.baseaddress+mi.basesize;
          end
          else
          begin
            try
              testptr:=symhandler.getAddressFromName(s2);
              if symhandler.getmodulebyaddress(testptr, mi) then
              begin
                aobscanmodules[m].minaddress:=mi.baseaddress;
                aobscanmodules[m].maxaddress:=mi.baseaddress+mi.basesize;
              end;
            except
              raise exception.Create(rsAAModuleNotFound+s2);
            end;
          end;

          if aobscanmodules[m].maxaddress=0 then
            raise exception.Create(rsAAModuleNotFound+s2);

          setlength(aobscanmodules[m].entries,0);
        end;

        j:=length(aobscanmodules[m].entries);
        setlength(aobscanmodules[m].entries, j+1);
        aobscanmodules[m].entries[j].name:=s1;
        aobscanmodules[m].entries[j].aobstring:=s3;
        aobscanmodules[m].entries[j].linenumber:=i;
      end;

      if syntaxcheckonly then
        code[i]:='DEFINE('+s1+', 00000000)';

      continue;
    end
    else
    if uppercase(copy(currentline,1,14))='AOBSCANREGION(' then
    begin
      a:=pos('(',currentline);
      b:=pos(',',currentline);
      c:=PosEx(',',currentline,b+1);
      d:=PosEx(',',currentline,c+1);
      e:=pos(')',currentline);
      if (a<=0) or (b<=0) or (c<=0) or (d<=0) or (e<=a) then
        raise exception.Create(rsWrongSyntaxAOBSCANREGION);

      s1:=trim(copy(currentline,a+1,b-a-1));
      s2:=trim(copy(currentline,b+1,c-b-1));
      s3:=trim(copy(currentline,c+1,d-c-1));
      s4:=trim(copy(currentline,d+1,e-d-1));

      if not syntaxcheckonly then
      begin
        startaddress:=symhandler.getAddressFromName(s2);
        stopaddress:=symhandler.getAddressFromName(s3);

        m:=-1;
        for j:=0 to length(aobscanmodules)-1 do
          if (startaddress=aobscanmodules[j].minaddress) and (stopaddress=aobscanmodules[j].maxaddress) then
          begin
            m:=j;
            break;
          end;

        if m=-1 then
        begin
          setlength(aobscanmodules, length(aobscanmodules)+1);
          m:=length(aobscanmodules)-1;
          aobscanmodules[m].name:='<REGION>';
          aobscanmodules[m].minaddress:=startaddress;
          aobscanmodules[m].maxaddress:=stopaddress;
          aobscanmodules[m].protection:='';
          setlength(aobscanmodules[m].entries,0);
        end;

        j:=length(aobscanmodules[m].entries);
        setlength(aobscanmodules[m].entries, j+1);
        aobscanmodules[m].entries[j].name:=s1;
        aobscanmodules[m].entries[j].aobstring:=s4;
        aobscanmodules[m].entries[j].linenumber:=i;
      end
      else
        code[i]:='DEFINE('+s1+', 00000000)';

      continue;
    end;
  end;

  if length(aobscanmodules)>0 then
    result:=true;

  for i:=0 to length(aobscanmodules)-1 do
  begin
    j:=0;
    while threads>=cpucount do
    begin
      if (aobscanmodules[j].memscan<>nil) and aobscanmodules[j].memscan.waittilldone(50) then
      begin
        finished(j);
        break;
      end;

      if i>0 then
        j:=(j+1) mod i
      else
        break;
    end;

    inc(threads);
    aobscanmodules[i].memscan:=TMemScan.Create(nil);
    aobscanmodules[i].memscan.OnlyOne:=true;
    if aobscanmodules[i].protection<>'' then
      aobscanmodules[i].memscan.parseProtectionflags(aobscanmodules[i].protection);

    aobstrings:='';
    for j:=0 to length(aobscanmodules[i].entries)-1 do
      aobstrings:=aobstrings+'('+aobscanmodules[i].entries[j].aobstring+')';

    if length(aobscanmodules[i].entries)=1 then
      aobscanmodules[i].memscan.firstscan(soExactValue, vtByteArray, rtRounded, aobscanmodules[i].entries[0].aobstring, '', aobscanmodules[i].minaddress, aobscanmodules[i].maxaddress, true, false, false, false, fsmNotAligned)
    else
      aobscanmodules[i].memscan.firstscan(soExactValue, vtByteArrays, rtRounded, aobstrings, '', aobscanmodules[i].minaddress, aobscanmodules[i].maxaddress, true, false, false, false, fsmNotAligned);
  end;

  for i:=0 to length(aobscanmodules)-1 do
    if aobscanmodules[i].memscan<>nil then
    begin
      aobscanmodules[i].memscan.waittilldone;
      finished(i);
    end;

  if error then
    raise exception.Create(errorstring);
end;

function autoassemble2(code: tstrings;popupmessages: boolean;syntaxcheckonly:boolean; targetself: boolean ;var ceallocarray:TCEAllocArray; registeredsymbols: tstringlist=nil; exceptions: PCEExceptionListArray=nil):boolean;
{
registeredsymbols is a stringlist that is initialized by the caller as case insensitive and no duplicates
}


type tassembled=record
  address: ptrUint;
  bytes: TAssemblerbytes;
  createthreadandwait: integer;
end;


type tlabel=record
  defined: boolean;
  insideAllocatedMemory: boolean;
  address:ptrUint;
  labelname: string;
  assemblerline: integer;
  references: array of integer; //index of assembled array
  references2: array of integer; //index of assemblerlines array
end;
type tfullaccess=record
  address: ptrUint;
  size: dword;
end;
type tdefine=record
  name: string;
  whatever: string;
end;
type treassembleentry=record
  address: string;
end;
type trelocationkind=(rkNone, rkRelative, rkRipRelative);
type tdecodedinstruction=record
  length: integer;
  relocationkind: trelocationkind;
  relocationoffset: integer;
  relocationsize: integer;
end;
var i,j,k,l,e: integer;
    currentline: string;
    currentlinenr: integer;
    currentlinep: pchar;

    currentaddress: ptrUint;
    assembled: array of tassembled;
    x: ptruint;
    y,op,op2:dword;
    ok1,ok2:boolean;
    loadbinary: array of record
      address: string; //string since it might be a label/alloc/define
      filename: string;
    end;

    readmems: array of record
      bytelength: integer;
      bytes: PByteArray;
    end;
    reassembles: array of treassembleentry;


    globalallocs, allocs, kallocs, sallocs: array of tcealloc;
    labels: array of tlabel;
    defines: array of tdefine;
    fullaccess: array of tfullaccess;
    dealloc: array of PtrUInt;
    addsymbollist: array of string;
    deletesymbollist: array of string;
    createthread: array of string;
    createthreadandwait: array of record
      name: string;
      position: integer;
      timeout: dword;
    end;

//    aoblist: array of TAOBEntry;

    a,b,c,d: integer;
    s1,s2,s3: string;

    assemblerlines: array of string;

    varsize: integer;
    tokens: tstringlist;
    baseaddress: ptrUint;

    multilineinjection: tstringlist;
    include: tstringlist;
    testdword,bw: dword;
    testPtr, testPtr2: ptrUint;
    binaryfile: tmemorystream;

    incomment: boolean;

    bytebuf: PByteArray;

    processhandle: THandle;
    ProcessID: DWORD;

    bytes: tbytes;
    prefered: ptrUint;
    allocationprotection: dword;
    createthreadandwaitid: integer;
    hastryexcept: boolean;
    exceptionlist: TAAExceptionInfoList;

    oldhandle: thandle;
    oldsymhandler: TSymHandler;


    threadhandle: THandle;
    parameters: TStringList;
    potentiallabels: TStringList;
    strictmode: boolean;

    function TryGetAddressFromScript(scriptaddress: string; var address: ptrUint): boolean;
    var index: integer;
    begin
      result:=true;

      try
        address:=symhandler.getAddressFromName(scriptaddress);
        exit;
      except
        result:=false;
      end;

      for index:=0 to length(labels)-1 do
        if uppercase(labels[index].labelname)=uppercase(scriptaddress) then
        begin
          address:=labels[index].address;
          exit(true);
        end;

      for index:=0 to length(allocs)-1 do
        if uppercase(allocs[index].varname)=uppercase(scriptaddress) then
        begin
          address:=allocs[index].address;
          exit(true);
        end;

      {$ifndef net}
      for index:=0 to length(kallocs)-1 do
        if uppercase(kallocs[index].varname)=uppercase(scriptaddress) then
        begin
          address:=kallocs[index].address;
          exit(true);
        end;
      {$endif}

      for index:=0 to length(defines)-1 do
        if uppercase(defines[index].name)=uppercase(scriptaddress) then
        begin
          try
            address:=symhandler.getAddressFromName(defines[index].whatever);
            exit(true);
          except
            exit(false);
          end;
        end;
    end;

    function ReadSignedImmediate(memory: PByteArray; offset, size: integer): int64;
    begin
      case size of
        1: result:=PShortInt(@memory[offset])^;
        2: result:=PSmallInt(@memory[offset])^;
        4: result:=PInteger(@memory[offset])^;
        else result:=0;
      end;
    end;

    procedure WriteSignedImmediate(memory: PByteArray; offset, size: integer; value: int64);
    begin
      case size of
        1: PShortInt(@memory[offset])^:=shortint(value);
        2: PSmallInt(@memory[offset])^:=smallint(value);
        4: PInteger(@memory[offset])^:=integer(value);
      end;
    end;

    function IsCandidatePrefixByte(bt: byte): boolean;
    begin
      result:=(bt=$66) or (bt=$f2) or (bt=$f3);
    end;

    function HasModRM(const entry: topcode): boolean;
    begin
      result:=(entry.opcode1 in [eo_reg0..eo_reg7, eo_reg]) or
              (entry.opcode2 in [eo_reg0..eo_reg7, eo_reg]);
    end;

    function GetAddressSize(addressoverride: boolean): integer;
    begin
      if processhandler.is64Bit then
      begin
        if addressoverride then
          result:=4
        else
          result:=8;
      end
      else
      begin
        if addressoverride then
          result:=2
        else
          result:=4;
      end;
    end;

    function GetExtraOpcodeSize(extraopcode: textraopcode): integer;
    begin
      case extraopcode of
        eo_cb, eo_ib: result:=1;
        eo_cw, eo_iw: result:=2;
        eo_cd, eo_id: result:=4;
        eo_cp: result:=6;
        else result:=0;
      end;
    end;

    function GetMoffsSize(param: tparam; addressoverride: boolean): integer;
    begin
      case param of
        par_moffs8, par_moffs16, par_moffs32: result:=GetAddressSize(addressoverride);
        else result:=0;
      end;
    end;

    function GetModRMExtraSize(memory: PByteArray; modrmindex: integer; addresssize: integer; is64: boolean; var ripoffset: integer): integer;
    var modrm, sib, modvalue, rmvalue, basevalue: byte;
    begin
      result:=1;
      ripoffset:=-1;
      modrm:=memory[modrmindex];
      modvalue:=modrm shr 6;
      rmvalue:=modrm and 7;

      if modvalue=3 then
        exit;

      if addresssize=2 then
      begin
        case modvalue of
          0:
            if rmvalue=6 then
              inc(result,2);
          1: inc(result,1);
          2: inc(result,2);
        end;
        exit;
      end;

      if rmvalue=4 then
      begin
        inc(result);
        sib:=memory[modrmindex+1];
        basevalue:=sib and 7;
        case modvalue of
          0:
            if basevalue=5 then
              inc(result,4);
          1: inc(result,1);
          2: inc(result,4);
        end;
      end
      else
      begin
        case modvalue of
          0:
            if rmvalue=5 then
            begin
              if is64 and (addresssize=8) then
                ripoffset:=modrmindex+1;
              inc(result,4);
            end;
          1: inc(result,1);
          2: inc(result,4);
        end;
      end;
    end;

    function TryDecodeX86Instruction(memory: PByteArray; bytesread: ptruint; out info: tdecodedinstruction): boolean;
    var
      prefixcount, actualprefixcount, opcodeindex, candidateprefixcount, opcodebytesonlycount: integer;
      actualprefixes: array[0..2] of byte;
      bt, lastrex: byte;
      addressoverride: boolean;
      entry: topcode;
      entrybytes: array[0..2] of byte;
      prefixscore, bestscore: integer;
      candidateinfo: tdecodedinstruction;
      modrmindex, addresssize, ripoffset, immediatesize, moffssize, regvalue: integer;
      matched, sameprefixes: boolean;
      idx: integer;
      paramrelsize: integer;
    begin
      result:=false;
      fillchar(info, sizeof(info), 0);
      prefixcount:=0;
      actualprefixcount:=0;
      addressoverride:=false;
      lastrex:=0;

      while prefixcount<bytesread do
      begin
        bt:=memory[prefixcount];
        case bt of
          $f0, $2e, $36, $3e, $26, $64, $65:
            ;
          $f2, $f3, $66:
            begin
              if actualprefixcount<length(actualprefixes) then
              begin
                actualprefixes[actualprefixcount]:=bt;
                inc(actualprefixcount);
              end;
            end;
          $67:
            addressoverride:=true;
          else
          begin
            if processhandler.is64Bit and ((bt and $f0)=$40) then
              lastrex:=bt
            else
              break;
          end;
        end;

        inc(prefixcount);
      end;

      if prefixcount>=bytesread then
        exit;

      bestscore:=-1;
      for idx:=1 to opcodecount do
      begin
        entry:=opcodes[idx];
        if processhandler.is64Bit and entry.invalidin64bit then
          continue;
        if (not processhandler.is64Bit) and entry.invalidin32bit then
          continue;

        entrybytes[0]:=entry.bt1;
        entrybytes[1]:=entry.bt2;
        entrybytes[2]:=entry.bt3;

        candidateprefixcount:=0;
        while (candidateprefixcount<entry.bytes) and IsCandidatePrefixByte(entrybytes[candidateprefixcount]) do
          inc(candidateprefixcount);

        if candidateprefixcount>actualprefixcount then
          continue;

        matched:=true;
        sameprefixes:=candidateprefixcount=actualprefixcount;
        for opcodeindex:=0 to candidateprefixcount-1 do
        begin
          if actualprefixes[opcodeindex]<>entrybytes[opcodeindex] then
          begin
            matched:=false;
            break;
          end;
        end;

        if not matched then
          continue;

        opcodebytesonlycount:=entry.bytes-candidateprefixcount;
        if prefixcount+opcodebytesonlycount>bytesread then
          continue;

        for opcodeindex:=0 to opcodebytesonlycount-1 do
          if memory[prefixcount+opcodeindex]<>entrybytes[candidateprefixcount+opcodeindex] then
          begin
            matched:=false;
            break;
          end;

        if not matched then
          continue;

        candidateinfo.length:=prefixcount+opcodebytesonlycount;
        candidateinfo.relocationkind:=rkNone;
        candidateinfo.relocationoffset:=0;
        candidateinfo.relocationsize:=0;

        modrmindex:=candidateinfo.length;
        if HasModRM(entry) then
        begin
          if modrmindex>=bytesread then
            continue;

          regvalue:=(memory[modrmindex] shr 3) and 7;
          if (entry.opcode1 in [eo_reg0..eo_reg7]) and (regvalue<>(ord(entry.opcode1)-ord(eo_reg0))) then
            continue;
          if (entry.opcode2 in [eo_reg0..eo_reg7]) and (regvalue<>(ord(entry.opcode2)-ord(eo_reg0))) then
            continue;

          addresssize:=GetAddressSize(addressoverride);
          candidateinfo.length:=candidateinfo.length+GetModRMExtraSize(memory, modrmindex, addresssize, processhandler.is64Bit, ripoffset);

          if ripoffset<>-1 then
          begin
            candidateinfo.relocationkind:=rkRipRelative;
            candidateinfo.relocationoffset:=ripoffset;
            candidateinfo.relocationsize:=4;
          end;
        end;

        moffssize:=GetMoffsSize(entry.paramtype1, addressoverride)+GetMoffsSize(entry.paramtype2, addressoverride)+GetMoffsSize(entry.paramtype3, addressoverride);
        immediatesize:=GetExtraOpcodeSize(entry.opcode1)+GetExtraOpcodeSize(entry.opcode2)+moffssize;
        candidateinfo.length:=candidateinfo.length+immediatesize;

        paramrelsize:=0;
        if entry.paramtype1=par_rel8 then paramrelsize:=1 else
        if entry.paramtype1=par_rel16 then paramrelsize:=2 else
        if entry.paramtype1=par_rel32 then paramrelsize:=4 else
        if entry.paramtype2=par_rel8 then paramrelsize:=1 else
        if entry.paramtype2=par_rel16 then paramrelsize:=2 else
        if entry.paramtype2=par_rel32 then paramrelsize:=4 else
        if entry.paramtype3=par_rel8 then paramrelsize:=1 else
        if entry.paramtype3=par_rel16 then paramrelsize:=2 else
        if entry.paramtype3=par_rel32 then paramrelsize:=4;

        if paramrelsize>0 then
        begin
          candidateinfo.relocationkind:=rkRelative;
          candidateinfo.relocationsize:=paramrelsize;
          candidateinfo.relocationoffset:=candidateinfo.length-paramrelsize;
        end;

        if candidateinfo.length>bytesread then
          continue;

        if candidateinfo.length>15 then
          continue;

        if sameprefixes then
          prefixscore:=2
        else
          prefixscore:=1;

        prefixscore:=prefixscore*100 + opcodebytesonlycount*10;
        if candidateinfo.relocationkind<>rkNone then
          inc(prefixscore);

        if prefixscore>bestscore then
        begin
          info:=candidateinfo;
          bestscore:=prefixscore;
          result:=true;
        end;
      end;
    end;

    procedure BuildReassembledBytes(const sourceexpression: string; targetaddress: ptruint; var outbytes: TAssemblerBytes);
    var
      sourceaddress: ptruint;
      instructionbuffer: array[0..15] of byte;
      bytesread: ptruint;
      decoded: tdecodedinstruction;
      originaltarget: int64;
      newoffset: int64;
      index: integer;
    begin
      if processhandler.SystemArchitecture<>archX86 then
        raise exception.Create(Format(rsCouldNotDecodeInstructionForReassemble, [sourceexpression]));

      if not TryGetAddressFromScript(sourceexpression, sourceaddress) then
        raise exception.Create(Format(rsCouldNotBeFound, [sourceexpression]));

      if not ReadProcessMemory(processhandle, pointer(sourceaddress), @instructionbuffer[0], sizeof(instructionbuffer), bytesread) then
        raise exception.Create(Format(rsCouldNotDecodeInstructionForReassemble, [sourceexpression]));

      if not TryDecodeX86Instruction(@instructionbuffer[0], bytesread, decoded) then
        raise exception.Create(Format(rsCouldNotDecodeInstructionForReassemble, [sourceexpression]));

      setlength(outbytes, decoded.length);
      for index:=0 to decoded.length-1 do
        outbytes[index]:=instructionbuffer[index];

      case decoded.relocationkind of
        rkRelative:
        begin
          originaltarget:=sourceaddress+decoded.length+ReadSignedImmediate(@instructionbuffer[0], decoded.relocationoffset, decoded.relocationsize);
          newoffset:=originaltarget-(targetaddress+decoded.length);

          case decoded.relocationsize of
            1:
              if (newoffset<low(shortint)) or (newoffset>high(shortint)) then
                raise exception.Create(Format(rsReassembleTargetOutOfRange, [sourceexpression]));
            2:
              if (newoffset<low(smallint)) or (newoffset>high(smallint)) then
                raise exception.Create(Format(rsReassembleTargetOutOfRange, [sourceexpression]));
            4:
              if (newoffset<low(integer)) or (newoffset>high(integer)) then
                raise exception.Create(Format(rsReassembleTargetOutOfRange, [sourceexpression]));
          end;

          WriteSignedImmediate(@outbytes[0], decoded.relocationoffset, decoded.relocationsize, newoffset);
        end;

        rkRipRelative:
        begin
          originaltarget:=sourceaddress+decoded.length+ReadSignedImmediate(@instructionbuffer[0], decoded.relocationoffset, decoded.relocationsize);
          newoffset:=originaltarget-(targetaddress+decoded.length);
          if (newoffset<low(integer)) or (newoffset>high(integer)) then
            raise exception.Create(Format(rsReassembleTargetOutOfRange, [sourceexpression]));

          WriteSignedImmediate(@outbytes[0], decoded.relocationoffset, decoded.relocationsize, newoffset);
        end;
      end;
    end;

    procedure handleCreateThreadAndWait(ctawi: integer);
    begin
      if not TryGetAddressFromScript(createthreadandwait[ctawi].name, testptr) then
        raise exception.Create(Format(rsTheAddressInCreatethreadAndWaitIsNotValid, [createthreadandwait[ctawi].name]));

      threadhandle:=createremotethread(processhandle,nil,0,pointer(testptr),nil,0,bw);
      ok2:=threadhandle>0;

      if ok2 then
      begin
        try
          k:=createthreadandwait[ctawi].timeout;
          if k<=0 then y:=INFINITE else y:=k;

          if WaitForSingleObject(threadhandle, y)<>WAIT_OBJECT_0 then
            raise exception.Create('createthreadandwait did not execute properly');
        finally
          closehandle(threadhandle);
        end;
      end;

      createthreadandwait[ctawi].position:=-1;
    end;
begin
  setlength(readmems,0);
  setlength(reassembles,0);
  setlength(allocs,0);
  setlength(kallocs,0);
  setlength(globalallocs,0);
  setlength(sallocs,0);
  setlength(createthread,0);
  setlength(createthreadandwait,0);
  setlength(exceptionlist,0);
  hastryexcept:=false;

  currentaddress:=0;


  if syntaxcheckonly and (registeredsymbols<>nil) then
  begin
    //add the symbols as defined labels
    setlength(labels,registeredsymbols.count);
    for i:=0 to registeredsymbols.count-1 do
    begin
      labels[i].labelname:=registeredsymbols[i];
      labels[i].defined:=true;
      labels[i].address:=0;
      labels[i].assemblerline:=0;
      setlength(labels[i].references,0);
      setlength(labels[i].references2,0);
    end;
  end;

  if targetself then
  begin
    //get this function to use the symbolhandler that's pointing to CE itself and the self processid/handle
    oldhandle:=cefuncproc.ProcessHandle;
    processid:=getcurrentprocessid;
    processhandle:=getcurrentprocess;
    oldsymhandler:=symhandler;
    symhandler:=selfsymhandler;
    processhandler.processhandle:=processhandle;
  end
  else
  begin
    processid:=cefuncproc.ProcessID;
    processhandle:=cefuncproc.ProcessHandle;
  end;

  symhandler.waitforsymbolsloaded(true);

//{$ifndef standalonetrainer}
//  if pluginhandler=nil then exit; //Error. Cheat Engine is not properly configured


//2 pass scanner
  try
    potentiallabels:=TStringList.Create;
    potentiallabels.CaseSensitive:=false;

    setlength(assembled,1);
    setlength(kallocs,0);
    setlength(allocs,0);
    setlength(dealloc,0);
    setlength(assemblerlines,0);
    setlength(fullaccess,0);
    setlength(addsymbollist,0);
    setlength(deletesymbollist,0);
    setlength(defines,0);
    setlength(loadbinary,0);
//    setlength(aoblist,0);

    tokens:=tstringlist.Create;
    parameters:=TStringList.Create;

    incomment:=false;

    strictmode:=false;
    hastryexcept:=false;
    for i:=0 to code.Count-1 do
      if uppercase(TrimRight(code[i]))='{$STRICT}' then
        strictmode:=true
      else
      if uppercase(TrimRight(code[i]))='{$TRY}' then
      begin
        hastryexcept:=true;
      end;

    if hastryexcept then
      parseTryExcept(code, exceptionlist);

    removecomments(code);  //also trims each line
    unlabeledlabels(code);
    if not strictmode then
      getPotentialLabels(code, potentiallabels);
    aobscans(code, syntaxcheckonly);


    //first pass

    i:=0;
    while i<code.Count do
    begin
      try
        try
          currentline:=code[i];
          currentlinenr:=ptrUint(code.Objects[i]);

          //check if useless
          if length(currentline)=0 then continue;
          if copy(currentline,1,2)='//' then continue; //skip

          //do this first. Do not touch registersymbol with any kind of define/label/whatsoever
          if uppercase(copy(currentline,1,15))='REGISTERSYMBOL(' then
          begin
            //add this symbol to the register symbollist
            a:=pos('(',currentline);
            b:=pos(')',currentline);

            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              splitparameters(s1, parameters);

              for j:=0 to parameters.Count-1 do
              begin
                setlength(addsymbollist,length(addsymbollist)+1);
                addsymbollist[length(addsymbollist)-1]:=parameters[j];

                if registeredsymbols<>nil then
                  registeredsymbols.Add(parameters[j]);
              end;
            end
            else raise exception.Create(rsSyntaxError);

            continue;
          end;



          //apply defines (before DEFINE since define(bla, 123) and define(xxx, bla+123) should work


          //also, do not touch define with any previous define
          if uppercase(copy(currentline,1,7))='DEFINE(' then
          begin
            //syntax: alloc(x,size)    x=variable name size=bytes
            //allocate memory
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);
            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=copy(currentline,b+1,c-b-1);


              //apply earlier defines to the second part
              for j:=0 to length(defines)-1 do
                 s2:=replacetoken(s2,defines[j].name,defines[j].whatever);



              ok1:=true;
              for j:=0 to length(defines)-1 do
                if uppercase(defines[j].name)=uppercase(s1) then
                begin
                  //redefined from here on
                  ok1:=false;
                  defines[length(defines)-1].whatever:=s2
                end;

              if ok1 then //not duplicate, create it
              begin
                setlength(defines,length(defines)+1);
                defines[length(defines)-1].name:=s1;
                defines[length(defines)-1].whatever:=s2;
              end;

              continue;
            end else raise exception.Create(rsWrongSyntaxDEFINENameWhatever+' Got '+currentline);
          end;


          //normal loop code

          for j:=0 to length(defines)-1 do
             currentline:=replacetoken(currentline,defines[j].name,defines[j].whatever);



          setlength(assemblerlines,length(assemblerlines)+1);
          assemblerlines[length(assemblerlines)-1]:=currentline;


          //if the newline is empty then it has been handled and the plugin doesn't want it to be added for phase2
          if length(currentline)=0 then
          begin
            setlength(assemblerlines,length(assemblerlines)-1);
            continue;
          end;
          //otherwise it hasn't been handled, or it has been handled and the string is a compatible string that passes the phase1 tests (so variablenames converted to 00000000 and whatever else is needed)

          //plugins^^^

          if uppercase(copy(currentline,1,7))='ASSERT(' then //assert(address,aob)
          begin
            if not syntaxcheckonly then
            begin
              a:=pos('(',currentline);
              b:=pos(',',currentline);
              c:=pos(')',currentline);
              if (a>0) and (b>0) and (c>0) then
              begin
                s1:=trim(copy(currentline,a+1,b-a-1));
                s2:=trim(copy(currentline,b+1,c-b-1));

                testPtr:= symhandler.getAddressFromName(s1,false);

                setlength(bytes,0);
                try
                  ConvertStringToBytes(s2, true, bytes);
                except
                  raise exception.create(Format(rsIsNotAValidBytestring, [s2]));
                end;

                if length(bytes)>0 then
                begin
                  getmem(bytebuf,length(bytes));
                  try
                    if ReadProcessMemory(processhandle, pointer(testPtr), bytebuf, length(bytes),x) then
                    begin

                        for j:=0 to length(bytes)-1 do
                        begin
                          if bytes[j]>=0 then
                            if byte(bytes[j])<>bytebuf[j] then
                              raise exception.Create(Format(rsTheBytesAtAreNotWhatWasExpected, [s1]));
                        end;
                    end else raise exception.Create(Format(rsTheMemoryAtCanNotBeRead, [s1]));
                  finally
                    freemem(bytebuf);
                  end;

                end
                else raise exception.Create(Format(rsIsNotAValidBytestring, [s2]));

              end
              else
                raise exception.Create(rsWrongSyntaxASSERTAddress1122335566);
            end;

            setlength(assemblerlines,length(assemblerlines)-1);
            continue;
          end;
              {
          if uppercase(copy(currentline,1,12))='SHAREDALLOC(' then
          begin
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);
            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=trim(copy(currentline,b+1,c-b-1));

              try
                x:=strtoint(s2);
              except
                raise exception.Create(Format(rsIsNotAValidSize, [s2]));
              end;

              setlength(sallocs,length(sallocs)+1);
              sallocs[length(sallocs)-1].address:=allocateSharedMemoryIntoTargetProcess(s1,x);
              sallocs[length(sallocs)-1].varname:=s1;
              sallocs[length(sallocs)-1].size:=x;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;

            end
            else raise exception.Create(rsWrongSyntaxSHAREDALLOCNameSize);
          end;  }

          if uppercase(copy(currentline,1,12))='GLOBALALLOC(' then
          begin
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=PosEx(',',currentline,b+1);
            d:=pos(')',currentline);
            if (a>0) and (b>0) and (d>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              if c>0 then
              begin
                s2:=trim(copy(currentline,b+1,c-b-1));
                s3:=trim(copy(currentline,c+1,d-c-1));
              end
              else
              begin
                s2:=trim(copy(currentline,b+1,d-b-1));
                s3:='';
              end;

              try
                x:=strtoint(s2);
              except
                raise exception.Create(Format(rsIsNotAValidSize, [s2]));
              end;

              //define it here already
              if s3<>'' then
                symhandler.SetUserdefinedSymbolAllocSize(s1, x, symhandler.getAddressFromName(s3))
              else
                symhandler.SetUserdefinedSymbolAllocSize(s1, x);

              setlength(globalallocs,length(globalallocs)+1);
              globalallocs[length(globalallocs)-1].address:=symhandler.GetUserdefinedSymbolByName(s1);
              globalallocs[length(globalallocs)-1].varname:=s1;
              globalallocs[length(globalallocs)-1].size:=x;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;

            end
            else raise exception.Create(rsWrongSyntaxGLOBALALLOCNameSize);
          end;

          if uppercase(copy(currentline,1,8))='INCLUDE(' then
          begin
            a:=pos('(',currentline);
            b:=pos(')',currentline);

            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));

              if ExtractFileExt(uppercase(s1))='.' then
                s1:=s1+'CEA';

              if ExtractFileExt(uppercase(s1))='' then
                s1:=s1+'.CEA';

              if not fileexists(s1) then //check if it's inside the current location
              begin
                //if not, check the default paths
                s2:=cheatenginedir+'includes'+pathdelim+'s1';
                if fileexists(s2) then s1:=s2 else
                begin
                  s2:=cheatenginedir+s1;
                  if fileexists(s2) then s1:=s2
                  else
                  begin
                    s2:=tablesdir+s1;
                    if fileexists(s2) then s1:=s2;
                  end;
                end;

                if not fileexists(s1) then
                  raise exception.Create(Format(rsCouldNotBeFound, [s1]));
              end;







              include:=tstringlist.Create;
              try
                include.LoadFromFile(s1{$if FPC_FULLVERSION >= 030200}, true{$endif});
                removecomments(include);
                unlabeledlabels(include);

                for j:=i+1 to (i+1)+(include.Count-1) do
                  code.Insert(j,include[j-(i+1)]);
              finally
                include.Free;
              end;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;
            end
            else raise exception.Create(rsWrongSyntaxIncludeFilenameCea);
          end;

          if uppercase(copy(currentline,1,20))='CREATETHREADANDWAIT' then
          begin
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);
            if (a>0) and (c>0) then
            begin
              if (b>0) and (b<c) then
              begin
                s1:=trim(copy(currentline,a+1,b-a-1));
                s2:=trim(copy(currentline,b+1,c-b-1));
                try
                  x:=strtoint(s2);
                except
                  raise exception.Create(rsWrongSyntaxCreateThreadAddress);
                end;
              end
              else
              begin
                s1:=trim(copy(currentline,a+1,c-a-1));
                x:=0;
              end;

              setlength(createthreadandwait,length(createthreadandwait)+1);
              createthreadandwait[length(createthreadandwait)-1].name:=s1;
              createthreadandwait[length(createthreadandwait)-1].position:=length(assemblerlines)-1;
              createthreadandwait[length(createthreadandwait)-1].timeout:=x;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;
            end else raise exception.Create(rsWrongSyntaxCreateThreadAddress);
          end;

          if uppercase(copy(currentline,1,13))='CREATETHREAD(' then
          begin
            //load a binary file into memory , this one already executes BEFORE the 2nd pass to get addressnames correct
            a:=pos('(',currentline);
            b:=pos(')',currentline);
            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
            
              setlength(createthread,length(createthread)+1);
              createthread[length(createthread)-1]:=s1;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;
            end else raise exception.Create(rsWrongSyntaxCreateThreadAddress);
          end;

          if uppercase(copy(currentline,1,12))='LOADLIBRARY(' then
          begin
            //load a library into memory , this one already executes BEFORE the 2nd pass to get addressnames correct
            a:=pos('(',currentline);
            b:=pos(')',currentline);

            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));

              if pos(':',s1)=0 then
              begin
                s2:=extractfilename(s1);

                if fileexists(cheatenginedir+s2) then s1:=cheatenginedir+s2 else
                  if fileexists(getcurrentdir+'\'+s2) then s1:=getcurrentdir+'\'+s2 else
                    if fileexists(cheatenginedir+s1) then s1:=cheatenginedir+s1;

                //else just hope it's in the dll searchpath
              end; //else direct file path

              try
                InjectDll(s1,'');
                symhandler.reinitialize;
                symhandler.waitforsymbolsloaded
              except
                raise exception.create(Format(rsCouldNotBeInjected, [s1]));
              end;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;
            end else raise exception.Create(rsWrongSyntaxLoadLibraryFilename);
          end;

          if uppercase(copy(currentline,1,8))='READMEM(' then
          begin
            //read memory and place it here (readmem(address,size) )
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);
            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=trim(copy(currentline,b+1,c-b-1));

              //read memory and replace with lines of DB xx xx xx xx xx xx xx xx
              try
                testptr:=symhandler.getAddressFromName(s1);
              except
                if not syntaxcheckonly then
                  raise exception.Create(rsInvalidAddressForReadMem)
                else
                  testptr:=0;
              end;

              try
                a:=strtoint(s2);
              except
                raise exception.Create(rsInvalidSizeForReadMem);
              end;

              if a=0 then
                raise exception.Create(rsInvalidSizeForReadMem);


              getmem(bytebuf,a);
              try
                if not syntaxcheckonly then
                begin
                  if (not ReadProcessMemory(processhandle, pointer(testptr),bytebuf,a,x)) or (x<a) then
                    raise exception.Create(Format(rsTheMemoryAtCouldNotBeFullyRead, [s1]));
                end;
              except
                on e:exception do
                begin
                  if bytebuf<>nil then
                    freemem(bytebuf);

                  raise exception.create(e.Message);
                end;
              end;


              //still here so everything ok
              assemblerlines[length(assemblerlines)-1]:='<READMEM'+IntToStr(length(readmems))+'>';
              setlength(readmems, length(readmems)+1);
              readmems[length(readmems)-1].bytelength:=a;
              readmems[length(readmems)-1].bytes:=bytebuf;
              bytebuf:=nil;

              continue;



            end else raise exception.Create(rsWrongSyntaxReadMemAddressSize);

            continue;
          end;

          if uppercase(copy(currentline,1,11))='LOADBINARY(' then
          begin
            //load a binary file into memory
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);
            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=trim(copy(currentline,b+1,c-b-1));

              if not fileexists(s2) then raise exception.Create(Format(rsTheFileDoesNotExist, [s2]));

              setlength(loadbinary,length(loadbinary)+1);
              loadbinary[length(loadbinary)-1].address:=s1;
              loadbinary[length(loadbinary)-1].filename:=s2;

              setlength(assemblerlines,length(assemblerlines)-1);
              continue;
            end else raise exception.Create(rsWrongSyntaxLoadBinaryAddressFilename);
          end;

          if uppercase(copy(currentline,1,11))='REASSEMBLE(' then
          begin
            a:=pos('(',currentline);
            b:=pos(')',currentline);
            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              setlength(reassembles, length(reassembles)+1);
              reassembles[length(reassembles)-1].address:=s1;
              assemblerlines[length(assemblerlines)-1]:='<REASSEMBLE'+IntToStr(length(reassembles)-1)+'>';
              continue;
            end
            else
              raise exception.Create(rsWrongSyntaxReAssemble);
          end;



          if uppercase(copy(currentline,1,17))='UNREGISTERSYMBOL(' then
          begin
            //add this symbol to the register symbollist
            a:=pos('(',currentline);
            b:=pos(')',currentline);

            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              splitparameters(s1, parameters);

              if (parameters.Count=1) and (parameters[0]='*') and (registeredsymbols<>nil) then
              begin
                for j:=0 to registeredsymbols.Count-1 do
                begin
                  setlength(deletesymbollist,length(deletesymbollist)+1);
                  deletesymbollist[length(deletesymbollist)-1]:=registeredsymbols[j];
                end;
              end
              else
              begin
                for j:=0 to parameters.Count-1 do
                begin
                  setlength(deletesymbollist,length(deletesymbollist)+1);
                  deletesymbollist[length(deletesymbollist)-1]:=parameters[j];
                end;
              end;
            end
            else raise exception.Create(rsSyntaxError);

            setlength(assemblerlines,length(assemblerlines)-1);
            continue;
          end;

          //AOBSCAN used to live here, but he moved up

          //define
          if uppercase(copy(currentline,1,7))='STRUCT ' then
          begin
            replaceStructWithDefines(code, i);
            setlength(assemblerlines,length(assemblerlines)-1);
            dec(i); //repeat from this line
            continue;
          end;



          if uppercase(copy(currentline,1,11))='FULLACCESS(' then
          begin
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);

            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=trim(copy(currentline,b+1,c-b-1));

              setlength(fullaccess,length(fullaccess)+1);
              fullaccess[length(fullaccess)-1].address:=symhandler.getAddressFromName(s1);
              fullaccess[length(fullaccess)-1].size:=strtoint(s2);
            end else raise exception.Create(rsSyntaxErrorFullAccessAddressSize);

            setlength(assemblerlines,length(assemblerlines)-1);
            continue;
          end;


          if uppercase(copy(currentline,1,6))='LABEL(' then
          begin
            //syntax: label(x)  x=name of the label
            //later on in the code there has to be a line with "labelname:"
            a:=pos('(',currentline);
            b:=pos(')',currentline);

            if (a>0) and (b>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));

              splitparameters(s1, parameters);
              for k:=0 to parameters.Count-1 do
              begin
                s1:=parameters[k];

                val('$'+s1,j,a);
                if a=0 then raise exception.Create(Format(rsIsNotAValidIdentifier, [s1]));

                varsize:=length(s1);
                j:=0;
                while (j<length(labels)) and (length(labels[j].labelname)>varsize) do
                begin
                  if labels[j].labelname=s1 then
                    raise exception.Create(Format(rsIsBeingRedeclared, [s1]));
                  inc(j);
                end;

                j:=length(labels);
                l:=j;

                ok1:=false;
                for j:=0 to code.Count-1 do
                  if trim(code[j])=s1+':' then
                  begin
                    if ok1 then raise exception.Create(Format(rsLabelIsBeingDefinedMoreThanOnce, [s1]));
                    ok1:=true;
                  end;

                if not ok1 then raise exception.Create(Format(rsLabelIsNotDefinedInTheScript, [s1]));

                setlength(labels,length(labels)+1);
                labels[l].labelname:=s1;
                labels[l].defined:=false;
                setlength(labels[l].references,0);
                setlength(labels[l].references2,0);
              end;

              setlength(assemblerlines,length(assemblerlines)-1);

              continue;
            end else raise exception.Create(rsSyntaxError);
          end;

          if (uppercase(copy(currentline,1,8))='DEALLOC(') then
          begin
            if (ceallocarray<>nil) then//memory dealloc=possible
            begin

              //syntax: dealloc(x)  x=name of region to deallocate
              //later on in the code there has to be a line with "labelname:"
              a:=pos('(',currentline);
              b:=pos(')',currentline);

              if (a>0) and (b>0) then
              begin
                s1:=trim(copy(currentline,a+1,b-a-1));
                splitparameters(s1, parameters);

                if (parameters.Count=1) and (parameters[0]='*') then
                begin
                  for j:=0 to length(ceallocarray)-1 do
                  begin
                    setlength(dealloc,length(dealloc)+1);
                    dealloc[length(dealloc)-1]:=ceallocarray[j].address;
                  end;
                end
                else
                begin
                  for k:=0 to parameters.Count-1 do
                    for j:=0 to length(ceallocarray)-1 do
                      if uppercase(ceallocarray[j].varname)=uppercase(parameters[k]) then
                      begin
                        setlength(dealloc,length(dealloc)+1);
                        dealloc[length(dealloc)-1]:=ceallocarray[j].address;
                      end;
                end;
              end;
            end;
            setlength(assemblerlines,length(assemblerlines)-1);
            continue;
          end;

          //memory alloc
          if (uppercase(copy(currentline,1,6))='ALLOC(') or
             (uppercase(copy(currentline,1,8))='ALLOCNX(') or
             (uppercase(copy(currentline,1,8))='ALLOCXO(') then
          begin
            //syntax: alloc(x,size)    x=variable name size=bytes
            //or
            //syntax: alloc(x,size,prefered region)    x=variable name size=bytes
            //allocate memory
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=PosEx(',',currentline,b+1);
            d:=pos(')',currentline);



            if (a>0) and (b>0) and (d>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));

              if uppercase(copy(currentline,1,8))='ALLOCNX(' then
                allocationprotection:=PAGE_READWRITE
              else
              if uppercase(copy(currentline,1,8))='ALLOCXO(' then
                allocationprotection:=PAGE_EXECUTE_READ
              else
                allocationprotection:=PAGE_EXECUTE_READWRITE;

              if c>0 then
              begin
                s2:=trim(copy(currentline,b+1,c-b-1));
                s3:=trim(copy(currentline,c+1,d-c-1));
              end
              else
              begin
                s2:=trim(copy(currentline,b+1,d-b-1));
                s3:='';
              end;

              val('$'+s1,j,a);
              if a=0 then raise exception.Create(Format(rsIsNotAValidIdentifier, [s1]));

              varsize:=length(s1);

              //check for duplicate identifiers
              j:=0;
              while (j<length(allocs)) and (length(allocs[j].varname)>varsize) do
              begin
                if allocs[j].varname=s1 then
                  raise exception.Create(Format(rsTheIdentifierHasAlreadyBeenDeclared, [s1]));

                inc(j);
              end;

              j:=length(allocs);//quickfix

              setlength(allocs,length(allocs)+1);

              //longest varnames first so the rename of a shorter matching var wont override the longer one
              //move up the other allocs so I can inser this element (A linked list might have been better)
              for k:=length(allocs)-1 downto j+1 do
                allocs[k]:=allocs[k-1];

              allocs[j].varname:=s1;
              allocs[j].size:=StrToInt(s2);
              allocs[j].protection:=allocationprotection;
              if s3<>'' then
              begin

                allocs[j].prefered:=symhandler.getAddressFromName(s3);
              end
              else
                allocs[j].prefered:=0;


              setlength(assemblerlines,length(assemblerlines)-1);   //don't bother with this in the 2nd pass
              continue;
            end else raise exception.Create(rsWrongSyntaxALLOCIdentifierSizeinbytes);
          end;


          //replace ALLOC identifiers with values so the assemble error check doesnt crash on that
          if processhandler.is64bit then
          begin
            for j:=0 to length(allocs)-1 do
              currentline:=replacetoken(currentline,allocs[j].varname,'ffffffffffffffff');
          end
          else
          begin
            for j:=0 to length(allocs)-1 do
              currentline:=replacetoken(currentline,allocs[j].varname,'00000000');
          end;

          {$ifndef net}

          //memory kalloc
          if uppercase(copy(currentline,1,7))='KALLOC(' then
          begin
            if not DBKReadWrite then raise exception.Create(rsNeedToUseKernelmodeReadWriteprocessmemory);

            if DBKLoaded=false then
              raise exception.Create(rsSorryButWithoutTheDriverKALLOCWillNotFunction);

            //syntax: kalloc(x,size)    x=variable name size=bytes
            //kallocate memory
            a:=pos('(',currentline);
            b:=pos(',',currentline);
            c:=pos(')',currentline);

            if (a>0) and (b>0) and (c>0) then
            begin
              s1:=trim(copy(currentline,a+1,b-a-1));
              s2:=trim(copy(currentline,b+1,c-b-1));

              val('$'+s1,j,a);
              if a=0 then raise exception.Create(Format(rsIsNotAValidIdentifier, [s1]));

              varsize:=length(s1);

              //check for duplicate identifiers
              j:=0;
              while (j<length(kallocs)) and (length(kallocs[j].varname)>varsize) do
              begin
                if kallocs[j].varname=s1 then
                  raise exception.Create(Format(rsTheIdentifierHasAlreadyBeenDeclared, [s1]));

                inc(j);
              end;

              j:=length(kallocs);//quickfix

              setlength(kallocs,length(kallocs)+1);

              //longest varnames first so the rename of a shorter matching var wont override the longer one
              //move up the other kallocs so I can inser this element (A linked list might have been better)
              for k:=length(kallocs)-1 downto j+1 do
                kallocs[k]:=kallocs[k-1];

              kallocs[j].varname:=s1;
              kallocs[j].size:=StrToInt(s2);

              setlength(assemblerlines,length(assemblerlines)-1);   //don't bother with this in the 2nd pass
              continue;
            end else raise exception.Create(rsWrongSyntaxKallocIdentifierSizeinbytes);
          end;

          {$endif}

          //replace KALLOC identifiers with values so the assemble error check doesnt crash on that
          if processhandler.is64bit then
          begin
            for j:=0 to length(kallocs)-1 do
              currentline:=replacetoken(currentline,kallocs[j].varname,'ffffffffffffffff');
          end
          else
          begin
            for j:=0 to length(kallocs)-1 do
              currentline:=replacetoken(currentline,kallocs[j].varname,'00000000');
          end;



          //check for assembler errors
          //address

          if currentline[length(currentline)]=':' then
          begin
            try
              ok1:=false;
              for j:=0 to length(labels)-1 do
                if currentline=labels[j].labelname+':' then
                begin
                  labels[j].assemblerline:=length(assemblerlines)-1;
                  ok1:=true;
                  continue;
                end;

              if ok1 then continue; //no check


              //still here, so more complex
              if syntaxcheckonly and (registeredsymbols<>nil) then
              begin
                //replace tokens with registered symbols from the enable part
                for j:=0 to registeredsymbols.count-1 do
                  currentline:=replacetoken(currentline, registeredsymbols[j], '00000000');
              end;

              try
                j:=symhandler.getAddressFromName(copy(currentline,1,length(currentline)-1));
              except
                currentline:=inttohex(symhandler.getaddressfromname(copy(currentline,1,length(currentline)-1)),8)+':';
                assemblerlines[length(assemblerlines)-1]:=currentline;
              end;

              continue; //next line
            except
              if potentiallabels.IndexOf(copy(currentline,1,length(currentline)-1))=-1 then
                raise exception.Create(rsThisAddressSpecifierIsNotValid);

              j:=length(labels);
              setlength(labels,j+1);
              labels[j].labelname:=copy(currentline,1,length(currentline)-1);
              labels[j].assemblerline:=length(assemblerlines)-1;
              labels[j].defined:=false;
              labels[j].address:=0;
              labels[j].insideAllocatedMemory:=false;
              setlength(labels[j].references,0);
              setlength(labels[j].references2,0);

              continue;
            end;
          end;

          //replace label references with 00000000 so the assembler check doesn't complain about it

          if processhandler.is64bit then
          begin
            for j:=0 to length(labels)-1 do
              currentline:=replacetoken(currentline,labels[j].labelname,'ffffffffffffffff');
          end
          else
          begin
            for j:=0 to length(labels)-1 do
              currentline:=replacetoken(currentline,labels[j].labelname,'00000000');
          end;


          try
            //replace identifiers in the line with their address
            ok1:=false;
            try
              ok1:=assemble(currentline,currentaddress,assembled[0].bytes, apNone, true);
            except
            end;

            if not ok1 then
            begin
              for j:=0 to potentiallabels.count-1 do
              begin
                if processhandler.is64bit then
                  currentline:=replacetoken(currentline,potentiallabels[j],'ffffffffffffffff')
                else
                  currentline:=replacetoken(currentline,potentiallabels[j],'00000000');

                try
                  ok1:=assemble(currentline,currentaddress,assembled[0].bytes, apNone, true);
                  if ok1 then
                  begin
                    k:=length(labels);
                    setlength(labels, k+1);
                    labels[k].labelname:=potentiallabels[j];
                    labels[k].defined:=false;
                    labels[k].address:=0;
                    labels[k].insideAllocatedMemory:=false;
                    labels[k].assemblerline:=-1;
                    setlength(labels[k].references,0);
                    setlength(labels[k].references2,0);
                    break;
                  end;
                except
                end;
              end;
            end;

            if not ok1 then
              raise exception.Create('bla');
          except
            raise exception.Create(rsThisInstructionCanTBeCompiled);
          end;

        finally
          inc(i);
        end;

      except
        on E:exception do
          raise exception.Create(Format(rsErrorInLine, [IntToStr(currentlinenr), currentline, e.Message]));

      end;
    end;

    if length(addsymbollist)>0 then
    begin
      //now scan the addsymbollist entries for allocs and labels and see if they exist
      for i:=0 to length(addsymbollist)-1 do
      begin
        ok1:=false;
        for j:=0 to length(allocs)-1 do  //scan allocs
          if uppercase(addsymbollist[i])=uppercase(allocs[j].varname) then
          begin
            ok1:=true;
            break;
          end;

        if not ok1 then //scan labels
          for j:=0 to length(labels)-1 do
            if uppercase(addsymbollist[i])=uppercase(labels[j].labelname) then
            begin
              ok1:=true;
              break;
            end;

        if not ok1 then //scan defines
          for j:=0 to length(defines)-1 do
            if uppercase(addsymbollist[i])=uppercase(defines[j].name) then
            begin
              ok1:=true;
              break;
            end;

        if not ok1 then raise exception.Create(Format(rsWasSupposedToBeAddedToTheSymbollistButItIsnTDeclar, [addsymbollist[i]]));
      end;
    end;

    //check to see if the addresses are valid (label, alloc, define)
    if length(createthread)>0 then
      for i:=0 to length(createthread)-1 do
        if not TryGetAddressFromScript(createthread[i], testptr) then
          raise exception.Create(Format(rsTheAddressInCreatethreadIsNotValid, [createthread[i]]));

    if length(createthreadandwait)>0 then
      for i:=0 to length(createthreadandwait)-1 do
        if not TryGetAddressFromScript(createthreadandwait[i].name, testptr) then
          raise exception.Create(Format(rsTheAddressInCreatethreadAndWaitIsNotValid, [createthreadandwait[i].name]));

    if length(loadbinary)>0 then
      for i:=0 to length(loadbinary)-1 do
      begin
        ok1:=TryGetAddressFromScript(loadbinary[i].address, testptr);

        if not ok1 then raise exception.Create(Format(rsTheAddressInLoadbinaryIsNotValid, [loadbinary[i].address, loadbinary[i].filename]));

      end;


    if syntaxcheckonly then
    begin
      result:=true;
      exit;
    end;

    if popupmessages and (messagedlg(rsThisCodeCanBeInjectedAreYouSure, mtConfirmation	, [mbyes, mbno], 0)<>mryes) then exit;

    //allocate the memory

    if length(allocs)>0 then
    begin

      j:=0; //entry to go from
      prefered:=allocs[0].prefered;
      allocationprotection:=allocs[0].protection;
      x:=allocs[0].size;

      for i:=1 to length(allocs)-1 do
      begin
        //does this entry have a prefered location or a different protection?
        if ((allocs[i].prefered<>0) and (prefered<>allocs[i].prefered) and (prefered<>0)) or
           (allocs[i].protection<>allocationprotection) then
        begin
          if x>0 then //it has some previous entries with compatible locations
          begin
            k:=10;
            allocs[j].address:=0;
            while (k>0) and (allocs[j].address=0) do
            begin
              //try allocating until a memory region has been found (e.g due to quick allocating by the game)
              allocs[j].address:=ptrUint(virtualallocex(processhandle,FindFreeBlockForRegion(prefered,x),x, MEM_RESERVE or MEM_COMMIT,allocationprotection));
              if allocs[j].address=0 then OutputDebugString(rsFailureToAllocateMemory+' 1');

              dec(k);
            end;

            if allocs[j].address=0 then
              allocs[j].address:=ptrUint(virtualallocex(processhandle,nil,x, MEM_RESERVE or MEM_COMMIT,allocationprotection));

            if allocs[j].address=0 then OutputDebugString(rsFailureToAllocateMemory+' 2');

            //adjust the addresses of entries that are part of this block
            for k:=j+1 to i-1 do
              allocs[k].address:=allocs[k-1].address+allocs[k-1].size;
            x:=0;
          end;

          //new prefered address / protection
          j:=i;
          prefered:=allocs[i].prefered;
          allocationprotection:=allocs[i].protection;
        end;

        //no prefered location specified, OR same prefered location

        inc(x,allocs[i].size);
      end; //after the loop


      if x>0 then
      begin
        //adjust the address of entries that are part of this final block
        k:=10;
        allocs[j].address:=0;
        while (k>0) and (allocs[j].address=0) do
        begin
          i:=0;
          prefered:=ptrUint(FindFreeBlockForRegion(prefered,x));


          allocs[j].address:=ptrUint(virtualallocex(processhandle,pointer(prefered),x, MEM_RESERVE or MEM_COMMIT,allocationprotection));
          if allocs[j].address=0 then OutputDebugString(rsFailureToAllocateMemory+' 3');
          dec(k);
        end;

        if allocs[j].address=0 then
          allocs[j].address:=ptrUint(virtualallocex(processhandle,nil,x, MEM_RESERVE or MEM_COMMIT,allocationprotection));

        if allocs[j].address=0 then raise exception.create(rsFailureToAllocateMemory+' 4');

        for i:=j+1 to length(allocs)-1 do
          allocs[i].address:=allocs[i-1].address+allocs[i-1].size;


      end;
    end;

    {$ifndef net}
    //kernel alloc
    if length(kallocs)>0 then
    begin
      x:=0;
      for i:=0 to length(kallocs)-1 do
       inc(x,kallocs[i].size);

      kallocs[0].address:=ptrUint(KernelAlloc(x));

      for i:=1 to length(kallocs)-1 do
        kallocs[i].address:=kallocs[i-1].address+kallocs[i-1].size;
    end;
    {$endif}

    //-----------------------2nd pass------------------------
    //assemblerlines only contains label specifiers and assembler instructions
    
    setlength(assembled,0);
    for i:=0 to length(assemblerlines)-1 do
    begin
      currentline:=assemblerlines[i];

      createthreadandwaitid:=-1;
      for j:=0 to length(createthreadandwait)-1 do
      begin
        if (i>createthreadandwait[j].position) or (i=length(assemblerlines)-1) then
          createthreadandwaitid:=j;
      end;


      tokenize(currentline,tokens);
      //if alloc then replace with the address
      for j:=0 to length(allocs)-1 do
        currentline:=replacetoken(currentline,allocs[j].varname,IntToHex(allocs[j].address,8));

      //if kalloc then replace with the address
      for j:=0 to length(kallocs)-1 do
        currentline:=replacetoken(currentline,kallocs[j].varname,IntToHex(kallocs[j].address,8));

      for j:=0 to length(defines)-1 do
        currentline:=replacetoken(currentline,defines[j].name,defines[j].whatever);


      ok1:=false;
      if currentline[length(currentline)]<>':' then //if it's not a definition then
      begin
        for j:=0 to length(labels)-1 do
        begin
          if tokencheck(currentline,labels[j].labelname) then
          begin
            if not labels[j].defined then
            begin
              //the address hasn't been found yet
              //this is the part that causes those nops after a short jump below the current instruction

              //problem: The size of these instructions determine where this label will be defined

              //close
              s1:=replacetoken(currentline,labels[j].labelname,IntToHex(currentaddress,8));

              //far and big

              if processhandler.SystemArchitecture=archarm then
              begin
                currentline:=replacetoken(currentline,labels[j].labelname,IntToHex(currentaddress+$4FFFFF8,8));
              end
              else
              begin
                if (processhandler.is64Bit) then //and not in region
                  currentline:=replacetoken(currentline,labels[j].labelname,IntToHex(currentaddress+$1000FFFFF,8))
                else
                  currentline:=replacetoken(currentline,labels[j].labelname,IntToHex(currentaddress+$FFFFF,8));
              end;



              setlength(assembled,length(assembled)+1);
              assembled[length(assembled)-1].address:=currentaddress;
              assembled[length(assembled)-1].createthreadandwait:=createthreadandwaitid;
              assemble(currentline,currentaddress,assembled[length(assembled)-1].bytes, apnone, true);
              a:=length(assembled[length(assembled)-1].bytes);

              assemble(s1,currentaddress,assembled[length(assembled)-1].bytes, apnone, true);
              b:=length(assembled[length(assembled)-1].bytes);

              if a>b then //pick the biggest one
                assemble(currentline,currentaddress,assembled[length(assembled)-1].bytes);

              setlength(labels[j].references,length(labels[j].references)+1);
              labels[j].references[length(labels[j].references)-1]:=length(assembled)-1;

              setlength(labels[j].references2,length(labels[j].references2)+1);
              labels[j].references2[length(labels[j].references2)-1]:=i;

              inc(currentaddress,length(assembled[length(assembled)-1].bytes));
              ok1:=true;
            end else currentline:=replacetoken(currentline,labels[j].labelname,IntToHex(labels[j].address,8));

            break;
          end;
        end;
      end;

      if ok1 then continue;

      if currentline[length(currentline)]=':' then
      begin
        ok1:=false;
        for j:=0 to length(labels)-1 do
        begin
          if i=labels[j].assemblerline then
          begin
            labels[j].address:=currentaddress;
            labels[j].defined:=true;
            ok1:=true;


            //reassemble the instructions that had no target
            for k:=0 to length(labels[j].references)-1 do
            begin
              a:=length(assembled[labels[j].references[k]].bytes); //original size of the assembled code
              s1:=replacetoken(assemblerlines[labels[j].references2[k]],labels[j].labelname,IntToHex(labels[j].address,8));
              {$ifdef cpu64}
              if processhandler.is64Bit then
                assemble(s1,assembled[labels[j].references[k]].address,assembled[labels[j].references[k]].bytes)
              else
              {$endif}
              assemble(s1,assembled[labels[j].references[k]].address,assembled[labels[j].references[k]].bytes, apLong);


              b:=length(assembled[labels[j].references[k]].bytes); //new size

              setlength(assembled[labels[j].references[k]].bytes,a); //original size (original size is always bigger or equal than newsize)
              //fill the difference with nops (not the most efficient approach, but it should work)
              if processhandler.SystemArchitecture=archarm then
              begin
                for l:=0 to ((a-b+3) div 4)-1 do
                  pdword(@assembled[labels[j].references[k]].bytes[b+l*4])^:=$e1a00000;      //<mov r0,r0: (nop equivalent)
              end
              else
              begin
                for l:=b to a-1 do
                  assembled[labels[j].references[k]].bytes[l]:=$90; //nop
              end;
            end;


            break;
          end;
        end;
        if ok1 then continue;

        try
          currentaddress:=symhandler.getAddressFromName(copy(currentline,1,length(currentline)-1));
          continue; //next line
        except
          raise exception.Create(rsThisAddressSpecifierIsNotValid);
        end;
      end;


      setlength(assembled,length(assembled)+1);
      assembled[length(assembled)-1].address:=currentaddress;
    assembled[length(assembled)-1].createthreadandwait:=createthreadandwaitid;

      if (currentline<>'') and (currentline[1]='<') then //special assembler instruction
      begin

        if copy(currentline,1,11)='<REASSEMBLE' then
        begin
          l:=StrToInt(copy(currentline,12,length(currentline)-12));
          BuildReassembledBytes(reassembles[l].address, currentaddress, assembled[length(assembled)-1].bytes);
        end
        else
        if copy(currentline,1,8)='<READMEM' then
        begin
          //lets try this for once
          sscanf(currentline, '<READMEM%d>', [@l]);
          setlength(assembled[length(assembled)-1].bytes, readmems[l].bytelength);
          CopyMemory(@assembled[length(assembled)-1].bytes[0], readmems[l].bytes, readmems[l].bytelength);
        end
        else
          assemble(currentline,currentaddress,assembled[length(assembled)-1].bytes);
      end
      else
        assemble(currentline,currentaddress,assembled[length(assembled)-1].bytes);

      inc(currentaddress,length(assembled[length(assembled)-1].bytes));
    end;
    //end of loop

    ok2:=true;

    //unprotectmemory
    for i:=0 to length(fullaccess)-1 do
    begin
      virtualprotectex(processhandle,pointer(fullaccess[i].address),fullaccess[i].size,PAGE_EXECUTE_READWRITE,op);

      if (fullaccess[i].address>$80000000) and (DBKLoaded) then
        MakeWritable(fullaccess[i].address,(fullaccess[i].size div 4096)*4096,false);
    end;

    //load binaries
    if length(loadbinary)>0 then
      for i:=0 to length(loadbinary)-1 do
      begin
        ok1:=TryGetAddressFromScript(loadbinary[i].address, testptr);

        if ok1 then
        begin
          binaryfile:=tmemorystream.Create;
          try
            binaryfile.LoadFromFile(loadbinary[i].filename);
            ok2:=writeprocessmemory(processhandle,pointer(testptr),binaryfile.Memory,binaryfile.Size,x);
          finally
            binaryfile.free;
          end;
        end;
      end;

    //we're still here so, inject it
    for i:=0 to length(assembled)-1 do
    begin
      testptr:=assembled[i].address;
      ok1:=virtualprotectex(processhandle,pointer(testptr),length(assembled[i].bytes),PAGE_EXECUTE_READWRITE,op);
      ok1:=WriteProcessMemory(processhandle,pointeR(testptr),@assembled[i].bytes[0],length(assembled[i].bytes),x);
      virtualprotectex(processhandle,pointer(testptr),length(assembled[i].bytes),op,op2);

      if not ok1 then ok2:=false;

      if ok2 and (assembled[i].createthreadandwait<>-1) then
      begin
        for j:=0 to assembled[i].createthreadandwait do
          if createthreadandwait[j].position<>-1 then
            handleCreateThreadAndWait(j);
      end;
    end;

    for i:=0 to length(createthreadandwait)-1 do
      if createthreadandwait[i].position<>-1 then
        handleCreateThreadAndWait(i);

    if not ok2 then
    begin
      if popupmessages then showmessage(rsNotAllInstructionsCouldBeInjected)
    end
    else
    begin
      //if ceallocarray<>nil then
      begin
        //see if all allocs are deallocated
        if (length(dealloc)>0) and (length(dealloc)=length(ceallocarray)) then //free everything
        begin
          {$ifdef cpu64}
          baseaddress:=ptrUint($FFFFFFFFFFFFFFFF);
          {$else}
          baseaddress:=$FFFFFFFF;
          {$endif}

          for i:=0 to length(ceallocarray)-1 do
          begin
            if ceallocarray[i].address<baseaddress then
              baseaddress:=ceallocarray[i].address;
          end;

          virtualfreeex(processhandle,pointer(baseaddress),0,MEM_RELEASE);
        end;

        setlength(ceallocarray,length(allocs));
        for i:=0 to length(allocs)-1 do
          ceallocarray[i]:=allocs[i];
      end;

      if exceptions<>nil then
      begin
        if (length(exceptions^)>0) and AutoAssemblerExceptionHandlerHasEntries then
        begin
          for i:=0 to length(exceptions^)-1 do
            AutoAssemblerExceptionHandlerRemoveExceptionRange(exceptions^[i]);

          AutoAssemblerExceptionHandlerApplyChanges;
        end;

        if length(exceptionlist)>0 then
        begin
          InitializeAutoAssemblerExceptionHandler;

          for i:=length(exceptionlist)-1 downto 0 do
          begin
            if (not TryGetAddressFromScript(exceptionlist[i].trylabel, x)) or
              (not TryGetAddressFromScript(exceptionlist[i].exceptlabel, testPtr2)) then
              raise exception.Create('Failed to resolve {$TRY}/{$EXCEPT} labels');

            AutoAssemblerExceptionHandlerAddExceptionRange(x, testPtr2);
          end;

          AutoAssemblerExceptionHandlerApplyChanges;
        end;

        setlength(exceptions^, length(exceptionlist));
        for i:=0 to length(exceptionlist)-1 do
        begin
          if not TryGetAddressFromScript(exceptionlist[i].trylabel, x) then
            raise exception.Create('Failed to resolve {$TRY} label');

          exceptions^[i]:=x;
        end;
      end;





      //check the addsymbollist array and deletesymbollist array

      //first delete
      for i:=0 to length(deletesymbollist)-1 do
        symhandler.DeleteUserdefinedSymbol(deletesymbollist[i]);

      //now scan the addsymbollist array and add them to the userdefined list
      for i:=0 to length(addsymbollist)-1 do
      begin
        ok1:=false;
        for j:=0 to length(allocs)-1 do
          if uppercase(addsymbollist[i])=uppercase(allocs[j].varname) then
          begin
            try
              symhandler.DeleteUserdefinedSymbol(addsymbollist[i]); //delete old one so you can add the new one
              symhandler.AddUserdefinedSymbol(inttohex(allocs[j].address,8),addsymbollist[i], true);
              ok1:=true;
            except
              //don't crash when it's already defined or address=0
            end;

            break;
          end;

        if not ok1 then
          for j:=0 to length(labels)-1 do
            if uppercase(addsymbollist[i])=uppercase(labels[j].labelname) then
            begin
              try
                symhandler.DeleteUserdefinedSymbol(addsymbollist[i]); //delete old one so you can add the new one
                symhandler.AddUserdefinedSymbol(inttohex(labels[j].address,8),addsymbollist[i]);
                ok1:=true;
              except
                //don't crash when it's already defined or address=0
              end;

            end;

        if not ok1 then
          for j:=0 to length(defines)-1 do
            if uppercase(addsymbollist[i])=uppercase(defines[j].name) then
            begin
              try
                symhandler.DeleteUserdefinedSymbol(addsymbollist[i]); //delete old one so you can add the new one
                symhandler.AddUserdefinedSymbol(defines[j].whatever, addsymbollist[i]);
                ok1:=true;
              except
              end;
            end;
      end;

      //still here, so create threads if needed
      if length(createthread)>0 then
        for i:=0 to length(createthread)-1 do
        begin
          ok1:=TryGetAddressFromScript(createthread[i], testptr);

          if ok1 then //address found
          begin
            try
              threadhandle:=createremotethread(processhandle,nil,0,pointer(testptr),nil,0,bw);
              ok2:=threadhandle>0;

              if ok2 then
                closehandle(threadhandle);
            finally
            end;
          end;
        end;

      if popupmessages then
      begin
        s1:='';
        for i:=0 to length(globalallocs)-1 do
          s1:=s1+#13#10+globalallocs[i].varname+'='+IntToHex(globalallocs[i].address,8);


        for i:=0 to length(allocs)-1 do
          s1:=s1+#13#10+allocs[i].varname+'='+IntToHex(allocs[i].address,8);

        if length(kallocs)>0 then
        begin
          s1:=#13#10+rsTheFollowingKernelAddressesWhereAllocated+':';
          for i:=0 to length(kallocs)-1 do
            s1:=s1+#13#10+kallocs[i].varname+'='+IntToHex(kallocs[i].address,8);
        end;

        showmessage(rsTheCodeInjectionWasSuccessfull+s1);
      end;
    end;

    result:=ok2;

  finally
    for i:=0 to length(assembled)-1 do
      setlength(assembled[i].bytes,0);

    setlength(assembled,0);

    for i:=0 to length(readmems)-1 do
      if readmems[i].bytes<>nil then
        freemem(readmems[i].bytes);

    setlength(readmems,0);



    tokens.free;
    parameters.free;
    potentiallabels.Free;

    if targetself then
    begin
      processhandler.processhandle:=oldhandle;
      symhandler:=oldsymhandler;
    end;
  end;
end;


function getenableanddisablepos(code:tstrings;var enablepos,disablepos: integer): boolean;
var i,j: integer;
    currentline: string;
begin
  result:=false;
  enablepos:=-1;
  disablepos:=-1;

  for i:=0 to code.Count-1 do
  begin
    currentline:=code[i];
    j:=pos('//',currentline);
    if j>0 then
      currentline:=copy(currentline,1,j-1);

    while (length(currentline)>0) and (currentline[1]=' ') do currentline:=copy(currentline,2,length(currentline)-1);
    while (length(currentline)>0) and (currentline[length(currentline)]=' ') do currentline:=copy(currentline,1,length(currentline)-1);

    if length(currentline)=0 then continue;
    if copy(currentline,1,2)='//' then continue; //skip

    if (uppercase(currentline))='[ENABLE]' then
    begin
      result:=true; //there's at least a enable section, so it's ok
      if enablepos<>-1 then
      begin
        enablepos:=-2;
        exit;
      end;

      enablepos:=i;
    end;

    if (uppercase(currentline))='[DISABLE]' then
    begin
      if disablepos<>-1 then
      begin
        disablepos:=-2;
        exit;
      end;

      disablepos:=i;
    end;

  end;
end;


procedure getScript(code: TStrings; newscript: tstrings; enablescript: boolean);
{
removes the enable or disable section from a script leaving only the outer code and the selected script routine
}
var
  i: integer;
  insideenable: boolean;
  insidedisable: boolean;
begin
  insideenable:=false;
  insidedisable:=false;

  for i:=0 to code.Count-1 do
  begin
    if (uppercase(trim(code[i])))='[ENABLE]' then
    begin
      insideenable:=true;
      insidedisable:=false;
      continue;
    end;

    if (uppercase(trim(code[i])))='[DISABLE]' then
    begin
      insideenable:=false;
      insidedisable:=true;
      continue;
    end;

    //
    if ((not insideenable) and (not insidedisable)) or
       (insideenable and enablescript) or
       (insidedisable and not enablescript) then newscript.AddObject(code[i], code.Objects[i]);



  end;

end;

procedure stripCPUspecificCode(code: tstrings; strip32bit: boolean);
var i: integer;
  s: string;
  inexcludedbitblock: boolean;

begin
  inexcludedbitblock:=false;
  for i:=0 to code.Count-1 do
  begin
    s:=uppercase(Trim(code[i]));

    if s='[32-BIT]' then
    begin
      if strip32bit then
        inexcludedbitblock:=true;
      code[i]:=' ';
    end;

    if s='[/32-BIT]' then
    begin
      if strip32bit then
        inexcludedbitblock:=false;
      code[i]:=' ';
    end;

    if s='[64-BIT]' then
    begin
      if not strip32bit then
        inexcludedbitblock:=true;
      code[i]:=' ';
    end;

    if s='[/64-BIT]' then
    begin
      if not strip32bit then
        inexcludedbitblock:=false;
      code[i]:=' ';
    end;

    if inexcludedbitblock then
      code[i]:=' ';



  end;
end;

function autoassemble(code: Tstrings; popupmessages,enable,syntaxcheckonly, targetself: boolean;var CEAllocarray: TCEAllocArray; registeredsymbols: tstringlist=nil; exceptions: PCEExceptionListArray=nil): boolean; overload;
{
targetself defines if the process that gets injected to is CE itself or the target process
}
var tempstrings: tstringlist;
  i: integer;
    enablepos,disablepos: integer;
  strip32bitcode: boolean;
begin
  //add line numbers to the code
  for i:=0 to code.Count-1 do
    code.Objects[i]:=pointer(i+1);

  getenableanddisablepos(code,enablepos,disablepos);

  result:=false;
  
  if enablepos=-2 then
  begin
    if not popupmessages then exit;
    raise exception.Create(rsYouCanOnlyHaveOneEnableSection);
  end;

  if disablepos=-2 then
  begin
    if not popupmessages then exit;
    raise exception.Create(rsYouCanOnlyHaveOneDisableSection);
  end;

  tempstrings:=tstringlist.create;
  try
    if (enablepos=-1) and (disablepos=-1) then
    begin
      //everything
      tempstrings.AddStrings(code);
    end
    else
    begin
      if (enablepos=-1) then
      begin
        if not popupmessages then exit;
        raise exception.Create(rsYouHavnTSpecifiedAEnableSection);

      end;

      if (disablepos=-1) then
      begin
        if not popupmessages then exit;
        raise exception.Create(rsYouHavnTSpecifiedADisableSection);
        
      end;

      if enable then
      begin
        getscript(code, tempstrings, true);
      end
      else
      begin
        getscript(code, tempstrings,false);
      end;
    end;

    strip32bitcode:=processhandler.is64Bit;
    if targetself then
      strip32bitcode:={$ifdef cpu64}true{$else}false{$endif};

    Stripcpuspecificcode(tempstrings, strip32bitcode);

    result:=autoassemble2(tempstrings,popupmessages,syntaxcheckonly,targetself,ceallocarray, registeredsymbols, exceptions);
  finally
    tempstrings.Free;
  end;
end;

function autoassemble(code: Tstrings; popupmessages,enable,syntaxcheckonly, targetself: boolean):boolean; overload;
var aa: TCEAllocArray;
begin
  setlength(aa,0);
  result:=autoassemble(code,popupmessages,enable,syntaxcheckonly,targetself,aa,nil);
end;

function autoassemble(code: tstrings;popupmessages: boolean):boolean; overload;
var aa: TCEAllocArray;
begin
  setlength(aa,0);
  result:=autoassemble(code,popupmessages,true,false,false,aa,nil);
end;


end.




