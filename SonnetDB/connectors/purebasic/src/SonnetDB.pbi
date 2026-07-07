; SonnetDB PureBasic connector over the native C ABI.

EnableExplicit

#SonnetDB_TypeNull = 0
#SonnetDB_TypeInt64 = 1
#SonnetDB_TypeDouble = 2
#SonnetDB_TypeBool = 3
#SonnetDB_TypeText = 4

CompilerSelect #PB_Compiler_OS
  CompilerCase #PB_OS_Windows
    #SonnetDB_DefaultNativeLibrary = "SonnetDB.Native.dll"
  CompilerCase #PB_OS_Linux
    #SonnetDB_DefaultNativeLibrary = "SonnetDB.Native.so"
  CompilerDefault
    #SonnetDB_DefaultNativeLibrary = "SonnetDB.Native"
CompilerEndSelect

PrototypeC.i SonnetDbOpenPrototype(*dataSource)
PrototypeC SonnetDbClosePrototype(*connection)
PrototypeC.i SonnetDbExecutePrototype(*connection, *sql)
PrototypeC SonnetDbResultFreePrototype(*result)
PrototypeC.l SonnetDbResultRecordsAffectedPrototype(*result)
PrototypeC.l SonnetDbResultColumnCountPrototype(*result)
PrototypeC.i SonnetDbResultColumnNamePrototype(*result, ordinal.l)
PrototypeC.l SonnetDbResultNextPrototype(*result)
PrototypeC.l SonnetDbResultValueTypePrototype(*result, ordinal.l)
PrototypeC.q SonnetDbResultValueInt64Prototype(*result, ordinal.l)
PrototypeC.d SonnetDbResultValueDoublePrototype(*result, ordinal.l)
PrototypeC.l SonnetDbResultValueBoolPrototype(*result, ordinal.l)
PrototypeC.i SonnetDbResultValueTextPrototype(*result, ordinal.l)
PrototypeC.l SonnetDbFlushPrototype(*connection)
PrototypeC.l SonnetDbCopyStringPrototype(*buffer, bufferLength.l)

Global SonnetDbLibrary.i
Global SonnetDbLastLoadError.s

Global SonnetDbOpen_.SonnetDbOpenPrototype
Global SonnetDbClose_.SonnetDbClosePrototype
Global SonnetDbExecute_.SonnetDbExecutePrototype
Global SonnetDbResultFree_.SonnetDbResultFreePrototype
Global SonnetDbResultRecordsAffected_.SonnetDbResultRecordsAffectedPrototype
Global SonnetDbResultColumnCount_.SonnetDbResultColumnCountPrototype
Global SonnetDbResultColumnName_.SonnetDbResultColumnNamePrototype
Global SonnetDbResultNext_.SonnetDbResultNextPrototype
Global SonnetDbResultValueType_.SonnetDbResultValueTypePrototype
Global SonnetDbResultValueInt64_.SonnetDbResultValueInt64Prototype
Global SonnetDbResultValueDouble_.SonnetDbResultValueDoublePrototype
Global SonnetDbResultValueBool_.SonnetDbResultValueBoolPrototype
Global SonnetDbResultValueText_.SonnetDbResultValueTextPrototype
Global SonnetDbFlush_.SonnetDbFlushPrototype
Global SonnetDbVersion_.SonnetDbCopyStringPrototype
Global SonnetDbLastError_.SonnetDbCopyStringPrototype

