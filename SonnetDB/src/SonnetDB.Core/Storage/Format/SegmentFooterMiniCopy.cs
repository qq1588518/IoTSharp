namespace SonnetDB.Storage.Format;

internal readonly record struct SegmentFooterMiniCopy(
    int IndexCount,
    long IndexOffset,
    long FileLength,
    uint IndexCrc32)
{
    public bool IsShapeValid()
    {
        if (IndexCount < 0)
            return false;

        if (IndexOffset < FormatSizes.SegmentHeaderSize)
            return false;

        if (FileLength < FormatSizes.SegmentHeaderSize + FormatSizes.SegmentFooterSize)
            return false;

        long indexBytes = (long)IndexCount * FormatSizes.BlockIndexEntrySize;
        if (IndexOffset > long.MaxValue - indexBytes)
            return false;

        long footerOffset = IndexOffset + indexBytes;
        return footerOffset <= FileLength - FormatSizes.SegmentFooterSize;
    }
}
