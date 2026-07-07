EnableExplicit

XIncludeFile "../src/SonnetDB.pbi"

OpenConsole()

Define dataDir.s = GetTemporaryDirectory() + "sonnetdb-purebasic-quickstart-" + Str(ElapsedMilliseconds())
Define *connection
Define *result
Define inserted.l

CreateDirectory(dataDir)

If SonnetDB_Load() = #False
  PrintN("SonnetDB load error: " + SonnetDB_LastError())
  End 1
EndIf

PrintN("SonnetDB native version: " + SonnetDB_Version())

*connection = SonnetDB_Open(dataDir)
If *connection = 0
  PrintN("SonnetDB open error: " + SonnetDB_LastError())
  End 1
EndIf

If SonnetDB_ExecuteNonQuery(*connection, "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)") < 0
  PrintN("SonnetDB create error: " + SonnetDB_LastError())
  End 1
EndIf

inserted = SonnetDB_ExecuteNonQuery(*connection, "INSERT INTO cpu (time, host, usage) VALUES (1710000000000, 'edge-1', 0.42),(1710000001000, 'edge-1', 0.73)")
If inserted < 0
  PrintN("SonnetDB insert error: " + SonnetDB_LastError())
  End 1
EndIf
PrintN("inserted rows: " + Str(inserted))

*result = SonnetDB_Execute(*connection, "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")
If *result = 0
  PrintN("SonnetDB select error: " + SonnetDB_LastError())
  End 1
EndIf

PrintN(SonnetDB_ResultColumnName(*result, 0) + Chr(9) + SonnetDB_ResultColumnName(*result, 1) + Chr(9) + SonnetDB_ResultColumnName(*result, 2))

While SonnetDB_ResultNext(*result) = 1
  PrintN(Str(SonnetDB_ResultValueInt64(*result, 0)) + Chr(9) + SonnetDB_ResultValueText(*result, 1) + Chr(9) + StrD(SonnetDB_ResultValueDouble(*result, 2), 3))
Wend

SonnetDB_ResultFree(*result)
SonnetDB_Close(*connection)
PrintN("data directory: " + dataDir)