Procedure.i SonnetDB_ToUtf8(Text.s)
  Protected byteLength.i = StringByteLength(Text, #PB_UTF8) + 1
  Protected *buffer = AllocateMemory(byteLength)
  If *buffer
    PokeS(*buffer, Text, -1, #PB_UTF8)
  EndIf
  ProcedureReturn *buffer
EndProcedure

Procedure.i SonnetDB_Resolve(Symbol.s)
  Protected *function = GetFunction(SonnetDbLibrary, Symbol)
  If *function = 0
    SonnetDbLastLoadError = "Cannot resolve SonnetDB native symbol: " + Symbol
  EndIf
  ProcedureReturn *function
EndProcedure

Procedure.i SonnetDB_Load(NativeLibraryPath.s = "")
  If SonnetDbLibrary <> 0
    ProcedureReturn #True
  EndIf

  If NativeLibraryPath = ""
    NativeLibraryPath = #SonnetDB_DefaultNativeLibrary
  EndIf

  SonnetDbLibrary = OpenLibrary(#PB_Any, NativeLibraryPath)
  If SonnetDbLibrary = 0
    SonnetDbLastLoadError = "Cannot load SonnetDB native library: " + NativeLibraryPath
    ProcedureReturn #False
  EndIf

  SonnetDbOpen_ = SonnetDB_Resolve("sonnetdb_open")
  SonnetDbClose_ = SonnetDB_Resolve("sonnetdb_close")
  SonnetDbExecute_ = SonnetDB_Resolve("sonnetdb_execute")
  SonnetDbResultFree_ = SonnetDB_Resolve("sonnetdb_result_free")
  SonnetDbResultRecordsAffected_ = SonnetDB_Resolve("sonnetdb_result_records_affected")
  SonnetDbResultColumnCount_ = SonnetDB_Resolve("sonnetdb_result_column_count")
  SonnetDbResultColumnName_ = SonnetDB_Resolve("sonnetdb_result_column_name")
  SonnetDbResultNext_ = SonnetDB_Resolve("sonnetdb_result_next")
  SonnetDbResultValueType_ = SonnetDB_Resolve("sonnetdb_result_value_type")
  SonnetDbResultValueInt64_ = SonnetDB_Resolve("sonnetdb_result_value_int64")
  SonnetDbResultValueDouble_ = SonnetDB_Resolve("sonnetdb_result_value_double")
  SonnetDbResultValueBool_ = SonnetDB_Resolve("sonnetdb_result_value_bool")
  SonnetDbResultValueText_ = SonnetDB_Resolve("sonnetdb_result_value_text")
  SonnetDbFlush_ = SonnetDB_Resolve("sonnetdb_flush")
  SonnetDbVersion_ = SonnetDB_Resolve("sonnetdb_version")
  SonnetDbLastError_ = SonnetDB_Resolve("sonnetdb_last_error")

  If SonnetDbOpen_ = 0 Or SonnetDbClose_ = 0 Or SonnetDbExecute_ = 0 Or SonnetDbResultFree_ = 0 Or SonnetDbResultRecordsAffected_ = 0 Or SonnetDbResultColumnCount_ = 0 Or SonnetDbResultColumnName_ = 0 Or SonnetDbResultNext_ = 0 Or SonnetDbResultValueType_ = 0 Or SonnetDbResultValueInt64_ = 0 Or SonnetDbResultValueDouble_ = 0 Or SonnetDbResultValueBool_ = 0 Or SonnetDbResultValueText_ = 0 Or SonnetDbFlush_ = 0 Or SonnetDbVersion_ = 0 Or SonnetDbLastError_ = 0
    CloseLibrary(SonnetDbLibrary)
    SonnetDbLibrary = 0
    ProcedureReturn #False
  EndIf

  ProcedureReturn #True
EndProcedure

Procedure SonnetDB_Unload()
  If SonnetDbLibrary <> 0
    CloseLibrary(SonnetDbLibrary)
    SonnetDbLibrary = 0
  EndIf
EndProcedure

Procedure.s SonnetDB_CopyUtf8String(CopyString.SonnetDbCopyStringPrototype)
  Protected bufferLength.l = 4096
  Protected required.l
  Protected result.s
  Protected *buffer = AllocateMemory(bufferLength)

  If *buffer = 0
    ProcedureReturn ""
  EndIf

  required = CopyString(*buffer, bufferLength)
  If required < 0
    FreeMemory(*buffer)
    ProcedureReturn ""
  EndIf

  If required >= bufferLength
    FreeMemory(*buffer)
    bufferLength = required + 1
    *buffer = AllocateMemory(bufferLength)
    If *buffer = 0
      ProcedureReturn ""
    EndIf

    required = CopyString(*buffer, bufferLength)
    If required < 0
      FreeMemory(*buffer)
      ProcedureReturn ""
    EndIf
  EndIf

  result = PeekS(*buffer, -1, #PB_UTF8)
  FreeMemory(*buffer)
  ProcedureReturn result
EndProcedure

Procedure.s SonnetDB_LastError()
  If SonnetDbLibrary = 0 Or SonnetDbLastError_ = 0
    ProcedureReturn SonnetDbLastLoadError
  EndIf
  ProcedureReturn SonnetDB_CopyUtf8String(SonnetDbLastError_)
EndProcedure

Procedure.s SonnetDB_Version()
  If SonnetDB_Load() = #False
    ProcedureReturn ""
  EndIf
  ProcedureReturn SonnetDB_CopyUtf8String(SonnetDbVersion_)
EndProcedure

Procedure.i SonnetDB_Open(DataSource.s)
  Protected *dataSource
  Protected *connection

  If SonnetDB_Load() = #False
    ProcedureReturn 0
  EndIf

  *dataSource = SonnetDB_ToUtf8(DataSource)
  If *dataSource = 0
    SonnetDbLastLoadError = "Out of memory."
    ProcedureReturn 0
  EndIf

  *connection = SonnetDbOpen_(*dataSource)
  FreeMemory(*dataSource)
  ProcedureReturn *connection
EndProcedure

Procedure SonnetDB_Close(*connection)
  If SonnetDB_Load() And *connection <> 0
    SonnetDbClose_(*connection)
  EndIf
EndProcedure

Procedure.i SonnetDB_Execute(*connection, Sql.s)
  Protected *sql
  Protected *result

  If SonnetDB_Load() = #False
    ProcedureReturn 0
  EndIf

  *sql = SonnetDB_ToUtf8(Sql)
  If *sql = 0
    SonnetDbLastLoadError = "Out of memory."
    ProcedureReturn 0
  EndIf

  *result = SonnetDbExecute_(*connection, *sql)
  FreeMemory(*sql)
  ProcedureReturn *result
EndProcedure

Procedure.l SonnetDB_ExecuteNonQuery(*connection, Sql.s)
  Protected *result = SonnetDB_Execute(*connection, Sql)
  Protected affected.l

  If *result = 0
    ProcedureReturn -1
  EndIf

  affected = SonnetDbResultRecordsAffected_(*result)
  SonnetDbResultFree_(*result)
  ProcedureReturn affected
EndProcedure

Procedure SonnetDB_ResultFree(*result)
  If SonnetDB_Load() And *result <> 0
    SonnetDbResultFree_(*result)
  EndIf
EndProcedure

Procedure.l SonnetDB_ResultRecordsAffected(*result)
  ProcedureReturn SonnetDbResultRecordsAffected_(*result)
EndProcedure

Procedure.l SonnetDB_ResultColumnCount(*result)
  ProcedureReturn SonnetDbResultColumnCount_(*result)
EndProcedure

Procedure.s SonnetDB_ResultColumnName(*result, ordinal.l)
  Protected *value = SonnetDbResultColumnName_(*result, ordinal)
  If *value = 0
    ProcedureReturn ""
  EndIf
  ProcedureReturn PeekS(*value, -1, #PB_UTF8)
EndProcedure

Procedure.i SonnetDB_ResultNext(*result)
  ProcedureReturn SonnetDbResultNext_(*result)
EndProcedure

Procedure.l SonnetDB_ResultValueType(*result, ordinal.l)
  ProcedureReturn SonnetDbResultValueType_(*result, ordinal)
EndProcedure

Procedure.q SonnetDB_ResultValueInt64(*result, ordinal.l)
  ProcedureReturn SonnetDbResultValueInt64_(*result, ordinal)
EndProcedure

Procedure.d SonnetDB_ResultValueDouble(*result, ordinal.l)
  ProcedureReturn SonnetDbResultValueDouble_(*result, ordinal)
EndProcedure

Procedure.i SonnetDB_ResultValueBool(*result, ordinal.l)
  ProcedureReturn SonnetDbResultValueBool_(*result, ordinal) <> 0
EndProcedure

Procedure.s SonnetDB_ResultValueText(*result, ordinal.l)
  Protected *value = SonnetDbResultValueText_(*result, ordinal)
  If *value = 0
    ProcedureReturn ""
  EndIf
  ProcedureReturn PeekS(*value, -1, #PB_UTF8)
EndProcedure

Procedure.l SonnetDB_Flush(*connection)
  ProcedureReturn SonnetDbFlush_(*connection)
EndProcedure
