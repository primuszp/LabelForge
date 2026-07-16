using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LabelForge.App.Services;

public enum AiJobStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed class AiJob : INotifyPropertyChanged
{
    private AiJobStatus status = AiJobStatus.Queued;
    private double progress;
    private string? message;

    internal AiJob(string name, Func<IProgress<double>, CancellationToken, Task> work)
    { Name = name; Work = work; }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public AiJobStatus Status { get => status; internal set { status = value; Changed(); } }
    public double Progress { get => progress; internal set { progress = value; Changed(); } }
    public string? Message { get => message; internal set { message = value; Changed(); } }
    internal Func<IProgress<double>, CancellationToken, Task> Work { get; }
    internal CancellationTokenSource Cancellation { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AiJobService : IAsyncDisposable
{
    private readonly Channel<AiJob> queue = Channel.CreateUnbounded<AiJob>();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;

    public AiJobService() => worker = Task.Run(ProcessAsync);
    public ObservableCollection<AiJob> Jobs { get; } = [];

    public AiJob Enqueue(string name, Func<IProgress<double>, CancellationToken, Task> work)
    {
        var job = new AiJob(name, work);
        Jobs.Insert(0, job);
        queue.Writer.TryWrite(job);
        return job;
    }

    public void Cancel(AiJob job) => job.Cancellation.Cancel();

    private async Task ProcessAsync()
    {
        await foreach (var job in queue.Reader.ReadAllAsync(shutdown.Token))
        {
            job.Status = AiJobStatus.Running;
            var progress = new Progress<double>(value => job.Progress = Math.Clamp(value, 0, 1));
            try
            {
                await job.Work(progress, job.Cancellation.Token);
                job.Progress = 1;
                job.Status = AiJobStatus.Completed;
            }
            catch (OperationCanceledException) { job.Status = AiJobStatus.Cancelled; }
            catch (Exception exception) { job.Message = exception.Message; job.Status = AiJobStatus.Failed; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        queue.Writer.TryComplete();
        try { await worker; } catch (OperationCanceledException) { }
        shutdown.Dispose();
    }
}
