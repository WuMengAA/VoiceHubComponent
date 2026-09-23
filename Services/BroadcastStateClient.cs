using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceHubComponent.Models;

namespace VoiceHubComponent.Services
{
    /// <summary>
    /// 广播状态客户端：以 SSE（GET /api/music/websocket）为主通道，30 秒轮询（GET /api/music/broadcast）
    /// 作为断线兜底，拉取服务端权威「正在播放」状态。后台线程运行，断线指数退避重连（1s→2s→4s，封顶 30s）。
    ///
    /// 设计约束（见 AI-必读-广播正在播放-桥接提示词.md）：
    /// - 只读，绝不向 VoiceHub 写入任何数据。
    /// - 复用主刷新用的 _httpClient 会被设成 10s 超时，长连接 SSE 会因此秒断，故本服务持有独立 HttpClient（无限超时）。
    /// - 任何 404/403/超时都按「没有广播」处理，绝不污染主刷新的 ComponentState 状态机。
    /// </summary>
    public sealed class BroadcastStateClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private const int PollIntervalSeconds = 30;
        private const int PollTimeoutSeconds = 20;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

        private readonly Func<Uri?> _originProvider;
        private readonly ILogger? _logger;
        private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly CancellationTokenSource _cts = new();
        private Task? _runTask;
        private bool _started;

        // 合并后的最新状态（加锁保护）
        private readonly object _stateLock = new();
        private BroadcastState? _broadcast;
        private BroadcastNextUp? _nextUp;
        private int _listeners;
        private bool _baselineReleased;
        private DateTime _lastReceivedUtc = DateTime.MinValue;

        /// <summary>
        /// 广播状态变化事件（SSE 或轮询任一通道更新时触发）。回调可能发生在后台线程，订阅方需自行切回 UI 线程。
        /// </summary>
        public event Action<BroadcastUpdate>? BroadcastChanged;

