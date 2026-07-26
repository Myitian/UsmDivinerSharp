using System.Buffers;

namespace UsmDivinerSharp;

readonly struct LightweightArrayPoolHandler<T>(int size) : IDisposable
{
    public T[] Array { get; } = ArrayPool<T>.Shared.Rent(size);
    public void Dispose()
        => ArrayPool<T>.Shared.Return(Array);
}