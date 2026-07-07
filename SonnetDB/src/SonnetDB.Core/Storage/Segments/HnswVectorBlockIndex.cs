using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Query;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 单个 VECTOR block 的 HNSW 图索引。
/// </summary>
internal sealed class HnswVectorBlockIndex
{
    private readonly int[][][] _neighbors;

    private HnswVectorBlockIndex(
        int blockIndex,
        int dimension,
        int m,
        int ef,
        int entryPoint,
        int maxLevel,
        int[][][] neighbors)
    {
        BlockIndex = blockIndex;
        Dimension = dimension;
        M = m;
        Ef = ef;
        EntryPoint = entryPoint;
        MaxLevel = maxLevel;
        _neighbors = neighbors;
        EstimatedBytes = EstimateBytes(neighbors);
    }

    /// <summary>所属 block 的序号。</summary>
    public int BlockIndex { get; }

    /// <summary>向量维度。</summary>
    public int Dimension { get; }

    /// <summary>HNSW 参数 m。</summary>
    public int M { get; }

    /// <summary>HNSW 参数 ef。</summary>
    public int Ef { get; }

    /// <summary>图中的节点数量。</summary>
    public int Count => _neighbors.Length;

    /// <summary>索引在托管堆上的近似驻留字节数。</summary>
    internal long EstimatedBytes { get; }

    /// <summary>当前图的入口点。</summary>
    public int EntryPoint { get; }

    /// <summary>当前图的最高层。</summary>
    public int MaxLevel { get; }

    /// <summary>
    /// 基于 block 内全部向量构建 HNSW 图。
    /// </summary>
    /// <param name="blockIndex">所属 block 的序号。</param>
    /// <param name="points">按时间顺序排列的向量点。</param>
    /// <param name="options">HNSW 参数。</param>
    /// <returns>构建完成的图索引。</returns>
    public static HnswVectorBlockIndex Build(
        int blockIndex,
        ReadOnlySpan<DataPoint> points,
        HnswVectorIndexOptions options)
    {
        if (points.IsEmpty)
            throw new ArgumentException("HNSW 图至少需要 1 个向量点。", nameof(points));

        int dimension = points[0].Value.VectorDimension;
        int count = points.Length;
        var levels = new int[count];
        var neighbors = new int[count][][];

        ulong rngState = ComputeSeed(blockIndex, count, dimension, options.M, options.Ef);
        for (int node = 0; node < count; node++)
        {
            int dim = points[node].Value.VectorDimension;
            if (dim != dimension)
                throw new InvalidOperationException(
                    $"HNSW 构建要求 block 内向量维度一致：首点 dim={dimension}，节点 {node} dim={dim}。");

            int level = SampleLevel(ref rngState, options.M);
            levels[node] = level;
            neighbors[node] = new int[level + 1][];
            for (int l = 0; l <= level; l++)
                neighbors[node][l] = [];
        }

        int entryPoint = 0;
        int maxLevel = levels[0];

        for (int node = 1; node < count; node++)
        {
            int nodeLevel = levels[node];
            int currentEntry = entryPoint;
            double entryDistance = VectorDistance.ComputeCosine(
                points[node].Value.AsVector().Span,
                points[currentEntry].Value.AsVector().Span);

            for (int level = maxLevel; level > nodeLevel; level--)
                currentEntry = GreedySearch(points, neighbors, node, currentEntry, level, ref entryDistance);

            int searchDownTo = Math.Min(nodeLevel, maxLevel);
            for (int level = searchDownTo; level >= 0; level--)
            {
                var candidates = SearchLayer(points, neighbors, node, currentEntry, level, options.Ef);
                int take = Math.Min(options.M, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    int neighbor = candidates[i].Node;
                    neighbors[node][level] = AddNeighbor(
                        neighbors[node][level],
                        neighbor,
                        options.M,
                        node,
                        points);
                    neighbors[neighbor][level] = AddNeighbor(
                        neighbors[neighbor][level],
                        node,
                        options.M,
                        neighbor,
                        points);
                }

                if (candidates.Count > 0)
                {
                    currentEntry = candidates[0].Node;
                    entryDistance = candidates[0].Distance;
                }
            }

            if (nodeLevel > maxLevel)
            {
                entryPoint = node;
                maxLevel = nodeLevel;
            }
        }

        return new HnswVectorBlockIndex(blockIndex, dimension, options.M, options.Ef, entryPoint, maxLevel, neighbors);
    }

