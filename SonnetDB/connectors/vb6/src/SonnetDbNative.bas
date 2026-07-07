Attribute VB_Name = "SonnetDbNative"
Option Explicit

Public Const SonnetDbTypeNull As Long = 0
Public Const SonnetDbTypeInt64 As Long = 1
Public Const SonnetDbTypeDouble As Long = 2
Public Const SonnetDbTypeBool As Long = 3
Public Const SonnetDbTypeText As Long = 4

Private Const DefaultBufferLength As Long = 4096
Private Const ErrorBase As Long = vbObjectError + 23000

Private Declare Function NativeInitialize Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_initialize" (ByVal nativeLibraryPath As Long) As Long
Private Declare Function NativeOpen Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_open" (ByVal dataSource As Long) As Long
Private Declare Sub NativeClose Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_close" (ByVal connection As Long)
Private Declare Function NativeExecute Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_execute" (ByVal connection As Long, ByVal sql As Long) As Long
Private Declare Sub NativeResultFree Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_free" (ByVal result As Long)
Private Declare Function NativeRecordsAffected Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_records_affected" (ByVal result As Long) As Long
Private Declare Function NativeColumnCount Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_column_count" (ByVal result As Long) As Long
Private Declare Function NativeColumnName Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_column_name" (ByVal result As Long, ByVal ordinal As Long, ByVal buffer As Long, ByVal bufferLength As Long) As Long
Private Declare Function NativeNext Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_next" (ByVal result As Long) As Long
Private Declare Function NativeValueType Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_type" (ByVal result As Long, ByVal ordinal As Long) As Long
Private Declare Function NativeValueInt64Double Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_int64_double" (ByVal result As Long, ByVal ordinal As Long) As Double
Private Declare Function NativeValueInt64Text Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_int64_text" (ByVal result As Long, ByVal ordinal As Long, ByVal buffer As Long, ByVal bufferLength As Long) As Long
Private Declare Function NativeValueDouble Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_double" (ByVal result As Long, ByVal ordinal As Long) As Double
Private Declare Function NativeValueBool Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_bool" (ByVal result As Long, ByVal ordinal As Long) As Long
Private Declare Function NativeValueText Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_result_value_text" (ByVal result As Long, ByVal ordinal As Long, ByVal buffer As Long, ByVal bufferLength As Long) As Long
Private Declare Function NativeFlush Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_flush" (ByVal connection As Long) As Long
Private Declare Function NativeVersion Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_version" (ByVal buffer As Long, ByVal bufferLength As Long) As Long
Private Declare Function NativeLastError Lib "SonnetDB.VB6.Native.dll" Alias "sonnetdb_vb6_last_error" (ByVal buffer As Long, ByVal bufferLength As Long) As Long

Public Function SonnetDbInitialize(Optional ByVal nativeLibraryPath As String = "") As Boolean
    SonnetDbInitialize = (NativeInitialize(StrPtr(nativeLibraryPath)) = 0)
End Function

Public Function SonnetDbOpenHandle(ByVal dataSource As String) As Long
    SonnetDbOpenHandle = NativeOpen(StrPtr(dataSource))
End Function

Public Sub SonnetDbCloseHandle(ByVal connection As Long)
    If connection <> 0 Then
        NativeClose connection
    End If
End Sub

Public Function SonnetDbExecuteHandle(ByVal connection As Long, ByVal sql As String) As Long
    SonnetDbExecuteHandle = NativeExecute(connection, StrPtr(sql))
End Function

Public Sub SonnetDbResultFreeHandle(ByVal result As Long)
    If result <> 0 Then
        NativeResultFree result
    End If
End Sub

Public Function SonnetDbRecordsAffected(ByVal result As Long) As Long
    SonnetDbRecordsAffected = NativeRecordsAffected(result)
End Function

Public Function SonnetDbColumnCount(ByVal result As Long) As Long
    SonnetDbColumnCount = NativeColumnCount(result)
End Function

Public Function SonnetDbColumnName(ByVal result As Long, ByVal ordinal As Long) As String
    SonnetDbColumnName = ReadColumnName(result, ordinal, DefaultBufferLength)
End Function

Public Function SonnetDbResultNext(ByVal result As Long) As Long
    SonnetDbResultNext = NativeNext(result)
End Function

Public Function SonnetDbValueType(ByVal result As Long, ByVal ordinal As Long) As Long
    SonnetDbValueType = NativeValueType(result, ordinal)
End Function

Public Function SonnetDbValueInt64(ByVal result As Long, ByVal ordinal As Long) As Double
    SonnetDbValueInt64 = NativeValueInt64Double(result, ordinal)
End Function

Public Function SonnetDbValueInt64Text(ByVal result As Long, ByVal ordinal As Long) As String
    SonnetDbValueInt64Text = ReadValueInt64Text(result, ordinal, DefaultBufferLength)
