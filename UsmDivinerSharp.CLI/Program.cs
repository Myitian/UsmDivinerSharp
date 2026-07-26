using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsmDivinerSharp;

Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;
Stopwatch sw = new();
if (args.Length == 0)
{
    Console.Error.WriteLine("""
        Usage:
            UsmDivinerSharp.CLI [<usm-file> [output-format]]

        Available output formats:
            [j]son (default, with detailed report)
            [b]inary (raw key bytes in big-endian, 8 bytes if found, empty if not found)
            [h]exadecimal (key in upper-case hexadecimal string, 16 characters if found, empty if not found)
            [d]ecimal  (key in hexadecimal string, empty if not found)

        USM file path:
        """);
    string file = Console.ReadLine().AsSpan().Trim().Trim('"').ToString();
    sw.Restart();
    byte[] usmFile = File.ReadAllBytes(file);
    sw.Stop();
    Console.Error.WriteLine($"Read file time: {sw.Elapsed}");
    sw.Restart();
    (long? key, CrackReport report) = Crack.CrackFromBuffer(usmFile);
    sw.Stop();
    Console.Error.WriteLine($"Crack key time: {sw.Elapsed}");
    Console.WriteLine(key.HasValue ? $"Key(Hex): {key.Value:X16}  Key(Dec): {key.Value}" : "failed");
    Console.WriteLine(JsonSerializer.Serialize(report, AppJsonSerializerContext.Default.CrackReport));
}
else
{
    sw.Restart();
    byte[] usmFile = File.ReadAllBytes(args[0]);
    sw.Stop();
    TimeSpan readFileTime = sw.Elapsed;
    sw.Restart();
    (long? key, CrackReport report) = Crack.CrackFromBuffer(usmFile);
    sw.Stop();
    TimeSpan crackKeyTime = sw.Elapsed;
    using Stream stdout = Console.OpenStandardOutput();
    char outputFormat = args is [_, [char c, ..], ..] ? char.ToLowerInvariant(c) : 'j';
    Span<byte> buffer = stackalloc byte[20];
    switch (outputFormat)
    {
        case 'b':
            if (key.HasValue)
            {
                BinaryPrimitives.WriteInt64BigEndian(buffer, key.Value);
                stdout.Write(buffer[..8]);
            }
            break;
        case 'h':
            if (key.HasValue)
            {
                _ = key.Value.TryFormat(buffer, out int bytesWritten, "X16", null);
                stdout.Write(buffer[..bytesWritten]);
            }
            break;
        case 'd':
            if (key.HasValue)
            {
                _ = key.Value.TryFormat(buffer, out int bytesWritten, default, null);
                stdout.Write(buffer[..bytesWritten]);
            }
            break;
        case 'j':
            byte[]? keyBytes = null;
            string? keyHex = null;
            string? keyDec = null;
            if (key.HasValue)
            {
                keyBytes = BitConverter.GetBytes(key.Value);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(keyBytes);
                keyHex = Convert.ToHexString(keyBytes);
                keyDec = $"{key.Value}";
            }
            JsonSerializer.Serialize(stdout, new FullReport
            {
                Key = key,
                KeyDec = keyDec,
                KeyHex = keyHex,
                KeyBase64 = keyBytes,
                ReadFileTime = readFileTime,
                CrackKeyTime = crackKeyTime,
                CrackReport = report
            }, AppJsonSerializerContext.Default.FullReport);
            break;
        default:
            Console.Error.WriteLine($"Unknown output format: {outputFormat}");
            return 1;
    }
}
return 0;

sealed class FullReport
{
    public long? Key { get; init; }
    public string? KeyDec { get; init; }
    public string? KeyHex { get; init; }
    public byte[]? KeyBase64 { get; init; }
    public TimeSpan ReadFileTime { get; init; }
    public TimeSpan CrackKeyTime { get; init; }
    public CrackReport? CrackReport { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FullReport))]
sealed partial class AppJsonSerializerContext : JsonSerializerContext;