package com.sonnetdb;

/**
 * SonnetDB C ABI 暴露的结果值类型。
 */
public enum SonnetDbValueType {
    /** NULL 值。 */
    NULL(0),

    /** 64 位整数。 */
    INT64(1),

    /** 64 位浮点数。 */
    DOUBLE(2),

    /** 布尔值。 */
    BOOL(3),

    /** UTF-8 文本。 */
    TEXT(4);

    private final int code;

    SonnetDbValueType(int code) {
        this.code = code;
    }

    /**
     * 返回 C ABI 中的整型编码。
     *
     * @return 类型编码。
     */
    public int code() {
        return code;
    }

    /**
     * 从 C ABI 编码解析值类型。
     *
     * @param code 类型编码。
     * @return 值类型。
     */
    public static SonnetDbValueType fromCode(int code) {
        for (SonnetDbValueType type : values()) {
            if (type.code == code) {
                return type;
            }
        }
        throw new SonnetDbException("Unknown SonnetDB value type code: " + code);
    }
}
