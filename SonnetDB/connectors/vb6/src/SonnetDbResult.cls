VERSION 1.0 CLASS
BEGIN
  MultiUse = -1
END
Attribute VB_Name = "SonnetDbResult"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = True
Option Explicit

Private m_Handle As Long

Friend Sub Attach(ByVal handle As Long)
    m_Handle = handle
End Sub

Public Property Get RecordsAffected() As Long
    EnsureOpen
    RecordsAffected = SonnetDbRecordsAffected(m_Handle)
End Property

Public Property Get ColumnCount() As Long
    EnsureOpen
    ColumnCount = SonnetDbColumnCount(m_Handle)
End Property

Public Function ColumnName(ByVal ordinal As Long) As String
    EnsureOpen
    ColumnName = SonnetDbColumnName(m_Handle, ordinal)
End Function

Public Function NextRow() As Boolean
    Dim value As Long

    EnsureOpen
    value = SonnetDbResultNext(m_Handle)
    If value < 0 Then
        SonnetDbRaiseLastError "sonnetdb_vb6_result_next failed."
    End If
    NextRow = (value = 1)
End Function

Public Function ValueType(ByVal ordinal As Long) As Long
    EnsureOpen
    ValueType = SonnetDbValueType(m_Handle, ordinal)
End Function

Public Function GetInt64(ByVal ordinal As Long) As Double
    EnsureOpen
    GetInt64 = SonnetDbValueInt64(m_Handle, ordinal)
End Function

Public Function GetInt64Text(ByVal ordinal As Long) As String
    EnsureOpen
    GetInt64Text = SonnetDbValueInt64Text(m_Handle, ordinal)
End Function

Public Function GetDouble(ByVal ordinal As Long) As Double
    EnsureOpen
    GetDouble = SonnetDbValueDouble(m_Handle, ordinal)
End Function

Public Function GetBoolean(ByVal ordinal As Long) As Boolean
    EnsureOpen
    GetBoolean = SonnetDbValueBool(m_Handle, ordinal)
End Function

Public Function GetString(ByVal ordinal As Long) As String
    EnsureOpen
    GetString = SonnetDbValueText(m_Handle, ordinal)
End Function

Public Function GetValueText(ByVal ordinal As Long) As String
    Select Case ValueType(ordinal)
        Case SonnetDbTypeNull
            GetValueText = ""
        Case SonnetDbTypeInt64
            GetValueText = GetInt64Text(ordinal)
        Case SonnetDbTypeDouble
            GetValueText = CStr(GetDouble(ordinal))
        Case SonnetDbTypeBool
            GetValueText = CStr(GetBoolean(ordinal))
        Case Else
            GetValueText = GetString(ordinal)
    End Select
End Function

Public Sub Close()
    If m_Handle <> 0 Then
        SonnetDbResultFreeHandle m_Handle
        m_Handle = 0
    End If
End Sub

Public Property Get IsOpen() As Boolean
    IsOpen = (m_Handle <> 0)
End Property

Private Sub EnsureOpen()
    If m_Handle = 0 Then
        Err.Raise vbObjectError + 23002, "SonnetDB", "SonnetDB result is closed."
    End If
End Sub

Private Sub Class_Terminate()
    Close
End Sub
