using Microsoft.ML.OnnxRuntime;
using SonnetDB.Configuration;

namespace SonnetDB.Copilot;

/// <summary>
/// 本地 ONNX embedding provider 骨架实现。
/// </summary>
public sealed class LocalOnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly CopilotEmbeddingOptions _options;
    private InferenceSession? _session;
    private bool _disposed;

    public LocalOnnxEmbeddingProvider(CopilotEmbeddingOptions options)
    {
        _options = options;
    }

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input cannot be empty.", nameof(text));

        var session = EnsureSession();
        _ = cancellationToken;
        _ = session;

        throw new NotSupportedException("Local ONNX embedding execution is not wired yet; this PR only establishes the provider skeleton.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _session?.Dispose();
        _session = null;
        _disposed = true;
    }

    private InferenceSession EnsureSession()
    {
        if (_session is not null)
            return _session;

        if (string.IsNullOrWhiteSpace(_options.LocalModelPath))
            throw new InvalidOperationException("Copilot local embedding model path is missing.");

        var modelPath = Path.GetFullPath(_options.LocalModelPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Copilot local embedding model file was not found.", modelPath);

        _session = new InferenceSession(modelPath);
        return _session;
    }
}
