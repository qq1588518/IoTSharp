#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>
#include <windows.h>

typedef void* (*sonnetdb_open_fn)(const char*);
typedef void (*sonnetdb_close_fn)(void*);
typedef void* (*sonnetdb_execute_fn)(void*, const char*);
typedef void (*sonnetdb_result_free_fn)(void*);
typedef int32_t (*sonnetdb_result_records_affected_fn)(void*);
typedef int32_t (*sonnetdb_result_column_count_fn)(void*);
typedef const char* (*sonnetdb_result_column_name_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_result_next_fn)(void*);
typedef int32_t (*sonnetdb_result_value_type_fn)(void*, int32_t);
typedef int64_t (*sonnetdb_result_value_int64_fn)(void*, int32_t);
typedef double (*sonnetdb_result_value_double_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_result_value_bool_fn)(void*, int32_t);
typedef const char* (*sonnetdb_result_value_text_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_flush_fn)(void*);
typedef int32_t (*sonnetdb_version_fn)(char*, int32_t);
typedef int32_t (*sonnetdb_last_error_fn)(char*, int32_t);

#define SONNETDB_VB6_API __declspec(dllexport)

static HMODULE native_library;
static wchar_t bridge_last_error[2048];

static sonnetdb_open_fn p_sonnetdb_open;
static sonnetdb_close_fn p_sonnetdb_close;
static sonnetdb_execute_fn p_sonnetdb_execute;
static sonnetdb_result_free_fn p_sonnetdb_result_free;
static sonnetdb_result_records_affected_fn p_sonnetdb_result_records_affected;
static sonnetdb_result_column_count_fn p_sonnetdb_result_column_count;
static sonnetdb_result_column_name_fn p_sonnetdb_result_column_name;
static sonnetdb_result_next_fn p_sonnetdb_result_next;
static sonnetdb_result_value_type_fn p_sonnetdb_result_value_type;
static sonnetdb_result_value_int64_fn p_sonnetdb_result_value_int64;
static sonnetdb_result_value_double_fn p_sonnetdb_result_value_double;
static sonnetdb_result_value_bool_fn p_sonnetdb_result_value_bool;
static sonnetdb_result_value_text_fn p_sonnetdb_result_value_text;
static sonnetdb_flush_fn p_sonnetdb_flush;
static sonnetdb_version_fn p_sonnetdb_version;
static sonnetdb_last_error_fn p_sonnetdb_last_error;

static void clear_bridge_error(void)
{
    bridge_last_error[0] = L'\0';
}

static void set_bridge_error(const wchar_t* message)
{
    if (message == NULL)
    {
        message = L"SonnetDB VB6 bridge error.";
    }

    wcsncpy_s(bridge_last_error, _countof(bridge_last_error), message, _TRUNCATE);
}

static void set_last_win32_error(const wchar_t* prefix)
{
    DWORD error = GetLastError();
    wchar_t message[1024];
    DWORD written = FormatMessageW(
        FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        NULL,
        error,
        0,
        message,
        (DWORD)_countof(message),
        NULL);

    if (written == 0)
    {
        swprintf_s(bridge_last_error, _countof(bridge_last_error), L"%s failed with Win32 error %lu.", prefix, error);
        return;
    }

    swprintf_s(bridge_last_error, _countof(bridge_last_error), L"%s failed: %s", prefix, message);
}

static FARPROC resolve_symbol(const char* name)
{
    FARPROC symbol = GetProcAddress(native_library, name);
    if (symbol == NULL)
    {
        wchar_t message[256];
        swprintf_s(message, _countof(message), L"Cannot resolve SonnetDB native symbol: %S.", name);
        set_bridge_error(message);
    }
    return symbol;
}

#define RESOLVE_SYMBOL(field, type, name) \
    do { \
        field = (type)resolve_symbol(name); \
        if (field == NULL) { \
            return 0; \
        } \
    } while (0)