        public BroadcastStateClient(Func<Uri?> originProvider, ILogger? logger = null)
        {
            _originProvider = originProvider;
            _logger = logger;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _runTask = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        }

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }

            try
            {
                _runTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 忽略等待异常
            }

            _cts.Dispose();
            _httpClient.Dispose();
        }

        /// <summary>
        /// 返回当前合并后的广播状态快照（线程安全）。
        /// </summary>
        public BroadcastUpdate Current()
        {
            lock (_stateLock)
            {
                return new BroadcastUpdate(_broadcast, _nextUp, _listeners, _baselineReleased, _lastReceivedUtc);
            }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            var backoff = TimeSpan.FromSeconds(1);
            while (!token.IsCancellationRequested)
            {
                var origin = _originProvider();
                if (origin == null)
                {
                    // 配置尚未就绪（例如 API 地址为空），稍后重试
                    await DelayAsync(token, TimeSpan.FromSeconds(5));
                    continue;
                }

                try
                {
                    await ConnectSseAsync(origin, token);
                    backoff = TimeSpan.FromSeconds(1); // 成功建连后重置退避
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 连接失败：退避后重连；期间的轮询兜底由连接内独立任务继续承担
                    _logger?.LogWarning("广播 SSE 连接失败：{Message}，{Backoff} 秒后重连", ex.Message, backoff.TotalSeconds);
                    await DelayAsync(token, backoff);
                    backoff = backoff * 2;
                    if (backoff > MaxBackoff)
                    {
                        backoff = MaxBackoff;
                    }
                }
            }
        }

        private async Task ConnectSseAsync(Uri origin, CancellationToken token)
        {
            var sseUri = new Uri(origin, "/api/music/websocket");
            using var request = new HttpRequestMessage(HttpMethod.Get, sseUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("X-Requested-From", "ClassIslandPlugin");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"SSE 返回 {(int)response.StatusCode} {response.StatusCode}");
            }

            // SSE 长连接期间，并行跑一个 30s 轮询兜底，断线时仍能自愈
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollLoopAsync(origin, pollCts.Token);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);

                string? eventType = null;
                var dataBuilder = new StringBuilder();
                string? line;
                while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                {
                    if (line.Length == 0)
                    {
                        // 一个事件块结束
                        if (string.Equals(eventType, "broadcast_state", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyBroadcastStateData(dataBuilder.ToString());
                        }

                        eventType = null;
                        dataBuilder.Clear();
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventType = line.Substring(6).Trim();
                    }
                    else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        dataBuilder.Append(line.Substring(5).Trim());
                    }
                    // 以 ':' 开头的注释行忽略
                }
            }
            finally
            {
                pollCts.Cancel();
                try
                {
                    await pollTask;
                }
                catch
                {
                    // 忽略轮询任务收尾异常
                }
            }
        }

        private async Task PollLoopAsync(Uri origin, CancellationToken token)
        {
            var pollUri = new Uri(origin, "/api/music/broadcast");
            while (!token.IsCancellationRequested)
            {
                using var perCallCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                perCallCts.CancelAfter(TimeSpan.FromSeconds(PollTimeoutSeconds));
                try
                {
                    var json = await _httpClient.GetStringAsync(pollUri, perCallCts.Token);
                    var snapshot = JsonSerializer.Deserialize<BroadcastSnapshot>(json, JsonOptions);
                    if (snapshot != null)
                    {
                        lock (_stateLock)
                        {
                            _broadcast = snapshot.Broadcast;
                            _nextUp = snapshot.NextUp;
                            _listeners = snapshot.Listeners;
                            _baselineReleased = snapshot.BaselineReleased;
                            _lastReceivedUtc = DateTime.UtcNow;
                        }

                        RaiseChanged();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 轮询失败只记日志，不进主刷新状态机；下个周期再试
                    _logger?.LogTrace("广播状态轮询失败（忽略）：{Message}", ex.Message);
                }

                await DelayAsync(token, TimeSpan.FromSeconds(PollIntervalSeconds));
            }
        }

        private void ApplyBroadcastStateData(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            try
            {
                var state = JsonSerializer.Deserialize<BroadcastState>(data, JsonOptions);
                lock (_stateLock)
                {
                    _broadcast = state;
                    _lastReceivedUtc = DateTime.UtcNow;
                }

                RaiseChanged();
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning("广播状态 SSE 数据解析失败：{Message}", ex.Message);
            }
        }

        private void RaiseChanged()
        {
            BroadcastState? broadcast;
            BroadcastNextUp? nextUp;
            int listeners;
            bool baselineReleased;
            DateTime lastReceivedUtc;
            lock (_stateLock)
            {
                broadcast = _broadcast;
                nextUp = _nextUp;
                listeners = _listeners;
                baselineReleased = _baselineReleased;
                lastReceivedUtc = _lastReceivedUtc;
            }

            BroadcastChanged?.Invoke(new BroadcastUpdate(broadcast, nextUp, listeners, baselineReleased, lastReceivedUtc));
        }

        private static async Task DelayAsync(CancellationToken token, TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 正常取消，忽略
            }
        }
    }

    /// <summary>
    /// 广播状态合并快照，作为 <see cref="BroadcastStateClient.BroadcastChanged"/> 的事件参数。
    /// </summary>
    public sealed class BroadcastUpdate
    {
        public BroadcastUpdate(BroadcastState? broadcast, BroadcastNextUp? nextUp, int listeners, bool baselineReleased, DateTime lastReceivedUtc)
        {
            Broadcast = broadcast;
            NextUp = nextUp;
            Listeners = listeners;
            BaselineReleased = baselineReleased;
            LastReceivedUtc = lastReceivedUtc;
        }

        /// <summary>
        /// 当前正在播放的曲目状态；为 null 表示没有广播。
        /// </summary>
        public BroadcastState? Broadcast { get; }

        /// <summary>
        /// 连播队列里的下一首；为 null 表示无。
        /// </summary>
        public BroadcastNextUp? NextUp { get; }

        /// <summary>
        /// 当前在线收听人数（无广播时为 0）。
        /// </summary>
        public int Listeners { get; }

        /// <summary>
        /// 基准播控人掉线释放后为 true。
        /// </summary>
        public bool BaselineReleased { get; }

        /// <summary>
        /// UTC 时间，最近一次成功收到快照的时刻。用于判定快照是否过期（停播/掉线自愈）。
        /// </summary>
        public DateTime LastReceivedUtc { get; }
    }
}
