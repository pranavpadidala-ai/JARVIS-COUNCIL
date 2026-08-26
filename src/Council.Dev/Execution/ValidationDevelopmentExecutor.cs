using Council.Dev.Tasks;
using Council.Dev.Validation;

namespace Council.Dev.Execution;

public sealed class ValidationDevelopmentExecutor : IDevelopmentExecutor
{
    private readonly BuildTestExecutor _buildTestExecutor;

    public ValidationDevelopmentExecutor(
        BuildTestExecutor buildTestExecutor)
    {
        ArgumentNullException.ThrowIfNull(buildTestExecutor);

        _buildTestExecutor = buildTestExecutor;
    }

    public async Task<DevelopmentExecutionResult> ExecuteAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        cancellationToken.ThrowIfCancellationRequested();

        var validationResult =
            await _buildTestExecutor.RunAsync(cancellationToken);

        if (!validationResult.Success)
        {
            return DevelopmentExecutionResult.Failed(
                validationResult.Message);
        }

        return DevelopmentExecutionResult.Completed(
            validationResult.Message);
    }
}
