using System.Buffers;
using System.Security.Cryptography;

namespace SubRenamer.Mobile.Services;

public sealed record CopyFingerprint(long Length, string Sha256);

public static class StreamCopyService
{
    // Subtitle placement is usually sequential and SAF-backed. A larger buffer
    // cuts the number of provider/native read+write calls without changing the
    // one-pass SHA-256 safety fingerprint.
    private const int CopyBufferSize = 256 * 1024;

    public static async Task<CopyFingerprint> CopyWithSha256Async(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
            }

            return new CopyFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
