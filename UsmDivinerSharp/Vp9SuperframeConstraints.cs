using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PlainVm1Constraints = System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>;
using Vm1Constraints = System.Collections.Generic.Dictionary<int, UsmDivinerSharp.Vm1ConstraintEntry>;

namespace UsmDivinerSharp;

static class Vp9SuperframeConstraints
{
    const int MaxExactSizeUnknownBytes = 2;
    const string C9TemplateReason = "vp9_c9_second_frame_4byte_header";
    static uint GetC9SecondFrame4BytePrefixes(uint value)
    {
        // Empirical mapping for common C9 superframes:
        // hidden inter-frame prefix -> shown inter-frame prefix.
        return value switch
        {
            0x84008049 => 0x86004096,
            0x84004085 => 0x8600410e,
            _ => 0
        };
    }
    static Vm1Constraints MergeFinalConstraints(Vm1Constraints @base, Vm1Constraints extra)
    {
        foreach (KeyValuePair<int, Vm1ConstraintEntry> kvp in extra)
        {
            ref Vm1ConstraintEntry? current = ref CollectionsMarshal.GetValueRefOrAddDefault(@base, kvp.Key, out _);
            if (current is null)
            {
                current = kvp.Value;
                continue;
            }
            current.Values.IntersectWith(kvp.Value.Values);
            if (current.Values.Count == 0)
            {
                @base.Clear();
                break;
            }
            current = MergeCompatibleEntries(current, kvp.Value, current.Values);
        }
        return @base;
    }
    static Vm1ConstraintEntry MergeCompatibleEntries(Vm1ConstraintEntry current, Vm1ConstraintEntry entry, HashSet<int> values)
    {
        ConstraintTrust trust;
        string reason;
        if (current.Trust > entry.Trust)
        {
            trust = current.Trust;
            reason = current.Reason;
        }
        else
        {
            trust = entry.Trust;
            reason = entry.Reason;
        }
        int support = current.Reason == entry.Reason ? current.Support + entry.Support : 1;
        return new(values, trust, reason, support);
    }
    static void RecordPlaintextConstraints(scoped ReadOnlySpan<byte> payload, scoped ReadOnlySpan<PlaintextConstraint> plaintextConstraints, ConstraintTrust trust, Vp9ConstraintStats stats)
    {
        // Plaintext facts only become evidence when the offset has a single-vm1 relation.
        foreach (PlaintextConstraint constraint in plaintextConstraints)
        {
            if (!Vm1ValuesFromPlaintextConstraint(payload, constraint.PayloadOffset, constraint.AllowedValue, out int column, out int allowed))
                continue;
            Vm1Evidence evidence = new(column, [allowed], trust, constraint.Reason);
            stats.AddEvidence(evidence);
        }
    }
    static MarkerCandidate? DetectSuperframeIndex(scoped ReadOnlySpan<byte> payload, int frameStart, int frameEnd, bool enableC9Template = true)
    {
        // Prefer spec-backed evidence first; the C9 template is only a fallback.
        bool tooManyBothMarkers = false;
        bool tooManyExactSize = false;
        bool tooManyTemplate = false;
        MarkerCandidate? bothMarkers = null;
        MarkerCandidate? exactSize = null;
        MarkerCandidate? template = null;
        for (int marker = 0xC0; marker < 0xE0; marker++)
        {
            if (!TryParseSuperframeIndex(payload, frameStart, frameEnd, marker, out SuperframeIndex index))
                continue;
            if (BothMarkersObserved(payload, ref index))
            {
                if (bothMarkers is not null)
                {
                    tooManyBothMarkers = true;
                    break;
                }
                bothMarkers = new(index, "both_marker_superframe", ConstraintTrust.BothMarker);
            }
            else if (!tooManyExactSize && SingleMarkerWithExactSize(payload, ref index))
            {
                if (exactSize is not null)
                {
                    tooManyExactSize = true;
                    continue;
                }
                exactSize = new(index, "exact_size_superframe", ConstraintTrust.SingleMarkerExactSize);
            }
            // C9 template fallback. Use only when no stronger marker candidate exists.
            else if (!tooManyExactSize && !tooManyTemplate && enableC9Template && IsC9TemplateSupported(payload, frameStart, ref index))
            {
                if (template is not null)
                {
                    tooManyTemplate = true;
                    continue;
                }
                template = new(index, "c9_template_superframe", ConstraintTrust.C9Template);
            }
        }
        if (tooManyBothMarkers)
            return null;
        if (bothMarkers is not null)
            return bothMarkers;
        if (tooManyExactSize)
            return null;
        if (exactSize is not null)
            return exactSize;
        if (tooManyTemplate)
            return null;
        return template;
    }
    static bool TryParseSuperframeIndex(scoped ReadOnlySpan<byte> payload, int frameStart, int frameEnd, int marker, out SuperframeIndex index)
    {
        index = default;
        if (!IsVp9SuperframeMarker(marker))
            return false;
        SuperframeMeta(marker, out int bytesPerSize, out int frameCount, out int indexSize);
        int indexStart = frameEnd - indexSize;
        if (indexStart < frameStart || indexStart < 0 || frameEnd > payload.Length)
            return false;
        if (PlainByteCanBe(payload, indexStart, marker) is not true)
            return false;
        if (PlainByteCanBe(payload, frameEnd - 1, marker) is not true)
            return false;
        int totalSubframeSize = frameEnd - frameStart - indexSize;
        if (!SuperframeSizesFeasible(payload, indexStart, totalSubframeSize, bytesPerSize, frameCount))
            return false;
        index = new(marker, bytesPerSize, frameCount, indexStart, indexSize, frameEnd - frameStart);
        return true;
    }
    static bool BothMarkersObserved(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        bool startObserved = KeylessPlainByte(payload, index.IndexStart) == index.Marker;
        bool endObserved = KeylessPlainByte(payload, index.IndexStart + index.IndexSize - 1) == index.Marker;
        return startObserved && endObserved;
    }
    static bool SingleMarkerWithExactSize(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        bool startObserved = KeylessPlainByte(payload, index.IndexStart) == index.Marker;
        bool endObserved = KeylessPlainByte(payload, index.IndexStart + index.IndexSize - 1) == index.Marker;
        if (startObserved == endObserved)
            return false;
        // One observed marker plus exact observed sizes is strong, but not absolute.
        return SuperframeSizesKnownAndExact(payload, in index);
    }
    static bool IsC9TemplateSupported(scoped ReadOnlySpan<byte> payload, int frameStart, ref readonly SuperframeIndex index)
    {
        if (index.Marker != 0xC9)
            return false;
        return C9SecondFrameTemplateIsVerified(payload, frameStart, in index);
    }
    static PlaintextConstraint[] VerifiedSuperframeConstraints(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        PlaintextConstraint[] a = SuperframeMarkerConstraints(payload, in index);
        PlaintextConstraint[] b = ExactSuperframeSizeConstraints(payload, in index);
        return a.ConcatWith(b);
    }
    static PlaintextConstraint[] SuperframeMarkerConstraints(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        PlaintextConstraint[] constraints = new PlaintextConstraint[2];
        int i = 0;
        int offset = index.IndexStart;
        if (SingleVm1Relation(payload, offset, out _, out _, out _))
            constraints[i++] = new(offset, index.Marker, "vp9_superframe_start_marker");
        offset += index.IndexSize - 1;
        if (SingleVm1Relation(payload, offset, out _, out _, out _))
            constraints[i++] = new(offset, index.Marker, "vp9_superframe_tail_marker");
        return i switch
        {
            0 => [],
            1 => [constraints[0]],
            _ => constraints
        };
    }
    static PlaintextConstraint[] ExactSuperframeSizeConstraints(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        int[]? sizes = SolveExactSuperframeSizes(payload, in index);
        if (sizes is null)
            return [];
        PlaintextConstraint[] constraints = new PlaintextConstraint[sizes.Length * index.BytesPerSize];
        int i = 0;
        foreach (int size in sizes)
        {
            for (int byteIndex = 0; byteIndex < index.BytesPerSize; byteIndex++, i++)
            {
                int value = (size >> (byteIndex * 8)) & 0xFF;
                constraints[i] = new(index.IndexStart + i + 1, value, "vp9_superframe_exact_size");
            }
        }
        return constraints;
    }
    static PlaintextConstraint[] C9SecondFrame4ByteHeaderConstraints(scoped ReadOnlySpan<byte> payload, int frameStart, ref readonly SuperframeIndex index)
    {
        if (index is not { Marker: 0xC9, BytesPerSize: 2, FrameCount: 2 })
            return [];
        int[]? sizes = SolveExactSuperframeSizes(payload, in index);
        if (sizes is null)
            return [];
        int firstSize = sizes[0];
        int secondFrameOffset = frameStart + firstSize;
        if (secondFrameOffset <= frameStart || secondFrameOffset + 4 >= index.IndexStart)
            return [];
        if (KeylessPlainByte(payload, frameStart + 0) is not int v0)
            return [];
        uint value = (uint)v0;
        if (KeylessPlainByte(payload, frameStart + 1) is not int v1)
            return [];
        value = (value << 8) | (uint)v1;
        if (KeylessPlainByte(payload, frameStart + 2) is not int v2)
            return [];
        value = (value << 8) | (uint)v2;
        if (KeylessPlainByte(payload, frameStart + 3) is not int v3)
            return [];
        value = (value << 8) | (uint)v3;
        uint expected = GetC9SecondFrame4BytePrefixes(value);
        if (expected == 0)
            return [];
        // Empirical C9-only header template; keep isolated and lower-trust.
        return [
            new(secondFrameOffset + 0, (int)((expected >> 24) & 0xFF), C9TemplateReason),
            new(secondFrameOffset + 1, (int)((expected >> 16) & 0xFF), C9TemplateReason),
            new(secondFrameOffset + 2, (int)((expected >> 8) & 0xFF), C9TemplateReason),
            new(secondFrameOffset + 3, (int)(expected & 0xFF), C9TemplateReason)];
    }
    static bool C9SecondFrameTemplateIsVerified(scoped ReadOnlySpan<byte> payload, int frameStart, ref readonly SuperframeIndex index)
    {
        if (index is not { Marker: 0xC9, BytesPerSize: 2, FrameCount: 2 })
            return false;
        int[]? sizes = SolveExactSuperframeSizes(payload, in index);
        if (sizes is null)
            return false;
        int firstSize = sizes[0];
        int secondFrameOffset = frameStart + firstSize;
        if (secondFrameOffset <= frameStart || secondFrameOffset + 4 >= index.IndexStart)
            return false;
        if (KeylessPlainByte(payload, frameStart + 0) is not int v0)
            return false;
        uint value = (uint)v0;
        if (KeylessPlainByte(payload, frameStart + 1) is not int v1)
            return false;
        value = (value << 8) | (uint)v1;
        if (KeylessPlainByte(payload, frameStart + 2) is not int v2)
            return false;
        value = (value << 8) | (uint)v2;
        if (KeylessPlainByte(payload, frameStart + 3) is not int v3)
            return false;
        value = (value << 8) | (uint)v3;
        return GetC9SecondFrame4BytePrefixes(value) != 0;
    }
    static int[]? SolveExactSuperframeSizes(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        // Exact size evidence is emitted only when all unknown bytes have one solution.
        int bytePerSize = index.BytesPerSize;
        int totalSubframeSize = index.FrameSize - index.IndexSize;
        if (totalSubframeSize < index.FrameCount)
            return null;
        int?[] known = new int?[index.FrameCount * bytePerSize];
        for (int i = 0; i < known.Length; i++)
            known[i] = KeylessPlainByte(payload, index.IndexStart + i + 1);
        int unknownCount = known.AsSpan().Count((int?)null);
        if (unknownCount > MaxExactSizeUnknownBytes)
            return null;

        int[]? possible = null;
        int[] candidateValues = new int[known.Length];
        return Search(0) ? possible : null;

        bool Search(int bytePos)
        {
            if (bytePos == known.Length)
            {
                int[] sizes = SizeBytesToValues(candidateValues, bytePerSize);
                if (sizes.AsSpan().ContainsAnyInRange(int.MinValue, 0))
                    return false;
                if (sizes.Sum() == totalSubframeSize)
                {
                    if (possible is not null)
                        return false;
                    possible = sizes;
                }
                return true;
            }
            if (known[bytePos] is int value)
            {
                candidateValues[bytePos] = value;
                return Search(bytePos + 1);
            }
            for (int guessed = 0; guessed < 256; guessed++)
            {
                candidateValues[bytePos] = guessed;
                if (!Search(bytePos + 1))
                    return false;
            }
            return true;
        }
    }
    static int[] SizeBytesToValues(scoped ReadOnlySpan<int> values, int bytePerSize)
    {
        int[] sizes = new int[values.Length / bytePerSize];
        for (int i = 0, start = 0; start < values.Length; i++, start += bytePerSize)
        {
            int size = 0;
            for (int byteIndex = 0; byteIndex < bytePerSize; byteIndex++)
                size |= values[start + byteIndex] << (byteIndex * 8);
            sizes[i] = size;
        }
        return sizes;
    }
    static bool SuperframeSizesKnownAndExact(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        int[]? sizes = SolveObservedSuperframeSizes(payload, in index);
        if (sizes is null)
            return false;
        return sizes.Sum() == index.FrameSize - index.IndexSize;
    }
    static int[]? SolveObservedSuperframeSizes(scoped ReadOnlySpan<byte> payload, ref readonly SuperframeIndex index)
    {
        List<int> values = [];
        for (int i = 1, end = index.FrameCount * index.BytesPerSize; i <= end; i++)
        {
            int? value = KeylessPlainByte(payload, index.IndexStart + i);
            if (!value.HasValue)
                return null;
            values.Add(value.Value);
        }
        int[] sizes = SizeBytesToValues(CollectionsMarshal.AsSpan(values), index.BytesPerSize);
        if (sizes.AsSpan().ContainsAnyInRange(int.MinValue, 0))
            return null;
        return sizes;
    }
    static bool SuperframeSizesFeasible(scoped ReadOnlySpan<byte> payload, int indexStart, int totalSubframeSize, int bytesPerSize, int frameCount)
    {
        int minSum = 0;
        int maxSum = 0;
        int pos = indexStart + 1;
        bool allKnown = true;
        for (int i = 0; i < frameCount; i++)
        {
            int minValue = 0;
            int maxValue = 0;
            for (int byteIndex = 0; byteIndex < bytesPerSize; byteIndex++)
            {
                int coefficient = 1 << (byteIndex * 8);
                int? known = KeylessPlainByte(payload, pos + byteIndex);
                if (!known.HasValue)
                {
                    allKnown = false;
                    maxValue += 0xFF * coefficient;
                }
                else
                {
                    coefficient *= known.Value;
                    minValue += coefficient;
                    maxValue += coefficient;
                }
            }
            minSum += minValue;
            maxSum += maxValue;
            pos += bytesPerSize;
        }
        if (allKnown)
            return minSum == totalSubframeSize;
        return minSum <= totalSubframeSize && totalSubframeSize <= maxSum;
    }
    static bool? PlainByteCanBe(scoped ReadOnlySpan<byte> payload, int offset, int value)
    {
        int? known = KeylessPlainByte(payload, offset);
        if (known.HasValue)
            return known.Value == value;
        if (IsDirectVm1Offset(offset))
            return true;
        return null;
    }
    static bool Vm1ValuesFromPlaintextConstraint(scoped ReadOnlySpan<byte> payload, int payloadOffset, int allowedPlaintext, out int column, out int allowedValue)
    {
        allowedValue = default;
        if (!SingleVm1Relation(payload, payloadOffset, out column, out int knownXor, out bool xorFF))
            return false;
        allowedValue = knownXor ^ allowedPlaintext ^ (xorFF ? 0xFF : 0);
        return true;
    }
    static int? KeylessPlainByte(scoped ReadOnlySpan<byte> payload, int payloadOffset)
    {
        if (payloadOffset < 0 || payloadOffset >= payload.Length)
            return null;
        if (payloadOffset < Constants.VIDEO_MASK_START)
            return payload[payloadOffset];
        if (payloadOffset < Constants.VIDEO_CRACK_START)
        {
            (int headBlock, int column) = Math.DivRem(payloadOffset - Constants.VIDEO_MASK_START, 32);
            if (headBlock % 2 == 0)
                return null;
            int? currentS = EncryptedPrefixXor(payload, headBlock, column);
            if (!currentS.HasValue)
                return null;
            return payload[payloadOffset] ^ currentS.Value ^ 0xFF;
        }
        else
        {
            (int blockIndex, int column) = Math.DivRem(payloadOffset - Constants.VIDEO_CRACK_START, 32);
            if (blockIndex % 2 == 0)
                return null;
            return EncryptedPrefixXor(payload, blockIndex, column);
        }
    }
    static bool SingleVm1Relation(scoped ReadOnlySpan<byte> payload, int payloadOffset, out int vm1Column, out int knownXor, out bool xorFF)
    {
        vm1Column = default;
        knownXor = default;
        xorFF = default;
        if (payloadOffset < 0 || payloadOffset >= payload.Length)
            return false;

        if (payloadOffset is >= Constants.VIDEO_MASK_START and < Constants.VIDEO_CRACK_START)
        {
            (int headBlock, int column) = Math.DivRem(payloadOffset - Constants.VIDEO_MASK_START, 32);
            if (headBlock % 2 == 0)
                return false;
            int? currentS = EncryptedPrefixXor(payload, headBlock, column);
            if (!currentS.HasValue)
                return false;
            vm1Column = column;
            knownXor = payload[payloadOffset] ^ currentS.Value;
            xorFF = false;
            return true;
        }
        if (payloadOffset >= Constants.VIDEO_CRACK_START)
        {
            (int blockIndex, int column) = Math.DivRem(payloadOffset - Constants.VIDEO_CRACK_START, 32);
            if (blockIndex % 2 != 0)
                return false;
            int? currentS = EncryptedPrefixXor(payload, blockIndex, column);
            if (!currentS.HasValue)
                return false;
            vm1Column = column;
            knownXor = currentS.Value;
            xorFF = true;
            return true;
        }
        return false;
    }
    static int? EncryptedPrefixXor(scoped ReadOnlySpan<byte> payload, int blockIndex, int column)
    {
        int value = 0;
        for (int block = 0; block <= blockIndex; block++)
        {
            int pos = Constants.VIDEO_CRACK_START + block * 32 + column;
            if (pos >= payload.Length)
                return null;
            value ^= payload[pos];
        }
        return value;
    }
    static bool IsDirectVm1Offset(int payloadOffset)
    {
        if (payloadOffset is >= Constants.VIDEO_MASK_START and < Constants.VIDEO_CRACK_START)
            return (payloadOffset - Constants.VIDEO_MASK_START) / 32 % 2 != 0;
        if (payloadOffset < Constants.VIDEO_CRACK_START)
            return false;
        return (payloadOffset - Constants.VIDEO_CRACK_START) / 32 % 2 == 0;
    }
    static (int Start, int End) GetVp9FrameRange(scoped ReadOnlySpan<byte> payload)
    {
        const uint DKIF = 'D' | ('K' << 8) | ('I' << 16) | ('F' << 24);
        const uint VP90 = 'V' | ('P' << 8) | ('9' << 16) | ('0' << 24);
        const uint vp90 = 'v' | ('p' << 8) | ('9' << 16) | ('0' << 24);
        if (payload.Length >= 44 && BinaryPrimitives.ReadUInt32LittleEndian(payload) is DKIF)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]) is not (VP90 or vp90))
                goto None;
            int frameSize = BinaryPrimitives.ReadInt32LittleEndian(payload[32..]);
            int frameStart = 44;
            int frameEnd = frameStart + frameSize;
            if (frameSize > 0 && frameEnd <= payload.Length)
                return (frameStart, frameEnd);
            goto None;
        }
        if (payload.Length >= 12)
        {
            int frameSize = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
            int frameStart = 12;
            int frameEnd = frameStart + frameSize;
            if (frameSize > 0 && frameEnd <= payload.Length)
                return (frameStart, frameEnd);
        }
    None:
        return (-1, -1);
    }
    static bool IsVp9SuperframeMarker(int marker)
    {
        return (marker & 0xE0) == 0xC0;
    }
    static void SuperframeMeta(int marker, out int bytesPerSize, out int frameCount, out int indexSize)
    {
        bytesPerSize = ((marker >> 3) & 3) + 1;
        frameCount = (marker & 7) + 1;
        indexSize = 2 + bytesPerSize * frameCount;
    }

    public static bool PayloadStartsVp9Stream(scoped ReadOnlySpan<byte> bytes)
    {
        const uint DKIF = 'D' | ('K' << 8) | ('I' << 16) | ('F' << 24);
        const uint VP90 = 'V' | ('P' << 8) | ('9' << 16) | ('0' << 24);
        const uint vp90 = 'v' | ('p' << 8) | ('9' << 16) | ('0' << 24);
        return bytes.Length >= 12
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes) is DKIF
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) is VP90 or vp90;
    }
    public static Vp9ConstraintStats ExtractVp9SuperframeConstraints(scoped ReadOnlySpan<byte> payload, bool enableC9Template = true)
    {
        Vp9ConstraintStats stats = new();
        (int frameStart, int frameEnd) = GetVp9FrameRange(payload);
        if (frameStart >= 0 && frameEnd >= 0)
        {
            stats.AttemptedFrames++;
            MarkerCandidate? candidate = DetectSuperframeIndex(payload, frameStart, frameEnd, enableC9Template);
            if (candidate is null)
                goto Return;
            stats.MatchedFrames++;
            stats.ExtractorCounts.IncrementCount(candidate.Source);
            RecordPlaintextConstraints(payload, VerifiedSuperframeConstraints(payload, in candidate.Index), candidate.Trust, stats);
            if (enableC9Template)
            {
                // Empirical C9 template; keep isolated from structural evidence.
                RecordPlaintextConstraints(payload, C9SecondFrame4ByteHeaderConstraints(payload, frameStart, in candidate.Index), ConstraintTrust.C9Template, stats);
            }
        }
    Return:
        return stats;
    }
    public static Vm1Constraints MergeVm1Constraints(Vm1Constraints @base, Vm1Constraints extra, Vp9ConstraintStats? stats = null)
    {
        if (stats is null)
            return MergeFinalConstraints(@base, extra);
        foreach (KeyValuePair<int, Vm1ConstraintEntry> kvp in extra)
            stats.AddEvidence(new(kvp.Key, kvp.Value.Values, kvp.Value.Trust, kvp.Value.Reason));
        return BuildVm1Constraints(stats);
    }
    public static Vm1Constraints BuildVm1Constraints(Vp9ConstraintStats stats)
    {
        Vm1Constraints constraints = [];
        foreach (Vm1Evidence evidence in stats.Evidences)
        {
            if (evidence.Trust <= stats.DisabledTrustThreshold)
                continue;
            Vm1ConstraintEntry entry = new(evidence.Values, evidence.Trust, evidence.Reason);
            ref Vm1ConstraintEntry? current = ref CollectionsMarshal.GetValueRefOrAddDefault(constraints, evidence.Column, out _);
            if (current is null)
            {
                current = entry;
                continue;
            }
            entry.Values.IntersectWith(current.Values);
            if (entry.Values.Count == 0)
            {
                constraints.Clear();
                break;
            }
            current = MergeCompatibleEntries(current, entry, entry.Values);
        }
        return constraints;
    }
    public static PlainVm1Constraints PlainVm1Constraints(Vm1Constraints constraints)
    {
        PlainVm1Constraints plain = new(constraints.Count);
        foreach (KeyValuePair<int, Vm1ConstraintEntry> kvp in constraints)
            plain[kvp.Key] = kvp.Value.Values;
        return plain;
    }
    public static SortedDictionary<string, string[]> FormatVm1Constraints(Vm1Constraints constraints)
    {
        SortedDictionary<string, string[]> formatted = [];
        foreach (KeyValuePair<int, Vm1ConstraintEntry> kvp in constraints)
            formatted[$"{kvp.Key}"] = kvp.Value.Values.Select(it => $"{it:X2}").ToSortedArray();
        return formatted;
    }

    sealed record class MarkerCandidate(
        SuperframeIndex Index,
        string Source,
        ConstraintTrust Trust)
    {
        public readonly SuperframeIndex Index = Index;
    }
}