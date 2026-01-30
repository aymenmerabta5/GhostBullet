using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Views.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Forms;

namespace OpenBullet2.Native.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ImportCookiesDialog.xaml
    /// </summary>
    public partial class ImportCookiesDialog : Page
    {
        private readonly Wordlists caller;
        private readonly IWordlistRepository wordlistRepo;

        public ImportCookiesDialog(Wordlists caller)
        {
            this.caller = caller;
            wordlistRepo = SP.GetService<IWordlistRepository>();
            InitializeComponent();
        }

        private void SelectFolder(object sender, MouseButtonEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select folder containing cookie files",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                folderTextbox.Text = dialog.SelectedPath;
                
                // Auto-generate a name if empty
                if (string.IsNullOrWhiteSpace(nameTextbox.Text))
                {
                    var folderName = Path.GetFileName(dialog.SelectedPath);
                    nameTextbox.Text = $"Cookies_{folderName}";
                }

                statusText.Text = "Ready to scan. Click 'Scan and Import' to proceed.";
            }
        }

        private async void Accept(object sender, RoutedEventArgs e)
        {
            // Capture UI values on UI thread before any async operations
            var folderPath = folderTextbox.Text;
            var wordlistName = nameTextbox.Text;
            var purpose = purposeTextbox.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                Alert.Error("No folder selected", "Please select a folder to scan for cookie files.");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Alert.Error("Folder not found", "The selected folder does not exist.");
                return;
            }

            if (string.IsNullOrWhiteSpace(wordlistName))
            {
                Alert.Error("Invalid name", "Please enter a name for the wordlist.");
                return;
            }

            acceptButton.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            statusText.Text = "Scanning for cookie files...";

            try
            {
                // Run scan on background thread with captured folder path
                var cookiePaths = await Task.Run(() => ScanForCookieFiles(folderPath));

                if (cookiePaths.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Alert.Warning("No cookies found", "No cookie files (*.txt files in paths containing 'cookie') were found in the selected folder.");
                        statusText.Text = "No cookie files found.";
                    });
                    return;
                }

                await Dispatcher.InvokeAsync(() => statusText.Text = $"Found {cookiePaths.Count} cookie files. Saving wordlist...");

                // Create the wordlist content
                var content = string.Join(Environment.NewLine, cookiePaths);
                var contentBytes = Encoding.UTF8.GetBytes(content);

                using var stream = new MemoryStream(contentBytes);

                var entity = new WordlistEntity
                {
                    Name = wordlistName,
                    Type = "Cookies",
                    Purpose = purpose,
                    Total = cookiePaths.Count
                };

                await wordlistRepo.AddAsync(entity, stream);

                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = $"Successfully imported {cookiePaths.Count} cookie file paths!";
                    Alert.Success("Import complete", $"Successfully imported {cookiePaths.Count} cookie file paths as wordlist '{wordlistName}'.");
                });

                // Refresh the wordlists page
                await caller.RefreshAfterImport();

                await Dispatcher.InvokeAsync(() => ((MainDialog)Parent).Close());
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Alert.Exception(ex);
                    statusText.Text = $"Error: {ex.Message}";
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    acceptButton.IsEnabled = true;
                    progressBar.Visibility = Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// Scans a directory for cookie files.
        /// Includes .txt files inside subfolders whose path contains "cookie" (case-insensitive).
        /// </summary>
        private List<string> ScanForCookieFiles(string rootPath)
        {
            var cookiePaths = new List<string>();

            try
            {
                // Recursively find all .txt files
                var allTxtFiles = Directory.EnumerateFiles(rootPath, "*.txt", SearchOption.AllDirectories);

                foreach (var filePath in allTxtFiles)
                {
                    // Check if the path contains "cookie" (case-insensitive)
                    if (filePath.Contains("cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        cookiePaths.Add(filePath);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we can't access
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning directory: {ex.Message}");
            }

            return cookiePaths;
        }
    }
}
