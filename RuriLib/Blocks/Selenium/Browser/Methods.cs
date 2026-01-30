using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;

using ProxyType = RuriLib.Models.Proxies.ProxyType;
using OpenQA.Selenium.Remote;
using RuriLib.Models.Settings;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Firefox;
using System;
using System.Collections.Generic;
using OpenQA.Selenium;
using System.Linq;
using System.Drawing;
using RuriLib.Helpers;

namespace RuriLib.Blocks.Selenium.Browser
{
    [BlockCategory("Browser", "Blocks for interacting with a selenium browser", "#bdda57")]
    public static class Methods
    {
        [Block("Opens a new selenium browser", name = "Open Browser")]
        public static void SeleniumOpenBrowser(BotData data, string extraCmdLineArgs = "")
        {
            data.Logger.LogHeader();

            // Check if there is already an open browser
            var oldBrowser = data.TryGetObject<RemoteWebDriver>("selenium");
            if (oldBrowser is not null)
            {
                data.Logger.Log("The browser is already open, close it if you want to open a new browser", LogColors.JuneBud);
                return;
            }

            var provider = data.Providers.SeleniumBrowser;
            var args = string.Empty;

            switch (provider.BrowserType)
            {
                case SeleniumBrowserType.Chrome:
                    var chromeop = new ChromeOptions();
                    var chromeservice = ChromeDriverService.CreateDefaultService();
                    chromeservice.SuppressInitialDiagnosticInformation = true;
                    chromeservice.HideCommandPromptWindow = true;
                    chromeservice.EnableVerboseLogging = false;
                    chromeop.AddArgument("--log-level=3");
                    chromeop.BinaryLocation = provider.ChromeBinaryLocation;

                    if (Utils.IsDocker())
                    {
                        if (RootChecker.IsRoot())
                        {
                            chromeop.AddArgument("--no-sandbox");
                        }
                        
                        chromeop.AddArgument("--whitelisted-ips=''");
                        chromeop.AddArgument("--disable-dev-shm-usage");
                    }

                    // Disable password manager and leak detection to prevent popups
                    chromeop.AddUserProfilePreference("credentials_enable_service", false);
                    chromeop.AddUserProfilePreference("profile.password_manager_enabled", false);
                    chromeop.AddUserProfilePreference("profile.password_manager_leak_detection", false);

                    // Add stealth mode arguments for anti-bot detection
                    if (data.ConfigSettings.BrowserSettings.StealthMode)
                    {
                        // Remove automation flags
                        chromeop.AddExcludedArgument("enable-automation");
                        
                        // Anti-detection arguments
                        chromeop.AddArgument("--disable-blink-features=AutomationControlled");
                        chromeop.AddArgument("--disable-dev-shm-usage");
                        chromeop.AddArgument("--no-first-run");
                        chromeop.AddArgument("--no-default-browser-check");
                        chromeop.AddArgument("--disable-infobars");
                        chromeop.AddArgument("--disable-features=IsolateOrigins,site-per-process");
                        chromeop.AddArgument("--window-size=1920,1080");
                    }

                    if (data.ConfigSettings.BrowserSettings.Headless)
                    {
                        chromeop.AddArgument("--headless=new");
                    }

                    if (data.ConfigSettings.BrowserSettings.DismissDialogs)
                    {
                        chromeop.AddArgument("--disable-notifications");
                    }

                    args = data.ConfigSettings.BrowserSettings.CommandLineArgs;

                    if (!string.IsNullOrWhiteSpace(args))
                    {
                        // Extra command line args (to have dynamic args via variables)
                        if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
                        {
                            args += ' ' + extraCmdLineArgs;
                        }

                        chromeop.AddArgument(args);
                    }

                    if (data.UseProxy)
                    {
                        // TODO: Add support for auth proxies using yove
                        chromeop.AddArgument($"--proxy-server={data.Proxy.Type.ToString().ToLower()}://{data.Proxy.Host}:{data.Proxy.Port}");
                    }

                    var chromeDriver = new ChromeDriver(chromeservice, chromeop);
                    
                    // Apply CDP commands to hide webdriver property when stealth mode is enabled
                    if (data.ConfigSettings.BrowserSettings.StealthMode)
                    {
                        ApplyChromeAntiDetection(chromeDriver);
                    }
                    
                    data.SetObject("selenium", chromeDriver);
                    break;

                case SeleniumBrowserType.Firefox:
                    var fireop = new FirefoxOptions();
                    var fireservice = FirefoxDriverService.CreateDefaultService();
                    var fireprofile = new FirefoxProfile();

                    fireservice.SuppressInitialDiagnosticInformation = true;
                    fireservice.HideCommandPromptWindow = true;
                    fireop.AddArgument("--log-level=3");
                    fireop.BrowserExecutableLocation = provider.FirefoxBinaryLocation;

                    if (Utils.IsDocker())
                    {
                        fireop.AddArgument("--whitelisted-ips=''");
                    }

                    // Add stealth mode preferences for anti-bot detection
                    if (data.ConfigSettings.BrowserSettings.StealthMode)
                    {
                        // Disable webdriver detection
                        fireprofile.SetPreference("dom.webdriver.enabled", false);
                        fireprofile.SetPreference("useAutomationExtension", false);
                        
                        // Additional anti-detection preferences
                        fireprofile.SetPreference("media.navigator.permission.disabled", true);
                        fireprofile.SetPreference("privacy.trackingprotection.enabled", false);
                        fireprofile.SetPreference("privacy.trackingprotection.pbmode.enabled", false);
                        
                        // Set window size via argument
                        fireop.AddArgument("--width=1920");
                        fireop.AddArgument("--height=1080");
                    }

                    if (data.ConfigSettings.BrowserSettings.Headless)
                    {
                        fireop.AddArgument("--headless");
                    }

                    if (data.ConfigSettings.BrowserSettings.DismissDialogs)
                    {
                        fireprofile.SetPreference("dom.webnotifications.enabled", false);
                    }

                    args = data.ConfigSettings.BrowserSettings.CommandLineArgs;

                    if (!string.IsNullOrWhiteSpace(args))
                    {
                        // Extra command line args (to have dynamic args via variables)
                        if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
                        {
                            args += ' ' + extraCmdLineArgs;
                        }

                        fireop.AddArgument(args);
                    }

                    if (data.UseProxy)
                    {
                        fireprofile.SetPreference("network.proxy.type", 1);
                        if (data.Proxy.Type == ProxyType.Http)
                        {
                            fireprofile.SetPreference("network.proxy.http", data.Proxy.Host);
                            fireprofile.SetPreference("network.proxy.http_port", data.Proxy.Port);
                            fireprofile.SetPreference("network.proxy.ssl", data.Proxy.Host);
                            fireprofile.SetPreference("network.proxy.ssl_port", data.Proxy.Port);
                        }
                        else
                        {
                            fireprofile.SetPreference("network.proxy.socks", data.Proxy.Host);
                            fireprofile.SetPreference("network.proxy.socks_port", data.Proxy.Port);

                            if (data.Proxy.Type == ProxyType.Socks4)
                            {
                                fireprofile.SetPreference("network.proxy.socks_version", 4);
                            }
                            else if (data.Proxy.Type == ProxyType.Socks5)
                            {
                                fireprofile.SetPreference("network.proxy.socks_version", 5);
                            }
                        }
                    }

                    fireop.Profile = fireprofile;
                    data.SetObject("selenium", new FirefoxDriver(fireservice, fireop, new TimeSpan(0, 1, 0)));
                    break;
            }

            data.Logger.Log($"{(data.ConfigSettings.BrowserSettings.Headless ? "Headless " : "")}Browser opened successfully!", LogColors.JuneBud);
            UpdateSeleniumData(data);
        }