static int ensure_loaded(const wchar_t* native_library_path)
{
    if (native_library != NULL)
    {
        return 1;
    }

    if (native_library_path != NULL && native_library_path[0] != L'\0')
    {
        native_library = LoadLibraryW(native_library_path);
    }
    else
    {
        native_library = LoadLibraryW(L"SonnetDB.Native.dll");
    }

    if (native_library == NULL)
    {
        set_last_win32_error(L"LoadLibraryW(SonnetDB.Native.dll)");
        return 0;
    }

    RESOLVE_SYMBOL(p_sonnetdb_open, sonnetdb_open_fn, "sonnetdb_open");
    RESOLVE_SYMBOL(p_sonnetdb_close, sonnetdb_close_fn, "sonnetdb_close");
    RESOLVE_SYMBOL(p_sonnetdb_execute, sonnetdb_execute_fn, "sonnetdb_execute");
    RESOLVE_SYMBOL(p_sonnetdb_result_free, sonnetdb_result_free_fn, "sonnetdb_result_free");
    RESOLVE_SYMBOL(p_sonnetdb_result_records_affected, sonnetdb_result_records_affected_fn, "sonnetdb_result_records_affected");
    RESOLVE_SYMBOL(p_sonnetdb_result_column_count, sonnetdb_result_column_count_fn, "sonnetdb_result_column_count");
    RESOLVE_SYMBOL(p_sonnetdb_result_column_name, sonnetdb_result_column_name_fn, "sonnetdb_result_column_name");
    RESOLVE_SYMBOL(p_sonnetdb_result_next, sonnetdb_result_next_fn, "sonnetdb_result_next");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_type, sonnetdb_result_value_type_fn, "sonnetdb_result_value_type");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_int64, sonnetdb_result_value_int64_fn, "sonnetdb_result_value_int64");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_double, sonnetdb_result_value_double_fn, "sonnetdb_result_value_double");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_bool, sonnetdb_result_value_bool_fn, "sonnetdb_result_value_bool");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_text, sonnetdb_result_value_text_fn, "sonnetdb_result_value_text");
    RESOLVE_SYMBOL(p_sonnetdb_flush, sonnetdb_flush_fn, "sonnetdb_flush");
    RESOLVE_SYMBOL(p_sonnetdb_version, sonnetdb_version_fn, "sonnetdb_version");
    RESOLVE_SYMBOL(p_sonnetdb_last_error, sonnetdb_last_error_fn, "sonnetdb_last_error");
    return 1;
}

static char* utf16_to_utf8(const wchar_t* value)
{
    int length;
    char* buffer;

    if (value == NULL)
    {
        set_bridge_error(L"String pointer must not be NULL.");
        return NULL;
    }

    length = WideCharToMultiByte(CP_UTF8, 0, value, -1, NULL, 0, NULL, NULL);
    if (length <= 0)
    {
        set_last_win32_error(L"WideCharToMultiByte");
        return NULL;
    }

    buffer = (char*)malloc((size_t)length);
    if (buffer == NULL)
    {
        set_bridge_error(L"Out of memory.");
        return NULL;
    }

    if (WideCharToMultiByte(CP_UTF8, 0, value, -1, buffer, length, NULL, NULL) <= 0)
    {
        free(buffer);
        set_last_win32_error(L"WideCharToMultiByte");
        return NULL;
    }

    return buffer;
}

static int32_t copy_wide(const wchar_t* value, wchar_t* buffer, int32_t buffer_length)
{
    int32_t required;
    int32_t copy_length;

    if (value == NULL)
    {
        value = L"";
    }

    required = (int32_t)wcslen(value);
    if (buffer == NULL || buffer_length <= 0)
    {
        return required;
    }

    copy_length = required < buffer_length - 1 ? required : buffer_length - 1;
    if (copy_length > 0)
    {
        wmemcpy(buffer, value, (size_t)copy_length);
    }
    buffer[copy_length] = L'\0';
    return required;
}

static int32_t copy_utf8(const char* value, wchar_t* buffer, int32_t buffer_length)
{
    int required_with_null;
    int required;
    wchar_t* temp;
    int32_t result;

    if (value == NULL)
    {
        value = "";
    }

    required_with_null = MultiByteToWideChar(CP_UTF8, 0, value, -1, NULL, 0);
    if (required_with_null <= 0)
    {
        set_last_win32_error(L"MultiByteToWideChar");
        return -1;
    }

    required = required_with_null - 1;
    if (buffer == NULL || buffer_length <= 0)
    {
        return required;
    }

    temp = (wchar_t*)malloc((size_t)required_with_null * sizeof(wchar_t));
    if (temp == NULL)
    {
        set_bridge_error(L"Out of memory.");
        return -1;
    }

    if (MultiByteToWideChar(CP_UTF8, 0, value, -1, temp, required_with_null) <= 0)
    {
        free(temp);
        set_last_win32_error(L"MultiByteToWideChar");
        return -1;
    }

    result = copy_wide(temp, buffer, buffer_length);
    free(temp);
    return result;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_initialize(const wchar_t* native_library_path)
{
    clear_bridge_error();
    return ensure_loaded(native_library_path) ? 0 : -1;
}

SONNETDB_VB6_API void* __stdcall sonnetdb_vb6_open(const wchar_t* data_source)
{
    char* utf8;
    void* connection;

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return NULL;
    }

    utf8 = utf16_to_utf8(data_source);
    if (utf8 == NULL)
    {
        return NULL;
    }

    connection = p_sonnetdb_open(utf8);
    free(utf8);
    return connection;
}

SONNETDB_VB6_API void __stdcall sonnetdb_vb6_close(void* connection)
{
    clear_bridge_error();
    if (ensure_loaded(NULL) && connection != NULL)
    {
        p_sonnetdb_close(connection);
    }
}

SONNETDB_VB6_API void* __stdcall sonnetdb_vb6_execute(void* connection, const wchar_t* sql)
{
    char* utf8;
    void* result;

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return NULL;
    }

    utf8 = utf16_to_utf8(sql);
    if (utf8 == NULL)
    {
        return NULL;
    }

    result = p_sonnetdb_execute(connection, utf8);
    free(utf8);
    return result;
}

