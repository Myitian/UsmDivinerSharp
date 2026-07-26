using System.Buffers;

namespace UsmDivinerSharp;

readonly struct ScoreMatrix() : IDisposable
{
    private readonly int[] _buffer = ArrayPool<int>.Shared.Rent(65536 * 32);
    public ref int Unigram(int y, int x) => ref _buffer[y * 256 + x]; // real size: 256*32
    public ref int Bigram(int y, int x) => ref _buffer[y * 65536 + x + 65536]; // real size: 65536*31
    public void Dispose()
        => ArrayPool<int>.Shared.Return(_buffer);
}