VERSION 1.0 CLASS
BEGIN
  MultiUse = -1
END
Attribute VB_Name = "SonnetDbConnection"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = True
Option Explicit

Private m_Handle As Long

Public Sub Open(ByVal dataSource As String)
    Close
    m_Handle = SonnetDbOpenHandle(dataSource)
    If m_Handle = 0 Then
        SonnetDbRaiseLastError "sonnetdb_vb6_open failed."
    End If
End Sub

Public Function Execute(ByVal sql As String) As SonnetDbResult
    Dim resultHandle As Long
    Dim result As SonnetDbResult

    EnsureOpen
    resultHandle = SonnetDbExecuteHandle(m_Handle, sql)
    If resultHandle = 0 Then
        SonnetDbRaiseLastError "sonnetdb_vb6_execute failed."
    End If

    Set result = New SonnetDbResult
    result.Attach resultHandle
    Set Execute = result
End Function

Public Function ExecuteNonQuery(ByVal sql As String) As Long
    Dim result As SonnetDbResult

    Set result = Execute(sql)
    ExecuteNonQuery = result.RecordsAffected
    result.Close
End Function

Public Sub Flush()
    EnsureOpen
    If Not SonnetDbFlush(m_Handle) Then
        SonnetDbRaiseLastError "sonnetdb_vb6_flush failed."
    End If
End Sub

Public Sub Close()
    If m_Handle <> 0 Then
        SonnetDbCloseHandle m_Handle
        m_Handle = 0
    End If
End Sub

Public Property Get IsOpen() As Boolean
    IsOpen = (m_Handle <> 0)
End Property

Private Sub EnsureOpen()
    If m_Handle = 0 Then
        Err.Raise vbObjectError + 23001, "SonnetDB", "SonnetDB connection is closed."
    End If
End Sub

Private Sub Class_Terminate()
    Close
End Sub
