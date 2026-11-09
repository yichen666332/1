using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// DeepSeek(OpenAI兼容)最小客户端接口，仅保留通用提示词调用。
    /// </summary>
    public interface IDeepSeekClient
    {
        /// <summary>
        /// 让 LLM 基于“外部自定义提示词”给出下一步 AKF 调参建议（严格 JSON）。
        /// 你可以为每个滤波器传不同的 system/user 提示词。
        /// </summary>
        Task<LLMSuggestion> GetTuningAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens = 128,
            double temperature = 0.2,
            CancellationToken ct = default);

        /// <summary>连通性自检：期望返回 {"ok":true}。</summary>
        Task<(bool ok, string detail)> PingAsync(CancellationToken ct = default);

        /// <summary>简单对话：验证模型能正常回答。</summary>
        Task<string> QuickChatAsync(string prompt, CancellationToken ct = default);
    }

    /// <summary>
    /// DeepSeek(OpenAI兼容) 客户端实现。BaseAddress: https://api.deepseek.com/v1/
    /// </summary>
    public sealed class DeepSeekClient : IDeepSeekClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _apiKey;
        private readonly string _model;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public DeepSeekClient(string apiKey, string model = "deepseek-reasoner", HttpClient http = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("apiKey is null or whitespace", nameof(apiKey));

            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "deepseek-reasoner" : model;

            if (http is null)
            {
                _http = new HttpClient
                {
                    BaseAddress = new Uri("https://api.deepseek.com/v1/"),
                    Timeout = Timeout.InfiniteTimeSpan   // 超时交由 CancellationToken 控制
                };
                _ownsHttp = true;
            }
            else
            {
                _http = http;
                if (_http.BaseAddress is null)
                    _http.BaseAddress = new Uri("https://api.deepseek.com/v1/");
            }
        }

        // ===================== Public APIs =====================

        public async Task<LLMSuggestion> GetTuningAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens = 128,
            double temperature = 0.2,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                throw new ArgumentException("systemPrompt is null or whitespace", nameof(systemPrompt));
            if (string.IsNullOrWhiteSpace(userPrompt))
                throw new ArgumentException("userPrompt is null or whitespace", nameof(userPrompt));

            var body = new
            {
                model = _model,          // deepseek-reasoner
                temperature = temperature,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" },
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt   }
                }
            };

            string content = await PostChatAsync(body, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LLMSuggestion>(content, JsonOpts);
        }

        public async Task<(bool ok, string detail)> PingAsync(CancellationToken ct = default)
        {
            try
            {
                string system = "You are a helpful assistant. Only output JSON. No extra text.";
                string user = @"Return exactly this JSON: {""ok"": true}";

                var body = new
                {
                    model = _model,       // 建议检测用 deepseek-chat，调参用 reasoner
                    temperature = 0.0,
                    max_tokens = 16,
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = system },
                        new { role = "user",   content = user   }
                    }
                };

                using var cts = ct.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : new CancellationTokenSource(TimeSpan.FromSeconds(20));

                string content = await PostChatAsync(body, cts.Token).ConfigureAwait(false);
                var pong = JsonSerializer.Deserialize<PingObj>(content, JsonOpts);
                return pong?.ok == true ? (true, "ok") : (false, $"unexpected content: {content}");
            }
            catch (HttpRequestException ex)
            {
                return (false, "HttpRequestException: " + ex.Message);
            }
            catch (TaskCanceledException)
            {
                return (false, "Timeout");
            }
            catch (Exception ex)
            {
                return (false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public async Task<string> QuickChatAsync(string prompt, CancellationToken ct = default)
        {
            var body = new
            {
                model = _model,
                temperature = 0.2,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user",   content = prompt }
                }
            };

            string raw = await PostRawAsync(body, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("choices")[0]
                     .GetProperty("message").GetProperty("content").GetString();
        }

        // ===================== Internals =====================

        private async Task<string> PostChatAsync(object body, CancellationToken ct)
        {
            string raw = await PostRawAsync(body, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("choices", out _))
            {
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new InvalidOperationException(err.ToString());
                throw new InvalidOperationException("Invalid response: " + raw);
            }

            return doc.RootElement.GetProperty("choices")[0]
                         .GetProperty("message").GetProperty("content").GetString();
        }

        private async Task<string> PostRawAsync(object body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} | {raw}");

            return raw;
        }

        public void Dispose()
        {
            if (_ownsHttp) _http?.Dispose();
        }

        private sealed class PingObj { public bool ok { get; set; } }
    }

    /// <summary>LLM 的调参建议（严格 JSON）。</summary>
    public sealed class LLMSuggestion
    {
        public float proposed_q { get; set; }
        public float proposed_r { get; set; }
        public float proposed_alpha { get; set; }
        public float confidence { get; set; }
        public string rationale { get; set; }
    }
}