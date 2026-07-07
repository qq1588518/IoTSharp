package com.sonnetdb;

import com.sonnetdb.internal.NativeBackend;

/**
 * SonnetDB SQL 执行结果。
 */
public final class SonnetDbResult implements AutoCloseable {
    private final NativeBackend backend;
    private long handle;

    SonnetDbResult(NativeBackend backend, long handle) {
        this.backend = backend;
        this.handle = handle;
    }

    /**
     * 返回受影响行数。SELECT 返回 -1。
     *
     * @return 受影响行数。
     */
    public int recordsAffected() {
        ensureOpen();
        return backend.recordsAffected(handle);
    }

    /**
     * 返回结果列数。
     *
     * @return 列数。
     */
    public int columnCount() {
        ensureOpen();
        return backend.columnCount(handle);
    }

    /**
     * 返回列名。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 列名。
     */
    public String columnName(int ordinal) {
        ensureOpen();
        return backend.columnName(handle, ordinal);
    }

    /**
     * 移动到下一行。
     *
     * @return 若存在下一行返回 true。
     */
    public boolean next() {
        ensureOpen();
        return backend.next(handle);
    }

    /**
     * 返回当前行指定列的值类型。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 值类型。
     */
    public SonnetDbValueType valueType(int ordinal) {
        ensureOpen();
        return backend.valueType(handle, ordinal);
    }

    /**
     * 以 long 读取当前行指定列。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 64 位整数值。
     */
    public long getLong(int ordinal) {
        ensureOpen();
        return backend.valueInt64(handle, ordinal);
    }

    /**
     * 以 double 读取当前行指定列。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 双精度浮点值。
     */
    public double getDouble(int ordinal) {
        ensureOpen();
        return backend.valueDouble(handle, ordinal);
    }

    /**
     * 以 boolean 读取当前行指定列。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 布尔值。
     */
    public boolean getBoolean(int ordinal) {
        ensureOpen();
        return backend.valueBool(handle, ordinal);
    }

    /**
     * 以字符串读取当前行指定列。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return 字符串值；NULL 返回 null。
     */
    public String getString(int ordinal) {
        ensureOpen();
        return backend.valueText(handle, ordinal);
    }

    /**
     * 按自然 Java 类型读取当前行指定列。
     *
     * @param ordinal 从 0 开始的列序号。
     * @return Long、Double、Boolean、String 或 null。
     */
    public Object getObject(int ordinal) {
        switch (valueType(ordinal)) {
            case NULL:
                return null;
            case INT64:
                return Long.valueOf(getLong(ordinal));
            case DOUBLE:
                return Double.valueOf(getDouble(ordinal));
            case BOOL:
                return Boolean.valueOf(getBoolean(ordinal));
            case TEXT:
                return getString(ordinal);
            default:
                throw new SonnetDbException("Unsupported SonnetDB value type.");
        }
    }

    /**
     * 释放 native result 句柄。
     */
    @Override
    public void close() {
        long current = handle;
        handle = 0L;
        if (current != 0L) {
            backend.resultFree(current);
        }
    }

    private void ensureOpen() {
        if (handle == 0L) {
            throw new SonnetDbException("SonnetDB result is closed.");
        }
    }
}
