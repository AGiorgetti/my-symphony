using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Workflows;
using Symphony.Application.Configuration;
using Symphony.Domain.Workflows;

namespace Symphony.Infrastructure.Configuration;

public sealed class WorkflowOptionsProvider : IWorkflowOptionsProvider, IWorkflowDefinitionProvider, IDisposable
{
    private readonly IWorkflowLoader _workflowLoader;
    private readonly IWorkflowOptionsResolver _workflowOptionsResolver;
    private readonly WorkflowLoadStatusTracker _workflowLoadStatusTracker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowOptionsProvider> _logger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private WorkflowSnapshot? _currentSnapshot;
    private FileSystemWatcher? _watcher;
    private string? _watchedWorkflowPath;
    private int _reloadRequested;
    private bool _disposed;

    public WorkflowOptionsProvider(
        IWorkflowLoader workflowLoader,
        IWorkflowOptionsResolver workflowOptionsResolver,
        WorkflowLoadStatusTracker workflowLoadStatusTracker,
        TimeProvider timeProvider,
        ILogger<WorkflowOptionsProvider> logger)
    {
        _workflowLoader = workflowLoader;
        _workflowOptionsResolver = workflowOptionsResolver;
        _workflowLoadStatusTracker = workflowLoadStatusTracker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Options;
    }

    public async Task<WorkflowDefinition> GetCurrentDefinitionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Definition;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher?.Dispose();
        _reloadGate.Dispose();
    }

    private async Task<WorkflowSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        var workflowPath = GetWorkflowPath();

        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            EnsureWatcher(workflowPath);

            var currentSnapshot = _currentSnapshot;
            if (currentSnapshot is null)
            {
                currentSnapshot = await LoadSnapshotAsync(workflowPath, cancellationToken).ConfigureAwait(false);
                _currentSnapshot = currentSnapshot;
                Volatile.Write(ref _reloadRequested, 0);
                return currentSnapshot;
            }

            var workflowPathChanged = !StringComparer.Ordinal.Equals(currentSnapshot.WorkflowPath, workflowPath);
            var currentFileState = GetWorkflowFileState(workflowPath);
            var fileStateChanged = currentSnapshot.FileState != currentFileState;
            var reloadRequested = Volatile.Read(ref _reloadRequested) == 1;

            if (reloadRequested && !workflowPathChanged && !fileStateChanged)
            {
                Volatile.Write(ref _reloadRequested, 0);
                return currentSnapshot;
            }

            if (workflowPathChanged || fileStateChanged)
            {
                currentSnapshot = await TryReloadSnapshotAsync(currentSnapshot, workflowPath, cancellationToken).ConfigureAwait(false);
            }

            return currentSnapshot;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task<WorkflowSnapshot> TryReloadSnapshotAsync(
        WorkflowSnapshot currentSnapshot,
        string workflowPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var reloadedSnapshot = await LoadSnapshotAsync(workflowPath, cancellationToken).ConfigureAwait(false);
            _currentSnapshot = reloadedSnapshot;

            _logger.LogInformation(
                "workflow_reload completed workflow_path={workflow_path} outcome=completed",
                workflowPath);

            return reloadedSnapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _workflowLoadStatusTracker.RecordFailed(
                workflowPath,
                GetErrorCode(exception),
                exception.Message,
                _timeProvider.GetUtcNow());
            _logger.LogError(
                exception,
                "workflow_reload failed workflow_path={workflow_path} error_code={error_code} outcome=failed",
                workflowPath,
                GetErrorCode(exception));

            return currentSnapshot;
        }
        finally
        {
            Volatile.Write(ref _reloadRequested, 0);
        }
    }

    private async Task<WorkflowSnapshot> LoadSnapshotAsync(string workflowPath, CancellationToken cancellationToken)
    {
        var workflowDefinition = await _workflowLoader.LoadAsync(workflowPath, cancellationToken).ConfigureAwait(false);
        var workflowOptions = _workflowOptionsResolver.Resolve(workflowDefinition);
        _workflowLoadStatusTracker.RecordLoaded(workflowPath, workflowOptions, _timeProvider.GetUtcNow());

        return new WorkflowSnapshot(
            workflowPath,
            GetWorkflowFileState(workflowPath),
            workflowDefinition,
            workflowOptions);
    }

    private void EnsureWatcher(string workflowPath)
    {
        if (StringComparer.Ordinal.Equals(_watchedWorkflowPath, workflowPath))
        {
            return;
        }

        _watcher?.Dispose();

        var directoryPath = Path.GetDirectoryName(workflowPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            directoryPath = Directory.GetCurrentDirectory();
        }

        var fileName = Path.GetFileName(workflowPath);
        var watcher = new FileSystemWatcher(directoryPath, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        watcher.Changed += OnWorkflowFileChanged;
        watcher.Created += OnWorkflowFileChanged;
        watcher.Deleted += OnWorkflowFileChanged;
        watcher.Renamed += OnWorkflowFileChanged;

        _watcher = watcher;
        _watchedWorkflowPath = workflowPath;
    }

    private void OnWorkflowFileChanged(object sender, FileSystemEventArgs args)
    {
        Volatile.Write(ref _reloadRequested, 1);
    }

    private static string GetWorkflowPath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");
    }

    private static WorkflowFileState GetWorkflowFileState(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            return new WorkflowFileState(false, DateTime.MinValue, 0);
        }

        var fileInfo = new FileInfo(workflowPath);
        return new WorkflowFileState(true, fileInfo.LastWriteTimeUtc, fileInfo.Length);
    }

    private static string GetErrorCode(Exception exception)
    {
        return exception switch
        {
            WorkflowLoadException workflowLoadException => workflowLoadException.Code,
            WorkflowConfigurationException workflowConfigurationException => workflowConfigurationException.Code,
            _ => "workflow_reload_error"
        };
    }

    private sealed record WorkflowSnapshot(
        string WorkflowPath,
        WorkflowFileState FileState,
        WorkflowDefinition Definition,
        WorkflowServiceOptions Options);

    private readonly record struct WorkflowFileState(bool Exists, DateTime LastWriteTimeUtc, long Length);
}
