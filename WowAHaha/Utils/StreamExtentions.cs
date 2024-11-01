using JetBrains.Annotations;

namespace WowAHaha.Utils;

public static class StreamExtentions
{
    [MustUseReturnValue]
    public static long TrimEnd(this Stream stream)
    {
        const int bufferSize = 8;
        Span<byte> bytes = stackalloc byte[bufferSize];
        long seekPosition;
        var done = false;
        do
        {
            stream.Seek(0, SeekOrigin.End);
            // remove last line if it's empty
            seekPosition = stream.Position;
            if (seekPosition == 0)
            {
                return seekPosition;
            }

            stream.Seek(-Math.Min(seekPosition, bufferSize), SeekOrigin.Current);
            var bytesRead = stream.Read(bytes);
            if (bytesRead == 0)
            {
                return seekPosition;
            }
            for (var i = bytesRead - 1; i >= 0 && !done; i--)
            {
                switch ((char)bytes[i])
                {
                    case '\n':
                    case '\r':
                    case '\0':
                    case '\t':
                    case ' ':
                        seekPosition--;
                        if (seekPosition == 0)
                        {
                            done = true;
                        }
                        break;
                    default:
                        done = true;
                        break;
                }
            }
        } while (!done);


        stream.SetLength(seekPosition);
        stream.Seek(seekPosition, SeekOrigin.Begin);

        return seekPosition;
    }
}