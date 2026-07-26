using System.Buffers;

namespace UsmDivinerSharp;

readonly struct LightweightArrayPoolHandler<T>(int size, bool clearBeforeReturn = false) : IDisposable
{
    public T[] Array { get; } = ArrayPool<T>.Shared.Rent(size);
    public void Dispose()
        => ArrayPool<T>.Shared.Return(Array, clearBeforeReturn);

    public static implicit operator T[](LightweightArrayPoolHandler<T> handler) => handler.Array;
    public static implicit operator Span<T>(LightweightArrayPoolHandler<T> handler) => handler.Array;
    public static implicit operator ReadOnlySpan<T>(LightweightArrayPoolHandler<T> handler) => handler.Array;
    public static implicit operator Memory<T>(LightweightArrayPoolHandler<T> handler) => handler.Array;
    public static implicit operator ReadOnlyMemory<T>(LightweightArrayPoolHandler<T> handler) => handler.Array;
}