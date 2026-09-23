using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VoiceHubComponent.Models
{
    /// <summary>
    /// 歌曲信息模型
    /// </summary>
    public class Song
    {
        /// <summary>
        /// 歌曲标题
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 艺术家/歌手
        /// </summary>
        [JsonPropertyName("artist")]
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// 点歌人
        /// </summary>
        [JsonPropertyName("requester")]
        public string Requester { get; set; } = string.Empty;

        /// <summary>
        /// 投票数/热度
        /// </summary>
        [JsonPropertyName("voteCount")]
        public int VoteCount { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("musicPlatform")]
        public string? MusicPlatform { get; set; }

        [JsonPropertyName("musicId")]
        public string? MusicId { get; set; }

        [JsonPropertyName("playUrl")]
        public string? PlayUrl { get; set; }

        /// <summary>
        /// 是否为重播歌曲
        /// </summary>
        [JsonPropertyName("isReplay")]
        public bool IsReplay { get; set; }

        /// <summary>
        /// 歌曲封面 URL
        /// </summary>
        [JsonPropertyName("cover")]
        public string? Cover { get; set; }
    }

    /// <summary>
    /// 排期歌曲项目模型
    /// </summary>
    public class SongItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// 播放日期
        /// </summary>
        [JsonPropertyName("playDate")]
        public string PlayDate { get; set; } = string.Empty;

        /// <summary>
        /// 播放序号
        /// </summary>
        [JsonPropertyName("sequence")]
        public int Sequence { get; set; }

        /// <summary>
        /// 歌曲信息
        /// </summary>
        [JsonPropertyName("song")]
        public Song Song { get; set; } = new Song();

        [JsonPropertyName("playTime")]
        public PlayTimeInfo? PlayTime { get; set; }

        /// <summary>
        /// 【新增读取字段，不破坏旧契约】逐条排期是否为音乐管理员正在播放的当前曲目。
        /// 老服务端无此字段时反序列化为 false，等同于“没有广播”。
        /// </summary>
        [JsonPropertyName("isPlaying")]
        public bool IsPlaying { get; set; }

        /// <summary>
        /// 【新增读取字段，不破坏旧契约】取得响应当刻已播放到的秒数；未在播为 null。
        /// 老服务端无此字段时反序列化为 null。
        /// </summary>
        [JsonPropertyName("broadcastPosition")]
        public double? BroadcastPosition { get; set; }

        /// <summary>
        /// 获取播放日期的DateTime对象
        /// </summary>
        public DateTime GetPlayDateTime()
        {
            if (string.IsNullOrWhiteSpace(PlayDate))
            {
                return DateTime.MinValue;
            }

            // 日期格式固定为 yyyy-MM-dd，直接解析
            if (DateTime.TryParseExact(PlayDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// 获取播放日期（仅日期部分）
        /// </summary>
        public DateTime GetPlayDate()
        {
            return GetPlayDateTime().Date;
        }
    }

    public class PlayTimeInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("startTime")]
        public string? StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public string? EndTime { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// 逐字歌词单词项
    /// </summary>
    public class LyricWordItem
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = string.Empty;

        public LyricWordItem Clone()
        {
            return new LyricWordItem
            {
                Start = Start,
                End = End,
                Text = Text
            };
        }
    }

    /// <summary>
    /// 歌词行，支持逐字时间轴、翻译、罗马音与 TTML 背景/合唱标记
    /// </summary>
    public class LyricLineItem
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Translation { get; set; }
        public string? Romanization { get; set; }

        /// <summary>
        /// TTML 背景声行
        /// </summary>
        public bool IsBG { get; set; }

        /// <summary>
        /// TTML 对唱行（第二个声部）
        /// </summary>
        public bool IsDuet { get; set; }

        /// <summary>
        /// 逐字时间轴；为空表示行级歌词
        /// </summary>
        public List<LyricWordItem> Words { get; set; } = new();

        /// <summary>
        /// 是否具备有效的逐字时间轴
        /// </summary>
        public bool HasWordTiming => Words.Any(word => word.End > word.Start);

        public LyricLineItem Clone()
        {
            return new LyricLineItem
            {
                Start = Start,
                End = End,
                Text = Text,
                Translation = Translation,
                Romanization = Romanization,
                IsBG = IsBG,
                IsDuet = IsDuet,
                Words = Words.Select(word => word.Clone()).ToList()
            };
        }
    }

    /// <summary>
    /// 组件状态枚举
    /// </summary>
    public enum ComponentState
    {
        /// <summary>
        /// 加载中
        /// </summary>
        Loading,
        /// <summary>
        /// 正常显示
        /// </summary>
        Normal,
        /// <summary>
        /// 网络错误
        /// </summary>
        NetworkError,
        /// <summary>
        /// 暂无排期
        /// </summary>
        NoSchedule
    }

    /// <summary>
    /// 显示数据模型
    /// </summary>
    public class DisplayData
    {
        /// <summary>
        /// 组件状态
        /// </summary>
        public ComponentState State { get; set; }

        /// <summary>
        /// 歌曲列表
        /// </summary>
        public List<SongItem> Songs { get; set; } = new List<SongItem>();

        /// <summary>
        /// 显示日期
        /// </summary>
        public DateTime? DisplayDate { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// 服务端广播权威状态（GET /api/music/broadcast 的 broadcast 字段 / SSE broadcast_state 的 data）。
    /// 全部为只读展示字段；broadcast 为 null 表示当前无广播（或播控总开关关闭）。
    /// </summary>
    public class BroadcastState
    {
        [JsonPropertyName("songId")] public int SongId { get; set; }
        [JsonPropertyName("scheduleId")] public int? ScheduleId { get; set; }
        [JsonPropertyName("playDate")] public string? PlayDate { get; set; }
        [JsonPropertyName("sequence")] public int Sequence { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("artist")] public string? Artist { get; set; }
        [JsonPropertyName("cover")] public string? Cover { get; set; }
        [JsonPropertyName("musicPlatform")] public string? MusicPlatform { get; set; }
        [JsonPropertyName("musicId")] public string? MusicId { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("position")] public double Position { get; set; }
        [JsonPropertyName("isPlaying")] public bool IsPlaying { get; set; }
        [JsonPropertyName("publisherName")] public string? PublisherName { get; set; }
    }

    /// <summary>
    /// 连播队列里的「下一首」摘要（快照 nextUp；无连播时为 null）。
    /// </summary>
    public class BroadcastNextUp
    {
        [JsonPropertyName("songId")] public int SongId { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("artist")] public string? Artist { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
    }

    /// <summary>
    /// GET /api/music/broadcast 的完整快照。
    /// </summary>
    public class BroadcastSnapshot
    {
        [JsonPropertyName("broadcast")] public BroadcastState? Broadcast { get; set; }
        [JsonPropertyName("nextUp")] public BroadcastNextUp? NextUp { get; set; }
        [JsonPropertyName("listeners")] public int Listeners { get; set; }
        [JsonPropertyName("baselineReleased")] public bool BaselineReleased { get; set; }
        [JsonPropertyName("serverTime")] public long ServerTime { get; set; }
    }
}
