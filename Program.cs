using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace SisalScraper
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var scraper = new SisalScraper();

            //  await scraper.RunAsync("rugby");
        //    await scraper.RunAsync("american football"); 
        //   await scraper.RunAsync("Basket");
            // await scraper.RunAsync("Calcio");
            // await scraper.RunAsync("baseball");
            // await scraper.RunAsync("ice hockey");
        //    await scraper.RunAsync("Tennis");
        }
    }
}



// using Microsoft.Playwright;
// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using System.Text.Json;
// using System.Threading.Tasks;

// namespace SisalScraper
// {
//     public class SisalScraper
//     {
//         private const string SessionFile = "session.json";

//         public async Task RunAsync()
//         {
//             using var playwright = await Playwright.CreateAsync();
//             var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
//             {
//                 Headless = false,
//                 SlowMo = 150
//             });

//             var context = await browser.NewContextAsync();
//             await LoadSessionAsync(context);

//             var page = await context.NewPageAsync();
//             Console.WriteLine("Navigating to Sisal...");
//             await page.GotoAsync("https://www.sisal.it", new() { WaitUntil = WaitUntilState.NetworkIdle });
//             await SaveSessionAsync(context);

//             Console.WriteLine("Clicking 'Scommesse'...");
//             await page.Locator("#dropdown1-tab1").ClickAsync();

//             Console.WriteLine("Clicking 'Sport'...");
//             var sportLink = page.Locator("a.card-title[aria-label='Accedi alla sezione Sport']");
//             await sportLink.ClickAsync();

//             Console.WriteLine("Waiting for sport slider...");
//             var sportItems = page.Locator(".horizontalScroll_container__ACxu6 > div > a");
//             await sportItems.First.WaitForAsync();

//             int count = await sportItems.CountAsync();
//             Console.WriteLine($"Found {count} sport items in the slider.");

//             for (int i = 0; i < count; i++)
//             {
//                 var sportElement = sportItems.Nth(i);
//                 var sportText = (await sportElement.InnerTextAsync()).Trim().ToLower();

//                 if (sportText.Contains("quote top") || sportText.Contains("tipster"))
//                 {
//                     Console.WriteLine($"Skipping: {sportText}");
//                     continue;
//                 }

//                 Console.WriteLine($"\n=== Clicking sport: {sportText} ===");

//                 // Ensure blockers cleared before click
//                 await HandleAllBlockers(page);

//                 // Click the sport
//                 await sportElement.ClickAsync(new LocatorClickOptions { Force = true });

//                 // Immediately check for popup after click (faster dismissal)
//                 await AutoDismissPopupAsync(page);

//                 // Handle overlays (if any)
//                 await DismissOverlayAsync(page);

//                 // Wait for content load
//                 await page.WaitForTimeoutAsync(1000);

//                 // Process the sport: just navigate and log countries/leagues
//                 await ProcessSportAsync(page, sportText);
//             }

//             await browser.CloseAsync();
//         }

//         private async Task HandleAllBlockers(IPage page)
//         {
//             await AutoDismissPopupAsync(page);
//             await DismissOverlayAsync(page);
//             await page.WaitForTimeoutAsync(150);
//         }

//      private async Task ProcessSportAsync(IPage page, string sportName)
// {
//     Console.WriteLine($"Processing sport: {sportName}");
//     await HandleAllBlockers(page);

//     // Log top events (just for debug)
//     var topEventCheckboxes = page.Locator(".competitionMenu-theme ul li a[data-qa^='manifestazione']");
//     int topCount = await topEventCheckboxes.CountAsync();
//     Console.WriteLine($"Top events found: {topCount}");

//     // Process Countries
//     var countries = page.Locator(".FR-Accordion");
//     int countryCount = await countries.CountAsync();
//     Console.WriteLine($"{countryCount} country groups found.");

//     int restartThreshold = 2; // Restart after 3rd country (index 2)

//     // === FIRST PASS: Just expand and collapse first 3 countries ===
//     for (int i = 0; i <= restartThreshold && i < countryCount; i++)
//     {
//         countries = page.Locator(".FR-Accordion");
//         var country = countries.Nth(i);
//         var countryName = (await country.Locator(".FR-Accordion__header span.tw-fr-text-paragraph-s").InnerTextAsync()).Trim();
//         Console.WriteLine($"\n▶ (Pass 1) Country: {countryName}");

//         await HandleAllBlockers(page);

//         // Expand country
//         await country.Locator(".FR-Accordion__header").ClickAsync();
//         await page.WaitForTimeoutAsync(500);

//         // Collapse country
//         try
//         {
//             await country.Locator(".FR-Accordion__header").ClickAsync();
//             await page.WaitForTimeoutAsync(300);
//         }
//         catch
//         {
//             Console.WriteLine($"Could not collapse {countryName}, continuing...");
//         }
//     }

//     Console.WriteLine("\n🔄 Restarting country loop from the beginning for checkbox selection...");

//     // === SECOND PASS: Now click all countries and tick their checkboxes ===
//     for (int i = 0; i < countryCount; i++)
//     {
//         countries = page.Locator(".FR-Accordion");
//         var country = countries.Nth(i);
//         var countryName = (await country.Locator(".FR-Accordion__header span.tw-fr-text-paragraph-s").InnerTextAsync()).Trim();
//         Console.WriteLine($"\n▶ (Pass 2) Country: {countryName}");

//         await HandleAllBlockers(page);

