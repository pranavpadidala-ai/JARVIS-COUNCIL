using Council.Dev.Tasks;

namespace Council.Dev.Planning;

public interface IDevelopmentPlanner
{
    Task<DevelopmentPlan> CreatePlanAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default);

    Task<DevelopmentPlan> CreateRecoveryPlanAsync(
        DevelopmentTask task,
        string failure,
        CancellationToken cancellationToken = default);
}