SONNETDB_VB6_API void __stdcall sonnetdb_vb6_result_free(void* result)
{
    clear_bridge_error();
    if (ensure_loaded(NULL) && result != NULL)
    {
        p_sonnetdb_result_free(result);
    }
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_records_affected(void* result)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_records_affected(result) : -1;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_column_count(void* result)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_column_count(result) : -1;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_column_name(
    void* result,
    int32_t ordinal,
    wchar_t* buffer,
    int32_t buffer_length)
{
    const char* value;

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return -1;
    }

    value = p_sonnetdb_result_column_name(result, ordinal);
    return value == NULL ? -1 : copy_utf8(value, buffer, buffer_length);
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_next(void* result)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_next(result) : -1;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_value_type(void* result, int32_t ordinal)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_value_type(result, ordinal) : -1;
}

SONNETDB_VB6_API double __stdcall sonnetdb_vb6_result_value_int64_double(void* result, int32_t ordinal)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? (double)p_sonnetdb_result_value_int64(result, ordinal) : 0.0;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_value_int64_text(
    void* result,
    int32_t ordinal,
    wchar_t* buffer,
    int32_t buffer_length)
{
    int64_t value;
    wchar_t text[64];

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return -1;
    }

    value = p_sonnetdb_result_value_int64(result, ordinal);
    swprintf_s(text, _countof(text), L"%lld", (long long)value);
    return copy_wide(text, buffer, buffer_length);
}

SONNETDB_VB6_API double __stdcall sonnetdb_vb6_result_value_double(void* result, int32_t ordinal)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_value_double(result, ordinal) : 0.0;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_value_bool(void* result, int32_t ordinal)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_result_value_bool(result, ordinal) : -1;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_result_value_text(
    void* result,
    int32_t ordinal,
    wchar_t* buffer,
    int32_t buffer_length)
{
    const char* value;

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return -1;
    }

    value = p_sonnetdb_result_value_text(result, ordinal);
    return copy_utf8(value, buffer, buffer_length);
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_flush(void* connection)
{
    clear_bridge_error();
    return ensure_loaded(NULL) ? p_sonnetdb_flush(connection) : -1;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_version(wchar_t* buffer, int32_t buffer_length)
{
    char stack_buffer[4096];
    int32_t required;
    char* heap_buffer;
    int32_t result;

    clear_bridge_error();
    if (!ensure_loaded(NULL))
    {
        return -1;
    }

    required = p_sonnetdb_version(stack_buffer, (int32_t)sizeof(stack_buffer));
    if (required < 0)
    {
        return -1;
    }

    if (required < (int32_t)sizeof(stack_buffer))
    {
        return copy_utf8(stack_buffer, buffer, buffer_length);
    }

    heap_buffer = (char*)malloc((size_t)required + 1);
    if (heap_buffer == NULL)
    {
        set_bridge_error(L"Out of memory.");
        return -1;
    }

    required = p_sonnetdb_version(heap_buffer, required + 1);
    result = required < 0 ? -1 : copy_utf8(heap_buffer, buffer, buffer_length);
    free(heap_buffer);
    return result;
}

SONNETDB_VB6_API int32_t __stdcall sonnetdb_vb6_last_error(wchar_t* buffer, int32_t buffer_length)
{
    char stack_buffer[4096];
    int32_t required;

    if (bridge_last_error[0] != L'\0')
    {
        return copy_wide(bridge_last_error, buffer, buffer_length);
    }

    if (p_sonnetdb_last_error == NULL)
    {
        return copy_wide(L"", buffer, buffer_length);
    }

    required = p_sonnetdb_last_error(stack_buffer, (int32_t)sizeof(stack_buffer));
    if (required < 0)
    {
        return copy_wide(L"sonnetdb_last_error failed.", buffer, buffer_length);
    }

    return copy_utf8(stack_buffer, buffer, buffer_length);
}

#if defined(_M_IX86)
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_initialize=_sonnetdb_vb6_initialize@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_open=_sonnetdb_vb6_open@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_close=_sonnetdb_vb6_close@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_execute=_sonnetdb_vb6_execute@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_free=_sonnetdb_vb6_result_free@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_records_affected=_sonnetdb_vb6_result_records_affected@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_column_count=_sonnetdb_vb6_result_column_count@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_column_name=_sonnetdb_vb6_result_column_name@16")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_next=_sonnetdb_vb6_result_next@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_type=_sonnetdb_vb6_result_value_type@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_int64_double=_sonnetdb_vb6_result_value_int64_double@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_int64_text=_sonnetdb_vb6_result_value_int64_text@16")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_double=_sonnetdb_vb6_result_value_double@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_bool=_sonnetdb_vb6_result_value_bool@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_result_value_text=_sonnetdb_vb6_result_value_text@16")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_flush=_sonnetdb_vb6_flush@4")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_version=_sonnetdb_vb6_version@8")
#pragma comment(linker, "/EXPORT:sonnetdb_vb6_last_error=_sonnetdb_vb6_last_error@8")
#endif
