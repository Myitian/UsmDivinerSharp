using System.Buffers.Binary;
using System.Numerics.Tensors;
using BigramWeights = (int Bigram00Weight, int BigramFFWeight);
using Candidate = (int Score, UsmDivinerSharp.Array32<byte> V);
using PlainVm1Constraints = System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>;
using Vm1Constraints = System.Collections.Generic.Dictionary<int, UsmDivinerSharp.Vm1ConstraintEntry>;

namespace UsmDivinerSharp;

public static class Crack
{
    static int AccmulateScoreMatrices(scoped ReadOnlySpan<byte> data, int start, int size, ScoreMatrix matrix, out int oddBigram00, out int oddBigramFF)
    {
        int numBlocks = size / 32;
        Array32<byte> currentS = new();
        int rows = 0;
        oddBigram00 = 0;
        oddBigramFF = 0;
        for (int i = 0; i < numBlocks; i++)
        {
            int blockStart = start + i * 32;
            TensorPrimitives.Xor(currentS, data.Slice(blockStart, 32), currentS);
            if (i % 2 == 0)
            {
                rows++;
                for (int j = 0; j < 32; j++)
                {
                    byte value = currentS[j];
                    matrix.Unigram(j, value)++;
                    matrix.Unigram(j, value ^ 0xFF)++;
                }
                for (int j = 0; j < 31; j++)
                {
                    byte left = currentS[j];
                    byte right = currentS[j + 1];
                    matrix.Bigram(j, (left << 8) | right)++;
                }
            }
            else
            {
                for (int j = 0; j < 31; j++)
                {
                    byte left = currentS[j];
                    byte right = currentS[j + 1];
                    if (left == 0 && right == 0)
                        oddBigram00++;
                    else if (left == 0xFF && right == 0xFF)
                        oddBigramFF++;
                }
            }
        }
        return rows;
    }
    static double EstimateBigramWeights(int odd00, int oddFF, out BigramWeights bigramWeights)
    {
        int totalHits = odd00 + oddFF;
        int weight00;
        int weightFF;
        double rawRatio = oddFF != 0 ? (double)odd00 / oddFF : Constants.BIGRAM_RATIO_MAX;
        if (totalHits < Constants.BIGRAM_ADAPT_MIN_HITS)
        {
            weight00 = Constants.BIGRAM_LOW_CONF_ZERO_WEIGHT;
            weightFF = Constants.BIGRAM_LOW_CONF_FF_WEIGHT;
        }
        else
        {
            double adjustedRatio = Math.Clamp(rawRatio, Constants.BIGRAM_RATIO_MIN, Constants.BIGRAM_RATIO_MAX);
            weight00 = (int)Math.Round(Constants.BIGRAM_WEIGHT_TOTAL * adjustedRatio / (1 + adjustedRatio));
            weightFF = Constants.BIGRAM_WEIGHT_TOTAL - weight00;
        }
        bigramWeights = (weight00, weightFF);
        return rawRatio;
    }
    static Candidate SolveVm1Bigram(ScoreMatrix matrix, int beamSize, int l1BeamSize, ref readonly BigramWeights bigramWeights, PlainVm1Constraints knownVm1)
    {
        ReadOnlySpan<Candidate> level1 = ExtendLevel1(matrix, l1BeamSize, in bigramWeights, knownVm1);
        ReadOnlySpan<Candidate> level2 = ExtendLevel<L0>(level1, matrix, beamSize, in bigramWeights, knownVm1);
        ReadOnlySpan<Candidate> level3 = ExtendLevel<L3>(level2, matrix, beamSize, in bigramWeights, knownVm1);
        ReadOnlySpan<Candidate> level4 = ExtendLevel<L4>(level3, matrix, beamSize, in bigramWeights, knownVm1);
        ReadOnlySpan<Candidate> level5 = ExtendLevel<L6>(level4, matrix, beamSize, in bigramWeights, knownVm1);
        ReadOnlySpan<Candidate> level6 = ExtendLevel<L5>(level5, matrix, beamSize, in bigramWeights, knownVm1);
        return level6.IsEmpty ? (-1, default) : level6[0];
    }
    static Candidate[] ExtendLevel1(ScoreMatrix matrix, int beam, ref readonly BigramWeights bigramWeights, PlainVm1Constraints knownVm1)
    {
        using LightweightArrayPoolHandler<Candidate> @out = new(65536);
        int count = 0;
        for (int vx = 0; vx < 65536; vx++)
        {
            int v1 = vx >> 8;
            int v2 = vx & 0xFF;
            Array32<byte> v = default;
            v[1] = (byte)v1;
            v[2] = (byte)v2;
            v[8] = (byte)(v2 + v1);
            v[10] = (byte)~v2;
            v[11] = (byte)~v1;
            v[15] = (byte)(v[10] - v[11]);
            v[16] = (byte)(v[8] - v[15]);
            v[18] = (byte)~v[15];
            if (!MatchesKnown(ref v, knownVm1, [1, 2, 8, 10, 11, 15, 16, 18]))
                continue;
            int score = matrix.Unigram(1, v[1])
                + matrix.Unigram(2, v[2])
                + matrix.Unigram(8, v[8])
                + matrix.Unigram(10, v[10])
                + matrix.Unigram(11, v[11])
                + matrix.Unigram(15, v[15])
                + matrix.Unigram(16, v[16])
                + matrix.Unigram(18, v[18])
                + Bg(matrix, 1, v[1], v[2], in bigramWeights)
                + Bg(matrix, 10, v[10], v[11], in bigramWeights)
                + Bg(matrix, 15, v[15], v[16], in bigramWeights);
            @out.Array[count++] = (score, v);
        }
        return Top(@out.Array.AsSpan(0, count), beam);
    }
    static Candidate[] ExtendLevel<T>(scoped ReadOnlySpan<Candidate> candidates, ScoreMatrix matrix, int beam, ref readonly BigramWeights bigramWeights, PlainVm1Constraints knownVm1)
        where T : IExtendLevel
    {
        using LightweightArrayPoolHandler<Candidate> @out = new(256 * candidates.Length);
        int count = 0;
        foreach (Candidate candidate in candidates)
        {
            for (int vx = 0; vx < 256; vx++)
            {
                Array32<byte> v = candidate.V;
                T.ProcessV(ref v, vx);
                if (!MatchesKnown(ref v, knownVm1, T.Indicies))
                    continue;
                int score = T.CalculateScore(in v, candidate.Score, matrix, in bigramWeights);
                @out.Array[count++] = (score, v);
            }
        }
        return Top(@out.Array.AsSpan(0, count), beam);
    }
    static bool MatchesKnown(ref readonly Array32<byte> vm1, PlainVm1Constraints knownVm1, scoped ReadOnlySpan<int> indicies)
    {
        foreach (int index in indicies)
        {
            if (knownVm1.TryGetValue(index, out HashSet<int>? allowed) && !allowed.Contains(vm1[index]))
                return false;
        }
        return true;
    }
    static int Bg(ScoreMatrix matrix, int index, byte left, byte right, ref readonly BigramWeights bigramWeights)
    {
        int pairFF = (left << 8) | right;
        int pair00 = ((left ^ 0xFF) << 8) | (right ^ 0xFF);
        return bigramWeights.BigramFFWeight * matrix.Bigram(index, pairFF) + bigramWeights.Bigram00Weight * matrix.Bigram(index, pair00);
    }
    static Candidate[] Top(scoped ReadOnlySpan<Candidate> candidates, int beam)
    {
        return candidates.TopN(beam, ReversedCandidateComparer.Instance);
    }
    sealed class ReversedCandidateComparer : IComparer<Candidate>
    {
        public static readonly ReversedCandidateComparer Instance = new();
        public int Compare(Candidate x, Candidate y) => y.Score.CompareTo(x.Score);
    }
    public static (long? Key, CrackReport Report) CrackFromBuffer(scoped ReadOnlySpan<byte> data, int maxVideoBytes = 0, int beamSize = Constants.SOLVER_BEAM, int l1BeamSize = Constants.SOLVER_L1_BEAM)
    {
        using ScoreMatrix matrix = new();
        int offset = 0;
        int videoBlocksFound = 0;
        int chunksSeen = 0;
        int videoCrackBytesUsed = 0;
        int sampleRows = 0;
        int oddBigram00 = 0;
        int oddBigramFF = 0;
        Vm1Constraints vp9Constraints = [];
        Vp9ConstraintStats vp9ConstraintStats = new();
        bool vp9StreamDetected = false;
        bool enableC9Template = data.Length < 2_000_000;

        while (offset + 32 <= data.Length)
        {
            uint signature = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            uint dataSize = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
            byte dataOffset = data[offset + 9];
            ushort paddingSize = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 10)..]);
            byte dataType = data[offset + 15];

            long nextOffset = 8L + offset + dataSize;
            if (dataSize < 0x18 || nextOffset > data.Length)
                throw new InvalidDataException($"bad USM chunk at 0x{offset:X}: data_size={dataSize}");

            int payloadStart = 8 + offset + dataOffset;
            int payloadSize = (int)dataSize - dataOffset - paddingSize;
            int payloadEnd = payloadStart + payloadSize;
            if (payloadSize < 0 || payloadEnd > data.Length)
                throw new InvalidDataException($"bad USM chunk at 0x{offset:X}: payload_size={payloadSize}");

            chunksSeen++;
            if (signature == Constants.SIG_SFV && dataType == 0)
            {
                if (!vp9StreamDetected)
                {
                    int headerProbeSize = Math.Min(payloadSize, 44);
                    if (Vp9SuperframeConstraints.PayloadStartsVp9Stream(data.Slice(payloadStart, headerProbeSize)))
                        vp9StreamDetected = true;
                }
                if (payloadSize - Constants.VIDEO_MASK_START >= 0x200)
                {
                    int encryptedStart = payloadStart + Constants.VIDEO_CRACK_START;
                    int encryptedSize = payloadSize - Constants.VIDEO_CRACK_START;
                    int constraintEncryptedSize = encryptedSize;
                    if (maxVideoBytes > 0)
                    {
                        int remaining = maxVideoBytes - videoCrackBytesUsed;
                        if (remaining < 32)
                            break;
                        constraintEncryptedSize = Math.Min(encryptedSize, remaining);
                        encryptedSize = constraintEncryptedSize;
                    }
                    encryptedSize &= ~31;
                    if (encryptedSize >= 32)
                    {
                        int constraintPayloadSize = Math.Min(payloadSize, Constants.VIDEO_CRACK_START + constraintEncryptedSize);
                        if (vp9StreamDetected)
                        {
                            ReadOnlySpan<byte> payload = data.Slice(payloadStart, constraintPayloadSize);
                            Vp9ConstraintStats newStats = Vp9SuperframeConstraints.ExtractVp9SuperframeConstraints(payload, enableC9Template);
                            vp9ConstraintStats.Merge(newStats);
                            vp9Constraints = Vp9SuperframeConstraints.BuildVm1Constraints(vp9ConstraintStats);
                        }
                        videoBlocksFound++;
                        videoCrackBytesUsed += encryptedSize;
                        int rows = AccmulateScoreMatrices(data, encryptedStart, encryptedSize, matrix, out int odd00, out int oddFF);
                        sampleRows += rows;
                        oddBigram00 += odd00;
                        oddBigramFF += oddFF;
                        if (maxVideoBytes > 0 && videoCrackBytesUsed >= maxVideoBytes)
                            break;
                    }
                }
            }
            offset = (int)nextOffset;
        }
        double oddRatio = EstimateBigramWeights(oddBigram00, oddBigramFF, out BigramWeights bigramWeights);
        if (videoBlocksFound == 0 || sampleRows == 0)
        {
            return (null, new()
            {
                VideoBlocksFound = videoBlocksFound,
                ChunksSeen = chunksSeen,
                Reason = "no encrypted/long-enough @SFV video blocks found",
                Solver = "bigram",
                FastEnabled = maxVideoBytes > 0,
                VideoCrackBytesLimit = maxVideoBytes,
                VideoCrackBytesUsed = videoCrackBytesUsed,
                Bigram00Weight = bigramWeights.Bigram00Weight,
                BigramFFWeight = bigramWeights.BigramFFWeight,
                OddBigram00 = oddBigram00,
                OddBigramFF = oddBigramFF,
                OddBigramRatio = oddRatio,
                BeamSize = beamSize,
                L1BeamSize = l1BeamSize,
                Vp9 = vp9StreamDetected ? new(vp9Constraints, vp9ConstraintStats) : null
            });
        }
        Candidate best = SolveVm1Bigram(matrix, beamSize, l1BeamSize, in bigramWeights, Vp9SuperframeConstraints.PlainVm1Constraints(vp9Constraints));
        if (best.Score < 0)
        {
            return (null, new()
            {
                VideoBlocksFound = videoBlocksFound,
                Reason = "solver produced no candidate",
                Solver = "bigram",
                FastEnabled = maxVideoBytes > 0,
                VideoCrackBytesLimit = maxVideoBytes,
                VideoCrackBytesUsed = videoCrackBytesUsed,
                Bigram00Weight = bigramWeights.Bigram00Weight,
                BigramFFWeight = bigramWeights.BigramFFWeight,
                OddBigram00 = oddBigram00,
                OddBigramFF = oddBigramFF,
                OddBigramRatio = oddRatio,
                BeamSize = beamSize,
                L1BeamSize = l1BeamSize,
                Vp9 = vp9StreamDetected ? new(vp9Constraints, vp9ConstraintStats) : null
            });
        }
        best.V[3] += 0x34;
        best.V[4] -= 0xF9;
        best.V[5] ^= 0x13;
        best.V[6] -= 0x61;
        best.V[7] = 0;
        return (BinaryPrimitives.ReadInt64LittleEndian(best.V), new()
        {
            VideoBlocksFound = videoBlocksFound,
            ChunksSeen = chunksSeen,
            Solver = "bigram",
            SolverScore = best.Score,
            Samples = sampleRows,
            FastEnabled = maxVideoBytes > 0,
            VideoCrackBytesLimit = maxVideoBytes,
            VideoCrackBytesUsed = videoCrackBytesUsed,
            Bigram00Weight = bigramWeights.Bigram00Weight,
            BigramFFWeight = bigramWeights.BigramFFWeight,
            OddBigram00 = oddBigram00,
            OddBigramFF = oddBigramFF,
            OddBigramRatio = oddRatio,
            BeamSize = beamSize,
            L1BeamSize = l1BeamSize,
            Vp9 = vp9StreamDetected ? new(vp9Constraints, vp9ConstraintStats) : null
        });
    }
    interface IExtendLevel
    {
        public static abstract ReadOnlySpan<int> Indicies { get; }
        public static abstract void ProcessV(ref Array32<byte> v, int vx);
        public static abstract int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights);
    }
    readonly struct L0 : IExtendLevel
    {
        public static ReadOnlySpan<int> Indicies => [0, 7, 9, 12, 17];
        public static void ProcessV(ref Array32<byte> v, int vx)
        {
            v[0] = (byte)vx;
            v[7] = (byte)~vx;
            v[9] = (byte)(v[1] - v[7]);
            v[12] = (byte)(v[11] + v[9]);
            v[17] = (byte)(v[16] ^ v[7]);
        }
        public static int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights)
        {
            return candidateScore
                + matrix.Unigram(0, v[0])
                + matrix.Unigram(7, v[7])
                + matrix.Unigram(9, v[9])
                + matrix.Unigram(12, v[12])
                + matrix.Unigram(17, v[17])
                + Bg(matrix, 0, v[0], v[1], in bigramWeights)
                + Bg(matrix, 7, v[7], v[8], in bigramWeights)
                + Bg(matrix, 8, v[8], v[9], in bigramWeights)
                + Bg(matrix, 9, v[9], v[10], in bigramWeights)
                + Bg(matrix, 11, v[11], v[12], in bigramWeights)
                + Bg(matrix, 16, v[16], v[17], in bigramWeights)
                + Bg(matrix, 17, v[17], v[18], in bigramWeights);
        }
    }
    readonly struct L3 : IExtendLevel
    {
        public static ReadOnlySpan<int> Indicies => [3, 13, 14, 19, 23, 25, 28];
        public static void ProcessV(ref Array32<byte> v, int vx)
        {
            v[3] = (byte)vx;
            v[13] = (byte)(v[8] - vx);
            v[14] = (byte)~v[13];
            v[19] = (byte)(vx ^ 0x10);
            v[23] = (byte)(v[19] - v[15]);
            v[25] = (byte)(0x21 - v[19]);
            v[28] = (byte)(v[23] + 0x44);
        }
        public static int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights)
        {
            return candidateScore
                + matrix.Unigram(3, v[3])
                + matrix.Unigram(13, v[13])
                + matrix.Unigram(14, v[14])
                + matrix.Unigram(19, v[19])
                + matrix.Unigram(23, v[23])
                + matrix.Unigram(25, v[25])
                + matrix.Unigram(28, v[28])
                + Bg(matrix, 2, v[2], v[3], in bigramWeights)
                + Bg(matrix, 12, v[12], v[13], in bigramWeights)
                + Bg(matrix, 13, v[13], v[14], in bigramWeights)
                + Bg(matrix, 14, v[14], v[15], in bigramWeights)
                + Bg(matrix, 18, v[18], v[19], in bigramWeights);
        }
    }
    readonly struct L4 : IExtendLevel
    {
        public static ReadOnlySpan<int> Indicies => [4, 20, 26, 29, 31];
        public static void ProcessV(ref Array32<byte> v, int vx)
        {
            v[4] = (byte)vx;
            v[20] = (byte)(vx - 0x32);
            v[26] = (byte)(v[20] ^ v[23]);
            v[29] = (byte)(v[3] + vx);
            v[31] = (byte)(v[29] ^ v[19]);
        }
        public static int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights)
        {
            return candidateScore
                + matrix.Unigram(4, v[4])
                + matrix.Unigram(20, v[20])
                + matrix.Unigram(26, v[26])
                + matrix.Unigram(29, v[29])
                + matrix.Unigram(31, v[31])
                + Bg(matrix, 3, v[3], v[4], in bigramWeights)
                + Bg(matrix, 19, v[19], v[20], in bigramWeights)
                + Bg(matrix, 25, v[25], v[26], in bigramWeights)
                + Bg(matrix, 28, v[28], v[29], in bigramWeights);
        }
    }
    readonly struct L6 : IExtendLevel
    {
        public static ReadOnlySpan<int> Indicies => [6, 22, 27];
        public static void ProcessV(ref Array32<byte> v, int vx)
        {
            v[6] = (byte)vx;
            v[22] = (byte)(vx ^ 0xF3);
            v[27] = (byte)(v[22] * 2);
        }
        public static int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights)
        {
            return candidateScore
                + matrix.Unigram(6, v[6])
                + matrix.Unigram(22, v[22])
                + matrix.Unigram(27, v[27])
                + Bg(matrix, 6, v[6], v[7], in bigramWeights)
                + Bg(matrix, 26, v[26], v[27], in bigramWeights)
                + Bg(matrix, 27, v[27], v[28], in bigramWeights);
        }
    }
    readonly struct L5 : IExtendLevel
    {
        public static ReadOnlySpan<int> Indicies => [5, 21, 24, 30];
        public static void ProcessV(ref Array32<byte> v, int vx)
        {
            v[5] = (byte)vx;
            v[21] = (byte)(vx + 0xED);
            v[24] = (byte)(v[21] + v[7]);
            v[30] = (byte)(vx - v[22]);
        }
        public static int CalculateScore(ref readonly Array32<byte> v, int candidateScore, ScoreMatrix matrix, ref readonly BigramWeights bigramWeights)
        {
            return candidateScore
                + matrix.Unigram(5, v[5])
                + matrix.Unigram(21, v[21])
                + matrix.Unigram(24, v[24])
                + matrix.Unigram(30, v[30])
                + Bg(matrix, 4, v[4], v[5], in bigramWeights)
                + Bg(matrix, 5, v[5], v[6], in bigramWeights)
                + Bg(matrix, 20, v[20], v[21], in bigramWeights)
                + Bg(matrix, 21, v[21], v[22], in bigramWeights)
                + Bg(matrix, 22, v[22], v[23], in bigramWeights)
                + Bg(matrix, 23, v[23], v[24], in bigramWeights)
                + Bg(matrix, 24, v[24], v[25], in bigramWeights)
                + Bg(matrix, 29, v[29], v[30], in bigramWeights)
                + Bg(matrix, 30, v[30], v[31], in bigramWeights);
        }
    }
}