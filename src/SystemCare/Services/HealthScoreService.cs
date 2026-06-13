using SystemCare.Models;

namespace SystemCare.Services;

public interface IHealthScoreService
{
    HealthReport Compute(HealthInputs inputs);
}

public class HealthScoreService : IHealthScoreService
{
    private const double JunkFullPenaltyBytes = 2L * 1024 * 1024 * 1024; // 2 GiB of junk ⇒ full 40-point penalty

    public HealthReport Compute(HealthInputs inputs)
    {
        double junkPenalty = Math.Min(40, 40.0 * inputs.JunkBytes / JunkFullPenaltyBytes);
        double startupPenalty = Math.Min(25, 3.0 * Math.Max(0, inputs.EnabledStartupItems - 4));
        double ramPenalty = inputs.RamLoadPercent <= 50 ? 0 : Math.Min(35, (inputs.RamLoadPercent - 50) * 0.7);

        int score = (int)Math.Clamp(100 - junkPenalty - startupPenalty - ramPenalty, 0, 100);

        return new HealthReport
        {
            Score = score,
            Band = score switch
            {
                >= 90 => HealthBand.Excellent,
                >= 70 => HealthBand.Good,
                >= 40 => HealthBand.NeedsAttention,
                _ => HealthBand.Poor,
            },
            JunkPenalty = junkPenalty,
            StartupPenalty = startupPenalty,
            RamPenalty = ramPenalty,
        };
    }
}
