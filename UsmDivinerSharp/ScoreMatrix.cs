using System.Buffers;

namespace UsmDivinerSharp;

sealed class ScoreMatrix : IDisposable
{
    public int[] Buffer { get; private set; } = ArrayPool<int>.Shared.Rent(65536 * 32);
    public ref int Unigram(int y, int x) => ref Buffer[y * 256 + x];
    public ref int Bigram(int y, int x) => ref Buffer[y * 65536 + x + 65536];
    public void Dispose()
    {
        if (Buffer is not null)
        {
            ArrayPool<int>.Shared.Return(Buffer, true);
            Buffer = null!;
        }
    }
}