End Function

Public Function SonnetDbValueDouble(ByVal result As Long, ByVal ordinal As Long) As Double
    SonnetDbValueDouble = NativeValueDouble(result, ordinal)
End Function

Public Function SonnetDbValueBool(ByVal result As Long, ByVal ordinal As Long) As Boolean
    SonnetDbValueBool = (NativeValueBool(result, ordinal) <> 0)
End Function

Public Function SonnetDbValueText(ByVal result As Long, ByVal ordinal As Long) As String
    SonnetDbValueText = ReadValueText(result, ordinal, DefaultBufferLength)
End Function

Public Function SonnetDbFlush(ByVal connection As Long) As Boolean
    SonnetDbFlush = (NativeFlush(connection) = 0)
End Function

Public Function SonnetDbVersion() As String
    SonnetDbVersion = ReadVersion(DefaultBufferLength)
End Function

Public Function SonnetDbLastError() As String
    SonnetDbLastError = ReadLastError(DefaultBufferLength)
End Function

Public Sub SonnetDbRaiseLastError(Optional ByVal fallback As String = "SonnetDB native call failed.")
    Dim message As String
    message = SonnetDbLastError()
    If Len(message) = 0 Then
        message = fallback
    End If
    Err.Raise ErrorBase, "SonnetDB", message
End Sub

Private Function ReadColumnName(ByVal result As Long, ByVal ordinal As Long, ByVal bufferLength As Long) As String
    Dim required As Long
    Dim buffer As String

    buffer = String$(bufferLength, vbNullChar)
    required = NativeColumnName(result, ordinal, StrPtr(buffer), bufferLength)
    If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_column_name failed."

    If required >= bufferLength Then
        buffer = String$(required + 1, vbNullChar)
        required = NativeColumnName(result, ordinal, StrPtr(buffer), required + 1)
        If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_column_name failed."
    End If

    ReadColumnName = TrimNull(buffer)
End Function

Private Function ReadValueInt64Text(ByVal result As Long, ByVal ordinal As Long, ByVal bufferLength As Long) As String
    Dim required As Long
    Dim buffer As String

    buffer = String$(bufferLength, vbNullChar)
    required = NativeValueInt64Text(result, ordinal, StrPtr(buffer), bufferLength)
    If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_value_int64_text failed."

    If required >= bufferLength Then
        buffer = String$(required + 1, vbNullChar)
        required = NativeValueInt64Text(result, ordinal, StrPtr(buffer), required + 1)
        If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_value_int64_text failed."
    End If

    ReadValueInt64Text = TrimNull(buffer)
End Function

Private Function ReadValueText(ByVal result As Long, ByVal ordinal As Long, ByVal bufferLength As Long) As String
    Dim required As Long
    Dim buffer As String

    buffer = String$(bufferLength, vbNullChar)
    required = NativeValueText(result, ordinal, StrPtr(buffer), bufferLength)
    If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_value_text failed."

    If required >= bufferLength Then
        buffer = String$(required + 1, vbNullChar)
        required = NativeValueText(result, ordinal, StrPtr(buffer), required + 1)
        If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_result_value_text failed."
    End If

    ReadValueText = TrimNull(buffer)
End Function

Private Function ReadVersion(ByVal bufferLength As Long) As String
    Dim required As Long
    Dim buffer As String

    buffer = String$(bufferLength, vbNullChar)
    required = NativeVersion(StrPtr(buffer), bufferLength)
    If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_version failed."

    If required >= bufferLength Then
        buffer = String$(required + 1, vbNullChar)
        required = NativeVersion(StrPtr(buffer), required + 1)
        If required < 0 Then SonnetDbRaiseLastError "sonnetdb_vb6_version failed."
    End If

    ReadVersion = TrimNull(buffer)
End Function

Private Function ReadLastError(ByVal bufferLength As Long) As String
    Dim required As Long
    Dim buffer As String

    buffer = String$(bufferLength, vbNullChar)
    required = NativeLastError(StrPtr(buffer), bufferLength)
    If required < 0 Then
        ReadLastError = "sonnetdb_vb6_last_error failed."
        Exit Function
    End If

    If required >= bufferLength Then
        buffer = String$(required + 1, vbNullChar)
        required = NativeLastError(StrPtr(buffer), required + 1)
        If required < 0 Then
            ReadLastError = "sonnetdb_vb6_last_error failed."
            Exit Function
        End If
    End If

    ReadLastError = TrimNull(buffer)
End Function

Private Function TrimNull(ByVal value As String) As String
    Dim position As Long
    position = InStr(1, value, vbNullChar, vbBinaryCompare)
    If position > 0 Then
        TrimNull = Left$(value, position - 1)
    Else
        TrimNull = value
    End If
End Function
