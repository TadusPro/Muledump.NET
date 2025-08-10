using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MDTadusMod.Data;

namespace MDTadusMod.Services
{
    // Thrown by your API/reload path when it sees the provider lockout message.
    public sealed class LoginLockoutException : Exception
    {
        public LoginLockoutException(string message) : base(message) { }
    }

    public class ReloadQueueService
    {
        private readonly ConcurrentQueue<ReloadTask> _queue = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isProcessing;
        private DateTime _lastRequestTime = DateTime.MinValue;

        // Full-queue lockout window
        private DateTime? _loginLimitUntil = null;

        // Single scheduled resume task so we don't stack timers
        private Task? _resumeTask;

        // Run id to correlate logs
        private Guid _runId = Guid.Empty;

        private const int RateLimitDelayMs = 1000; // 1s

        public event Action? OnQueueChanged;
        public event Action<string>? OnStatusChanged;
        public event Action<string, Exception>? OnAccountError;
        public event Action? OnAllTasksCompleted;
        public event Action<Guid, AccountData>? OnAccountDataUpdated;
        public event Action<Guid, string>? OnAccountStatusChanged;
        public event Action<DateTime?>? OnLockoutUntilChanged;


        public bool IsProcessing => _isProcessing;
        public int QueueCount => _queue.Count;

        public void QueueAccount(Guid accountId, string accountEmail, Func<Task<AccountData>> reloadAction)
        {
            _queue.Enqueue(new ReloadTask
            {
                AccountId = accountId,
                AccountEmail = accountEmail,
                ReloadAction = reloadAction
            });
            OnQueueChanged?.Invoke();
            Log($"Queued single account: {accountEmail}. QueueCount={_queue.Count}");
            if (!_isProcessing) _ = ProcessQueueAsync();
        }

        public void QueueAllAccounts(IEnumerable<(Guid accountId, string accountEmail, Func<Task<AccountData>> reloadAction)> accounts)
        {
            var added = 0;
            foreach (var (accountId, accountEmail, reloadAction) in accounts)
            {
                _queue.Enqueue(new ReloadTask
                {
                    AccountId = accountId,
                    AccountEmail = accountEmail,
                    ReloadAction = reloadAction
                });
                added++;
            }
            OnQueueChanged?.Invoke();
            Log($"Queued {added} accounts. QueueCount={_queue.Count}");
            if (!_isProcessing) _ = ProcessQueueAsync();
        }

        public void CancelProcessing()
        {
            _cancellationTokenSource?.Cancel();
            var before = _queue.Count;
            _queue.Clear();
            _loginLimitUntil = null;
            _resumeTask = null;
            OnQueueChanged?.Invoke();
            Log($"CancelProcessing called. Cleared {before} items. Lockout cleared.");
        }

        private async Task ProcessQueueAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_isProcessing)
                {
                    Log("ProcessQueueAsync skipped: already processing.");
                    return;
                }

                _runId = Guid.NewGuid();
                Log($"=== PROCESS START (RunId={_runId}) QueueCount={_queue.Count} ===");

                // If we're still in a lockout, schedule resume and exit early.
                if (_loginLimitUntil is DateTime until && DateTime.UtcNow < until)
                {
                    var wait = until - DateTime.UtcNow;
                    Log($"Login limit in effect. Pausing for {wait.TotalSeconds:F0}s (until {until:O}).");
                    ScheduleResume(wait, "initial-lockout");
                    return; // finally will release semaphore
                }

                _isProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;
                var successCount = 0;
                var errorCount = 0;