        [Block("Closes an open selenium browser", name = "Close Browser")]
        public static void SeleniumCloseBrowser(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            browser.Close();
            browser.Quit();
            data.SetObject("selenium", null);
            data.Logger.Log("Browser closed successfully!", LogColors.JuneBud);
        }

        [Block("Opens a new page in a new browser tab", name = "New Tab")]
        public static void SeleniumNewTab(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            ((IJavaScriptExecutor)browser).ExecuteScript("window.open();");
            browser.SwitchTo().Window(browser.WindowHandles.Last());
            UpdateSeleniumData(data);

            data.Logger.Log("Opened a new tab", LogColors.JuneBud);
        }

        [Block("Closes the currently active browser tab", name = "Close Tab")]
        public static void SeleniumCloseTab(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            ((IJavaScriptExecutor)browser).ExecuteScript("window.close();");
            UpdateSeleniumData(data);

            data.Logger.Log("Closed the active tab", LogColors.JuneBud);
        }

        [Block("Switches to the browser tab with a specified index", name = "Switch to Tab")]
        public static void SeleniumSwitchToTab(BotData data, int index)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            browser.SwitchTo().Window(browser.WindowHandles[index]);
            UpdateSeleniumData(data);

            data.Logger.Log($"Switched to tab with index {index}", LogColors.JuneBud);
        }

