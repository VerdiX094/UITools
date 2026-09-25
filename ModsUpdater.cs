using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using ModLoader;
using SFS.Input;
using SFS.IO;
using SFS.Parsers.Json;
using SFS.UI;
using UnityEngine;

namespace UITools
{
    internal static class ModsUpdater
    {
        // Shared HttpClient instance
        static readonly HttpClient Http = new();

        // Tracks how many files were successfully updated
        static int loadedFiles;

        // Remote hashes are looked up online once per lifetime and reused from cache in between
        static readonly TimeSpan HashCacheLifetime = TimeSpan.FromHours(24);
        static readonly TimeSpan MeteredHashCacheLifetime = TimeSpan.FromDays(3);

        // Stored for a lookup that failed on a metered connection
        const string FailedLookup = "";

        // Remote hash by URL
        static Dictionary<string, string> hashes = new();
        static readonly IFolder modFolder = Main.main.GetModFolder();
        static readonly IFile hashCacheFile = modFolder.GetFile("hashes.txt");
        static readonly IFile hashUpdateFile = modFolder.GetFile("hashUpdate.txt");
        
        // Entry point for running the update process detached from scene context
        public static void StartUpdate()
        {
            UpdateAll().Forget();
        }

        // Main update flow
        static async UniTask UpdateAll()
        {
            try
            {
                // Check which files need updating (based on hash mismatch or failures)
                var updatesByMod = await GetFilesNeedingUpdate();
                if (updatesByMod.Count == 0) return;

                // Ask user to confirm update
                if (!await ConfirmUpdatePrompt(updatesByMod))
                    return;

                // Tracks failed mods and which files failed in each
                var failedMods = new Dictionary<Mod, List<string>>();

                // Attempt update per mod
                foreach (var entry in updatesByMod)
                {
                    Mod mod = entry.Key;
                    var files = entry.Value;

                    if (await TryUpdateMod(mod, files, failedMods)) continue;

                    // Ensure mod is listed in failedMods if any file fails
                    if (!failedMods.ContainsKey(mod))
                        failedMods[mod] = new List<string>();
                }

                // Retry failed mods if user agrees
                if (failedMods.Count > 0 && await ConfirmRetryPrompt(failedMods.Keys))
                {
                    foreach (Mod mod in failedMods.Keys.ToList())
                    {
                        var files = updatesByMod[mod];
                        if (await TryUpdateMod(mod, files, failedMods))
                            failedMods.Remove(mod);
                    }

                    // Show final list of failed files (if any)
                    if (failedMods.Count > 0)
                        await ShowFailedModsPrompt(failedMods);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                // If any files were updated, offer to restart
                if (loadedFiles > 0)
                    MenuGenerator.OpenConfirmation(CloseMode.Current,
                        () => "Updates successful. Restart so changes take effect?",
                        () => "Restart",
                        ApplicationUtility.Relaunch);
            }
        }

        // Returns a dictionary of mods -> files that need updating
        static async UniTask<Dictionary<Mod, List<(string url, IFile file)>>> GetFilesNeedingUpdate()
        {
            var metered = await ConnectionCost.IsMetered();

            // A fresh cache leaves only files it does not cover to look up online
            var refreshAll = !TryLoadHashCache(metered ? MeteredHashCacheLifetime : HashCacheLifetime);
            var fetchedAny = false;

            var result = new Dictionary<Mod, List<(string, IFile)>>();

            foreach ((Mod mod, Dictionary<string, IFile> files) in GetUpdatableMods())
            {
                foreach (var kvp in files)
                {
                    var url = kvp.Key;
                    IFile file = kvp.Value;

                    // A lookup that failed earlier is retried, unless the connection is metered
                    if (refreshAll || !hashes.TryGetValue(url, out var remote) ||
                        (string.IsNullOrEmpty(remote) && !metered))
                    {
                        remote = await FetchRemoteHash(url);
                        fetchedAny = true;

                        if (remote != null)
                            hashes[url] = remote;
                        else if (metered)
                            hashes[url] = FailedLookup; // Remembered, so the file is left alone on the next start
                        else
                            hashes.Remove(url);
                    }

                    // Nothing to compare against; the failure was logged when it happened
                    if (string.IsNullOrEmpty(remote)) continue;

                    try
                    {
                        var local = file.Exists() ? GetLocalSHA256(file) : "";

                        // If hash mismatch, mark for update
                        if (!string.Equals(local, remote, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!result.ContainsKey(mod))
                                result[mod] = new List<(string, IFile)>();
                            result[mod].Add((url, file));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[ModUpdater] Could not hash {file.Path} for {url}: {ex.Message}");
                    }
                }
            }

            if (fetchedAny)
                SaveHashCache(refreshAll);

            return result;
        }

        // The storage API interface wins when a mod implements both
        static IEnumerable<(Mod mod, Dictionary<string, IFile> files)> GetUpdatableMods()
        {
            foreach (Mod mod in Loader.main.GetAllMods())
            {
                if (mod is IUpdatableMod updatable)
                {
                    yield return (mod, updatable.UpdatableFiles);
                    continue;
                }

#pragma warning disable 618
                if (mod is IUpdatable legacy)
                    yield return (mod, legacy.UpdatableFiles.ToDictionary(
                        entry => entry.Key, entry => entry.Value.ToStorageFile()));
#pragma warning restore 618
            }
        }

        // Returns null after logging when the lookup fails
        static async UniTask<string> FetchRemoteHash(string url)
        {
            try
            {
                return await HashUtility.GetSHA256(url);
            }
            catch (HttpRequestException ex)
            {
                var message = $"[ModUpdater] Network error while checking hash for {url}: {ex.Message}";
                if (ex.InnerException != null)
                    message += "\nInner: " + ex.InnerException.GetType().Name + " - " + ex.InnerException.Message;
                Debug.Log(message);
            }
            catch (Exception ex)
            {
                Debug.Log($"[ModUpdater] Unexpected error while checking hash for {url}: {ex.Message}");
            }

            return null;
        }
        
        static bool TryLoadHashCache(TimeSpan lifetime)
        {
            hashes = new Dictionary<string, string>();
            try
            {
                if (!hashCacheFile.Exists() || !hashUpdateFile.Exists()) return false;
                if (!long.TryParse(hashUpdateFile.ReadText(), out var ticks)) return false;

                // A negative age means the clock was set back
                TimeSpan age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                if (age < TimeSpan.Zero || age >= lifetime) return false;

                var cached = JsonWrapper.FromJson<Dictionary<string, string>>(hashCacheFile.ReadText());
                if (cached == null) return false;

                hashes = cached;
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[ModUpdater] Could not read hash cache: {ex.Message}");
                return false;
            }
        }

        // Only a full refresh records the timestamp
        static void SaveHashCache(bool fullRefresh)
        {
            try
            {
                hashCacheFile.WriteText(JsonWrapper.ToJson(hashes, false));
                if (fullRefresh)
                    hashUpdateFile.WriteText(DateTime.UtcNow.Ticks.ToString());
            }
            catch (Exception ex)
            {
                Debug.Log($"[ModUpdater] Could not write hash cache: {ex.Message}");
            }
        }

        // Same lowercase hex format as HashUtility.GetSHA256
        static string GetLocalSHA256(IFile file)
        {
            using var sha256 = SHA256.Create();
            return HashUtility.GetHexDigest(sha256.ComputeHash(file.ReadBytes()));
        }

        // Attempts to update all files for a single mod atomically
        static async UniTask<bool> TryUpdateMod(Mod mod, List<(string url, IFile file)> files,
            Dictionary<Mod, List<string>> failedMods)
        {
            // Staged in the OS temp directory, which is not mod storage
            var tempDir = Path.Combine(Path.GetTempPath(), "ModUpdates", Guid.NewGuid().ToString());
            IFolder tempFolder = new DefaultFolder(tempDir).Create();

            var semaphore = new SemaphoreSlim(3);
            var downloads = new List<(string url, IFile original, IFile staged)>();
            var failedFiles = new List<string>();

            await UniTask.WhenAll(files.Select(async file =>
            {
                var url = file.url;
                IFile originalFile = file.file;
                var fileName = originalFile.Name;
                IFile stagedFile = tempFolder.GetFile(fileName);

                await semaphore.WaitAsync();
                try
                {
                    var success = await Download(url, stagedFile);
                    if (success)
                    {
                        lock (downloads)
                        {
                            downloads.Add((url, originalFile, stagedFile));
                        }
                    }
                    else
                    {
                        lock (failedFiles)
                        {
                            failedFiles.Add(fileName);
                        }

                        Debug.Log($"[ModUpdater] Failed to download '{fileName}' for mod '{mod.DisplayName}'");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            // If all downloads succeed, commit updates
            if (failedFiles.Count == 0)
            {
                foreach (var download in downloads)
                {
                    download.staged.Copy(download.original);
                    // Otherwise a remote file that changed since the fetch is offered again every start
                    hashes[download.url] = GetLocalSHA256(download.original);
                }
                SaveHashCache(false);

                loadedFiles += downloads.Count;

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                return true;
            }

            // Record failed files
            failedMods[mod] = failedFiles;

            // Clean up downloaded files if not committed
            foreach (var download in downloads.Where(download => download.staged.Exists()))
                download.staged.Delete();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

            return false;
        }

        // Download a file and write to disk
        static async UniTask<bool> Download(string url, IFile file)
        {
            try
            {
                HttpResponseMessage resp = await Http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return false;

                var data = await resp.Content.ReadAsByteArrayAsync();
                if (data == null || data.Length == 0) return false;

                file.WriteBytes(data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[ModUpdater] Exception during download: {ex.Message}");
                return false;
            }
        }

        // Confirmation prompt before updates begin
        static async UniTask<bool> ConfirmUpdatePrompt(
            Dictionary<Mod, List<(string url, IFile file)>> updates)
        {
            var list = string.Join("\n", updates
                .OrderBy(kvp => kvp.Key.DisplayName)
                .Select(kvp => kvp.Key.DisplayName));

            return await MenuGenerator.OpenConfirmationAsync(CloseMode.Current,
                () => $"Updates are available for the following mods:\n\n{list}\n\nInstall now?",
                () => "Update All");
        }

        // Retry prompt for mods that failed to update
        static async UniTask<bool> ConfirmRetryPrompt(IEnumerable<Mod> failed)
        {
            var list = string.Join("\n", failed.Select(m => m.DisplayName));
            return await MenuGenerator.OpenConfirmationAsync(CloseMode.Current,
                () => $"The following mods failed to update:\n\n{list}\n\nRetry failed mods?",
                () => "Retry");
        }

        // Show which files failed after retry
        static async UniTask ShowFailedModsPrompt(Dictionary<Mod, List<string>> failedMods)
        {
            var msg = string.Join("\n\n", failedMods.OrderBy(m => m.Key.DisplayName)
                .Select(kvp =>
                {
                    var files = string.Join("\n  - ", kvp.Value.OrderBy(f => f));
                    return $"{kvp.Key.DisplayName}\n  - {files}";
                }));

            await MenuGenerator.OpenConfirmationAsync(CloseMode.Current,
                () => $"The following mods still failed to update:\n\n{msg}",
                () => "OK");
        }
    }

    /// <summary>
    ///     Implement this interface on main mod class if you want it to be updated at game start
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    [Obsolete("Implement IUpdatableMod instead, which uses the IFile storage API.")]
    public interface IUpdatable
    {
        /// <summary>
        ///     Returns dictionary of files that should be updated
        ///     string is web link, FilePath is path where file will be downloaded
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, FilePath> UpdatableFiles { get; }
    }

    /// <summary>
    ///     Implement this interface on main mod class if you want it to be updated at game start
    /// </summary>
    public interface IUpdatableMod
    {
        /// <summary>
        ///     Returns dictionary of files that should be updated
        ///     string is web link, IFile is the file the download is written to
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, IFile> UpdatableFiles { get; }
    }
}