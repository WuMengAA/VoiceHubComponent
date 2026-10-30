using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VoiceHubComponent.Models;
using VoiceHubComponent.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Helpers.UI;
using FluentAvalonia.UI.Controls;

namespace VoiceHubComponent.Views
{
    public partial class VoiceHubSettingsView : ComponentBase<VoiceHubSettings>
    {
        private const string DefaultApiUrl = "https://voicehub.lao-shui.top/api/songs/public";
        private const string DefaultHelpDocUrl = "https://www.245959623.xyz";
        private readonly HttpClient _httpClient =
            VoiceHubHttp.CreateBoundedClient(TimeSpan.FromSeconds(15));

        public VoiceHubSettingsView()
        {
            InitializeComponent();
        }

        public VoiceHubSettingsView(VoiceHubSettings settings)
        {
            InitializeComponent();
            DataContext = settings;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryApplySettingsFromInputs())
            {
                return;
            }

            this.ShowSuccessToast("配置已保存");
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var originalContent = button.Content;
            button.Content = "刷新中...";
            button.IsEnabled = false;

            try
            {
                if (!TryApplySettingsFromInputs())
                {
                    return;
                }

                VoiceHubControl.ClearLyricCache();
                await VoiceHubControl.RequestManualRefreshAsync();
                this.ShowSuccessToast("配置已生效，组件已刷新");
            }
            catch (Exception ex)
            {
                this.ShowErrorToast($"刷新失败：{ex.Message}");
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private async void ForceLyricsRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var originalContent = button.Content;
            button.Content = "重新获取中...";
            button.IsEnabled = false;

            try
            {
                if (!TryApplySettingsFromInputs())
                {
                    return;
                }

                VoiceHubControl.ClearLyricCache();
                await VoiceHubControl.RequestManualRefreshAsync();
                this.ShowSuccessToast("歌词缓存已清除，正在重新获取");
            }
            catch (Exception ex)
            {
                this.ShowErrorToast($"重新获取失败：{ex.Message}");
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var originalContent = button.Content;
            button.Content = "测试中...";
            button.IsEnabled = false;

            try
            {
                var apiUrl = Settings.ApiUrl?.Trim();
                if (string.IsNullOrEmpty(apiUrl))
                {
                    this.ShowWarningToast("请输入API地址");
                    return;
                }

                // 设置超时时间
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync(apiUrl, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        this.ShowSuccessToast("连接成功！API响应正常");
                    }
                    else
                    {
                        this.ShowWarningToast("连接成功，但API返回空数据");
                    }
                }
                else
                {
                    this.ShowErrorToast($"连接失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                this.ShowErrorToast("连接超时，请检查网络或API地址");
            }
            catch (HttpRequestException ex)
            {
                this.ShowErrorToast($"网络错误：{ex.Message}");
            }
            catch (Exception ex)
            {
                this.ShowErrorToast($"测试失败：{ex.Message}");
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.ApiUrl = DefaultApiUrl;
            Settings.EnableLyrics = false;
            Settings.BroadcastStartTime = "12:20:00";
            Settings.NeteaseCookie = string.Empty;
            Settings.UseDebugScheduleDate = false;
            Settings.DebugScheduleDate = DateTime.Today;
            Settings.ShowCover = true;
            Settings.ShowTranslation = true;
            Settings.ShowRomanization = false;
            Settings.EnableLyricUpgrade = true;
            Settings.EnableNowPlaying = true;
            Settings.HelpDocUrl = DefaultHelpDocUrl;
            ApiUrlTextBox.Text = DefaultApiUrl;
            EnableLyricsCheckBox.IsChecked = false;
            BroadcastStartTimeTextBox.Text = "12:20:00";
            NeteaseCookieTextBox.Text = string.Empty;
            UseDebugScheduleDateCheckBox.IsChecked = false;
            DebugScheduleDatePicker.SelectedDate = DateTime.Today;
            ShowCoverCheckBox.IsChecked = true;
            ShowTranslationCheckBox.IsChecked = true;
            ShowRomanizationCheckBox.IsChecked = false;
            EnableLyricUpgradeCheckBox.IsChecked = true;
            EnableNowPlayingCheckBox.IsChecked = true;
            HelpDocUrlTextBox.Text = DefaultHelpDocUrl;
            this.ShowSuccessToast("已重置为默认配置");
        }

        private async void OpenHelpStudent_Click(object sender, RoutedEventArgs e)
            => await OpenHelpAsync("student");

        private async void OpenHelpStaff_Click(object sender, RoutedEventArgs e)
            => await OpenHelpAsync("staff");

        private async void OpenHelpOnepager_Click(object sender, RoutedEventArgs e)
            => await OpenHelpAsync("onepager");

        /// <summary>
        /// 用系统默认浏览器打开站点上的在线帮助文档（零额外依赖，走 Process shell 启动）。
        ///  anchor: student / staff / onepager，对应 /voicehub/help 的三个标签页。
        /// </summary>
        private async Task OpenHelpAsync(string anchor)
        {
            try
            {
                var baseUrl = (Settings.HelpDocUrl ?? string.Empty).Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(baseUrl))
                {
                    baseUrl = DefaultHelpDocUrl;
                }

                var url = string.IsNullOrEmpty(anchor)
                    ? $"{baseUrl}/voicehub/help"
                    : $"{baseUrl}/voicehub/help#{anchor}";

                await Task.Run(() =>
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
            }
            catch (Exception ex)
            {
                this.ShowErrorToast($"打开帮助失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 校验输入并把全部设置写回 Settings
        /// </summary>
        private bool TryApplySettingsFromInputs()
        {
            var apiUrl = ApiUrlTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(apiUrl))
            {
                this.ShowWarningToast("请输入API地址");
                return false;
            }

            var startTime = BroadcastStartTimeTextBox.Text?.Trim() ?? string.Empty;
            if (!TimeSpan.TryParseExact(
                    startTime,
                    new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss", @"h\:mm\:ss" },
                    CultureInfo.InvariantCulture,
                    out _))
            {
                this.ShowWarningToast("固定开始时间格式应为 HH:mm 或 HH:mm:ss");
                return false;
            }

            Settings.ApiUrl = apiUrl;
            Settings.EnableLyrics = EnableLyricsCheckBox.IsChecked == true;
            Settings.BroadcastStartTime = startTime;
            Settings.NeteaseCookie = NeteaseCookieTextBox.Text?.Trim() ?? string.Empty;
            Settings.UseDebugScheduleDate = UseDebugScheduleDateCheckBox.IsChecked == true;
            Settings.DebugScheduleDate = DebugScheduleDatePicker.SelectedDate ?? DateTime.Today;
            Settings.ShowCover = ShowCoverCheckBox.IsChecked == true;
            Settings.ShowTranslation = ShowTranslationCheckBox.IsChecked == true;
            Settings.ShowRomanization = ShowRomanizationCheckBox.IsChecked == true;
            Settings.EnableLyricUpgrade = EnableLyricUpgradeCheckBox.IsChecked == true;
            Settings.EnableNowPlaying = EnableNowPlayingCheckBox.IsChecked == true;
            Settings.HelpDocUrl = HelpDocUrlTextBox.Text?.Trim() ?? string.Empty;
            return true;
        }
    }
}
