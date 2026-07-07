Attribute VB_Name = "Quickstart"
Option Explicit

Public Sub Main()
    Dim connection As SonnetDbConnection
    Dim result As SonnetDbResult
    Dim dataDir As String
    Dim inserted As Long

    On Error GoTo Failed

    dataDir = App.Path & "\data-vb6"
    Set connection = New SonnetDbConnection

    Debug.Print "SonnetDB native version: " & SonnetDbVersion()
    connection.Open dataDir

    connection.ExecuteNonQuery "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)"
    inserted = connection.ExecuteNonQuery( _
        "INSERT INTO cpu (time, host, usage) VALUES " & _
        "(1710000000000, 'edge-1', 0.42)," & _
        "(1710000001000, 'edge-1', 0.73)")
    Debug.Print "inserted rows: " & CStr(inserted)

    Set result = connection.Execute("SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10")
    Debug.Print result.ColumnName(0) & vbTab & result.ColumnName(1) & vbTab & result.ColumnName(2)

    Do While result.NextRow
        Debug.Print result.GetInt64Text(0) & vbTab & result.GetString(1) & vbTab & CStr(result.GetDouble(2))
    Loop

    result.Close
    connection.Close
    Debug.Print "data directory: " & dataDir
    Exit Sub

Failed:
    Debug.Print "SonnetDB error: " & Err.Description
End Sub