        [Block("Reloads the current page", name = "Reload")]
        public static void SeleniumReload(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Navigate().Refresh();
            UpdateSeleniumData(data);
            data.Logger.Log("Reloaded the page", LogColors.JuneBud);
        }

        [Block("Goes back to the previously visited page", name = "Go Back")]
        public static void SeleniumGoBack(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Navigate().Back();
            UpdateSeleniumData(data);
            data.Logger.Log("Went back to the previously visited page", LogColors.JuneBud);
        }

        [Block("Goes forward to the next visited page", name = "Go Forward")]
        public static void SeleniumGoForward(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Navigate().Forward();
            UpdateSeleniumData(data);
            data.Logger.Log("Went forward to the next visited page", LogColors.JuneBud);
        }

        [Block("Minimizes the browser window", name = "Minimize")]
        public static void SeleniumMinimize(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Manage().Window.Minimize();
            data.Logger.Log("Minimized the browser window", LogColors.JuneBud);
        }

        [Block("Maximizes the browser window", name = "Maximize")]
        public static void SeleniumMaximize(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Manage().Window.Maximize();
            data.Logger.Log("Maximized the browser window", LogColors.JuneBud);
        }

        [Block("Makes the browser window full screen", name = "Full Screen")]
        public static void SeleniumFullScreen(BotData data)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Manage().Window.FullScreen();
            data.Logger.Log("Made the browser window full screen", LogColors.JuneBud);
        }

        [Block("Sets the height and width of the browser window", name = "Set Browser Size")]
        public static void SeleniumSetWindowSize(BotData data, int width, int height)
        {
            data.Logger.LogHeader();

            GetBrowser(data).Manage().Window.Size = new Size(width, height);
            data.Logger.Log($"Set the browser's size to {width} x {height}", LogColors.JuneBud);
        }

        private static WebDriver GetBrowser(BotData data)
                => data.TryGetObject<WebDriver>("selenium") ?? throw new Exception("The browser is not open!");

        private static void UpdateSeleniumData(BotData data)
        {
            var browser = data.TryGetObject<WebDriver>("selenium");

            if (browser != null)
            {
                data.ADDRESS = browser.Url;
                data.SOURCE = browser.PageSource;
            }
        }

        /// <summary>
        /// Applies Chrome DevTools Protocol commands to hide automation indicators.
        /// </summary>
        private static void ApplyChromeAntiDetection(ChromeDriver driver)
        {
            try
            {
                // JavaScript to remove webdriver property and other automation indicators
                var antiDetectionScript = @"
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                    
                    // Patch chrome object
                    window.chrome = {
                        runtime: {}
                    };
                    
                    // Override permissions query
                    const originalQuery = window.navigator.permissions.query;
                    window.navigator.permissions.query = (parameters) => (
                        parameters.name === 'notifications' ?
                            Promise.resolve({ state: Notification.permission }) :
                            originalQuery(parameters)
                    );
                    
                    // Remove $cdc_ variable that ChromeDriver adds
                    Object.defineProperty(document, '$cdc_asdjflasutopfhvcZLmcfl_', {
                        get: () => undefined
                    });
                    
                    // Override plugins to appear more realistic
                    Object.defineProperty(navigator, 'plugins', {
                        get: () => [1, 2, 3, 4, 5]
                    });
                    
                    // Override languages
                    Object.defineProperty(navigator, 'languages', {
                        get: () => ['en-US', 'en']
                    });
                ";

                // Use CDP to inject script on every new document
                var parameters = new Dictionary<string, object>
                {
                    { "source", antiDetectionScript }
                };

                driver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", parameters);
            }
            catch
            {
                // CDP might not be available in all environments, fail silently
            }
        }
    }
}
