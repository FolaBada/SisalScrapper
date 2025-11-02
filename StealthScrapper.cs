using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Linq;

namespace SisalScraper
{
    public class SisalScraper
    {
        private const string SessionFile = "session.json";

        // ----- SOCCER: GOAL subcategory candidates (order matters) -----
        private static readonly string[] GoalSubcategoryCandidates = new[]
        {
            "button[data-qa='classeEsito_1000002']",
            "button:has-text('TOP SCOMMESSE GOAL')",
            "button:has-text('GOAL/NO GOAL')",
            "button:has-text('GOL/NO GOL')",
            "button:has-text('ENTRAMBE SEGNANO')",
            "button:has-text('BTTS')",
            "button:has-text('GOAL')"
        };

        // ----- TENNIS: T/T Handicap Game subcategory candidates -----
        private static readonly string[] TennisTTSubcategoryCandidates = new[]
        {
            "button[data-qa='classeEsito_1127']",
            "button:has-text('T/T HANDICAP GAME')",
            "button:has-text('TT HANDICAP GAME')",
            "button:has-text('HANDICAP GAME')",
            "button:has-text('HANDICAP GIOCHI')"
        };

        public async Task RunAsync(string startSport = null)
        {
            using var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                SlowMo = 150
            });

            var context = await browser.NewContextAsync();
            await LoadSessionAsync(context);

            var page = await context.NewPageAsync();
            await InstallAggressivePopupKillerAsync(page);

            Console.WriteLine("Navigating to Sisal...");
            await page.GotoAsync("https://www.sisal.it", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await SaveSessionAsync(context);

            Console.WriteLine("Clicking 'Scommesse'...");
            await page.Locator("#dropdown1-tab1").ClickAsync();

            Console.WriteLine("Clicking 'Sport'...");
            var sportLink = page.Locator("a.card-title[aria-label='Accedi alla sezione Sport']");
            await sportLink.ClickAsync();

            Console.WriteLine("Waiting for sport slider...");
            var sportItems = page.Locator(".horizontalScroll_container__ACxu6 > div > a");
            await sportItems.First.WaitForAsync();

            // --- Baseball-only fast path (exact same flow you wanted) ---
            if (!string.IsNullOrWhiteSpace(startSport) &&
                startSport.Trim().Equals("baseball", StringComparison.OrdinalIgnoreCase))
            {
                var baseballTile = page.Locator("a#sport-link-45");
                try { await baseballTile.ScrollIntoViewIfNeededAsync(); } catch { }

                await HandleAllBlockers(page);
                await baseballTile.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);

                await WarmupFirstThreeCountriesAsync(page, "Baseball");
                await ExtractAllCountriesAsync(page, "Baseball");
                return; // stop after Baseball
            }

            // --- Ice Hockey-only fast path (IDENTICAL flow as Baseball) ---
            if (!string.IsNullOrWhiteSpace(startSport) &&
                startSport.Trim().Equals("ice hockey", StringComparison.OrdinalIgnoreCase))
            {
                var hockeyTile = page.Locator("a#sport-link-6"); // "hockey su ghiaccio"
                try { await hockeyTile.ScrollIntoViewIfNeededAsync(); } catch { }

                await HandleAllBlockers(page);
                await hockeyTile.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);
                //await page.PauseAsync();
                await WarmupFirstThreeCountriesAsync(page, "Ice Hockey");
                await ExtractAllCountriesAsync(page, "Ice Hockey");
                return; // stop after Hockey
            }

            // --- Rugby-only fast path (carousel -> warmup -> extract) ---
// --- Rugby-only fast path (carousel -> warmup -> extract) ---
// --- Rugby-only fast path (carousel -> warmup -> extract) ---
if (!string.IsNullOrWhiteSpace(startSport) &&
    startSport.Trim().Equals("rugby", StringComparison.OrdinalIgnoreCase))
{
    var rugbyTile = page.Locator("a#sport-link-12");
    if (!await rugbyTile.IsVisibleAsync(new() { Timeout = 1000 }))
        rugbyTile = page.Locator("a[href*='/scommesse-matchpoint/sport/rugby']").First;
    if (!await rugbyTile.IsVisibleAsync(new() { Timeout = 1000 }))
        rugbyTile = page.Locator(".horizontalScroll_container__ACxu6 a:has-text('rugby')").First;

    var sportItemss = page.Locator(".horizontalScroll_container__ACxu6 > div > a");
    try { await sportItemss.First.WaitForAsync(new() { Timeout = 5000 }); } catch { }
    try { await rugbyTile.ScrollIntoViewIfNeededAsync(); } catch { }

    await HandleAllBlockers(page);
    await rugbyTile.ClickAsync(new() { Force = true });
    await HandleAllBlockers(page);

    // NEW: ensure Rugby context + reveal league grid when accordions are absent
    await EnsureRugbyContextAsync(page);
    await TryOpenRugbyLeagueGridAsync(page);

    await WarmupFirstThreeCountriesAsync(page, "Rugby");
    await ExtractAllCountriesAsync(page, "Rugby");
    return; // stop after Rugby
}

            // --- American Football-only fast path (carousel -> warmup -> extract) ---
            if (!string.IsNullOrWhiteSpace(startSport) &&
                (startSport.Trim().Equals("american football", StringComparison.OrdinalIgnoreCase) ||
                 startSport.Trim().Equals("football americano", StringComparison.OrdinalIgnoreCase)))
            {
                var nflTile = page.Locator("a#sport-link-10"); // football americano
                try { await nflTile.ScrollIntoViewIfNeededAsync(); } catch { }

                await HandleAllBlockers(page);
                await nflTile.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);

                await WarmupFirstThreeCountriesAsync(page, "American Football");
                await ExtractAllCountriesAsync(page, "American Football");
                return; // stop after American Football
            }


            await InstallAnchorKillSwitchAsync(page);

            int totalTiles = await sportItems.CountAsync();
            Console.WriteLine($"Found {totalTiles} sport items in the slider.");

            // alias map for matching a requested sport
            var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Calcio"] = new[] { "calcio", "football", "soccer" },
                ["Pallacanestro"] = new[] { "pallacanestro", "basket", "basketball" },
                ["Baseball"] = new[] { "baseball" },
