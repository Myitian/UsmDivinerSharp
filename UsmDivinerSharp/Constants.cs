namespace UsmDivinerSharp;

static class Constants
{
    public const uint SIG_SFV = ('@' << 24) | ('S' << 16) | ('F' << 8) | 'V';
    public const int VIDEO_MASK_START = 0x40;
    public const int VIDEO_CRACK_START = 0x140;
    public const int BIGRAM_WEIGHT_TOTAL = 25;
    public const double BIGRAM_RATIO_MIN = 1;
    public const double BIGRAM_RATIO_MAX = 5;
    public const int BIGRAM_ADAPT_MIN_HITS = 100;
    public const int BIGRAM_LOW_CONF_ZERO_WEIGHT = 10;
    public const int BIGRAM_LOW_CONF_FF_WEIGHT = 4;
    public const int SOLVER_BEAM = 50;
    public const int SOLVER_L1_BEAM = 300;
}