    /// <summary>
    /// 基于连续的向量值载荷构建 HNSW 图，避免在基准场景中额外构造 <see cref="DataPoint"/> 数组。
    /// </summary>
    /// <param name="blockIndex">所属 block 的序号。</param>
    /// <param name="valPayload">连续的向量值载荷，布局为 <c>count × dimension × float32(LE)</c>。</param>
    /// <param name="count">向量数量。</param>
    /// <param name="dimension">向量维度。</param>
    /// <param name="options">HNSW 参数。</param>
    /// <returns>构建完成的图索引。</returns>
    public static HnswVectorBlockIndex Build(
        int blockIndex,
        ReadOnlySpan<byte> valPayload,
        int count,
        int dimension,
        HnswVectorIndexOptions options)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "HNSW 图至少需要 1 个向量点。");
        if (dimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimension), "向量维度必须大于 0。");

        int expectedLength = checked(count * dimension * sizeof(float));
        if (valPayload.Length != expectedLength)
            throw new ArgumentException(
                $"valPayload 长度必须等于 count × dimension × 4（期望 {expectedLength}，实际 {valPayload.Length}）。",
                nameof(valPayload));

        var levels = new int[count];
        var neighbors = new int[count][][];

        ulong rngState = ComputeSeed(blockIndex, count, dimension, options.M, options.Ef);
        for (int node = 0; node < count; node++)
        {
            int level = SampleLevel(ref rngState, options.M);
            levels[node] = level;
            neighbors[node] = new int[level + 1][];
            for (int l = 0; l <= level; l++)
                neighbors[node][l] = [];
        }

        int entryPoint = 0;
        int maxLevel = levels[0];

        for (int node = 1; node < count; node++)
        {
            int nodeLevel = levels[node];
            int currentEntry = entryPoint;
            double entryDistance = VectorDistance.ComputeCosine(
                GetVector(valPayload, node, dimension),
                GetVector(valPayload, currentEntry, dimension));

            for (int level = maxLevel; level > nodeLevel; level--)
                currentEntry = GreedySearch(valPayload, neighbors, node, currentEntry, level, dimension, ref entryDistance);

            int searchDownTo = Math.Min(nodeLevel, maxLevel);
            for (int level = searchDownTo; level >= 0; level--)
            {
                var candidates = SearchLayer(valPayload, neighbors, node, currentEntry, level, options.Ef, dimension);
                int take = Math.Min(options.M, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    int neighbor = candidates[i].Node;
                    neighbors[node][level] = AddNeighbor(
                        neighbors[node][level],
                        neighbor,
                        options.M,
                        node,
                        valPayload,
                        dimension);
                    neighbors[neighbor][level] = AddNeighbor(
                        neighbors[neighbor][level],
                        node,
                        options.M,
                        neighbor,
                        valPayload,
                        dimension);
                }

                if (candidates.Count > 0)
                {
                    currentEntry = candidates[0].Node;
                    entryDistance = candidates[0].Distance;
                }
            }

            if (nodeLevel > maxLevel)
            {
                entryPoint = node;
                maxLevel = nodeLevel;
            }
        }

        return new HnswVectorBlockIndex(blockIndex, dimension, options.M, options.Ef, entryPoint, maxLevel, neighbors);
    }

    /// <summary>
    /// 用 ANN 图对整个 block 做近邻搜索。
    /// </summary>
    /// <param name="queryVector">查询向量。</param>
    /// <param name="valPayload">block 的向量值载荷。</param>
    /// <param name="timestamps">block 内按点位顺序排列的时间戳数组。</param>
    /// <param name="k">返回结果上限。</param>
    /// <param name="metric">距离度量。</param>
    /// <returns>按距离升序排列的候选结果。</returns>
    public IReadOnlyList<HnswAnnSearchResult> Search(
        ReadOnlySpan<float> queryVector,
        ReadOnlySpan<byte> valPayload,
        ReadOnlySpan<long> timestamps,
        int resultLimit,
        KnnMetric metric)
    {
        if (resultLimit <= 0 || Count == 0 || metric != KnnMetric.Cosine || queryVector.Length != Dimension)
            return [];

        if (resultLimit >= Count)
            return ExactSearch(queryVector, valPayload, timestamps, Count, metric);

        int efSearch = Math.Min(Count, Math.Max(resultLimit, Ef));
        int entry = EntryPoint;
        double currentDistance = VectorDistance.Compute(metric, queryVector, GetVector(valPayload, entry, Dimension));

        for (int level = MaxLevel; level > 0; level--)
            entry = GreedySearch(valPayload, queryVector, entry, level, metric, ref currentDistance);

        var layer = SearchLayer(valPayload, queryVector, entry, 0, efSearch, metric);
        int take = Math.Min(resultLimit, layer.Count);
        var result = new HnswAnnSearchResult[take];
        for (int i = 0; i < take; i++)
        {
            int pointIndex = layer[i].Node;
            result[i] = new HnswAnnSearchResult(pointIndex, timestamps[pointIndex], layer[i].Distance);
        }

        return result;
    }

    private IReadOnlyList<HnswAnnSearchResult> ExactSearch(
        ReadOnlySpan<float> queryVector,
        ReadOnlySpan<byte> valPayload,
        ReadOnlySpan<long> timestamps,
        int resultLimit,
        KnnMetric metric)
    {
        var result = new HnswAnnSearchResult[Count];
        for (int i = 0; i < Count; i++)
        {
            double distance = VectorDistance.Compute(metric, queryVector, GetVector(valPayload, i, Dimension));
            result[i] = new HnswAnnSearchResult(i, timestamps[i], distance);
        }

        Array.Sort(result, static (left, right) => left.Distance.CompareTo(right.Distance));
        if (resultLimit == Count)
            return result;

        var trimmed = new HnswAnnSearchResult[resultLimit];
        Array.Copy(result, trimmed, resultLimit);
        return trimmed;
    }

    /// <summary>
    /// 把当前索引序列化写入流。
    /// </summary>
    /// <param name="stream">目标流。</param>
    public void WriteTo(Stream stream)
    {
        WriteInt32(stream, BlockIndex);
        WriteInt32(stream, Count);
        WriteInt32(stream, Dimension);
        WriteInt32(stream, M);
        WriteInt32(stream, Ef);
        WriteInt32(stream, MaxLevel);
        WriteInt32(stream, EntryPoint);

        for (int node = 0; node < _neighbors.Length; node++)
        {
            WriteInt32(stream, _neighbors[node].Length);
            for (int level = 0; level < _neighbors[node].Length; level++)
            {
                var levelNeighbors = _neighbors[node][level];
                WriteInt32(stream, levelNeighbors.Length);
                for (int i = 0; i < levelNeighbors.Length; i++)
                    WriteInt32(stream, levelNeighbors[i]);
            }
        }
    }

    /// <summary>
    /// 从流中读取一个 block 的 HNSW 索引。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <returns>反序列化得到的索引。</returns>
    public static HnswVectorBlockIndex ReadFrom(Stream stream)
    {
        var header = ReadSerializedHeader(stream);
        var neighbors = ReadNeighbors(stream, header.Count);

        return new HnswVectorBlockIndex(
            header.BlockIndex,
            header.Dimension,
            header.M,
            header.Ef,
            header.EntryPoint,
            header.MaxLevel,
            neighbors);
    }

    internal static int ReadBlockIndexAndSkip(Stream stream)
    {
        var header = ReadSerializedHeader(stream);
        SkipNeighbors(stream, header.Count);
        return header.BlockIndex;
    }

    private static SerializedHeader ReadSerializedHeader(Stream stream)
    {
        int blockIndex = ReadInt32(stream);
        int count = ReadInt32(stream);
        int dimension = ReadInt32(stream);
        int m = ReadInt32(stream);
        int ef = ReadInt32(stream);
        int maxLevel = ReadInt32(stream);
        int entryPoint = ReadInt32(stream);

        if (count < 0 || dimension <= 0 || m < 2 || ef <= 0 || maxLevel < 0)
            throw new InvalidDataException("HNSW sidecar 含有非法的 block 头部参数。");
        if (count == 0 && entryPoint != 0)
            throw new InvalidDataException("空 HNSW block 的 entryPoint 必须为 0。");
        if (count > 0 && (entryPoint < 0 || entryPoint >= count))
            throw new InvalidDataException("HNSW sidecar 的 entryPoint 越界。");

        return new SerializedHeader(blockIndex, count, dimension, m, ef, maxLevel, entryPoint);
    }

    private static int[][][] ReadNeighbors(Stream stream, int count)
    {
        var neighbors = new int[count][][];
        for (int node = 0; node < count; node++)
        {
            int levelCount = ReadInt32(stream);
            if (levelCount <= 0)
                throw new InvalidDataException("HNSW sidecar 的 levelCount 必须 > 0。");

            neighbors[node] = new int[levelCount][];
            for (int level = 0; level < levelCount; level++)
            {
                int neighborCount = ReadInt32(stream);
                if (neighborCount < 0)
                    throw new InvalidDataException("HNSW sidecar 的 neighborCount 不能为负。");

                var levelNeighbors = new int[neighborCount];
                for (int i = 0; i < neighborCount; i++)
                {
                    int neighbor = ReadInt32(stream);
                    if (neighbor < 0 || neighbor >= count)
                        throw new InvalidDataException("HNSW sidecar 的邻接节点越界。");
                    levelNeighbors[i] = neighbor;
                }

                neighbors[node][level] = levelNeighbors;
            }
        }

        return neighbors;
    }

    private static void SkipNeighbors(Stream stream, int count)
    {
        for (int node = 0; node < count; node++)
        {
            int levelCount = ReadInt32(stream);
            if (levelCount <= 0)
                throw new InvalidDataException("HNSW sidecar 的 levelCount 必须 > 0。");

            for (int level = 0; level < levelCount; level++)
            {
                int neighborCount = ReadInt32(stream);
                if (neighborCount < 0)
                    throw new InvalidDataException("HNSW sidecar 的 neighborCount 不能为负。");

                SkipBytes(stream, (long)neighborCount * sizeof(int));
            }
        }
    }

    private static long EstimateBytes(int[][][] neighbors)
    {
        const long ObjectBytes = 24L;
        const long ReferenceBytes = 8L;
        const long IndexObjectBytes = 64L;

        long bytes = IndexObjectBytes + ObjectBytes + (long)neighbors.Length * ReferenceBytes;
        for (int node = 0; node < neighbors.Length; node++)
        {
            var levels = neighbors[node];
            bytes += ObjectBytes + (long)levels.Length * ReferenceBytes;

            for (int level = 0; level < levels.Length; level++)
                bytes += ObjectBytes + (long)levels[level].Length * sizeof(int);
        }

        return Math.Max(1L, bytes);
    }

    private static int GreedySearch(
        ReadOnlySpan<DataPoint> points,
        int[][][] neighbors,
        int targetNode,
        int entryNode,
        int level,
        ref double currentDistance)
    {
        int current = entryNode;
        bool improved;
        do
        {
            improved = false;
            var levelNeighbors = neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                double distance = VectorDistance.ComputeCosine(
                    points[targetNode].Value.AsVector().Span,
                    points[neighbor].Value.AsVector().Span);
                if (distance < currentDistance)
                {
                    current = neighbor;
                    currentDistance = distance;
                    improved = true;
                }
            }
        }
        while (improved);

        return current;
    }

    private int GreedySearch(
        ReadOnlySpan<byte> valPayload,
        ReadOnlySpan<float> queryVector,
        int entryNode,
        int level,
        KnnMetric metric,
        ref double currentDistance)
    {
        int current = entryNode;
        bool improved;
        do
        {
            improved = false;
            var levelNeighbors = _neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                double distance = VectorDistance.Compute(metric, queryVector, GetVector(valPayload, neighbor, Dimension));
                if (distance < currentDistance)
                {
                    current = neighbor;
                    currentDistance = distance;
                    improved = true;
                }
            }
        }
        while (improved);

        return current;
    }

    private static int GreedySearch(
        ReadOnlySpan<byte> valPayload,
        int[][][] neighbors,
        int targetNode,
        int entryNode,
        int level,
        int dimension,
        ref double currentDistance)
    {
        int current = entryNode;
        bool improved;
        do
        {
            improved = false;
            var levelNeighbors = neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                double distance = VectorDistance.ComputeCosine(
                    GetVector(valPayload, targetNode, dimension),
                    GetVector(valPayload, neighbor, dimension));
                if (distance < currentDistance)
                {
                    current = neighbor;
                    currentDistance = distance;
                    improved = true;
                }
            }
        }
        while (improved);

        return current;
    }

    private static List<NeighborCandidate> SearchLayer(
        ReadOnlySpan<DataPoint> points,
        int[][][] neighbors,
        int targetNode,
        int entryNode,
        int level,
        int ef)
    {
        var visited = new bool[points.Length];
        var candidateQueue = new PriorityQueue<int, double>();
        var results = new List<NeighborCandidate>();

        double entryDistance = VectorDistance.ComputeCosine(
            points[targetNode].Value.AsVector().Span,
            points[entryNode].Value.AsVector().Span);
        candidateQueue.Enqueue(entryNode, entryDistance);
        visited[entryNode] = true;
        AddResult(results, new NeighborCandidate(entryNode, entryDistance), ef);

        while (candidateQueue.Count > 0)
        {
            candidateQueue.TryDequeue(out int current, out double currentDistance);
            double worstDistance = GetWorstDistance(results);
            if (results.Count >= ef && currentDistance > worstDistance)
                break;

            var levelNeighbors = neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                double distance = VectorDistance.ComputeCosine(
                    points[targetNode].Value.AsVector().Span,
                    points[neighbor].Value.AsVector().Span);
                if (results.Count < ef || distance < worstDistance)
                {
                    candidateQueue.Enqueue(neighbor, distance);
                    AddResult(results, new NeighborCandidate(neighbor, distance), ef);
                    worstDistance = GetWorstDistance(results);
                }
            }
        }

        results.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return results;
    }

    private List<NeighborCandidate> SearchLayer(
        ReadOnlySpan<byte> valPayload,
        ReadOnlySpan<float> queryVector,
        int entryNode,
        int level,
        int ef,
        KnnMetric metric)
    {
        var visited = new bool[Count];
        var candidateQueue = new PriorityQueue<int, double>();
        var results = new List<NeighborCandidate>();

        double entryDistance = VectorDistance.Compute(metric, queryVector, GetVector(valPayload, entryNode, Dimension));
        candidateQueue.Enqueue(entryNode, entryDistance);
        visited[entryNode] = true;
        AddResult(results, new NeighborCandidate(entryNode, entryDistance), ef);

        while (candidateQueue.Count > 0)
        {
            candidateQueue.TryDequeue(out int current, out double currentDistance);
            double worstDistance = GetWorstDistance(results);
            if (results.Count >= ef && currentDistance > worstDistance)
                break;

            var levelNeighbors = _neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                double distance = VectorDistance.Compute(metric, queryVector, GetVector(valPayload, neighbor, Dimension));
                if (results.Count < ef || distance < worstDistance)
                {
                    candidateQueue.Enqueue(neighbor, distance);
                    AddResult(results, new NeighborCandidate(neighbor, distance), ef);
                    worstDistance = GetWorstDistance(results);
                }
            }
        }

        results.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return results;
    }

    private static List<NeighborCandidate> SearchLayer(
        ReadOnlySpan<byte> valPayload,
        int[][][] neighbors,
        int targetNode,
        int entryNode,
        int level,
        int ef,
        int dimension)
    {
        int count = neighbors.Length;
        var visited = new bool[count];
        var candidateQueue = new PriorityQueue<int, double>();
        var results = new List<NeighborCandidate>();

        double entryDistance = VectorDistance.ComputeCosine(
            GetVector(valPayload, targetNode, dimension),
            GetVector(valPayload, entryNode, dimension));
        candidateQueue.Enqueue(entryNode, entryDistance);
        visited[entryNode] = true;
        AddResult(results, new NeighborCandidate(entryNode, entryDistance), ef);

        while (candidateQueue.Count > 0)
        {
            candidateQueue.TryDequeue(out int current, out double currentDistance);
            double worstDistance = GetWorstDistance(results);
            if (results.Count >= ef && currentDistance > worstDistance)
                break;

            var levelNeighbors = neighbors[current][level];
            for (int i = 0; i < levelNeighbors.Length; i++)
            {
                int neighbor = levelNeighbors[i];
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                double distance = VectorDistance.ComputeCosine(
                    GetVector(valPayload, targetNode, dimension),
                    GetVector(valPayload, neighbor, dimension));
                if (results.Count < ef || distance < worstDistance)
                {
                    candidateQueue.Enqueue(neighbor, distance);
                    AddResult(results, new NeighborCandidate(neighbor, distance), ef);
                    worstDistance = GetWorstDistance(results);
                }
            }
        }

        results.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return results;
    }

    private static int[] AddNeighbor(
        int[] existing,
        int neighbor,
        int maxNeighbors,
        int ownerNode,
        ReadOnlySpan<DataPoint> points)
    {
        if (existing.AsSpan().Contains(neighbor))
            return existing;

        var expanded = new int[existing.Length + 1];
        existing.CopyTo(expanded, 0);
        expanded[^1] = neighbor;

        if (expanded.Length <= maxNeighbors)
            return expanded;

        var ranked = new (int Node, double Distance)[expanded.Length];
        for (int i = 0; i < expanded.Length; i++)
        {
            ranked[i] = (expanded[i], VectorDistance.ComputeCosine(
                points[ownerNode].Value.AsVector().Span,
                points[expanded[i]].Value.AsVector().Span));
        }
        Array.Sort(ranked, static (left, right) => left.Distance.CompareTo(right.Distance));

        var trimmed = new int[maxNeighbors];
        for (int i = 0; i < trimmed.Length; i++)
            trimmed[i] = ranked[i].Node;
        return trimmed;
    }

    private static int[] AddNeighbor(
        int[] existing,
        int neighbor,
        int maxNeighbors,
        int ownerNode,
        ReadOnlySpan<byte> valPayload,
        int dimension)
    {
        if (existing.AsSpan().Contains(neighbor))
            return existing;

        var expanded = new int[existing.Length + 1];
        existing.CopyTo(expanded, 0);
        expanded[^1] = neighbor;

        if (expanded.Length <= maxNeighbors)
            return expanded;

        var ranked = new (int Node, double Distance)[expanded.Length];
        for (int i = 0; i < expanded.Length; i++)
        {
            ranked[i] = (expanded[i], VectorDistance.ComputeCosine(
                GetVector(valPayload, ownerNode, dimension),
                GetVector(valPayload, expanded[i], dimension)));
        }
        Array.Sort(ranked, static (left, right) => left.Distance.CompareTo(right.Distance));

        var trimmed = new int[maxNeighbors];
        for (int i = 0; i < trimmed.Length; i++)
            trimmed[i] = ranked[i].Node;
        return trimmed;
    }

    private static void AddResult(List<NeighborCandidate> results, NeighborCandidate candidate, int maxCount)
    {
        results.Add(candidate);
        if (results.Count <= maxCount)
            return;

        int worstIndex = 0;
        double worstDistance = results[0].Distance;
        for (int i = 1; i < results.Count; i++)
        {
            if (results[i].Distance > worstDistance)
            {
                worstDistance = results[i].Distance;
                worstIndex = i;
            }
        }

        results.RemoveAt(worstIndex);
    }

    private static double GetWorstDistance(List<NeighborCandidate> results)
    {
        if (results.Count == 0)
            return double.PositiveInfinity;

        double worst = results[0].Distance;
        for (int i = 1; i < results.Count; i++)
        {
            if (results[i].Distance > worst)
                worst = results[i].Distance;
        }

        return worst;
    }

    private static ReadOnlySpan<float> GetVector(ReadOnlySpan<byte> valPayload, int index, int dimension)
        => MemoryMarshal.Cast<byte, float>(
            valPayload.Slice(index * dimension * sizeof(float), dimension * sizeof(float)));

    private static ulong ComputeSeed(int blockIndex, int count, int dimension, int m, int ef)
    {
        ulong seed = (ulong)(uint)blockIndex;
        seed = (seed << 32) ^ (uint)count;
        seed ^= (ulong)(uint)dimension << 11;
        seed ^= (ulong)(uint)m << 21;
        seed ^= (ulong)(uint)ef << 33;
        return seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    private static int SampleLevel(ref ulong state, int m)
    {
        double probability = 1.0 / Math.Max(m, 2);
        int level = 0;
        while (level < 16 && NextUnitDouble(ref state) < probability)
            level++;
        return level;
    }

    private static double NextUnitDouble(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buf = stackalloc byte[4];
        FillBuffer(stream, buf);
        return BinaryPrimitives.ReadInt32LittleEndian(buf);
    }

    private static void SkipBytes(Stream stream, long byteCount)
    {
        if (byteCount <= 0)
            return;

        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (byteCount > remaining)
                throw new InvalidDataException("HNSW sidecar 文件截断。");
            stream.Seek(byteCount, SeekOrigin.Current);
            return;
        }

        Span<byte> buf = stackalloc byte[256];
        while (byteCount > 0)
        {
            int take = (int)Math.Min(buf.Length, byteCount);
            FillBuffer(stream, buf[..take]);
            byteCount -= take;
        }
    }

    private static void FillBuffer(Stream stream, Span<byte> buffer)
    {
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int read = stream.Read(buffer[readTotal..]);
            if (read == 0)
                throw new InvalidDataException("HNSW sidecar 文件截断。");
            readTotal += read;
        }
    }

    private readonly record struct SerializedHeader(
        int BlockIndex,
        int Count,
        int Dimension,
        int M,
        int Ef,
        int MaxLevel,
        int EntryPoint);

    private readonly record struct NeighborCandidate(int Node, double Distance);
}

/// <summary>
/// HNSW 查询命中的 block 内候选结果。
/// </summary>
/// <param name="PointIndex">block 内点位序号。</param>
/// <param name="Timestamp">命中点时间戳。</param>
/// <param name="Distance">与查询向量的距离。</param>
internal readonly record struct HnswAnnSearchResult(int PointIndex, long Timestamp, double Distance);