["Rugby"] = new[] { "rugby" },
                ["Football americano"] = new[] { "american football", "football americano", "nfl" },
                ["Hockey su ghiaccio"] = new[] { "ice hockey", "hockey su ghiaccio", "hockey" },
                ["Basket"] = new[] { "basket", "pallacanestro", "basketball" },
                ["Tennis"] = new[] { "tennis" }
            };

            string? requested = startSport?.Trim();
            bool singleSportMode = !string.IsNullOrWhiteSpace(requested);

            bool MatchesRequested(string tileText, string target)
            {
                static string Clean(string s)
                {
                    var filtered = new List<char>(s.Length);
                    foreach (var ch in s)
                        if (char.IsLetter(ch) || char.IsWhiteSpace(ch))
                            filtered.Add(char.ToLowerInvariant(ch));
                    return new string(filtered.ToArray()).Trim();
                }

                string tile = Clean(tileText);
                string want = Clean(target);

                if (tile.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                    tile.Contains(want, StringComparison.OrdinalIgnoreCase))
                    return true;

                foreach (var kv in aliases)
                {
                    if (kv.Value.Any(a => a.Equals(want, StringComparison.OrdinalIgnoreCase)))
                        if (kv.Value.Any(a => tile.Contains(a, StringComparison.OrdinalIgnoreCase)))
                            return true;

                    if (kv.Key.Equals(want, StringComparison.OrdinalIgnoreCase))
                        if (kv.Value.Any(a => tile.Contains(a, StringComparison.OrdinalIgnoreCase)) ||
                            tile.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                            return true;
                }

                return false;
            }

            int? requestedIndex = null;
            if (singleSportMode)
            {
                for (int i = 0; i < totalTiles; i++)
                {
                    var t = (await sportItems.Nth(i).InnerTextAsync()).Trim();
                    if (MatchesRequested(t, requested!))
                    {
                        requestedIndex = i;
                        break;
                    }
                }

                if (requestedIndex is null)
                {
                    Console.WriteLine($"⚠ Requested sport '{requested}' not found in slider. No-op.");
                    await browser.CloseAsync();
                    return;
                }
                Console.WriteLine($"✅ Will process only requested sport '{requested}' (tile #{requestedIndex.Value + 1}).");
            }

            int start = requestedIndex ?? 0;
            int endExclusive = requestedIndex is null ? totalTiles : requestedIndex.Value + 1;

            for (int i = start; i < endExclusive; i++)
            {
                sportItems = page.Locator(".horizontalScroll_container__ACxu6 > div > a");
                var sportElement = sportItems.Nth(i);
                var rawText = (await sportElement.InnerTextAsync()).Trim();
                var sportTextLower = rawText.ToLowerInvariant();

                if (sportTextLower.Contains("quote top") || sportTextLower.Contains("tipster"))
                {
                    Console.WriteLine($"Skipping tile: {rawText}");
                    continue;
                }

                Console.WriteLine($"\n=== Clicking sport: {rawText} ===");
                await HandleAllBlockers(page);

                await sportElement.ClickAsync(new LocatorClickOptions { Force = true });
                await HandleAllBlockers(page);
                await page.WaitForTimeoutAsync(800);

                await WarmupFirstThreeCountriesAsync(page, rawText);

                await ExtractAllCountriesAsync(page, rawText); // ⬅️ branching happens inside (tennis vs soccer)
                if (singleSportMode) break;
            }

            await browser.CloseAsync();
        }

        // =========================
        // PHASE A — WARM-UP ONLY
        // =========================
        private async Task WarmupFirstThreeCountriesAsync(IPage page, string sportName)
        {
            Console.WriteLine($"[Warm-up] Processing sport: {sportName}");
            await HandleAllBlockers(page);
            await page.PauseAsync();

            var countries = page.Locator(".FR-Accordion");
            int countryCount = await countries.CountAsync();
            int warmCount = Math.Min(3, countryCount);

            Console.WriteLine($"{countryCount} country groups found. Stabilizing first {warmCount} countries (1 pass, check→uncheck only).");

            for (int i = 0; i < warmCount; i++)
            {
                countries = page.Locator(".FR-Accordion");
                var country = countries.Nth(i);

                string countryName = await GetCountryNameSafeAsync(country);
                Console.WriteLine($"> Warm-up Country {i + 1}/{warmCount}: {countryName}");

                await HandleAllBlockers(page);

                if (!await ExpandCountryAsync(country, i, expandTimeoutMs: 8000))
                {
                    Console.WriteLine($"  ⚠ Could not expand {countryName}. Skipping warm-up for this country.");
                    continue;
                }

                await CheckAllLeaguesSequentialAsync(page, country, countryName);
                await UncheckAllLeaguesSequentialAsync(page, country, countryName);

                await CollapseCountryAsync(country);
                await page.WaitForTimeoutAsync(120);
            }

            Console.WriteLine("✅ Warm-up complete.");
        }

        // ==============================
        // PHASE B — FULL EXTRACTION RUN
        // ==============================
        // ==============================
        // PHASE B — FULL EXTRACTION RUN
        // ==============================
        private async Task ExtractAllCountriesAsync(IPage page, string sportName)
        {
            Console.WriteLine($"\n[Extraction] Processing sport: {sportName}");
            await HandleAllBlockers(page);

            var countries = page.Locator(".FR-Accordion");
            int countryCount = 0;
            try { countryCount = await countries.CountAsync(); } catch { }
            Console.WriteLine($"Total countries to extract: {countryCount}");

            // If the page shows no country accordions at all, just bail early.
            if (countryCount == 0)
            {
                // Hockey & Baseball sometimes expose a league grid instead of country accordions.
                bool isHockey = sportName.Contains("hockey", StringComparison.OrdinalIgnoreCase);
                bool isBaseball = sportName.Contains("baseball", StringComparison.OrdinalIgnoreCase);

                if (isHockey)
                {
                    int leagues = 0;
                    try { leagues = await page.Locator("a[data-qa^='manifestazione_6_']").CountAsync(); } catch { }
                    if (leagues > 0)
                    {
                        Console.WriteLine("[Hockey] No country accordions — using league-list flow.");
                        await ClickLeaguesAndExtractHockeyAsync(page, "Ice Hockey");
                        return;
                    }
                }

                if (isBaseball)
                {
                    int leagues = 0;
                    try { leagues = await page.Locator("a[data-qa^='manifestazione_45_']").CountAsync(); } catch { }
                    if (leagues > 0)
                    {
                        Console.WriteLine("[Baseball] No country accordions — using league-list flow.");
                        await ClickLeaguesAndExtractBaseballAsync(page, "Baseball");
                        return;
                    }
                }
// --- American Football fallback (sport id 10) ---
bool isAmericanFootball = sportName.Contains("american football", StringComparison.OrdinalIgnoreCase)
                       || sportName.Contains("football americano", StringComparison.OrdinalIgnoreCase);
                if (isAmericanFootball)
                {
                    int leagues = 0;
                    try { leagues = await page.Locator("a[data-qa^='manifestazione_10_']").CountAsync(); } catch { }
                    if (leagues > 0)
                    {
                        Console.WriteLine("[American Football] No country accordions — using league-list flow.");
                        await ClickLeaguesAndExtractAmericanFootballAsync(page, "American Football");
                        return;
                    }
                }
                  // --- Rugby fallback (sport id 12) ---
// --- Rugby fallback (sport id 12, plus URL-based detection) ---
// --- Rugby fallback (sport id 12) ---
bool isRugby = sportName.Contains("rugby", StringComparison.OrdinalIgnoreCase);
if (isRugby)
{
    int leagues = 0;
    try { leagues = await page.Locator("a[data-qa^='manifestazione_12_']").CountAsync(); } catch { }
    if (leagues > 0)
    {
        Console.WriteLine("[Rugby] No country accordions — opening first visible league only, 1-X-2 scrape.");
        await ClickSingleRugbyLeagueAndExtract1X2Async(page, "Rugby");
        return;
    }
}


Console.WriteLine("[Extraction] No countries and no league grid detected — nothing to extract.");
return;

            }

            // Track consecutive failures to expand; if Hockey headers won't open,
            // we switch to league-grid fallback after a few misses (same idea as Baseball).
            int consecutiveExpandFailures = 0;

            for (int i = 0; i < countryCount; i++)
            {
                // Refresh locator each iteration (virtualized DOM may re-mount nodes)
                countries = page.Locator(".FR-Accordion");
                var country = countries.Nth(i);

                string countryName = await GetCountryNameSafeAsync(country);
                Console.WriteLine($"\n▶ Extracting Country {i + 1}/{countryCount}: {countryName}");

                await HandleAllBlockers(page);

                if (!await ExpandCountryAsync(country, i, expandTimeoutMs: 9000))
                {
                    Console.WriteLine($"  ⚠ Could not expand {countryName}. Skipping.");
                    consecutiveExpandFailures++;

                    bool isHockeyy = sportName.Contains("hockey", StringComparison.OrdinalIgnoreCase);
                    if (isHockeyy && consecutiveExpandFailures >= 3)
                    {
                        // Try league-grid fallback for Hockey
                        int hockeyLeagues = 0;
                        try { hockeyLeagues = await page.Locator("a[data-qa^='manifestazione_6_']").CountAsync(); } catch { }
                        if (hockeyLeagues > 0)
                        {
                            Console.WriteLine("[Hockey] Accordion expansion unreliable — switching to league-list fallback.");
                            await ClickLeaguesAndExtractHockeyAsync(page, "Ice Hockey");
                            return;
                        }
                    }

                    // Move on to next country
                    continue;
                }

                // Reset failure counter on first successful expand
                consecutiveExpandFailures = 0;

               if (sportName.Contains("rugby", StringComparison.OrdinalIgnoreCase))
    await CheckAllLeaguesSequentialRugbyAsync(page, country, countryName);
else
    await CheckAllLeaguesSequentialAsync(page, country, countryName);

await ScrollToLoadAllFixturesAsync(page);


                // --- Branching by sport (names are case-insensitive substrings) ---
                bool isTennis = sportName.Contains("tennis", StringComparison.OrdinalIgnoreCase);
                bool isBasket = sportName.Contains("basket", StringComparison.OrdinalIgnoreCase)
                               || sportName.Contains("pallacanestro", StringComparison.OrdinalIgnoreCase);
                bool isBaseball = sportName.Contains("baseball", StringComparison.OrdinalIgnoreCase);
                bool isHockey = sportName.Contains("hockey", StringComparison.OrdinalIgnoreCase);
                bool isAmericanFootball = sportName.Contains("american football", StringComparison.OrdinalIgnoreCase)
                       || sportName.Contains("football americano", StringComparison.OrdinalIgnoreCase);
                bool isRugby = sportName.Contains("rugby", StringComparison.OrdinalIgnoreCase);


                if (isTennis)
                {
                    await ExtractTennisOddsAsync(page, sportName, countryName);
                }
                else if (isBasket)
                {
                    await ExtractBasketOddsAsync(page, sportName, countryName);
                }
                else if (isBaseball)
                {
                    await ExtractBaseballOddsAsync(page, sportName, countryName);
                }
                else if (isHockey)
                {
                    await ExtractHockeyOddsAsync(page, sportName, countryName);
                }
                else if (isAmericanFootball)
                {
                    await ExtractAmericanFootballOddsAsync(page, sportName, countryName);
                }
else if (isRugby)
{
                    await ExtractRugbyOddsAsync(page, sportName, countryName);
                    return;
}

                else
                {
                    // Default soccer path unchanged
                    await ExtractOddsAsync(page, sportName, countryName);
                }

                if (sportName.Contains("rugby", StringComparison.OrdinalIgnoreCase))
    await UncheckAllLeaguesSequentialRugbyAsync(page, country, countryName);
else
    await UncheckAllLeaguesSequentialAsync(page, country, countryName);

await CollapseCountryAsync(country);
await page.WaitForTimeoutAsync(150);

            }

            Console.WriteLine("\n✅ Extraction complete for all countries.");
        }

        // =========================
        // SOCCER (UNCHANGED)
        // =========================
        private async Task ExtractOddsAsync(IPage page, string sportName, string countryName)
        {
            Console.WriteLine($"  🧾 Extracting odds for {sportName} - {countryName} (regular → per-league GOAL sweep)…");

            var matchesList = new List<MatchData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await page.WaitForSelectorAsync(".grid_mg-row-wrapper__usTh4", new PageWaitForSelectorOptions { Timeout = 15000 });

            var matchContainers = page.Locator(".grid_mg-row-wrapper__usTh4");
            int matchCount = await matchContainers.CountAsync();
            Console.WriteLine($"  • Found {matchCount} fixtures (pre-filter).");

            // ---------- PASS A: Regular odds (default view) ----------
            for (int i = 0; i < matchCount; i++)
            {
                var match = matchContainers.Nth(i);

                // Skip live
                bool isLive = false;
                try
                {
                    if (await match.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                        isLive = true;
                    else
                    {
                        var text = "";
                        try { text = (await match.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                        if (text.Contains("live") || text.Contains("in play") || text.Contains("in-play")) isLive = true;
                    }
                }
                catch { }
                if (isLive) continue;

                // Teams
                List<string> teams;
                try
                {
                    teams = new List<string>(await match.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync());
                }
                catch { continue; }

                if (teams.Count < 2) continue;
                string home = teams[0].Trim();
                string away = teams[1].Trim();
                string teamNames = $"{home} vs {away}";
                if (!seen.Add(teamNames)) continue;

                var odds = new Dictionary<string, string>();

                // Regular markets by group index: 0 -> 1/X/2, 1 -> 1X/X2/12, 2 -> U/O
                var marketGroups = match.Locator(".grid_mg-market__gVuGf");
                int marketCount2 = await marketGroups.CountAsync();

                for (int m = 0; m < marketCount2; m++)
                {
                    var oddsButtons = marketGroups.Nth(m).Locator(".chips-commons span");
                    int oddsCount = await oddsButtons.CountAsync();

                    for (int o = 0; o < oddsCount; o++)
                    {
                        try
                        {
                            string oddValue = (await oddsButtons.Nth(o).InnerTextAsync(new LocatorInnerTextOptions { Timeout = 900 })).Trim();
                            string label = GetMarketLabel(m, o);
                            if (!string.IsNullOrWhiteSpace(oddValue))
                                odds[label] = oddValue.Replace(',', '.');
                        }
                        catch { /* best-effort */ }
                    }
                }

                matchesList.Add(new MatchData { Teams = teamNames, Odds = odds });
                Console.WriteLine($"    ⚽ {teamNames} | regular captured: {string.Join(", ", odds.Keys.Take(6))}{(odds.Count > 6 ? "…" : "")}");
            }

            // ---------- PASS B: Per-league GOAL sweep for GG/NG ----------
            var leagueBars = page.Locator(".filters-subcategory-theme");
            int leagueCount = await leagueBars.CountAsync();

            if (leagueCount == 0)
            {
                Console.WriteLine("  ⚠ No per-league subcategory bars detected; skipping league sweep.");
            }
            else
            {
                Console.WriteLine($"  • Detected {leagueCount} league sections for GOAL sweep.");

                for (int li = 0; li < leagueCount; li++)
                {
                    var bar = leagueBars.Nth(li);
                    var league = bar.Locator("xpath=ancestor::*[.//div[contains(@class,'grid_mg-row-wrapper__usTh4')]][1]");

                    bool leagueVisible = false;
                    try { leagueVisible = await league.IsVisibleAsync(new() { Timeout = 1200 }); } catch { }
                    if (!leagueVisible) league = bar;

                    await league.ScrollIntoViewIfNeededAsync();

                    bool activated = await ActivateGoalTabInLeagueAsync(league);
                    if (!activated)
                    {
                        Console.WriteLine($"    ↪ League {li + 1}/{leagueCount}: GOAL tab not found/active here.");
                        continue;
                    }

                    await ScrollToLoadAllFixturesAsync(page);
                    await WaitForCalmAsync(page);

                    foreach (var m in matchesList)
                    {
                        bool hasGG = !string.IsNullOrWhiteSpace(m.Odds.GetValueOrDefault("GG"));
                        bool hasNG = !string.IsNullOrWhiteSpace(m.Odds.GetValueOrDefault("NG"));
                        if (hasGG && hasNG) continue;

                        var parts = m.Teams.Split(" vs ", 2, StringSplitOptions.TrimEntries);
                        if (parts.Length < 2) continue;

                        var card = league.Locator($".grid_mg-row-wrapper__usTh4:has(:text('{parts[0]}')):has(:text('{parts[1]}'))").First;

                        try
                        {
                            if (await card.IsVisibleAsync(new() { Timeout = 1000 }))
                            {
                                try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
                                await AddGGNGIntoOddsForCardAsync(card, m.Odds);
                                Console.WriteLine($"    ➕ {m.Teams} | GG:{m.Odds.GetValueOrDefault("GG")} NG:{m.Odds.GetValueOrDefault("NG")}");
                            }
                        }
                        catch { /* per-card best-effort */ }
                    }
                }
            }

            // ---------- Export per country ----------
            string safeCountryName = string.Join("_", countryName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{sportName}_{safeCountryName}_odds.json";
            var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string jsonOutput = JsonSerializer.Serialize(matchesList, opts);
            await File.WriteAllTextAsync(fileName, jsonOutput);
            Console.WriteLine($"  ✅ Odds exported to {fileName} ({matchesList.Count} fixtures)\n");
        }

        private string GetMarketLabel(int marketIndex, int oddIndex)
        {
            var markets = new Dictionary<int, string[]>
            {
                { 0, new[] { "1", "X", "2" } },
                { 1, new[] { "1X", "X2", "12" } },
                { 2, new[] { "U", "O" } }
            };

            if (markets.ContainsKey(marketIndex) && oddIndex < markets[marketIndex].Length)
                return markets[marketIndex][oddIndex];

            return $"Market_{marketIndex + 1}_Odd_{oddIndex + 1}";
        }

        // =========================
        // TENNIS (NEW, SOCCER UNTOUCHED)
        // =========================
        private async Task ExtractTennisOddsAsync(IPage page, string sportName, string countryName)
        {
            Console.WriteLine($"  🎾 Extracting TENNIS odds for {sportName} - {countryName} (regular → per-league T/T HANDICAP GAME)…");

            var fixturesOut = new List<TennisFixture>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await page.WaitForSelectorAsync(".grid_mg-row-wrapper__usTh4", new PageWaitForSelectorOptions { Timeout = 15000 });
            var matchContainers = page.Locator(".grid_mg-row-wrapper__usTh4");
            int matchCount = await matchContainers.CountAsync();
            Console.WriteLine($"  • Found {matchCount} tennis fixtures (pre-filter).");

            // ---------- PASS A: Regular tennis odds (default view) ----------
            for (int i = 0; i < matchCount; i++)
            {
                var match = matchContainers.Nth(i);

                // Skip live
                bool isLive = false;
                try
                {
                    if (await match.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                        isLive = true;
                    else
                    {
                        var text = "";
                        try { text = (await match.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                        if (text.Contains("live") || text.Contains("in play") || text.Contains("in-play")) isLive = true;
                    }
                }
                catch { }
                if (isLive) continue;

                // Players / Teams
                List<string> teams;
                try
                {
                    teams = new List<string>(await match.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync());
                }
                catch { continue; }

                if (teams.Count < 2) continue;
                string p1 = teams[0].Trim();
                string p2 = teams[1].Trim();
                string teamNames = $"{p1} vs {p2}";
                if (!seen.Add(teamNames)) continue;

                var fx = new TennisFixture { Teams = teamNames };

                // 1) Moneyline 1–2 (prefer first market group's first two chips)
                await AddTennisMoneylineForCardAsync(match, fx.Odds);

                // 2) U/O Games (use currently displayed total line if present)
                await AddTennisUOForCardAsync(match, fx.Odds);

                fixturesOut.Add(fx);
                Console.WriteLine($"    🎾 {teamNames} | base captured: {string.Join(", ", fx.Odds.Keys)}");
            }

            // ---------- PASS B: Per-league T/T HANDICAP GAME ----------
            var leagueBars = page.Locator(".filters-subcategory-theme");
            int leagueCount = await leagueBars.CountAsync();

            if (leagueCount == 0)
            {
                Console.WriteLine("  ⚠ No per-league subcategory bars detected; skipping T/T Handicap sweep.");
            }
            else
            {
                Console.WriteLine($"  • Detected {leagueCount} league sections for T/T HANDICAP GAME sweep.");

                for (int li = 0; li < leagueCount; li++)
                {
                    var bar = leagueBars.Nth(li);
                    var league = bar.Locator("xpath=ancestor::*[.//div[contains(@class,'grid_mg-row-wrapper__usTh4')]][1]");

                    bool leagueVisible = false;
                    try { leagueVisible = await league.IsVisibleAsync(new() { Timeout = 1200 }); } catch { }
                    if (!leagueVisible) league = bar;

                    await league.ScrollIntoViewIfNeededAsync();

                    bool activated = await ActivateTennisTTTabInLeagueAsync(league);
                    if (!activated)
                    {
                        Console.WriteLine($"    ↪ League {li + 1}/{leagueCount}: T/T HANDICAP GAME not found/active here.");
                        continue;
                    }

                    await ScrollToLoadAllFixturesAsync(page);
                    await WaitForCalmAsync(page);

                    foreach (var fx in fixturesOut)
                    {
                        // if already have both, skip
                        if (fx.TTHandicap.ContainsKey("1") && fx.TTHandicap.ContainsKey("2")) continue;

                        var parts = fx.Teams.Split(" vs ", 2, StringSplitOptions.TrimEntries);
                        if (parts.Length < 2) continue;

                        var card = league.Locator($".grid_mg-row-wrapper__usTh4:has(:text('{parts[0]}')):has(:text('{parts[1]}'))").First;

                        try
                        {
                            if (await card.IsVisibleAsync(new() { Timeout = 1000 }))
                            {
                                try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
                                await AddTennisTTHandicapForCardAsync(card, fx.TTHandicap);
                                Console.WriteLine($"    ➕ {fx.Teams} | TT+H (1:{fx.TTHandicap.GetValueOrDefault("1")}, 2:{fx.TTHandicap.GetValueOrDefault("2")})");
                            }
                        }
                        catch { /* per-card best-effort */ }
                    }
                }
            }

            // ---------- Export per country (tennis) ----------
            string safeCountryName = string.Join("_", countryName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{sportName}_{safeCountryName}_odds.json";
            var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string jsonOutput = JsonSerializer.Serialize(fixturesOut, opts);
            await File.WriteAllTextAsync(fileName, jsonOutput);
            Console.WriteLine($"  ✅ (Tennis) Odds exported to {fileName} ({fixturesOut.Count} fixtures)\n");
        }

        // When countries are not visible and only league tiles are shown
        private async Task ClickLeaguesAndExtractBaseballAsync(IPage page, string sportName)
        {
            // league tiles look like: a[data-qa^='manifestazione_45_']
            var leagues = page.Locator("a[data-qa^='manifestazione_45_']");
            int count = 0;
            try { count = await leagues.CountAsync(); } catch { }

            Console.WriteLine($"[Baseball] Leagues visible: {count}");

            for (int i = 0; i < count; i++)
            {
                var lg = leagues.Nth(i);

                // Read a friendly league label if available
                string leagueName = "(league)";
                try
                {
                    var nameSpan = lg.Locator("span.tw-fr-text-paragraph-s").First;
                    if (await nameSpan.IsVisibleAsync(new() { Timeout = 700 }))
                        leagueName = (await nameSpan.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? leagueName;
                }
                catch { }

                Console.WriteLine($"> Opening league: {leagueName}");
                await HandleAllBlockers(page);
                try { await lg.ScrollIntoViewIfNeededAsync(); } catch { }
                await lg.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);

                // Wait briefly for fixtures to render (or no-events state)
                await Task.WhenAny(
                    page.Locator(".grid_mg-row-wrapper__usTh4").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
                    page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
                    page.WaitForTimeoutAsync(1200)
                );

                // Reuse the same extractor; pass league label as the "countryName" slot
                await ExtractBaseballOddsAsync(page, sportName, leagueName);
            }
        }

        // ---- Tennis helpers ----

        // Moneyline 1-2 for tennis (robust, default market)
        private async Task AddTennisMoneylineForCardAsync(ILocator match, IDictionary<string, object> odds)
        {
            // Prefer exact tennis data-qa tail patterns if present
            bool set = false;
            try
            {
                var one = match.Locator("button.chips-commons[data-qa*='_0_1'] span").First; // e.g., ..._20540_0_1
                var two = match.Locator("button.chips-commons[data-qa*='_0_2'] span").First;
                if (await one.IsVisibleAsync(new() { Timeout = 500 }) && await two.IsVisibleAsync(new() { Timeout = 500 }))
                {
                    var v1 = (await one.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    var v2 = (await two.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(v1)) odds["1"] = v1.Replace(',', '.');
                    if (!string.IsNullOrWhiteSpace(v2)) odds["2"] = v2.Replace(',', '.');
                    set = true;
                }
            }
            catch { }

            if (set) return;

            // Fallback: first market group → first two chips
            try
            {
                var firstGroup = match.Locator(".grid_mg-market__gVuGf").First;
                var chips = firstGroup.Locator("button.chips-commons span");
                if (await chips.CountAsync() >= 2)
                {
                    var v1 = (await chips.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    var v2 = (await chips.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(v1)) odds["1"] = v1.Replace(',', '.');
                    if (!string.IsNullOrWhiteSpace(v2)) odds["2"] = v2.Replace(',', '.');
                }
            }
            catch { }
        }
// Rugby-specific: checkboxes live under label.checkbox-theme input[type='checkbox'] inside the expanded country
private async Task CheckAllLeaguesSequentialRugbyAsync(IPage page, ILocator country, string countryName)
{
    var content = country.Locator(".FR-Accordion__content");
    var checkboxes = content.Locator("label.checkbox-theme input[type='checkbox']");
    int n = 0; try { n = await checkboxes.CountAsync(); } catch { }

    Console.WriteLine($"  {countryName} (Rugby): checking {n} leagues");

    for (int j = 0; j < n; j++)
    {
        var cb = checkboxes.Nth(j);
        try { await cb.ScrollIntoViewIfNeededAsync(); } catch { }
        await HandleAllBlockers(page);
        await EnsureCheckboxStateAsync(cb, true, j + 1, n);
        await page.WaitForTimeoutAsync(70);
    }

    if (n == 0)
        Console.WriteLine($"  ⚠ {countryName} (Rugby): no league checkboxes found under label.checkbox-theme.");
}

private async Task UncheckAllLeaguesSequentialRugbyAsync(IPage page, ILocator country, string countryName)
{
    var content = country.Locator(".FR-Accordion__content");
    var checkboxes = content.Locator("label.checkbox-theme input[type='checkbox']");
    int n = 0; try { n = await checkboxes.CountAsync(); } catch { }

    Console.WriteLine($"  {countryName} (Rugby): unchecking {n} leagues");

    for (int j = 0; j < n; j++)
    {
        var cb = checkboxes.Nth(j);
        try { await cb.ScrollIntoViewIfNeededAsync(); } catch { }
        await HandleAllBlockers(page);
        await EnsureCheckboxStateAsync(cb, false, j + 1, n);
        await page.WaitForTimeoutAsync(70);
    }

    if (n == 0)
        Console.WriteLine($"  ⚠ {countryName} (Rugby): no league checkboxes found under label.checkbox-theme.");
}

public class HockeyFixture
{
    public string Teams { get; set; } = string.Empty;

    // 1–X–2 for hockey (draw supported)
    public Dictionary<string, string> Odds { get; set; } = new()
    {
        ["1"] = "",
        ["X"] = "",
        ["2"] = ""
    };

    [JsonPropertyName("TT + Handicap")]
    public Dictionary<string, Dictionary<string, string>> TTPlusHandicap { get; set; } = new();

    [JsonPropertyName("O/U")]
    public Dictionary<string, Dictionary<string, string>> OU { get; set; } = new();
}
private async Task AddHockey1X2ForCardAsync(ILocator card, Dictionary<string, string> odds)
{
    // Preferred: explicit tails
    try
    {
        var one = card.Locator("button.chips-commons[data-qa$='_0_1'] span").First; // 1
        var draw = card.Locator("button.chips-commons[data-qa$='_0_2'] span").First; // X
        var two  = card.Locator("button.chips-commons[data-qa$='_0_3'] span").First; // 2

        if (await one.IsVisibleAsync(new() { Timeout = 700 }))
        {
            var t = (await one.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
        }
        if (await draw.IsVisibleAsync(new() { Timeout = 700 }))
        {
            var t = (await draw.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) odds["X"] = t.Replace(',', '.');
        }
        if (await two.IsVisibleAsync(new() { Timeout = 700 }))
        {
            var t = (await two.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
        }

        // If we already have at least two values, that’s good enough
        if (!string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) ||
            !string.IsNullOrWhiteSpace(odds.GetValueOrDefault("X")) ||
            !string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
            return;
    }
    catch { }

    // Fallback 1: inspect any chips matching _0_(1|2|3) and map by tail
    try
    {
        var chips = card.Locator("button.chips-commons[data-qa*='_0_']");
        int c = 0; try { c = await chips.CountAsync(); } catch { }
        for (int i = 0; i < c; i++)
        {
            string dqa = ""; string val = "";
            try { dqa = await chips.Nth(i).GetAttributeAsync("data-qa") ?? ""; } catch { }
            try {
                var span = chips.Nth(i).Locator("span").First;
                if (await span.IsVisibleAsync(new() { Timeout = 600 }))
                    val = (await span.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }
            if (string.IsNullOrWhiteSpace(val)) continue;

            var tail = dqa.Split('_').LastOrDefault();
            if (tail == "1") odds["1"] = odds.GetValueOrDefault("1") ?? val;
            else if (tail == "2") odds["X"] = odds.GetValueOrDefault("X") ?? val;
            else if (tail == "3") odds["2"] = odds.GetValueOrDefault("2") ?? val;
        }
    }
    catch { }

    // Fallback 2: DOM order 1, X, 2 (only if still missing)
    if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) ||
        string.IsNullOrWhiteSpace(odds.GetValueOrDefault("X")) ||
        string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
    {
        try
        {
            var spans = card.Locator(".grid_mg-market__gVuGf").First.Locator("button.chips-commons span");
            var vals = await spans.AllInnerTextsAsync();
            if (vals.Count >= 1 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1"))) odds["1"] = vals[0].Trim().Replace(',', '.');
            if (vals.Count >= 2 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("X"))) odds["X"] = vals[1].Trim().Replace(',', '.');
            if (vals.Count >= 3 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2"))) odds["2"] = vals[2].Trim().Replace(',', '.');
        }
        catch { }
    }
}

        // U/O Games – reads the currently displayed total (no dropdown traversal)
        // U/O Games – write nested object under Odds["U/O"]
        // Structure: "U/O": { "Line": "21.5", "U": "1.75", "O": "1.95" }
        private async Task AddTennisUOForCardAsync(ILocator match, IDictionary<string, object> odds)
        {
            string over = "", under = "", line = "";

            // (A) Prefer the displayed numeric total from the counter chip (e.g., 21.5)
            try
            {
                var totalBtn = match.Locator("button.counter-drop-chip-default-theme span").First;
                if (await totalBtn.IsVisibleAsync(new() { Timeout = 600 }))
                {
                    var t = (await totalBtn.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t))
                        line = t.Replace(',', '.');
                }
            }
            catch { }

            // (B) Read Over / Under odds via U/O (market 983) if present
            try
            {
                var oSpan = match.Locator("button.chips-commons[data-qa*='_983_'][data-qa$='_1'] span").First; // Over
                if (await oSpan.IsVisibleAsync(new() { Timeout = 500 }))
                {
                    var t = (await oSpan.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) over = t.Replace(',', '.');
                }
            }
            catch { }

            try
            {
                var uSpan = match.Locator("button.chips-commons[data-qa*='_983_'][data-qa$='_2'] span").First; // Under
                if (await uSpan.IsVisibleAsync(new() { Timeout = 500 }))
                {
                    var t = (await uSpan.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) under = t.Replace(',', '.');
                }
            }
            catch { }

            // (C) Fallback: derive the line from data-qa (e.g., _983_2150_ → 21.5)
            if (string.IsNullOrWhiteSpace(line))
            {
                try
                {
                    var overBtn = match.Locator("button.chips-commons[data-qa*='_983_'][data-qa$='_1']").First;
                    if (await overBtn.IsVisibleAsync(new() { Timeout = 500 }))
                    {
                        var dqa = await overBtn.GetAttributeAsync("data-qa");
                        if (!string.IsNullOrEmpty(dqa))
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(dqa, @"_983_(\d+)_");
                            if (m.Success && int.TryParse(m.Groups[1].Value, out var raw))
                                line = (raw / 100.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch { }
            }

            // Build nested object with whichever fields are available (no placeholders)
            var uo = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(line)) uo["Line"] = line;
            if (!string.IsNullOrWhiteSpace(under)) uo["U"] = under;
            if (!string.IsNullOrWhiteSpace(over)) uo["O"] = over;

            if (uo.Count > 0) odds["U/O"] = uo;
        }

        // Activate T/T Handicap Game inside a league container
        private async Task<bool> ActivateTennisTTTabInLeagueAsync(ILocator league)
        {
            await league.ScrollIntoViewIfNeededAsync();
            await SweepSubcategoryBarWithinAsync(league);

            bool clicked = false;
            foreach (var sel in TennisTTSubcategoryCandidates)
            {
                try
                {
                    var btn = league.Locator(sel).First;
                    if (await btn.IsVisibleAsync(new() { Timeout = 700 }))
                    {
                        await btn.ScrollIntoViewIfNeededAsync();
                        await btn.ClickAsync(new() { Force = true });
                        clicked = true;
                        break;
                    }
                }
                catch { /* try next */ }
            }

            if (!clicked) return false;

            await WaitForCalmAsync(league.Page);
            try
            {
                await Task.WhenAny(
                    league.Locator("button.chips-commons[data-qa*='_1127_'][data-qa$='_1'] span").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }),
                    league.Locator("button.chips-commons[data-qa*='_1127_'][data-qa$='_2'] span").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }),
                    league.Page.WaitForTimeoutAsync(1000)
                );
            }
            catch { }

            var hasAny = await league.Locator("button.chips-commons[data-qa*='_1127_'] span").CountAsync() > 0;
            return hasAny;
        }

        // Read T/T Handicap odds (current line) for a card; store whichever side(s) exists
        private async Task AddTennisTTHandicapForCardAsync(ILocator match, Dictionary<string, string> tt)
        {
            try
            {
                var one = match.Locator("button.chips-commons[data-qa*='_1127_'][data-qa$='_1'] span").First;
                if (await one.IsVisibleAsync(new() { Timeout = 700 }))
                {
                    var v = (await one.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) tt["1"] = v.Replace(',', '.');
                }
            }
            catch { }

            try
            {
                var two = match.Locator("button.chips-commons[data-qa*='_1127_'][data-qa$='_2'] span").First;
                if (await two.IsVisibleAsync(new() { Timeout = 700 }))
                {
                    var v = (await two.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) tt["2"] = v.Replace(',', '.');
                }
            }
            catch { }
        }

        // =========================
        // LEAGUE-SCOPED (used by both)
        // =========================
        private async Task WaitForCalmAsync(IPage page)
        {
            try
            {
                await Task.WhenAny(
                    page.Locator(".tw-fr-spinner, .loader, [data-testid='spinner']").First
                        .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3000 }),
                    page.WaitForTimeoutAsync(800)
                );
            }
            catch { }
        }
private async Task EnableRugbyClickMuzzleAsync(IPage page)
{
    // Block clicks on subcategory bars, dropdown chips, and non-1X2 chips.
    // Allow only inline 1-X-2 chips (data-qa endings _0_1, _0_2, _0_3).
    const string script = @"
(() => {
  const allow1x2 = qa => /_0_(1|2|3)$/.test(qa || '');
  const isForbiddenTarget = el => {
    // forbid: subcategory bars, cluster filter tabs, dropdown chips
    if (el.closest('.filters-subcategory-theme,.filters-subcategory,.competitionMenu-theme')) return true;
    if (el.closest('button.counter-drop-chip-default-theme')) return true; // totals/handicap chips
    const qa = el.getAttribute && el.getAttribute('data-qa');
    if (qa && !allow1x2(qa)) return true; // any non-1X2 chip
    return false;
  };
  const handler = (e) => {
    const t = e.target.closest('button,a');
    if (!t) return;
    if (isForbiddenTarget(t)) {
      e.stopImmediatePropagation();
      e.stopPropagation();
      e.preventDefault();
    }
  };
  // Install once (idempotent)
  if (!window.__RUGBY_CLICK_MUZZLE__) {
    window.__RUGBY_CLICK_MUZZLE__ = true;
    document.addEventListener('click', handler, true);
  }
})();
";
    try { await page.EvaluateAsync(script); } catch { /* best-effort */ }
}

        private async Task SweepSubcategoryBarWithinAsync(ILocator league)
        {
            var bar = league.Locator(".filters-subcategory-theme, .filters-subcategory, .competitionMenu-theme").First;
            try
            {
                if (await bar.IsVisibleAsync(new() { Timeout = 1200 }))
                {
                    await bar.ScrollIntoViewIfNeededAsync();
                    await bar.EvaluateAsync("el => { el.scrollLeft = 0; }");
                    for (int i = 0; i < 6; i++)
                    {
                        await bar.EvaluateAsync("el => { el.scrollLeft += 1200; }");
                        await league.Page.WaitForTimeoutAsync(120);
                    }
                }
            }
            catch { }
        }

        // Ensures we're truly in the Rugby section and waits briefly for the page to settle.
private async Task EnsureRugbyContextAsync(IPage page)
{
    try
    {
        await Task.WhenAny(
            page.WaitForURLAsync(u => u.ToString().Contains("/scommesse-matchpoint/sport/rugby", StringComparison.OrdinalIgnoreCase), new() { Timeout = 7000 }),
            page.WaitForTimeoutAsync(800)
        );
    }
    catch { }

    await page.WaitForTimeoutAsync(400);
    await WaitForCalmAsync(page);
}

// Try to show the Rugby "league tiles" (Competizioni/Campionati tab). Idempotent.
private async Task TryOpenRugbyLeagueGridAsync(IPage page)
{
    var rugbyLeagueTiles = page.Locator("a[data-qa^='manifestazione_12_'], a[href*='/scommesse-matchpoint/evento/rugby/']");
    try
    {
        if (await rugbyLeagueTiles.CountAsync() > 0) return; // already visible
    }
    catch { }

    // Switch away from "In evidenza" to a competitions tab if present
    var candidates = new[]
    {
        "button:has-text('Competizioni')",
        "button:has-text('Campionati')",
        "button[data-qa='cluster-filter-2']",
        "button[data-qa='cluster-filter-1']",
        "button[data-qa='cluster-filter-0']"
    };

    foreach (var sel in candidates)
    {
        try
        {
            var btn = page.Locator(sel).First;
            if (await btn.IsVisibleAsync(new() { Timeout = 800 }))
            {
                await btn.ScrollIntoViewIfNeededAsync();
                await btn.ClickAsync(new() { Force = true });
                await WaitForCalmAsync(page);

                if (await rugbyLeagueTiles.CountAsync() > 0) return;
            }
        }
        catch { /* try next */ }
    }

    // Nudge scroll to trigger lazy render
    try { await page.EvaluateAsync("() => window.scrollBy(0, Math.max(800, window.innerHeight))"); } catch { }
    await page.WaitForTimeoutAsync(300);

    try
    {
        int finalCount = await rugbyLeagueTiles.CountAsync();
        Console.WriteLine($"[Rugby] league-grid probe after tab switch: {finalCount} tiles detected.");
    }
    catch { }
}


        // When countries are not visible and only league tiles are shown (Rugby = sport id 3)

// When countries are not visible and only league tiles are shown (Rugby = sport id 3)
// When countries are not visible and only league tiles are shown (Rugby = sport id 12)
private async Task ClickLeaguesAndExtractRugbyAsync(IPage page, string sportName)
{
    var leagues = page.Locator("a[data-qa^='manifestazione_12_']");
    int count = 0;
    try { count = await leagues.CountAsync(); } catch { }

    Console.WriteLine($"[Rugby] League tiles detected: {count}");
    for (int i = 0; i < count; i++)
    {
        var lg = leagues.Nth(i);

        // Read a friendly league label if available
        string leagueName = "(league)";
        try
        {
            var nameSpan = lg.Locator("span.tw-fr-text-paragraph-s").First;
            if (await nameSpan.IsVisibleAsync(new() { Timeout = 700 }))
                leagueName = (await nameSpan.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? leagueName;
        }
        catch { }

        Console.WriteLine($"> Opening league: {leagueName}");
        await HandleAllBlockers(page);
        try { await lg.ScrollIntoViewIfNeededAsync(); } catch { }
        await lg.ClickAsync(new() { Force = true });
        await HandleAllBlockers(page);

        // Wait briefly for fixtures or an empty state; don't click any tabs/filters
        await Task.WhenAny(
            page.Locator(".grid_mg-row-wrapper__usTh4").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
            page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
            page.WaitForTimeoutAsync(1200)
        );

        // 1-X-2 only, then move on to the next league
        await ExtractRugbyOddsAsync(page, sportName, leagueName);

        // DO NOT: click handicap/other markets; just continue to next tile
    }

    // After iterating every league, simply return to caller.
    return;
}


// Helper: try to read a friendly league name from a league tile
private async Task<string> TryGetLeagueNameAsync(ILocator leagueTile)
{
    // 1) Preferred label
    try
    {
        var nameSpan = leagueTile.Locator("span.tw-fr-text-paragraph-s").First;
        if (await nameSpan.IsVisibleAsync(new() { Timeout = 500 }))
        {
            var t = (await nameSpan.InnerTextAsync(new() { Timeout = 600 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }
    }
    catch { }

    // 2) Aria labels often contain something useful
    try
    {
        var aria = await leagueTile.GetAttributeAsync("aria-label");
        if (!string.IsNullOrWhiteSpace(aria)) return aria.Trim();
    }
    catch { }

    // 3) Fallback: visible text of the tile
    try
    {
        var t = (await leagueTile.InnerTextAsync(new() { Timeout = 600 }))?.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t;
    }
    catch { }

    return "Rugby";
}

// Open ONLY the first visible rugby league and scrape 1-X-2 (no other clicks)
private async Task ClickSingleRugbyLeagueAndExtract1X2Async(IPage page, string sportName)
{
    // First visible rugby league tile
    var firstLeague = page.Locator("a[data-qa^='manifestazione_12_']:visible").First;

    if (!await firstLeague.IsVisibleAsync(new() { Timeout = 2500 }))
    {
        Console.WriteLine("[Rugby] No visible league tile found. Nothing to do.");
        return;
    }

    string leagueName = await TryGetLeagueNameAsync(firstLeague);
    Console.WriteLine($"> Opening league: {leagueName}");

    await HandleAllBlockers(page);
    try { await firstLeague.ScrollIntoViewIfNeededAsync(); } catch { }
    await firstLeague.ClickAsync(new() { Force = true });
    await HandleAllBlockers(page);

    // Wait for either fixtures or an empty state
    await Task.WhenAny(
        page.Locator(".grid_mg-row-wrapper__usTh4").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
        page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
        page.WaitForTimeoutAsync(1500)
    );

    // IMPORTANT: we only read inline 1-X-2; ExtractRugbyOddsAsync does not click anything
    await ExtractRugbyOddsAsync(page, sportName, leagueName);
}

        // When countries are not visible and only league tiles are shown (American Football = sport id 10)
        private async Task ClickLeaguesAndExtractAmericanFootballAsync(IPage page, string sportName)
        {
            var leagues = page.Locator("a[data-qa^='manifestazione_10_']");
            int count = 0;
            try { count = await leagues.CountAsync(); } catch { }

            Console.WriteLine($"[American Football] League tiles detected: {count}");
            for (int i = 0; i < count; i++)
            {
                var lg = leagues.Nth(i);
                string leagueName = "(league)";
                try
                {
                    var nameSpan = lg.Locator("span.tw-fr-text-paragraph-s").First;
                    if (await nameSpan.IsVisibleAsync(new() { Timeout = 700 }))
                        leagueName = (await nameSpan.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? leagueName;
                }
                catch { }

                Console.WriteLine($"> Opening league: {leagueName}");
                await HandleAllBlockers(page);
                try { await lg.ScrollIntoViewIfNeededAsync(); } catch { }
                await lg.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);

                await Task.WhenAny(
                    page.Locator(".grid_mg-row-wrapper__usTh4").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
                    page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 }),
                    page.WaitForTimeoutAsync(1200)
                );

                await ExtractAmericanFootballOddsAsync(page, sportName, leagueName);
            }
        }
// --- American Football DTO (moneyline + spread + totals, inline) ---
public class AmericanFootballFixture
{
    public string Teams { get; set; } = string.Empty;

    public Dictionary<string, string> Odds { get; set; } = new()
    {
        ["1"] = "",
        ["2"] = ""
    };

    [JsonPropertyName("TT + Handicap")]
    public Dictionary<string, Dictionary<string, string>> TTPlusHandicap { get; set; } = new();

    [JsonPropertyName("O/U")]
    public Dictionary<string, Dictionary<string, string>> OU { get; set; } = new();
}
private async Task ExtractAmericanFootballOddsAsync(IPage page, string sportName, string countryOrLeague)
{
    Console.WriteLine($"  🏈 [American Football] Extracting odds for {sportName} - {countryOrLeague}...");

    var fixturesOut = new List<AmericanFootballFixture>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var cards = page.Locator(".grid_mg-row-wrapper__usTh4");

    // Probe briefly (some groups may be empty)
    int matchCount = 0;
    for (int spin = 0; spin < 10; spin++)
    {
        try { matchCount = await cards.CountAsync(); } catch { matchCount = 0; }
        if (matchCount > 0) break;

        try
        {
            var empty = page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')");
            if (await empty.CountAsync() > 0) break;
        }
        catch { }

        await page.WaitForTimeoutAsync(300);
    }

    if (matchCount == 0)
    {
        Console.WriteLine($"  • [American Football] No fixtures visible for {countryOrLeague}; skipping.");
        return;
    }

    Console.WriteLine($"  • [American Football] Found {matchCount} fixtures (pre-filter).");

    for (int i = 0; i < matchCount; i++)
    {
        var card = cards.Nth(i);

        // Skip live/in-play
        bool isLive = false;
        try
        {
            if (await card.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                isLive = true;
            else
            {
                var t = "";
                try { t = (await card.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                if (t.Contains("live") || t.Contains("in play") || t.Contains("in-play")) isLive = true;
            }
        }
        catch { }
        if (isLive) continue;

        // Teams
        List<string> teams;
        try { teams = new List<string>(await card.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync()); }
        catch { continue; }
        if (teams.Count < 2) continue;

        string home = teams[0].Trim();
        string away = teams[1].Trim();
        string key = $"{home} vs {away}";
        if (!seen.Add(key)) continue;

        // Ensure virtualization renders odds
        try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
        try { await page.Mouse.WheelAsync(0, 400); } catch { }
        await page.WaitForTimeoutAsync(80);

        var fx = new AmericanFootballFixture { Teams = key };

        // 1–2 moneyline (inline)
        await AddAmericanFootball12ForCardAsync(card, fx.Odds);

        // Inline spreads and totals (no dropdowns, no header tabs)
        await ReadAFInlineSpreadForCardAsync(card, fx.TTPlusHandicap);
        await ReadAFInlineTotalsForCardAsync(card, fx.OU);

        fixturesOut.Add(fx);
        Console.WriteLine($"    🏈 {key} | 1:{fx.Odds.GetValueOrDefault("1")} 2:{fx.Odds.GetValueOrDefault("2")} | TT+H:{fx.TTPlusHandicap.Count} | O/U:{fx.OU.Count}");
    }

    // Export per country/league
    string safe = string.Join("_", countryOrLeague.Split(Path.GetInvalidFileNameChars()));
    string fileName = $"{sportName}_{safe}_odds.json";
    var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    string json = JsonSerializer.Serialize(fixturesOut, opts);
    await File.WriteAllTextAsync(fileName, json);
    Console.WriteLine($"  ✅ [American Football] Odds exported to {fileName} ({fixturesOut.Count} fixtures)\n");
}
// 1–2 moneyline for American Football (inline chips)
private async Task AddAmericanFootball12ForCardAsync(ILocator match, Dictionary<string, string> odds)
{
    try
    {
        var one = match.Locator("button.chips-commons[data-qa$='_0_1'] span").First;
        var two = match.Locator("button.chips-commons[data-qa$='_0_2'] span").First;

        if (await one.IsVisibleAsync(new() { Timeout = 800 }))
        {
            var t = (await one.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
        }
        if (await two.IsVisibleAsync(new() { Timeout = 800 }))
        {
            var t = (await two.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
        }
    }
    catch { }

    // Fallback to first group → first two chips
    if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) || string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
    {
        try
        {
            var chips = match.Locator(".grid_mg-market__gVuGf").First.Locator("button.chips-commons span");
            if (await chips.CountAsync() >= 2)
            {
                if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")))
                {
                    var t = (await chips.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
                }
                if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
                {
                    var t = (await chips.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
                }
            }
        }
        catch { }
    }
}

// Inline Spread reader: look for market blocks with "HANDICAP", "SPREAD", or "TESTA A TESTA HANDICAP"
// Inline Spread (TT + Handicap) for American Football using your exact selectors.
// Looks for cells that have line in ".counter-drop-chip-default-theme span"
// and odds in ".chips-commons" with data-qa "..._26_..._(1|2)" (26 = Handicap).
private async Task ReadAFInlineSpreadForCardAsync(ILocator card, Dictionary<string, Dictionary<string, string>> tt)
{
    try
    {
        // Each market row/cell (spread or totals)
        var cells = card.Locator(".marketAttributeSelectorCellCommon_mg-market-attribute-selector-cell__ISAm1");
        int count = 0;
        try { count = await cells.CountAsync(); } catch { }

        for (int i = 0; i < count; i++)
        {
            var cell = cells.Nth(i);

            // 1) Read the line (e.g., "-2.5")
            string line = "";
            try
            {
                var lineSpan = cell.Locator(".counter-drop-chip-default-theme span").First;
                if (await lineSpan.IsVisibleAsync(new() { Timeout = 700 }))
                    line = (await lineSpan.InnerTextAsync(new() { Timeout = 800 }))?.Trim().Replace(',', '.') ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(line)) continue;

            // 2) Confirm this cell is a SPREAD via data-qa marker (_26_ = Handicap)
            // We peek the first chips-commons in the cell and inspect its data-qa
            var chips = cell.Locator("button.chips-commons");
            int chipCount = 0;
            try { chipCount = await chips.CountAsync(); } catch { }

            if (chipCount < 2) continue; // need at least two outcome chips

            string dqa0 = "", dqa1 = "";
            try { dqa0 = await chips.Nth(0).GetAttributeAsync("data-qa") ?? ""; } catch { }
            try { dqa1 = await chips.Nth(1).GetAttributeAsync("data-qa") ?? ""; } catch { }

            bool looksSpread = (dqa0.Contains("_26_") || dqa1.Contains("_26_")); // 26 = Handicap
            if (!looksSpread) continue; // not a spread cell; skip (Totals reader will handle it)

            // 3) Read the odds from the two chips; map data-qa tail ..._1 -> "1", ..._2 -> "2"
            string odd1 = "", odd2 = "";

            try
            {
                var span0 = chips.Nth(0).Locator("span").First;
                if (await span0.IsVisibleAsync(new() { Timeout = 600 }))
                    odd1 = (await span0.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }

            try
            {
                var span1 = chips.Nth(1).Locator("span").First;
                if (await span1.IsVisibleAsync(new() { Timeout = 600 }))
                    odd2 = (await span1.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }

            // Ensure we place them under the line key
            if (!string.IsNullOrWhiteSpace(odd1) || !string.IsNullOrWhiteSpace(odd2))
            {
                // Initialize line bucket
                if (!tt.TryGetValue(line, out var obj))
                {
                    obj = new Dictionary<string, string>();
                    tt[line] = obj;
                }

                // Use the _..._(1|2) tail to map if available; otherwise fallback by order
                string tail0 = dqa0.Split('_').LastOrDefault() ?? "";
                string tail1 = dqa1.Split('_').LastOrDefault() ?? "";

                if (tail0 == "1" && !string.IsNullOrWhiteSpace(odd1)) obj["1"] = odd1;
                if (tail0 == "2" && !string.IsNullOrWhiteSpace(odd1)) obj["2"] = odd1;

                if (tail1 == "1" && !string.IsNullOrWhiteSpace(odd2)) obj["1"] = odd2;
                if (tail1 == "2" && !string.IsNullOrWhiteSpace(odd2)) obj["2"] = odd2;

                // Fallback mapping by order if tails weren’t readable
                if (!obj.ContainsKey("1") && !string.IsNullOrWhiteSpace(odd1)) obj["1"] = odd1;
                if (!obj.ContainsKey("2") && !string.IsNullOrWhiteSpace(odd2)) obj["2"] = odd2;
            }
        }
    }
    catch { /* cell-level best-effort */ }
}


// Inline Totals (O/U) for American Football using your exact selectors.
// Looks for cells that have line in ".counter-drop-chip-default-theme span"
// and odds in ".chips-commons" with data-qa "..._14863_..._(1|2)" (14863 = Totals).
private async Task ReadAFInlineTotalsForCardAsync(ILocator card, Dictionary<string, Dictionary<string, string>> ou)
{
    try
    {
        var cells = card.Locator(".marketAttributeSelectorCellCommon_mg-market-attribute-selector-cell__ISAm1");
        int count = 0;
        try { count = await cells.CountAsync(); } catch { }

        for (int i = 0; i < count; i++)
        {
            var cell = cells.Nth(i);

            // 1) Read the line (e.g., "51.5")
            string line = "";
            try
            {
                var lineSpan = cell.Locator(".counter-drop-chip-default-theme span").First;
                if (await lineSpan.IsVisibleAsync(new() { Timeout = 700 }))
                    line = (await lineSpan.InnerTextAsync(new() { Timeout = 800 }))?.Trim().Replace(',', '.') ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(line)) continue;

            // 2) Confirm this cell is TOTALS via data-qa marker (_14863_ = Totals)
            var chips = cell.Locator("button.chips-commons");
            int chipCount = 0;
            try { chipCount = await chips.CountAsync(); } catch { }

            if (chipCount < 2) continue;

            string dqa0 = "", dqa1 = "";
            try { dqa0 = await chips.Nth(0).GetAttributeAsync("data-qa") ?? ""; } catch { }
            try { dqa1 = await chips.Nth(1).GetAttributeAsync("data-qa") ?? ""; } catch { }

            bool looksTotals = (dqa0.Contains("_14863_") || dqa1.Contains("_14863_")); // 14863 = O/U
            if (!looksTotals) continue; // not a totals cell; spread reader will handle it

            // 3) Read the two odds
            string v0 = "", v1 = "";
            try
            {
                var s0 = chips.Nth(0).Locator("span").First;
                if (await s0.IsVisibleAsync(new() { Timeout = 600 }))
                    v0 = (await s0.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }

            try
            {
                var s1 = chips.Nth(1).Locator("span").First;
                if (await s1.IsVisibleAsync(new() { Timeout = 600 }))
                    v1 = (await s1.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }

            if (string.IsNullOrWhiteSpace(v0) && string.IsNullOrWhiteSpace(v1)) continue;

            // 4) Map data-qa tail "..._(1|2)" to OVER/UNDER.
            // Convention we see on Sisal: _1 -> OVER, _2 -> UNDER.
            string tail0 = (dqa0.Split('_').LastOrDefault() ?? "").Trim();
            string tail1 = (dqa1.Split('_').LastOrDefault() ?? "").Trim();

            if (!ou.TryGetValue(line, out var obj))
            {
                obj = new Dictionary<string, string>();
                ou[line] = obj;
            }

            if (tail0 == "1" && !string.IsNullOrWhiteSpace(v0)) obj["OVER"] = v0;
            if (tail0 == "2" && !string.IsNullOrWhiteSpace(v0)) obj["UNDER"] = v0;

            if (tail1 == "1" && !string.IsNullOrWhiteSpace(v1)) obj["OVER"] = v1;
            if (tail1 == "2" && !string.IsNullOrWhiteSpace(v1)) obj["UNDER"] = v1;

            // Fallback if tails weren’t readable: first chip -> OVER, second -> UNDER
            if (!obj.ContainsKey("OVER")  && !string.IsNullOrWhiteSpace(v0)) obj["OVER"]  = v0;
            if (!obj.ContainsKey("UNDER") && !string.IsNullOrWhiteSpace(v1)) obj["UNDER"] = v1;
        }
    }
    catch { /* cell-level best-effort */ }
}

        private async Task<bool> ActivateGoalTabInLeagueAsync(ILocator league)
        {
            await league.ScrollIntoViewIfNeededAsync();
            await SweepSubcategoryBarWithinAsync(league);

            bool clicked = false;
            foreach (var sel in GoalSubcategoryCandidates)
            {
                try
                {
                    var btn = league.Locator(sel).First;
                    if (await btn.IsVisibleAsync(new() { Timeout = 700 }))
                    {
                        await btn.ScrollIntoViewIfNeededAsync();
                        await btn.ClickAsync(new() { Force = true });
                        clicked = true;
                        break;
                    }
                }
                catch { /* try next */ }
            }

            if (!clicked) return false;

            await WaitForCalmAsync(league.Page);
            try
            {
                await Task.WhenAny(
                    league.Locator("[data-qa*='_18_0_1'] span").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }),
                    league.Locator("[data-qa*='_18_0_2'] span").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }),
                    league.Page.WaitForTimeoutAsync(1000)
                );
            }
            catch { }

            var hasGG = await league.Locator("[data-qa*='_18_0_1'] span").CountAsync() > 0;
            var hasNG = await league.Locator("[data-qa*='_18_0_2'] span").CountAsync() > 0;
            return hasGG || hasNG;
        }
        // When no country accordion exists for hockey, click visible league tiles and extract.
        private async Task ClickLeaguesAndExtractHockeyAsync(IPage page, string sportName)
        {
            // Hockey sport id is 6 (see id="sport-link-6"); leagues use data-qa="manifestazione_6_*"
            var leagues = page.Locator("a[data-qa^='manifestazione_6_']");
            int count = 0;
            try { count = await leagues.CountAsync(); } catch { }

            Console.WriteLine($"[Hockey] League tiles detected: {count}");
            for (int i = 0; i < count; i++)
            {
                var league = leagues.Nth(i);
                string leagueName = "(league)";
                try
                {
                    var label = league.Locator("span.tw-fr-text-paragraph-s").First;
                    if (await label.IsVisibleAsync(new() { Timeout = 700 }))
                        leagueName = (await label.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? leagueName;
                }
                catch { }

                await HandleAllBlockers(page);
                try { await league.ScrollIntoViewIfNeededAsync(); } catch { }
                await league.ClickAsync(new() { Force = true });
                await HandleAllBlockers(page);
                await page.WaitForTimeoutAsync(400);

                await ScrollToLoadAllFixturesAsync(page);
                await ExtractHockeyOddsAsync(page, sportName, leagueName);
            }
        }

        private async Task ReadHockeyOUDropdownsForCardAsync(ILocator match, Dictionary<string, Dictionary<string, string>> ou)
        {
            try
            {
                var toggles = match.Locator(":scope button.counter-drop-chip-default-theme span");
                int count = await toggles.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    string raw = "";
                    try { raw = (await toggles.Nth(i).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? ""; }
                    catch { continue; }

                    var canon = raw.Replace(',', '.');

                    // Hockey totals are small (roughly 2.5–12.5). Treat only those as O/U.
                    if (!double.TryParse(canon, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        continue;
                    if (v < 2 || v > 15)
                        continue;

                    // Open the dropdown for this chip
                    var button = toggles.Nth(i).Locator("..");
                    try { await button.ScrollIntoViewIfNeededAsync(); } catch { }
                    await button.ClickAsync(new() { Force = true });

                    var panel = match.Page.Locator(".drop-list-chips-select-option-container-theme:visible").First;
                    try { await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2500 }); }
                    catch { continue; }

                    var rows = panel.Locator("div.select-selected-on-hover-drop-list-chips-theme");
                    int n = await rows.CountAsync();

                    for (int r = 0; r < n; r++)
                    {
                        try
                        {
                            var cols = rows.Nth(r).Locator("div.tw-fr-w-full span");
                            if (await cols.CountAsync() < 3) continue;

                            string total = (await cols.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim()?.Replace(',', '.') ?? "";
                            string over = (await cols.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim()?.Replace(',', '.') ?? "";
                            string under = (await cols.Nth(2).InnerTextAsync(new() { Timeout = 700 }))?.Trim()?.Replace(',', '.') ?? "";

                            if (string.IsNullOrWhiteSpace(total) || string.IsNullOrWhiteSpace(over) || string.IsNullOrWhiteSpace(under))
                                continue;

                            ou[total] = new Dictionary<string, string>
                            {
                                ["OVER"] = over,
                                ["UNDER"] = under
                            };
                        }
                        catch { /* row best-effort */ }
                    }

                    // Close and stop after the first valid small-number chip
                    try { await button.ClickAsync(new() { Force = true }); } catch { }
                    await match.Page.WaitForTimeoutAsync(100);
                    break;
                }
            }
            catch { /* best-effort */ }
        }
        // Activate the "HANDICAP" header tab for Hockey (idempotent)
        private async Task<bool> ActivateHockeyHandicapHeaderAsync(IPage page)
        {
            try
            {
                var btn = page.Locator("button[data-qa='cluster-filter-215']"); // HANDICAP
                                                                                // If it's visible, click it (safe to click multiple times)
                if (await btn.IsVisibleAsync(new() { Timeout = 1200 }))
                {
                    try { await btn.ScrollIntoViewIfNeededAsync(); } catch { }
                    await btn.ClickAsync(new() { Force = true });
                    // Wait a moment for market switch
                    await Task.WhenAny(
                        page.Locator(":text-matches('HANDICAP','i')").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1500 }),
                        page.WaitForTimeoutAsync(400)
                    );
                    return true;
                }
            }
            catch { }
            return false;
        }
        // Read Handicap rows shown under the HANDICAP tab for a single fixture card.
        // Output shape (per line): 
        //   "TT + Handicap": {
        //       "-1": { "1": "1.33", "X": "7.50", "2": "4.25" },
        //       "1":  { "1": "2.40", "X": "6.75", "2": "1.80" },
        //       "2":  { "1": "3.60", "X": "6.25", "2": "1.47" }
        //   }
        private async Task ReadHockeyHeaderHandicapForCardAsync(ILocator card, Dictionary<string, Dictionary<string, string>> tt)
        {
            try
            {
                // Each handicap market block has a title like "1X2 HANDICAP (-1)" / "1X2 HANDICAP (1)" / etc.
                var markets = card.Locator(".template_mg-market-attribute__Y16SU");
                int mcount = 0;
                try { mcount = await markets.CountAsync(); } catch { }

                for (int i = 0; i < mcount; i++)
                {
                    var mkt = markets.Nth(i);

                    // Get market title text
                    string title = "";
                    try
                    {
                        title = (await mkt
                            .Locator(".mg-market-attribute-desc .tw-fr-font-primary")
                            .First.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? "";
                    }
                    catch { }

                    // Only process 1X2 HANDICAP markets
                    if (string.IsNullOrWhiteSpace(title) || !title.Contains("HANDICAP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Extract the line inside parentheses, e.g. "(-1)" => "-1", "(1)" => "1", "(2)" => "2"
                    string line = "";
                    var m = System.Text.RegularExpressions.Regex.Match(title, @"HANDICAP\s*\(\s*([+\-]?\d+)\s*\)", RegexOptions.IgnoreCase);
                    if (m.Success) line = m.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Find the three 1X2 buttons within this market block (data-qa ends with _1, _2, _3)
                    // Usually order corresponds to 1, 2, 3 but labels are not printed; we map to "1","X","2" reading spans in order.
                    // In the sample HTML, the three chips are  _..._1  _..._2  _..._3; by UI they map to 1, 2, X or 1, X, 2 depending on template.
                    // The safe approach: read all three chips in DOM order and map as: first->"1", second->"2", third->"X"
                    // BUT since the title is explicitly "1X2 HANDICAP", the visual order is typically "1", "2", "X" or "1", "X", "2".
                    // We’ll detect "X" by looking at the data-qa tail (_..._3 usually means draw).
                    var chips = mkt.Locator("button.chips-commons span");
                    int c = await chips.CountAsync();
                    if (c < 3) continue;

                    // Try to pull by ending code: _1, _2, _3 (draw = _3)
                    string odd1 = "", oddX = "", odd2 = "";
                    try
                    {
                        var one = mkt.Locator("button.chips-commons[data-qa$='_1'] span").First;
                        if (await one.IsVisibleAsync(new() { Timeout = 400 }))
                            odd1 = (await one.InnerTextAsync(new() { Timeout = 500 }))?.Trim().Replace(',', '.') ?? "";
                    }
                    catch { }
                    try
                    {
                        var two = mkt.Locator("button.chips-commons[data-qa$='_2'] span").First;
                        if (await two.IsVisibleAsync(new() { Timeout = 400 }))
                            odd2 = (await two.InnerTextAsync(new() { Timeout = 500 }))?.Trim().Replace(',', '.') ?? "";
                    }
                    catch { }
                    try
                    {
                        var draw = mkt.Locator("button.chips-commons[data-qa$='_3'] span").First;
                        if (await draw.IsVisibleAsync(new() { Timeout = 400 }))
                            oddX = (await draw.InnerTextAsync(new() { Timeout = 500 }))?.Trim().Replace(',', '.') ?? "";
                    }
                    catch { }

                    // Build the object only when we have at least two of them; prefer all three if present
                    var lineObj = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(odd1)) lineObj["1"] = odd1;
                    if (!string.IsNullOrWhiteSpace(oddX)) lineObj["X"] = oddX; // 1X2-specific
                    if (!string.IsNullOrWhiteSpace(odd2)) lineObj["2"] = odd2;

                    if (lineObj.Count > 0)
                        tt[line] = lineObj;
                }
            }
            catch { /* best-effort per card */ }
        }

        // =========================
        // HOCKEY (moneyline + TT + Handicap + O/U) — same JSON as basketball
        // =========================
     // REPLACE the whole ExtractHockeyOddsAsync with this version
private async Task ExtractHockeyOddsAsync(IPage page, string sportName, string countryOrLeague)
{
    Console.WriteLine($"  🧊 [Hockey] Extracting odds for {sportName} - {countryOrLeague}...");

    var fixturesOut = new List<HockeyFixture>(); // now HockeyFixture (1-X-2)
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var matchContainers = page.Locator(".grid_mg-row-wrapper__usTh4");

    // light probe (some leagues may be empty)
    int matchCount = 0;
    for (int spin = 0; spin < 10; spin++)
    {
        try { matchCount = await matchContainers.CountAsync(); } catch { matchCount = 0; }
        if (matchCount > 0) break;

        try
        {
            var emptyState = page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')");
            if (await emptyState.CountAsync() > 0) break;
        }
        catch { }
        await page.WaitForTimeoutAsync(300);
    }

    if (matchCount == 0)
    {
        Console.WriteLine($"  • [Hockey] No fixtures visible for {countryOrLeague}; skipping.");
        return;
    }

    Console.WriteLine($"  • [Hockey] Found {matchCount} fixtures (pre-filter).");

    for (int i = 0; i < matchCount; i++)
    {
        var card = matchContainers.Nth(i);

        // skip live
        bool isLive = false;
        try
        {
            if (await card.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                isLive = true;
            else
            {
                var t = "";
                try { t = (await card.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                if (t.Contains("live") || t.Contains("in play") || t.Contains("in-play")) isLive = true;
            }
        }
        catch { }
        if (isLive) continue;

        // teams
        List<string> teams;
        try { teams = new List<string>(await card.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync()); }
        catch { continue; }
        if (teams.Count < 2) continue;

        string home = teams[0].Trim();
        string away = teams[1].Trim();
        string key = $"{home} vs {away}";
        if (!seen.Add(key)) continue;

        try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
        try { await page.Mouse.WheelAsync(0, 500); } catch { }
        await page.WaitForTimeoutAsync(100);

        var fx = new HockeyFixture { Teams = key };

        // >>> 1-X-2 for hockey, mapping _0_1 => "1", _0_2 => "X", _0_3 => "2"
        await AddHockey1X2ForCardAsync(card, fx.Odds);

        // O/U totals (dropdown on small-number chip, already implemented)
        await ReadHockeyOUDropdownsForCardAsync(card, fx.OU);

        // 1X2 HANDICAP lines (via header tab), keep as you already do
        await ActivateHockeyHandicapHeaderAsync(page);
        await ReadHockeyHeaderHandicapForCardAsync(card, fx.TTPlusHandicap);

        fixturesOut.Add(fx);
        Console.WriteLine($"    🧊 {key} | 1:{fx.Odds.GetValueOrDefault("1")} X:{fx.Odds.GetValueOrDefault("X")} 2:{fx.Odds.GetValueOrDefault("2")} | TT+H:{fx.TTPlusHandicap.Count} | O/U:{fx.OU.Count}");
    }

    // export
    string safeCountry = string.Join("_", countryOrLeague.Split(Path.GetInvalidFileNameChars()));
    string fileName = $"{sportName}_{safeCountry}_odds.json";
    var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    string jsonOutput = JsonSerializer.Serialize(fixturesOut, opts);
    await File.WriteAllTextAsync(fileName, jsonOutput);
    Console.WriteLine($"  ✅ [Hockey] Odds exported to {fileName} ({fixturesOut.Count} fixtures)\n");
}


        private async Task AddGGNGIntoOddsForCardAsync(ILocator match, Dictionary<string, string> odds)
        {
            string gg = "", ng = "";

            try
            {
                var ggSpan = match.Locator("[data-qa*='_18_0_1'] span, [data-qa*='_18_0_1'] .tw-fr-font-bold").First;
                if (await ggSpan.IsVisibleAsync(new() { Timeout = 800 }))
                {
                    var t = (await ggSpan.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) gg = t.Replace(',', '.');
                }
            }
            catch { }
            try
            {
                var ngSpan = match.Locator("[data-qa*='_18_0_2'] span, [data-qa*='_18_0_2'] .tw-fr-font-bold").First;
                if (await ngSpan.IsVisibleAsync(new() { Timeout = 800 }))
                {
                    var t = (await ngSpan.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) ng = t.Replace(',', '.');
                }
            }
            catch { }

            // fallbacks omitted for brevity; soccer path unchanged enough
            if (!string.IsNullOrWhiteSpace(gg)) odds["GG"] = gg;
            if (!string.IsNullOrWhiteSpace(ng)) odds["NG"] = ng;
        }

        // ==========================================
        // CHECK/UNCHECK LEAGUES + VIRTUALIZATION
        // ==========================================
        private async Task CheckAllLeaguesSequentialAsync(IPage page, ILocator country, string countryName)
        {
            var rows = country.Locator(".FR-Accordion__content ul li");
            int n = await rows.CountAsync();
            Console.WriteLine($"  {countryName}: checking {n} leagues");

            for (int j = 0; j < n; j++)
            {
                var row = rows.Nth(j);
                var cb = row.Locator("input[type='checkbox']");
                try { await row.ScrollIntoViewIfNeededAsync(); } catch { }
                await HandleAllBlockers(page);
                await EnsureCheckboxStateAsync(cb, true, j + 1, n);
                await page.WaitForTimeoutAsync(70);
            }
        }



        private async Task UncheckAllLeaguesSequentialAsync(IPage page, ILocator country, string countryName)
        {
            var rows = country.Locator(".FR-Accordion__content ul li");
            int n = await rows.CountAsync();
            Console.WriteLine($"  {countryName}: unchecking {n} leagues");

            for (int j = 0; j < n; j++)
            {
                var row = rows.Nth(j);
                var cb = row.Locator("input[type='checkbox']");
                try { await row.ScrollIntoViewIfNeededAsync(); } catch { }
                await HandleAllBlockers(page);
                await EnsureCheckboxStateAsync(cb, false, j + 1, n);
                await page.WaitForTimeoutAsync(70);
            }
        }

        private async Task ScrollToLoadAllFixturesAsync(IPage page, int maxRounds = 30, int settleMs = 200)
        {
            Console.WriteLine("  🔽 Scrolling to load all fixtures...");
            int stableRounds = 0;
            int lastCount = -1;
            for (int round = 1; round <= maxRounds; round++)
            {
                var rows = page.Locator(".grid_mg-row-wrapper__usTh4");
                int count = 0;
                try { count = await rows.CountAsync(); } catch { }

                Console.WriteLine($"    • round {round}: {count} fixtures visible");

                if (count <= 0)
                {
                    try { await page.Mouse.WheelAsync(0, 600); } catch { }
                    await page.WaitForTimeoutAsync(settleMs);
                    continue;
                }

                if (count == lastCount) stableRounds++; else stableRounds = 0;
                if (stableRounds >= 2)
                {
                    Console.WriteLine("  ✅ Fixture list stabilized.");
                    break;
                }

                lastCount = count;

                try
                {
                    await page.EvaluateAsync(@"() => { window.scrollBy(0, Math.max(600, window.innerHeight * 0.8)); }");
                }
                catch { }
                try { await page.Mouse.WheelAsync(0, 800); } catch { }

                await page.WaitForTimeoutAsync(settleMs);
            }
        }

        private async Task<string> GetCountryNameSafeAsync(ILocator country)
        {
            try
            {
                return (await country
                    .Locator(".FR-Accordion__header span.tw-fr-text-paragraph-s")
                    .InnerTextAsync(new() { Timeout = 2500 }))
                    .Trim();
            }
            catch
            {
                return "(unknown)";
            }
        }

        // =========================
        // BASEBALL (moneyline + Run Line + O/U totals)
        // =========================
        private async Task ExtractBaseballOddsAsync(IPage page, string sportName, string countryName)
        {
            Console.WriteLine($"  ⚾ [Baseball] Extracting odds for {sportName} - {countryName}…");

            var fixturesOut = new List<BaseballFixture>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await page.WaitForSelectorAsync(".grid_mg-row-wrapper__usTh4", new PageWaitForSelectorOptions { Timeout = 15000 });
            var cards = page.Locator(".grid_mg-row-wrapper__usTh4");
            int n = await cards.CountAsync();
            Console.WriteLine($"  • [Baseball] Found {n} fixtures (pre-filter).");

            for (int i = 0; i < n; i++)
            {
                var card = cards.Nth(i);

                // Skip live
                bool isLive = false;
                try
                {
                    if (await card.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0) isLive = true;
                    else
                    {
                        var t = "";
                        try { t = (await card.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                        if (t.Contains("live") || t.Contains("in play") || t.Contains("in-play")) isLive = true;
                    }
                }
                catch { }
                if (isLive) continue;

                // Teams
                List<string> teams;
                try { teams = new List<string>(await card.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync()); }
                catch { continue; }
                if (teams.Count < 2) continue;

                string home = teams[0].Trim();
                string away = teams[1].Trim();
                string matchKey = $"{home} vs {away}";
                if (!seen.Add(matchKey)) continue;

                // Ensure virtualization renders odds
                try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
                try { await page.Mouse.WheelAsync(0, 400); } catch { }
                await page.WaitForTimeoutAsync(80);

                var fx = new BaseballFixture { Teams = matchKey };

                // 1–2 moneyline
                await AddBaseball12ForCardAsync(card, fx.Odds);

                // Read both dropdown types; baseball totals are small (≈5.5–12.5), run line ≈±1.5
                await ReadBaseballDropdownsAsync(card, fx);

                fixturesOut.Add(fx);
                Console.WriteLine($"    ⚾ {matchKey} | 1:{fx.Odds.GetValueOrDefault("1")} 2:{fx.Odds.GetValueOrDefault("2")} | TT+H:{fx.TTPlusHandicap.Count} | O/U:{fx.OU.Count}");
            }

            // Export per country (baseball)
            string safeCountryName = string.Join("_", countryName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{sportName}_{safeCountryName}_odds.json";
            var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string json = JsonSerializer.Serialize(fixturesOut, opts);
            await File.WriteAllTextAsync(fileName, json);
            Console.WriteLine($"  ✅ [Baseball] Odds exported to {fileName} ({fixturesOut.Count} fixtures)\n");
        }

        // 1–2 moneyline (same pattern as basket/tennis)
        private async Task AddBaseball12ForCardAsync(ILocator match, Dictionary<string, string> odds)
        {
            try
            {
                var one = match.Locator("button.chips-commons[data-qa$='_0_1'] span").First;
                var two = match.Locator("button.chips-commons[data-qa$='_0_2'] span").First;

                if (await one.IsVisibleAsync(new() { Timeout = 700 }))
                {
                    var t = (await one.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
                }
                if (await two.IsVisibleAsync(new() { Timeout = 700 }))
                {
                    var t = (await two.InnerTextAsync(new() { Timeout = 800 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
                }
            }
            catch { }

            // Fallback to first group first two chips
            if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) || string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
            {
                try
                {
                    var chips = match.Locator(".grid_mg-market__gVuGf").First.Locator("button.chips-commons span");
                    if (await chips.CountAsync() >= 2)
                    {
                        if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")))
                        {
                            var t = (await chips.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
                        }
                        if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
                        {
                            var t = (await chips.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
                        }
                    }
                }
                catch { }
            }
        }

        // Open any dropdown chips and classify rows as Run Line vs Totals using heuristics
        private async Task ReadBaseballDropdownsAsync(ILocator match, BaseballFixture fx)
        {
            try
            {
                var toggles = match.Locator(":scope button.counter-drop-chip-default-theme span");
                int count = await toggles.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    string chipText = "";
                    try { chipText = (await toggles.Nth(i).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? ""; }
                    catch { continue; }

                    // Click chip to open panel
                    var button = toggles.Nth(i).Locator("..");
                    try { await button.ScrollIntoViewIfNeededAsync(); } catch { }
                    await button.ClickAsync(new() { Force = true });

                    var panel = match.Page.Locator(".drop-list-chips-select-option-container-theme:visible").First;
                    try { await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2500 }); }
                    catch { continue; }

                    var rows = panel.Locator("div.select-selected-on-hover-drop-list-chips-theme");
                    int rCount = await rows.CountAsync();

                    // Peek first row to decide market type (Run Line vs Totals)
                    string firstCol = "";
                    try
                    {
                        var cols = rows.Nth(0).Locator("div.tw-fr-w-full span");
                        if (await cols.CountAsync() > 0)
                            firstCol = (await cols.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? "";
                    }
                    catch { }

                    bool looksRunLine = firstCol.Contains("+") || firstCol.Contains("-");     // e.g., +1.5 / -1.5
                    bool looksTotals = !looksRunLine; // otherwise treat as totals (e.g., 7.5 / 8.5)

                    for (int r = 0; r < rCount; r++)
                    {
                        try
                        {
                            var cols = rows.Nth(r).Locator("div.tw-fr-w-full span");
                            if (await cols.CountAsync() < 3) continue;

                            string col0 = (await cols.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? "";
                            string col1 = (await cols.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? "";
                            string col2 = (await cols.Nth(2).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? "";

                            col0 = col0.Replace(',', '.');
                            col1 = col1.Replace(',', '.');
                            col2 = col2.Replace(',', '.');

                            if (looksRunLine)
                            {
                                // Store as "TT + Handicap": key = handicap (e.g., -1.5 / +1.5)
                                // For consistency with basketball, map columns to "1" (home) and "2" (away)
                                fx.TTPlusHandicap[col0] = new Dictionary<string, string>
                                {
                                    ["1"] = col1,
                                    ["2"] = col2
                                };
                            }
                            else
                            {
                                // Store as Totals O/U: key = total (e.g., 7.5)
                                fx.OU[col0] = new Dictionary<string, string>
                                {
                                    ["OVER"] = col1,
                                    ["UNDER"] = col2
                                };
                            }
                        }
                        catch { /* row best-effort */ }
                    }

                    // Close dropdown
                    try { await button.ClickAsync(new() { Force = true }); } catch { }
                    await match.Page.WaitForTimeoutAsync(80);
                }
            }
            catch { /* best-effort */ }
        }

        // --- Baseball DTO ---
        // --- Baseball DTO (match basketball JSON shape) ---
        public class BaseballFixture
        {
            public string Teams { get; set; } = string.Empty;

            // Moneyline 1–2 (same as basketball)
            public Dictionary<string, string> Odds { get; set; } = new()
            {
                ["1"] = "",
                ["2"] = ""
            };

            // Use the SAME property name and shape as basketball
            [JsonPropertyName("TT + Handicap")]
            public Dictionary<string, Dictionary<string, string>> TTPlusHandicap { get; set; } = new();

            // Identical to basketball
            [JsonPropertyName("O/U")]
            public Dictionary<string, Dictionary<string, string>> OU { get; set; } = new();
        }

// --- Rugby fixture: simple 1-X-2 ---
// --- Rugby fixture: 1-X-2 + O/U (header) ---
public class RugbyFixture
{
    public string Teams { get; set; } = string.Empty;

    // Moneyline 1-X-2
    public Dictionary<string, string> Odds { get; set; } = new()
    {
        ["1"] = "",
        ["X"] = "",
        ["2"] = ""
    };

    // NEW: Under/Over totals from header tab
    [JsonPropertyName("O/U")]
    public Dictionary<string, Dictionary<string, string>> OU { get; set; } = new();
}

private async Task<bool> ActivateRugbyOverUnderHeaderAsync(IPage page)
{
    // Prefer exact data-qa you provided
    var candidates = new[]
    {
        "button[data-qa='classeEsito_10055']",
        "button:has-text('UNDER/OVER')",
        "button:has(:text('UNDER/OVER'))"
    };

    foreach (var sel in candidates)
    {
        try
        {
            var btn = page.Locator(sel).First;
            if (await btn.IsVisibleAsync(new() { Timeout = 1200 }))
            {
                try { await btn.ScrollIntoViewIfNeededAsync(); } catch { }
                await btn.ClickAsync(new() { Force = true });

                // Wait briefly for O/U markets (data-qa contains _10055_) to appear
                await Task.WhenAny(
                    page.Locator("button.chips-commons[data-qa*='_10055_']").First
                        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 }),
                    page.WaitForTimeoutAsync(500)
                );
                return true;
            }
        }
        catch { /* try next */ }
    }
    return false;
}
private async Task ReadRugbyHeaderOUForCardAsync(ILocator card, Dictionary<string, Dictionary<string, string>> ou)
{
    try
    {
        // Each O/U block is a "market attribute" with a title like "U/O 48.5"
        var markets = card.Locator(".template_mg-market-attribute__Y16SU");
        int mcount = 0; try { mcount = await markets.CountAsync(); } catch { }

        for (int i = 0; i < mcount; i++)
        {
            var mkt = markets.Nth(i);

            // Get the market title text
            string title = "";
            try
            {
                title = (await mkt.Locator(".mg-market-attribute-desc .tw-fr-font-primary")
                                  .First.InnerTextAsync(new() { Timeout = 800 }))?.Trim() ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(title) || !title.Contains("U/O", StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract the numeric line (e.g., "48.5" / "50.5") from "U/O 48.5"
            string line = "";
            try
            {
                var m = Regex.Match(title.Replace(',', '.'), @"U\/O\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
                if (m.Success) line = m.Groups[1].Value.Trim();
            }
            catch { }
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Two chips inside this block: data-qa like ..._10055_4850_1 and ..._10055_4850_2
            // Convention: tail _1 => OVER, _2 => UNDER (matches your snippet).
            string over = "", under = "";

            try
            {
                var overBtn = mkt.Locator("button.chips-commons[data-qa*='_10055_'][data-qa$='_1'] span").First;
                if (await overBtn.IsVisibleAsync(new() { Timeout = 600 }))
                {
                    var t = (await overBtn.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) over = t.Replace(',', '.');
                }
            }
            catch { }

            try
            {
                var underBtn = mkt.Locator("button.chips-commons[data-qa*='_10055_'][data-qa$='_2'] span").First;
                if (await underBtn.IsVisibleAsync(new() { Timeout = 600 }))
                {
                    var t = (await underBtn.InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) under = t.Replace(',', '.');
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(over) || !string.IsNullOrWhiteSpace(under))
            {
                if (!ou.TryGetValue(line, out var obj))
                {
                    obj = new Dictionary<string, string>();
                    ou[line] = obj;
                }
                if (!string.IsNullOrWhiteSpace(over))  obj["OVER"]  = over;
                if (!string.IsNullOrWhiteSpace(under)) obj["UNDER"] = under;
            }
        }
    }
    catch { /* per-card best-effort */ }
}

        private async Task<bool> ExpandCountryAsync(ILocator country, int index, int expandTimeoutMs = 8000)
        {
            var header = country.Locator(".FR-Accordion__header");
            var content = country.Locator(".FR-Accordion__content");

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await HandleAllBlockers(header.Page);
                    await header.ScrollIntoViewIfNeededAsync();
                    await header.ClickAsync(new() { Force = true });

                    await content.WaitForAsync(new()
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = expandTimeoutMs
                    });

                    return true;
                }
                catch
                {
                    Console.WriteLine($"   ↪ Expand retry {attempt} for country idx {index}...");
                    await HandleAllBlockers(header.Page);
                    try { await header.Page.Mouse.WheelAsync(0, 350); } catch { }
                    await Task.Delay(250);
                }
            }
            return false;
        }

        private async Task CollapseCountryAsync(ILocator country)
        {
            try
            {
                var header = country.Locator(".FR-Accordion__header");
                var content = country.Locator(".FR-Accordion__content");
                await header.ScrollIntoViewIfNeededAsync();
                await header.ClickAsync(new() { Force = true });
                try
                {
                    await content.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 1500 });
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        private async Task EnsureCheckboxStateAsync(ILocator checkbox, bool desiredChecked, int index, int total, int maxRetries = 6)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                await HandleAllBlockers(checkbox.Page);

                bool current = false;
                try { current = await checkbox.IsCheckedAsync(); } catch { }

                if (current == desiredChecked)
                {
                    Console.WriteLine($"   {(desiredChecked ? "✅ Checked" : "❌ Unchecked")} league {index}/{total} (attempt {attempt})");
                    return;
                }

                try
                {
                    if (desiredChecked)
                        await checkbox.CheckAsync(new() { Force = true, Timeout = 1500 });
                    else
                        await checkbox.UncheckAsync(new() { Force = true, Timeout = 1500 });
                }
                catch
                {
                    try
                    {
                        await checkbox.EvaluateAsync(@"(el, desired) => {
                            const input = el;
                            if (!input) return;
                            input.checked = desired;
                            input.dispatchEvent(new Event('input', { bubbles: true }));
                            input.dispatchEvent(new Event('change', { bubbles: true }));
                        }", desiredChecked);
                    }
                    catch { }
                }

                await Task.Delay(120);
            }

            Console.WriteLine($"   ⚠ League {index}/{total} failed to reach desired state: {desiredChecked}");
        }

        // =========================
        // BLOCKERS / POPUPS
        // =========================
        private async Task HandleAllBlockers(IPage page)
        {
            try { await page.Keyboard.PressAsync("Escape"); } catch { }

            for (int i = 0; i < 2; i++)
            {
                await AutoDismissPopupAsyncStrong(page);
                await DismissOverlayAsyncStrong(page);
                await KillGenericModalsAsync(page);
                await page.WaitForTimeoutAsync(80);
            }
        }

        private async Task KillGenericModalsAsync(IPage page)
        {
            try
            {
                await page.EvaluateAsync(@"() => {
                    const sels = [
                      '#onetrust-banner-sdk', '#onetrust-consent-sdk', '.ot-sdk-container',
                      '.cookie', '.cookie-banner', '.cookie-consent',
                      '.toast', '.snackbar', '.tw-snackbar',
                      '.newsletter-modal', '.modal[open]', '.tw-modal', '[role=""dialog""]'
                    ];
                    for (const sel of sels) {
                      document.querySelectorAll(sel).forEach(e => {
                        try { e.style.display='none'; e.remove(); } catch {}
                      });
                    }
                }");
            }
            catch { }
        }

        private async Task AutoDismissPopupAsyncStrong(IPage page)
        {
            try
            {
                var popup = page.Locator(".customTooltip_container__hhqqn, .portal-theme[role='alertdialog'], .tw-modal, [role='dialog']");
                if (await popup.IsVisibleAsync(new() { Timeout = 300 }))
                {
                    Console.WriteLine("Popup detected and visible...");
                    var close = popup.Locator("button:has-text('Chiudi'), .icon-Close, [data-testid='close'], button[aria-label='Close']");
                    try
                    {
                        if (await close.First.IsVisibleAsync(new() { Timeout = 200 }))
                            await close.First.ClickAsync(new() { Force = true });
                    }
                    catch { }

                    await page.EvaluateAsync(@"(() => {
                        const sels = [
                          '.customTooltip_container__hhqqn',
                          '.portal-theme.tipster-theme .customTooltip_container__hhqqn',
                          '.portal-theme[role=""alertdialog""]',
                          '.tooltip_container', '.tw-modal', '[role=""dialog""]'
                        ];
                        for (const sel of sels) {
                          document.querySelectorAll(sel).forEach(n => { try { n.remove(); } catch {} });
                        }
                    })()");
                    await page.WaitForTimeoutAsync(80);
                }
            }
            catch { }
        }
        // =========================
        // BASKETBALL (updated odds; per-fixture scroll)
        // =========================
        private async Task ExtractBasketOddsAsync(IPage page, string sportName, string countryName)
        {
            Console.WriteLine($"  🏀 [Basket] Extracting odds for {sportName} - {countryName}...");

            var fixturesOut = new List<BasketFixture>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matchContainers = page.Locator(".grid_mg-row-wrapper__usTh4");

            // Probe briefly; some countries may legitimately have zero fixtures
            int matchCount = 0;
            for (int spin = 0; spin < 10; spin++)
            {
                try { matchCount = await matchContainers.CountAsync(); } catch { matchCount = 0; }
                if (matchCount > 0) break;

                try
                {
                    var emptyState = page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')");
                    if (await emptyState.CountAsync() > 0) break;
                }
                catch { /* ignore */ }

                await page.WaitForTimeoutAsync(300);
            }

            if (matchCount == 0)
            {
                Console.WriteLine($"  • [Basket] No fixtures visible for {countryName}; skipping.");
                return;
            }

            Console.WriteLine($"  • [Basket] Found {matchCount} fixtures (pre-filter).");

            for (int i = 0; i < matchCount; i++)
            {
                var card = matchContainers.Nth(i);

                // Skip live/in-play
                bool isLive = false;
                try
                {
                    if (await card.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                        isLive = true;
                    else
                    {
                        var t = "";
                        try { t = (await card.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                        if (t.Contains("live") || t.Contains("in play") || t.Contains("in-play")) isLive = true;
                    }
                }
                catch { }
                if (isLive) continue;

                // Teams
                List<string> teams;
                try { teams = new List<string>(await card.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync()); }
                catch { continue; }
                if (teams.Count < 2) continue;

                string home = teams[0].Trim();
                string away = teams[1].Trim();
                string teamNames = $"{home} vs {away}";
                if (!seen.Add(teamNames)) continue;

                // Per-fixture scroll to ensure virtualization renders odds
                try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
                try { await page.Mouse.WheelAsync(0, 500); } catch { }
                await page.WaitForTimeoutAsync(100);

                var fx = new BasketFixture { Teams = teamNames };

                // 1–2 odds
                await AddBasket12ForCardAsync(card, fx.Odds);

                // TT + Handicap: click the small-number chip (<100), read dropdown rows
                await ReadTTDropdownForCardAsync(card, fx.TTPlusHandicap);

                // O/U Totals: click the big-number chip (>=100), read dropdown rows
                await ReadOUDropdownForCardAsync(card, fx.OU);

                fixturesOut.Add(fx);
                Console.WriteLine($"    🏀 {teamNames} | 12(1:{fx.Odds.GetValueOrDefault("1")},2:{fx.Odds.GetValueOrDefault("2")}) | TT+H:{fx.TTPlusHandicap.Count} | O/U:{fx.OU.Count}");
            }

            // Export per country (basket)
            string safeCountryName = string.Join("_", countryName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{sportName}_{safeCountryName}_odds.json";

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string jsonOutput = JsonSerializer.Serialize(fixturesOut, opts);
            await File.WriteAllTextAsync(fileName, jsonOutput);
            Console.WriteLine($"  ✅ [Basket] Odds exported to {fileName} ({fixturesOut.Count} fixtures)\n");
        }

  private async Task ExtractRugbyOddsAsync(IPage page, string sportName, string countryOrLeague)
{
    Console.WriteLine($"  🏉 [Rugby] Extracting 1-X-2 odds for {sportName} - {countryOrLeague}...");

    var fixturesOut = new List<RugbyFixture>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var cards = page.Locator(".grid_mg-row-wrapper__usTh4");

    int matchCount = 0;
    for (int spin = 0; spin < 10; spin++)
    {
        try { matchCount = await cards.CountAsync(); } catch { matchCount = 0; }
        if (matchCount > 0) break;
        try
        {
            var empty = page.Locator(":text-matches('Nessun|Nessuna|Non ci sono|No events|No matches','i')");
            if (await empty.CountAsync() > 0) break;
        }
        catch { }
        await page.WaitForTimeoutAsync(300);
    }

    Console.WriteLine($"  • [Rugby] Found {matchCount} fixtures (pre-filter).");
    if (matchCount == 0) return;

    bool ouHeaderActivated = false; // <-- NEW

    for (int i = 0; i < matchCount; i++)
    {
        var card = cards.Nth(i);

        // Skip live
        bool isLive = false;
        try
        {
            if (await card.Locator(".live, .badge-live, .inplay, .live-badge, [data-live='true']").CountAsync() > 0)
                isLive = true;
            else
            {
                var t = "";
                try { t = (await card.InnerTextAsync(new() { Timeout = 300 })).ToLower(); } catch { }
                if (t.Contains("live") || t.Contains("in play") || t.Contains("in-play")) isLive = true;
            }
        }
        catch { }
        if (isLive) continue;

        // Teams
        List<string> teams;
        try { teams = new List<string>(await card.Locator("a.regulator_description__SY8Vw span").AllInnerTextsAsync()); }
        catch { continue; }
        if (teams.Count < 2) continue;

        string home = teams[0].Trim();
        string away = teams[1].Trim();
        string key = $"{home} vs {away}";
        if (!seen.Add(key)) continue;

        try { await card.ScrollIntoViewIfNeededAsync(); } catch { }
        try { await page.Mouse.WheelAsync(0, 300); } catch { }
        await page.WaitForTimeoutAsync(60);

        var fx = new RugbyFixture { Teams = key };

        // 1) FIRST: 1-X-2
        await AddRugby1X2ForCardAsync(card, fx.Odds);

        // 2) THEN: click the O/U header once, and extract O/U for this card
        if (!ouHeaderActivated)
        {
            ouHeaderActivated = await ActivateRugbyOverUnderHeaderAsync(page);
            // tiny settle
            await page.WaitForTimeoutAsync(200);
        }
        if (ouHeaderActivated)
        {
            await ReadRugbyHeaderOUForCardAsync(card, fx.OU);
        }

        fixturesOut.Add(fx);
        Console.WriteLine($"    🏉 {key} | 1:{fx.Odds.GetValueOrDefault("1")} X:{fx.Odds.GetValueOrDefault("X")} 2:{fx.Odds.GetValueOrDefault("2")} | O/U:{fx.OU.Count}");
    }

    string safe = string.Join("_", countryOrLeague.Split(Path.GetInvalidFileNameChars()));
    string fileName = $"{sportName}_{safe}_odds.json";
    var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    string json = JsonSerializer.Serialize(fixturesOut, opts);
    await File.WriteAllTextAsync(fileName, json);

    Console.WriteLine($"  ✅ [Rugby] 1-X-2 + O/U exported to {fileName} ({fixturesOut.Count} fixtures)");
}

// Rugby 1-X-2 reader from inline chips inside the specific market container you provided.
// It prefers data-qa tails (_..._1, _..._2, _..._3) to map 1 / 2 / X; falls back to DOM order 1, X, 2.
// Rugby 1-X-2 reader from inline chips inside the specific market container you provided.
// Data-qa tails mapping per your snippet: _1 => "1", _2 => "X", _3 => "2".
private async Task AddRugby1X2ForCardAsync(ILocator card, Dictionary<string, string> odds)
{
    try
    {
        var market = card.Locator(".template_mg-market-attribute__JNwjO").First;
        bool visible = false;
        try { visible = await market.IsVisibleAsync(new() { Timeout = 800 }); } catch { }
        if (!visible)
        {
            var allMkts = card.Locator(".template_mg-market-attribute__JNwjO");
            int mc = 0; try { mc = await allMkts.CountAsync(); } catch { }
            for (int i = 0; i < mc; i++)
            {
                var m = allMkts.Nth(i);
                try { if (await m.IsVisibleAsync(new() { Timeout = 400 })) { market = m; break; } } catch { }
            }
        }

        var chips = market.Locator("button.chips-commons");
        int c = 0; try { c = await chips.CountAsync(); } catch { }
        if (c < 2) return;

        for (int i = 0; i < c && i < 3; i++)
        {
            string v = "";
            try
            {
                var span = chips.Nth(i).Locator("span").First;
                if (await span.IsVisibleAsync(new() { Timeout = 600 }))
                    v = (await span.InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.') ?? "";
            } catch { }

            string tail = "";
            try
            {
                var dqa = await chips.Nth(i).GetAttributeAsync("data-qa") ?? "";
                tail = dqa.Split('_').LastOrDefault() ?? "";
            } catch { }

            if (string.IsNullOrWhiteSpace(v)) continue;

            // Your snippet: _1 => "1", _2 => "X", _3 => "2"
            if (tail == "1") odds["1"] = v;
            if (tail == "2") odds["X"] = v;
            if (tail == "3") odds["2"] = v;
        }

        // Fallback to DOM order 1, X, 2 if tails weren’t readable
        if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) ||
            string.IsNullOrWhiteSpace(odds.GetValueOrDefault("X")) ||
            string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
        {
            var vals = await chips.Locator("span").AllInnerTextsAsync();
            if (vals.Count >= 1 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1"))) odds["1"] = vals[0].Trim().Replace(',', '.');
            if (vals.Count >= 2 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("X"))) odds["X"] = vals[1].Trim().Replace(',', '.');
            if (vals.Count >= 3 && string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2"))) odds["2"] = vals[2].Trim().Replace(',', '.');
        }
    }
    catch { }
}

        // 1–2 moneyline for basketball
        private async Task AddBasket12ForCardAsync(ILocator match, Dictionary<string, string> odds)
        {
            try
            {
                // Prefer explicit tails: ..._0_1 (1) and ..._0_2 (2)
                var one = match.Locator("button.chips-commons[data-qa$='_0_1'] span").First;
                var two = match.Locator("button.chips-commons[data-qa$='_0_2'] span").First;

                if (await one.IsVisibleAsync(new() { Timeout = 800 }))
                {
                    var t = (await one.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
                }
                if (await two.IsVisibleAsync(new() { Timeout = 800 }))
                {
                    var t = (await two.InnerTextAsync(new() { Timeout = 900 }))?.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
                }
            }
            catch { }

            // Fallback: first market group → first two chips
            if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")) || string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
            {
                try
                {
                    var chips = match.Locator(".grid_mg-market__gVuGf").First.Locator("button.chips-commons span");
                    if (await chips.CountAsync() >= 2)
                    {
                        if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("1")))
                        {
                            var t = (await chips.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            if (!string.IsNullOrWhiteSpace(t)) odds["1"] = t.Replace(',', '.');
                        }
                        if (string.IsNullOrWhiteSpace(odds.GetValueOrDefault("2")))
                        {
                            var t = (await chips.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            if (!string.IsNullOrWhiteSpace(t)) odds["2"] = t.Replace(',', '.');
                        }
                    }
                }
                catch { }
            }
        }

        // Read "TT + Handicap" dropdown rows (chip value < 100); map line -> { "1","2" }
        private async Task ReadTTDropdownForCardAsync(ILocator match, Dictionary<string, Dictionary<string, string>> tt)
        {
            try
            {
                var toggles = match.Locator(":scope button.counter-drop-chip-default-theme span");
                int count = await toggles.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    string raw = "";
                    try { raw = (await toggles.Nth(i).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? ""; }
                    catch { continue; }

                    var canon = raw.Replace(',', '.');
                    if (!double.TryParse(canon, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        continue;

                    if (v >= 100) continue; // TT chip = small number like 15.5–30.5

                    var button = toggles.Nth(i).Locator("..");
                    try { await button.ScrollIntoViewIfNeededAsync(); } catch { }
                    await button.ClickAsync(new() { Force = true });

                    var panel = match.Page.Locator(".drop-list-chips-select-option-container-theme:visible").First;
                    try { await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2500 }); }
                    catch { continue; }

                    var rows = panel.Locator("div.select-selected-on-hover-drop-list-chips-theme");
                    int n = await rows.CountAsync();

                    for (int r = 0; r < n; r++)
                    {
                        try
                        {
                            var cols = rows.Nth(r).Locator("div.tw-fr-w-full span");
                            if (await cols.CountAsync() < 3) continue;

                            string line = (await cols.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            string odd1 = (await cols.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.');
                            string odd2 = (await cols.Nth(2).InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.');

                            if (!string.IsNullOrWhiteSpace(line) &&
                                !string.IsNullOrWhiteSpace(odd1) &&
                                !string.IsNullOrWhiteSpace(odd2))
                            {
                                line = line.Replace(',', '.');
                                tt[line] = new Dictionary<string, string>
                                {
                                    ["1"] = odd1,
                                    ["2"] = odd2
                                };
                            }
                        }
                        catch { /* row best-effort */ }
                    }

                    // Close dropdown and stop after first TT chip
                    try { await button.ClickAsync(new() { Force = true }); } catch { }
                    await match.Page.WaitForTimeoutAsync(100);
                    break;
                }
            }
            catch { /* best-effort */ }
        }

        // Read O/U dropdown rows (chip value >= 100); map total -> { "OVER","UNDER" }
        private async Task ReadOUDropdownForCardAsync(ILocator match, Dictionary<string, Dictionary<string, string>> ou)
        {
            try
            {
                var toggles = match.Locator(":scope button.counter-drop-chip-default-theme span");
                int count = await toggles.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    string raw = "";
                    try { raw = (await toggles.Nth(i).InnerTextAsync(new() { Timeout = 700 }))?.Trim() ?? ""; }
                    catch { continue; }

                    var canon = raw.Replace(',', '.');
                    if (!double.TryParse(canon, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        continue;

                    if (v < 100) continue; // O/U chip = big number like 150.5–250.5

                    var button = toggles.Nth(i).Locator("..");
                    try { await button.ScrollIntoViewIfNeededAsync(); } catch { }
                    await button.ClickAsync(new() { Force = true });

                    var panel = match.Page.Locator(".drop-list-chips-select-option-container-theme:visible").First;
                    try { await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2500 }); }
                    catch { continue; }

                    var rows = panel.Locator("div.select-selected-on-hover-drop-list-chips-theme");
                    int n = await rows.CountAsync();

                    for (int r = 0; r < n; r++)
                    {
                        try
                        {
                            var cols = rows.Nth(r).Locator("div.tw-fr-w-full span");
                            if (await cols.CountAsync() < 3) continue;

                            string total = (await cols.Nth(0).InnerTextAsync(new() { Timeout = 700 }))?.Trim();
                            string over = (await cols.Nth(1).InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.');
                            string under = (await cols.Nth(2).InnerTextAsync(new() { Timeout = 700 }))?.Trim().Replace(',', '.');

                            if (!string.IsNullOrWhiteSpace(total) &&
                                !string.IsNullOrWhiteSpace(over) &&
                                !string.IsNullOrWhiteSpace(under))
                            {
                                total = total.Replace(',', '.');
                                ou[total] = new Dictionary<string, string>
                                {
                                    ["OVER"] = over,
                                    ["UNDER"] = under
                                };
                            }
                        }
                        catch { /* row best-effort */ }
                    }

                    // Close dropdown and stop after first O/U chip
                    try { await button.ClickAsync(new() { Force = true }); } catch { }
                    await match.Page.WaitForTimeoutAsync(100);
                    break;
                }
            }
            catch { /* best-effort */ }
        }


        private async Task DismissOverlayAsyncStrong(IPage page)
        {
            try
            {
                var overlay = page.Locator(".react-joyride__overlay, #react-joyride-portal, .overlay, .modal-backdrop, .backdrop, .tw-overlay");
                if (await overlay.IsVisibleAsync(new() { Timeout = 300 }))
                {
                    Console.WriteLine("Overlay detected... dismissing it");
                    var closeIcon = page.Locator("#react-joyride-portal .icon-Close, .tw-modal [aria-label='Close'], .modal [data-testid='close']");
                    try
                    {
                        if (await closeIcon.First.IsVisibleAsync(new() { Timeout = 200 }))
                            await closeIcon.First.ClickAsync(new() { Force = true });
                    }
                    catch { }

                    await page.EvaluateAsync(@"(() => {
                        const sels = [
                          '.react-joyride__overlay','#react-joyride-portal',
                          '.overlay','.tw-overlay','.modal-backdrop','.backdrop'
                        ];
                        for (const sel of sels) {
                          document.querySelectorAll(sel).forEach(n => { try { n.remove(); } catch {} });
                        }
                    })()");
                    await page.WaitForTimeoutAsync(80);
                }
            }
            catch { }
        }

        // =========================
        // INIT SCRIPTS / UTIL
        // =========================
        private async Task InstallAggressivePopupKillerAsync(IPage page)
        {
            const string script = @"
(function(){
  const KILL_SELECTORS = [
    '.customTooltip_container__hhqqn',
    '.portal-theme.tipster-theme .customTooltip_container__hhqqn',
    '.portal-theme[role=""alertdialog""]',
    '.tooltip_container',
    '.tw-modal',
    '[role=""dialog""]',
    '.react-joyride__overlay',
    '#react-joyride-portal'
  ];
  const nuke = () => {
    for (const sel of KILL_SELECTORS) {
      document.querySelectorAll(sel).forEach(n => { try { n.remove(); } catch(e){} });
    }
  };
  const style = document.createElement('style');
  style.textContent = KILL_SELECTORS.map(s => `${s}{display:none !important;visibility:hidden !important;}`).join('');
  document.documentElement.appendChild(style);
  const obs = new MutationObserver(() => nuke());
  obs.observe(document.documentElement, { childList: true, subtree: true });
  nuke();
  setInterval(nuke, 500);
})();";
            try { await page.AddInitScriptAsync(script); } catch { }
            try { await page.EvaluateAsync(script); } catch { }
        }

        private async Task InstallAnchorKillSwitchAsync(IPage page)
        {
            const string script = @"
(function(){
  const kill = (rootSel) => {
    const root = document.querySelector(rootSel);
    if (!root) return;
    root.addEventListener('click', function(e){
      const a = e.target.closest('a');
      if (!a) return;
      if (root.contains(a)) {
        e.preventDefault();
        e.stopPropagation();
      }
    }, true);
  };
  kill('.FR-Accordion');
  kill('.competitionMenu-theme');
})();";
            try { await page.AddInitScriptAsync(script); } catch { }
            try { await page.EvaluateAsync(script); } catch { }
        }

        // =========================
        // SESSION PERSISTENCE
        // =========================
        private async Task SaveSessionAsync(IBrowserContext context)
        {
            var storage = new
            {
                Cookies = await context.CookiesAsync(),
                LocalStorage = await GetLocalStorageAsync(context)
            };

            var json = JsonSerializer.Serialize(storage, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SessionFile, json);
            Console.WriteLine("✅ Session saved.");
        }

        private async Task LoadSessionAsync(IBrowserContext context)
        {
            if (!File.Exists(SessionFile)) return;

            var json = await File.ReadAllTextAsync(SessionFile);
            var storage = JsonSerializer.Deserialize<SessionData>(json);

            if (storage?.Cookies != null)
                await context.AddCookiesAsync(storage.Cookies);

            var page = await context.NewPageAsync();
            await page.GotoAsync("https://www.sisal.it");

            if (storage?.LocalStorage != null)
            {
                foreach (var item in storage.LocalStorage)
                    await page.EvaluateAsync(@"([key, value]) => localStorage.setItem(key, value)", new object[] { item.Key, item.Value });
            }

            await page.ReloadAsync();
            await page.CloseAsync();
            Console.WriteLine("✅ Session loaded.");
        }

        private async Task<Dictionary<string, string>> GetLocalStorageAsync(IBrowserContext context)
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://www.sisal.it");
            var localStorage = await page.EvaluateAsync<Dictionary<string, string>>(@"() =>
            {
                let store = {};
                for (let i = 0; i < localStorage.length; i++) {
                    const key = localStorage.key(i);
                    store[key] = localStorage.getItem(key);
                }
                return store;
            }");
            await page.CloseAsync();
            return localStorage;
        }
    }

    public class SessionData
    {
        public List<Cookie> Cookies { get; set; } = new();
        public Dictionary<string, string> LocalStorage { get; set; } = new();
    }

    // Soccer fixture (unchanged structure)
    public class MatchData
    {
        public string Teams { get; set; } = string.Empty;
        public Dictionary<string, string> Odds { get; set; } = new();
    }


    // Tennis fixture (with TT + Handicap)
    // OLD
    // public Dictionary<string, string> Odds { get; set; } = new();

    // NEW
    public class TennisFixture
    {
        public string Teams { get; set; } = string.Empty;

        // allow "U/O" to be an object
        public Dictionary<string, object> Odds { get; set; } = new();

        [JsonPropertyName("TT + Handicap")]
        public Dictionary<string, string> TTHandicap { get; set; } = new();
    }

    public class BasketFixture
    {
        public string Teams { get; set; } = string.Empty;

        // Regular 12 market (only 1 & 2 for basket)
        public Dictionary<string, string> Odds { get; set; } = new()
        {
            ["1"] = "",
            ["2"] = ""
        };

        // "TT + Handicap" object, keyed by line (e.g., "15.5")
        [JsonPropertyName("TT + Handicap")]
        public Dictionary<string, Dictionary<string, string>> TTPlusHandicap { get; set; } = new();

        // "O/U" object, keyed by total (e.g., "171.5")
        [JsonPropertyName("O/U")]
        public Dictionary<string, Dictionary<string, string>> OU { get; set; } = new();
    }

}
