using System.Numerics;
using System.Runtime.InteropServices;

namespace UsmDivinerSharp;

static class Utils
{
    extension<T, TNumber>(Dictionary<T, TNumber> dict)
        where T : notnull
        where TNumber : struct, IIncrementOperators<TNumber>
    {
        public void IncrementCount(T key)
        {
            ref TNumber count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
            count++;
        }
    }
    extension<T>(IEnumerable<T> source)
    {
        public T[] ToSortedArray()
        {
            T[] array = [.. source];
            Array.Sort(array);
            return array;
        }
    }
    extension(ConstraintTrust trust)
    {
        public string FastToString()
        {
            return trust switch
            {
                ConstraintTrust.C9Template => nameof(ConstraintTrust.C9Template),
                ConstraintTrust.SingleMarkerExactSize => nameof(ConstraintTrust.SingleMarkerExactSize),
                ConstraintTrust.BothMarker => nameof(ConstraintTrust.BothMarker),
                _ => throw new ArgumentOutOfRangeException(nameof(trust), trust, null)
            };
        }
        public static string FastToString(ConstraintTrust low, ConstraintTrust high)
        {
            const string L12 = $"{nameof(ConstraintTrust.C9Template)}_vs_{nameof(ConstraintTrust.SingleMarkerExactSize)}";
            const string L13 = $"{nameof(ConstraintTrust.C9Template)}_vs_{nameof(ConstraintTrust.BothMarker)}";
            const string L23 = $"{nameof(ConstraintTrust.SingleMarkerExactSize)}_vs_{nameof(ConstraintTrust.BothMarker)}";

            return low switch
            {
                ConstraintTrust.C9Template => high switch
                {
                    ConstraintTrust.SingleMarkerExactSize => L12,
                    ConstraintTrust.BothMarker => L13,
                    _ => throw new ArgumentOutOfRangeException(nameof(high), high, null)
                },
                ConstraintTrust.SingleMarkerExactSize => high switch
                {
                    ConstraintTrust.SingleMarkerExactSize => L23,
                    _ => throw new ArgumentOutOfRangeException(nameof(high), high, null)
                },
                _ => throw new ArgumentOutOfRangeException(nameof(low), low, null)
            };
        }
    }
    extension<T>(T[] a)
    {
        public T[] ConcatWith(T[] b)
        {
            if (b.Length == 0)
                return a;
            if (a.Length == 0)
                return b;
            return [.. a, .. b];
        }
    }
    extension<T>(scoped ReadOnlySpan<T> source)
    {
        public T[] TopN(int n, IComparer<T>? comparer = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(n);
            if (n == 0 || source.IsEmpty)
                return [];
            comparer ??= Comparer<T>.Default;
            T[] result;
            if (source.Length <= n)
            {
                result = [.. source];
                result.AsSpan().Sort(comparer);
            }
            else
            {
                PriorityQueue<T, T> heap = new(comparer);
                foreach (T item in source)
                {
                    if (heap.Count < n)
                        heap.Enqueue(item, item);
                    else if (comparer.Compare(item, heap.Peek()) < 0)
                        heap.DequeueEnqueue(item, item);
                }
                result = new T[heap.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = heap.Dequeue();
            }
            return result;
        }
    }
}