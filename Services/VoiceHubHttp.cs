using System;
using System.Net;
using System.Net.Http;

namespace VoiceHubComponent.Services
{
    /// <summary>
    /// 共享 HTTP 设施：本插件对广播站点（VoiceHub 服务端）的全部请求复用同一个
    /// <see cref="SocketsHttpHandler"/>，以获得连接池复用、TLS 会话复用、DNS 轮转与响应压缩。
    ///
    /// 修复前各调用点各自 <c>new HttpClient()</c>，使用默认 handler：
    /// - 未开启 <see cref="AutomaticDecompression"/>，服务端返回的 gzip/brotli 排期/歌词 JSON
    ///   不会被解压，白白多出传输体积与解析延迟（<c>/api/songs/public</c> 是整包排期+歌词，体量最大）；
    /// - 每个客户端独立连接池，整天高频打歌词/音频接口时无法复用 TCP/TLS，握手开销累积；
    /// - 无连接生命周期管理，VoiceHub 服务端（通常位于 CDN/轮询后端之后）DNS 变更后
    ///   旧连接长期不失效。
    ///
    /// 该 handler 为进程级单例，生命周期与插件一致，无需释放。
    /// </summary>
    internal static class VoiceHubHttp
    {
        /// <summary>
        /// 全工程共享的 handler。连接按 2 分钟生命周期回收，使服务端 DNS 变更能及时生效，
        /// 同时保留空闲连接复用；开启全部压缩算法以缩减响应体积。
        /// </summary>
        public static readonly SocketsHttpHandler SharedHandler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        };

        /// <summary>
        /// 有界客户端：用于排期/歌词/音频等普通请求。超时由调用方按场景给定
        /// （主刷新 10s、设置页连接测试 15s），不在此处写死。
        /// </summary>
        public static HttpClient CreateBoundedClient(TimeSpan timeout) =>
            new(SharedHandler) { Timeout = timeout };

        /// <summary>
        /// 无限超时客户端：用于 SSE 长连接，超时由调用方用 <see cref="CancellationToken"/> 控制。
        /// 复用同一 handler 即可与有界请求共享连接池与压缩协商。
        /// </summary>
        public static HttpClient CreateInfiniteClient() =>
            new(SharedHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
