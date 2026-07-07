package com.sonnetdb;

/**
 * SonnetDB Java 连接器异常。
 */
public final class SonnetDbException extends RuntimeException {
    /**
     * 使用错误消息构造异常。
     *
     * @param message 错误消息。
     */
    public SonnetDbException(String message) {
        super(message);
    }

    /**
     * 使用错误消息和内部异常构造异常。
     *
     * @param message 错误消息。
     * @param cause 内部异常。
     */
    public SonnetDbException(String message, Throwable cause) {
        super(message, cause);
    }
}
