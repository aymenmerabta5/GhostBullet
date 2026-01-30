using System;
using System.Collections.Generic;

namespace RuriLib.Models.Configs.Settings
{
    public class BrowserSettings
    {
        public bool CloseBrowserOnFinish { get; set; } = false;
        public string[] QuitBrowserStatuses { get; set; } = Array.Empty<string>();
        public bool Headless { get; set; } = true;
        public string CommandLineArgs { get; set; } = "--disable-notifications --disable-features=PasswordLeakDetection,PasswordCheck --disable-save-password-bubble --disable-password-manager-reauthentication";
        public bool IgnoreHttpsErrors { get; set; } = false;
        public bool LoadOnlyDocumentAndScript { get; set; } = false;
        public bool DismissDialogs { get; set; } = false;
        public List<string> BlockedUrls { get; set; } = new();
        public bool StealthMode { get; set; } = true;
    }
}
