package com.sonnetdb.internal;

import com.sonnetdb.SonnetDbException;
import com.sonnetdb.jni.SonnetDbJniBackend;

/**
 * SonnetDB native 后端加载器。
 */
public final class NativeBackendLoader {
    private static final NativeBackend Backend = loadBackend();

    private NativeBackendLoader() {
    }

    /**
     * 返回当前进程使用的 native 后端。
     *
     * @return native 后端。
     */
    public static NativeBackend load() {
        return Backend;
    }

    private static NativeBackend loadBackend() {
        String requested = System.getProperty("sonnetdb.java.backend");
        if (requested == null || requested.trim().isEmpty()) {
            requested = System.getenv("SONNETDB_JAVA_BACKEND");
        }
        if (requested == null || requested.trim().isEmpty()) {
            requested = "jni";
        }

        String normalized = requested.trim().toLowerCase();
        if ("jni".equals(normalized)) {
            return new SonnetDbJniBackend();
        }
        if ("ffm".equals(normalized)) {
            return loadFfmBackend();
        }
        if ("auto".equals(normalized)) {
            if (javaFeatureVersion() >= 21) {
                try {
                    return loadFfmBackend();
                } catch (RuntimeException ignored) {
                    return new SonnetDbJniBackend();
                }
            }
            return new SonnetDbJniBackend();
        }

        throw new SonnetDbException(
            "Unknown SonnetDB Java backend '" + requested + "'. Use 'jni', 'ffm', or 'auto'.");
    }

    private static NativeBackend loadFfmBackend() {
        try {
            Class<?> type = Class.forName("com.sonnetdb.ffm.SonnetDbFfmBackend");
            return (NativeBackend) type.getDeclaredConstructor().newInstance();
        } catch (ReflectiveOperationException | LinkageError ex) {
            throw new SonnetDbException(
                "Cannot load SonnetDB FFM backend. Use JDK 21+ and run with --enable-preview on JDK 21.",
                ex);
        }
    }

    private static int javaFeatureVersion() {
        String version = System.getProperty("java.specification.version", "8");
        if (version.startsWith("1.")) {
            version = version.substring(2);
        }
        int dot = version.indexOf('.');
        if (dot >= 0) {
            version = version.substring(0, dot);
        }
        try {
            return Integer.parseInt(version);
        } catch (NumberFormatException ex) {
            return 8;
        }
    }
}