//         // Expand country
//         await country.Locator(".FR-Accordion__header").ClickAsync();
//         await page.WaitForTimeoutAsync(500);

//         // Tick checkboxes for leagues
//         var leagueCheckboxes = country.Locator(".FR-Accordion__content ul li a label.checkbox-theme input[type='checkbox']");
//         int leagueCount = await leagueCheckboxes.CountAsync();
//         Console.WriteLine($"Leagues in {countryName}: {leagueCount}");

//         for (int j = 0; j < leagueCount; j++)
//         {
//             await HandleAllBlockers(page);

//             var checkbox = leagueCheckboxes.Nth(j);
//             bool isChecked = await checkbox.IsCheckedAsync();

//             if (!isChecked)
//             {
//                 Console.WriteLine($"   ✅ Checking league {j + 1}/{leagueCount}");
//                 await checkbox.ScrollIntoViewIfNeededAsync();
//                 await checkbox.ClickAsync(new LocatorClickOptions { Force = true });
//                 await page.WaitForTimeoutAsync(100);
//             }
//         }

//         // Collapse country before moving on
//         try
//         {
//             await country.Locator(".FR-Accordion__header").ClickAsync();
//             await page.WaitForTimeoutAsync(300);
//         }
//         catch
//         {
//             Console.WriteLine($"Could not collapse {countryName}, continuing...");
//         }
//     }
// }

//         // ✅ Fast popup dismissal
//         private async Task AutoDismissPopupAsync(IPage page)
//         {
//             try
//             {
//                 var popup = page.Locator(".customTooltip_container__hhqqn");
//                 if (await popup.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1000 }))
//                 {
//                     Console.WriteLine("Popup detected and visible...");

//                     var closeButton = popup.Locator("button:has-text('Chiudi')");
//                     var closeIcon = popup.Locator(".icon-Close");

//                     if (await closeButton.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 500 }))
//                     {
//                         Console.WriteLine("Closing popup using 'Chiudi' button...");
//                         await closeButton.ClickAsync(new LocatorClickOptions { Force = true });
//                     }
//                     else if (await closeIcon.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 500 }))
//                     {
//                         Console.WriteLine("Closing popup using close icon...");
//                         await closeIcon.ClickAsync(new LocatorClickOptions { Force = true });
//                     }

//                     await page.WaitForTimeoutAsync(200);
//                 }
//             }
//             catch
//             {
//                 Console.WriteLine("No popup detected quickly.");
//             }
//         }

//         private async Task DismissOverlayAsync(IPage page)
//         {
//             var overlay = page.Locator(".react-joyride__overlay");

//             if (await overlay.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1000 }))
//             {
//                 Console.WriteLine("Overlay detected... dismissing it");

//                 var closeIcon = page.Locator("#react-joyride-portal .icon-Close");
//                 if (await closeIcon.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 500 }))
//                 {
//                     Console.WriteLine("Closing overlay using close icon...");
//                     await closeIcon.ClickAsync(new LocatorClickOptions { Force = true });
//                 }
//                 else
//                 {
//                     Console.WriteLine("No close icon found. Forcing overlay removal...");
//                     await page.EvaluateAsync("document.querySelector('.react-joyride__overlay')?.remove()");
//                     await page.EvaluateAsync("document.querySelector('#react-joyride-portal')?.remove()");
//                 }

//                 await page.WaitForTimeoutAsync(300);
//             }
//         }

//         private async Task SaveSessionAsync(IBrowserContext context)
//         {
//             var storage = new
//             {
//                 Cookies = await context.CookiesAsync(),
//                 LocalStorage = await GetLocalStorageAsync(context)
//             };

//             var json = JsonSerializer.Serialize(storage, new JsonSerializerOptions { WriteIndented = true });
//             await File.WriteAllTextAsync(SessionFile, json);
//             Console.WriteLine("✅ Session saved.");
//         }

//         private async Task LoadSessionAsync(IBrowserContext context)
//         {
//             if (!File.Exists(SessionFile)) return;

//             var json = await File.ReadAllTextAsync(SessionFile);
//             var storage = JsonSerializer.Deserialize<SessionData>(json);

//             if (storage?.Cookies != null)
//                 await context.AddCookiesAsync(storage.Cookies);

//             var page = await context.NewPageAsync();
//             await page.GotoAsync("https://www.sisal.it");

//             if (storage?.LocalStorage != null)
//             {
//                 foreach (var item in storage.LocalStorage)
//                 {
//                     await page.EvaluateAsync(@"([key, value]) => localStorage.setItem(key, value)", new object[] { item.Key, item.Value });
//                 }
//             }

//             await page.ReloadAsync();
//             await page.CloseAsync();
//             Console.WriteLine("✅ Session loaded.");
//         }

//         private async Task<Dictionary<string, string>> GetLocalStorageAsync(IBrowserContext context)
//         {
//             var page = await context.NewPageAsync();
//             await page.GotoAsync("https://www.sisal.it");
//             var localStorage = await page.EvaluateAsync<Dictionary<string, string>>(@"() => {
//                 let store = {};
//                 for (let i = 0; i < localStorage.length; i++) {
//                     const key = localStorage.key(i);
//                     store[key] = localStorage.getItem(key);
//                 }
//                 return store;
//             }");
//             await page.CloseAsync();
//             return localStorage;
//         }
//     }

//     public class SessionData
//     {
//         public List<Cookie> Cookies { get; set; } = new();
//         public Dictionary<string, string> LocalStorage { get; set; } = new();
//     }
// }
