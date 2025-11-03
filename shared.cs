using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedOdds
{
    // ---- Models expected by BuildClientPayload (match your Eurobet models) ----
    public class MatchData
    {
        public string Teams { get; set; } = string.Empty;     // "Home vs Away"
        public OddsSchema Odds { get; set; } = new();
    }

    public class OddsSchema
    {
        [JsonPropertyName("1")] public double? One { get; set; }
        [JsonPropertyName("X")] public double? X { get; set; }
        [JsonPropertyName("2")] public double? Two { get; set; }

        [JsonPropertyName("O/U")]
        public Dictionary<string, OverUnderNode> OU { get; set; } = new();

        [JsonPropertyName("1 2 + Handicap")]
        public Dictionary<string, OneTwoNode> OneTwoHandicap { get; set; } = new();

        [JsonPropertyName("GG")] public double? GG { get; set; }
        [JsonPropertyName("NG")] public double? NG { get; set; }
    }

    public class OverUnderNode
    {
        [JsonPropertyName("U")] public double? U { get; set; }
        [JsonPropertyName("O")] public double? O { get; set; }
    }

    public class OneTwoNode
    {
        [JsonPropertyName("1")] public double? One { get; set; }
        [JsonPropertyName("2")] public double? Two { get; set; }
    }

    public static class Payload
    {
        // Same endpoint you use in Eurobet
        public const string POST_ENDPOINT = "https://www.hh24tech.com/connector/index.php";

        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        // 1:1 copy of your Eurobet sport mapping decision (condensed)
        private static string MapSportValue(string? sportLabel,
            bool looksBasket, bool looksTennis, bool looksBaseball,
            bool looksAmericanFootball, bool looksIceHockey, bool looksRugby)
        {
            string norm = (sportLabel ?? "").Trim().ToUpperInvariant();
            if (looksBasket) return "basket";
            if (looksTennis) return "tennis";
            if (looksBaseball) return "baseball";
            if (looksAmericanFootball) return "americanfootball";
            if (looksIceHockey) return "hockey";
            if (looksRugby) return "rugby";
            if (norm == "CALCIO" || norm == "SOCCER" || norm == "FOOTBALL") return "soccer";
            return "soccer";
        }

        /// <summary>
        /// Exact clone of your Eurobet JSON builder so the shape/keys are identical.
        /// </summary>
        public static object BuildClientPayload(
            MatchData m,
            string? sportLabel = null,
            bool looksBasket = false,
            bool looksTennis = false,
            bool looksBaseball = false,
            bool looksAmericanFootball = false,
            bool looksIceHockey = false,
            bool looksRugby = false)
        {
            // Build O/U dictionary only with present values
            var ou = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in m.Odds.OU)
            {
                var node = kv.Value;
                if ((node?.U).HasValue || (node?.O).HasValue)
                {
                    var inner = new Dictionary<string, double?>();
                    if (node!.U.HasValue) inner["U"] = node.U.Value;
                    if (node.O.HasValue) inner["O"] = node.O.Value;
                    if (inner.Count > 0) ou[kv.Key] = inner;
                }
            }

            bool hasHandicap = m.Odds.OneTwoHandicap != null && m.Odds.OneTwoHandicap.Count > 0;

            // Non-soccer (or any with handicap) schema
            if (looksBasket || looksTennis || looksBaseball || looksAmericanFootball || looksIceHockey || hasHandicap || looksRugby)
            {
                var hcap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in m.Odds.OneTwoHandicap)
                {
                    var n = kv.Value;
                    if (n == null) continue;
                    var obj = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
                    if (n.One.HasValue) obj["1"] = n.One.Value;
                    if (n.Two.HasValue) obj["2"] = n.Two.Value;
                    if (obj.Count > 0) hcap[kv.Key] = obj;
                }

                var odds = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (m.Odds.One.HasValue) odds["1"] = m.Odds.One.Value;
                if (m.Odds.Two.HasValue) odds["2"] = m.Odds.Two.Value;

                // Rugby + Ice Hockey can be 3-way → include X if present
                if ((looksRugby || looksIceHockey) && m.Odds.X.HasValue) odds["X"] = m.Odds.X.Value;

                if (ou.Count > 0) odds["O/U"] = ou;
                if (hcap.Count > 0) odds["1 2 + Handicap"] = hcap;

                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sport"] = MapSportValue(sportLabel, looksBasket, looksTennis, looksBaseball, looksAmericanFootball, looksIceHockey, looksRugby),
                    ["Teams"] = m.Teams,
                    ["Bookmaker"] = "Sisal", // set bookmaker explicitly for Sisal
                    ["Odds"] = odds,
                    ["scraped_at_utc"] = DateTime.UtcNow.ToString("o")
                };
            }

            // Soccer schema (with 1/X/2 + GG/NG + O/U)
            var oddsSoccer = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (m.Odds.One.HasValue) oddsSoccer["1"] = m.Odds.One.Value;
            if (m.Odds.X.HasValue) oddsSoccer["X"] = m.Odds.X.Value;
            if (m.Odds.Two.HasValue) oddsSoccer["2"] = m.Odds.Two.Value;
            if (m.Odds.GG.HasValue) oddsSoccer["GG"] = m.Odds.GG.Value;
            if (m.Odds.NG.HasValue) oddsSoccer["NG"] = m.Odds.NG.Value;
            if (ou.Count > 0) oddsSoccer["O/U"] = ou;

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["sport"] = "soccer",
                ["Teams"] = m.Teams,
                ["Bookmaker"] = "Sisal",
                ["Odds"] = oddsSoccer,
                ["scraped_at_utc"] = DateTime.UtcNow.ToString("o")
            };
        }

        /// <summary>
        /// Exact retry + content-type behavior as your Eurobet helper.
        /// </summary>
        public static async Task<(bool ok, string? body)> PostJsonWithRetryAsync<T>(
            string url, T payload, int maxAttempts = 4)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            });

            Exception? lastEx = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var resp = await _http.PostAsync(url, content);
                    var respBody = await resp.Content.ReadAsStringAsync();

                    if ((int)resp.StatusCode is >= 200 and < 300)
                        return (true, respBody);

                    // retry common transients
                    if (resp.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                        or System.Net.HttpStatusCode.TooManyRequests
                        or System.Net.HttpStatusCode.BadGateway
                        or System.Net.HttpStatusCode.ServiceUnavailable
                        or System.Net.HttpStatusCode.GatewayTimeout)
                    {
                        await Task.Delay(400 * attempt * attempt);
                        continue;
                    }

                    return (false, $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}; body: {respBody}");
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    await Task.Delay(400 * attempt * attempt);
                }
            }
            return (false, $"post-failed after {maxAttempts} attempts: {lastEx?.Message}");
        }
    }
}
