namespace UsmDivinerSharp;

readonly record struct SuperframeIndex(
    int Marker,
    int BytesPerSize,
    int FrameCount,
    int IndexStart,
    int IndexSize,
    int FrameSize);