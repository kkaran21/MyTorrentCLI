namespace MyTorrentCLI.Extensions
{
    public static class StreamExtension
    {
        public static async Task ReadExactAsync(this Stream stream,byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int updatedCount = count;
            int updatedOffset = offset;

            while (updatedCount > 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, updatedOffset, updatedCount, cancellationToken);
                if (bytesRead == 0)
                    throw new EndOfStreamException("stream ended before reading all bytes");
                updatedCount -= bytesRead;
                updatedOffset += bytesRead;
            }            
        }
    }
}