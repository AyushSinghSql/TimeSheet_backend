// QueuedHostedService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace TimeSheet.BackgroundQueue
{
    public class QueuedHostedService : BackgroundService
    {
        private readonly ILogger<QueuedHostedService> _logger;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceProvider _serviceProvider;
        private const int MaxRetry = 3;

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILogger<QueuedHostedService> logger)
        {
            _taskQueue = taskQueue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service running.");

            while (!stoppingToken.IsCancellationRequested)
            {
                Func<IServiceProvider, CancellationToken, Task> workItem = null;
                try
                {
                    workItem = await _taskQueue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error dequeueing work item.");
                    continue;
                }

                // Create a scope for each work item so we can resolve scoped services
                using (var scope = _serviceProvider.CreateScope())
                {
                    var scopedProvider = scope.ServiceProvider;

                    int attempt = 0;
                    while (attempt < MaxRetry && !stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            attempt++;
                            await workItem(scopedProvider, stoppingToken);
                            break; // success
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Work item canceled by shutdown.");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing queued work item (attempt {Attempt}).", attempt);
                            // Simple exponential backoff
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken)
                                      .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
                        }
                    }
                }
            }

            _logger.LogInformation("Queued Hosted Service stopping.");
        }
    }

}