                while (_queue.TryDequeue(out var task) && !cancellationToken.IsCancellationRequested)
                {
                    Log($"Dequeue {task.AccountEmail}. Remaining={_queue.Count}");
                    try
                    {
                        OnAccountStatusChanged?.Invoke(task.AccountId, "Refreshing...");
                        Log($"Begin reload for {task.AccountEmail}");

                        // Simple 1 req/sec limiter
                        var since = DateTime.UtcNow - _lastRequestTime;
                        if (since.TotalMilliseconds < RateLimitDelayMs)
                        {
                            var delay = RateLimitDelayMs - (int)since.TotalMilliseconds;
                            Log($"Rate-limit sleep {delay}ms (sinceLast={since.TotalMilliseconds:F0}ms)");
                            await Task.Delay(delay, cancellationToken);
                        }

                        _lastRequestTime = DateTime.UtcNow;

                        AccountData accountData;
                        try
                        {
                            accountData = await task.ReloadAction();
                        }
                        catch (LoginLockoutException ex) // API detected lockout → hard pause queue
                        {
                            HandleLockout(task, ex.Message);
                            break; // stop processing now
                        }

                        // Optional safety: if API returns an object but embeds lockout in text
                        if (accountData?.LastErrorMessage?.IndexOf("LOGIN ATTEMPT LIMIT REACHED", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            HandleLockout(task, accountData.LastErrorMessage);
                            break; // stop processing now
                        }

                        // Normal handling
                        if (accountData != null)
                        {
                            var isSuccess = !accountData.PasswordError && string.IsNullOrEmpty(accountData.LastErrorMessage);
                            OnAccountDataUpdated?.Invoke(task.AccountId, accountData);

                            if (isSuccess)
                            {
                                successCount++;
                                OnAccountStatusChanged?.Invoke(task.AccountId, "Success");
                                Log($"Success for {task.AccountEmail}");
                            }
                            else if (accountData.PasswordError)
                            {
                                errorCount++;
                                OnAccountStatusChanged?.Invoke(task.AccountId, "Password Error");
                                Log($"Password error for {task.AccountEmail}");
                            }
                            else
                            {
                                errorCount++;
                                OnAccountStatusChanged?.Invoke(task.AccountId, $"Error: {accountData.LastErrorMessage}");
                                Log($"Error for {task.AccountEmail}: {accountData.LastErrorMessage}");
                            }
                        }
                        else
                        {
                            errorCount++;
                            OnAccountStatusChanged?.Invoke(task.AccountId, "Error");
                            Log($"Null data for {task.AccountEmail}");
                        }
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Log($"HTTP 429 for {task.AccountEmail}. Requeue and sleep 5s. Ex={ex.Message}");
                        await Task.Delay(5000, cancellationToken);
                        _queue.Enqueue(task);
                        OnQueueChanged?.Invoke();
                    }
                    catch (LoginLockoutException ex) // in case ReloadAction throws outside the inner try
                    {
                        HandleLockout(task, ex.Message);
                        break;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        OnAccountError?.Invoke(task.AccountEmail, ex);
                        OnAccountStatusChanged?.Invoke(task.AccountId, "Error");
                        LogException($"Error processing {task.AccountEmail}", ex);
                    }

                    OnQueueChanged?.Invoke();
                }

                var paused = _resumeTask != null || (_loginLimitUntil.HasValue && DateTime.UtcNow < _loginLimitUntil.Value);
                Log($"=== PROCESS END (RunId={_runId}) Success={successCount} Errors={errorCount} Paused={paused} QueueCount={_queue.Count} ===");

                if (!cancellationToken.IsCancellationRequested && _resumeTask == null)
                {
                    OnAllTasksCompleted?.Invoke();
                    Log("OnAllTasksCompleted invoked.");
                }
            }
            finally
            {
                _isProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _semaphore.Release();
            }
        }

        private void HandleLockout(ReloadTask task, string message)
        {
            // Put the current job back and pause everything.
            _queue.Enqueue(task);
            OnQueueChanged?.Invoke();

            _loginLimitUntil = DateTime.UtcNow.AddMinutes(5);
            OnLockoutUntilChanged?.Invoke(_loginLimitUntil);
            var until = _loginLimitUntil.Value;
            Log($"LOCKOUT '{message}'. Requeued {task.AccountEmail}. Pausing ALL until {until:O} (~{(until - DateTime.UtcNow).TotalSeconds:F0}s). Remaining={_queue.Count}");
            OnAccountStatusChanged?.Invoke(task.AccountId, "Login limit - paused");

            ScheduleResume(TimeSpan.FromMinutes(5), "lockout-detected");
        }

        private void ScheduleResume(TimeSpan wait, string reason)
        {
            if (_resumeTask != null)
            {
                Log($"Resume already scheduled. Reason={reason} Wait={wait.TotalSeconds:F0}s");
                return;
            }

            var resumeAt = DateTime.UtcNow + wait;
            Log($"Scheduling resume in {wait.TotalSeconds:F0}s at {resumeAt:O}. Reason={reason}");

            _resumeTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(wait);
                }
                catch (Exception ex)
                {
                    LogException("Resume timer faulted", ex);
                }
                finally
                {
                    _resumeTask = null;
                    // Let _loginLimitUntil naturally expire; don't clear early.
                    Log($"Resume timer fired at {DateTime.UtcNow:O}. QueueCount={_queue.Count}. IsProcessing={_isProcessing}");
                    if (_queue.Count > 0 && !_isProcessing)
                    {
                        Log("Restarting ProcessQueueAsync after pause.");
                        OnLockoutUntilChanged?.Invoke(null);
                        _ = ProcessQueueAsync();
                    }
                }
            });
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.UtcNow:O}] [ReloadQueue] [Run:{_runId}] {message}";
            OnStatusChanged?.Invoke(line);
            Debug.WriteLine(line);
        }

        private void LogException(string context, Exception ex)
        {
            var line = $"[{DateTime.UtcNow:O}] [ReloadQueue] [Run:{_runId}] {context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            OnStatusChanged?.Invoke(line);
            Debug.WriteLine(line);
        }

        private class ReloadTask
        {
            public Guid AccountId { get; set; }
            public string AccountEmail { get; set; } = string.Empty;
            public Func<Task<AccountData>> ReloadAction { get; set; } = () => Task.FromResult<AccountData>(null);
        }
    }
}
