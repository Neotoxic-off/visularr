using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks;

/// <summary>
/// Class TaskManager.
/// </summary>
public class TaskManager : ITaskManager
{
    /// <summary>
    /// The _task queue.
    /// </summary>
    private readonly ConcurrentQueue<Tuple<Type, TaskOptions>> _taskQueue =
        new ConcurrentQueue<Tuple<Type, TaskOptions>>();

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<TaskManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskManager" /> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public TaskManager(
        IApplicationPaths applicationPaths,
        ILogger<TaskManager> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;

        ScheduledTasks = [];
    }

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<IScheduledTaskWorker>>? TaskExecuting;

    /// <inheritdoc />
    public event EventHandler<TaskCompletionEventArgs>? TaskCompleted;

    /// <inheritdoc />
    public IReadOnlyList<IScheduledTaskWorker> ScheduledTasks { get; private set; }

    /// <inheritdoc />
    public void CancelIfRunningAndQueue<T>(TaskOptions options)
        where T : IScheduledTask
    {
        // visularr's headless setup changes the configuration before the tasks are registered: nothing to cancel or queue yet.
        IScheduledTaskWorker? task = ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.GetType() == typeof(T));
        if (task is null)
        {
            return;
        }

        ((ScheduledTaskWorker)task).CancelIfRunning();

        QueueScheduledTask<T>(options);
    }

    /// <inheritdoc />
    public void CancelIfRunningAndQueue<T>()
            where T : IScheduledTask
    {
        CancelIfRunningAndQueue<T>(new TaskOptions());
    }

    /// <inheritdoc />
    public void CancelIfRunning<T>()
                where T : IScheduledTask
    {
        IScheduledTaskWorker task = ScheduledTasks.First(t => t.ScheduledTask.GetType() == typeof(T));
        ((ScheduledTaskWorker)task).CancelIfRunning();
    }

    /// <inheritdoc />
    public void QueueScheduledTask<T>(TaskOptions options)
        where T : IScheduledTask
    {
        IScheduledTaskWorker? scheduledTask = ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.GetType() == typeof(T));

        if (scheduledTask is null)
        {
            _logger.LogError("Unable to find scheduled task of type {Type} in QueueScheduledTask.", typeof(T).Name);
        }
        else
        {
            QueueScheduledTask(scheduledTask, options);
        }
    }

    /// <inheritdoc />
    public void QueueScheduledTask<T>()
        where T : IScheduledTask
    {
        QueueScheduledTask<T>(new TaskOptions());
    }

    /// <inheritdoc />
    public void QueueIfNotRunning<T>()
        where T : IScheduledTask
    {
        IScheduledTaskWorker task = ScheduledTasks.First(t => t.ScheduledTask.GetType() == typeof(T));

        if (task.State != TaskState.Running)
        {
            QueueScheduledTask<T>(new TaskOptions());
        }
    }

    /// <inheritdoc />
    public void Execute<T>()
        where T : IScheduledTask
    {
        IScheduledTaskWorker? scheduledTask = ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.GetType() == typeof(T));

        if (scheduledTask is null)
        {
            _logger.LogError("Unable to find scheduled task of type {Type} in Execute.", typeof(T).Name);
        }
        else
        {
            Type type = scheduledTask.ScheduledTask.GetType();

            _logger.LogDebug("Queuing task {Name}", type.Name);

            lock (_taskQueue)
            {
                if (scheduledTask.State == TaskState.Idle)
                {
                    Execute(scheduledTask, new TaskOptions());
                }
            }
        }
    }

    /// <inheritdoc />
    public void QueueScheduledTask(IScheduledTask task, TaskOptions options)
    {
        IScheduledTaskWorker? scheduledTask = ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.GetType() == task.GetType());

        if (scheduledTask is null)
        {
            _logger.LogError("Unable to find scheduled task of type {Type} in QueueScheduledTask.", task.GetType().Name);
        }
        else
        {
            QueueScheduledTask(scheduledTask, options);
        }
    }

    /// <summary>
    /// Queues the scheduled task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="options">The task options.</param>
    private void QueueScheduledTask(IScheduledTaskWorker task, TaskOptions options)
    {
        Type type = task.ScheduledTask.GetType();

        _logger.LogDebug("Queuing task {Name}", type.Name);

        lock (_taskQueue)
        {
            if (task.State == TaskState.Idle)
            {
                Execute(task, options);
                return;
            }

            _taskQueue.Enqueue(new Tuple<Type, TaskOptions>(type, options));
        }
    }

    /// <inheritdoc />
    public void AddTasks(IEnumerable<IScheduledTask> tasks)
    {
        IEnumerable<ScheduledTaskWorker> list = tasks.Select(t => new ScheduledTaskWorker(t, _applicationPaths, this, _logger));

        ScheduledTasks = ScheduledTasks.Concat(list).ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool dispose)
    {
        foreach (IScheduledTaskWorker task in ScheduledTasks)
        {
            task.Dispose();
        }
    }

    /// <inheritdoc />
    public void Cancel(IScheduledTaskWorker task)
    {
        ((ScheduledTaskWorker)task).Cancel();
    }

    /// <inheritdoc />
    public Task Execute(IScheduledTaskWorker task, TaskOptions options)
    {
        return ((ScheduledTaskWorker)task).Execute(options);
    }

    /// <summary>
    /// Called when [task executing].
    /// </summary>
    /// <param name="task">The task.</param>
    internal void OnTaskExecuting(IScheduledTaskWorker task)
    {
        TaskExecuting?.Invoke(this, new GenericEventArgs<IScheduledTaskWorker>(task));
    }

    /// <summary>
    /// Called when [task completed].
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="result">The result.</param>
    internal void OnTaskCompleted(IScheduledTaskWorker task, TaskResult result)
    {
        TaskCompleted?.Invoke(task, new TaskCompletionEventArgs(task, result));

        ExecuteQueuedTasks();
    }

    /// <summary>
    /// Executes the queued tasks.
    /// </summary>
    private void ExecuteQueuedTasks()
    {
        lock (_taskQueue)
        {
            List<Tuple<Type, TaskOptions>> list = new List<Tuple<Type, TaskOptions>>();

            while (_taskQueue.TryDequeue(out Tuple<Type, TaskOptions>? item))
            {
                if (list.All(i => i.Item1 != item.Item1))
                {
                    list.Add(item);
                }
            }

            foreach (Tuple<Type, TaskOptions> enqueuedType in list)
            {
                IScheduledTaskWorker scheduledTask = ScheduledTasks.First(t => t.ScheduledTask.GetType() == enqueuedType.Item1);

                if (scheduledTask.State == TaskState.Idle)
                {
                    Execute(scheduledTask, enqueuedType.Item2);
                }
            }
        }
    }
}
