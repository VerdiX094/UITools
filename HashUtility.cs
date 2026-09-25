using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace UITools
{
    /// <summary>
    ///     Looks up SHA256 digests of remote files
    /// </summary>
    public static class HashUtility
    {
        const string GithubPrefix = "https://github.com/";

        // GitHub rejects API requests without a User-Agent
        const string UserAgent = "UITools";

        const string DigestPrefix = "sha256:";

        private static HttpClient client = new HttpClient();
        private static SHA256 hasher = SHA256.Create();

        /// <summary>
        ///     Returns the SHA256 digest of the file at <paramref name="url" />, as a lowercase hex string
        /// </summary>
        public static async UniTask<string> GetSHA256(string url)
        {
            if (url.StartsWith(GithubPrefix, StringComparison.OrdinalIgnoreCase))
                return await GetSHA256_Github(url);

            return await GetSHA256_Fallback(url);
        }

        private static async UniTask<string> GetSHA256_Github(string url)
        {
            return await GetReleaseDigest(url) ?? await GetSHA256_Fallback(url);
        }
        
        private static async UniTask<string> GetReleaseDigest(string url)
        {
            try
            {
                if (!TryGetReleaseApiUrl(url, out var apiUrl, out var assetName))
                    return null;

                using HttpRequestMessage request = new(HttpMethod.Get, apiUrl);
                request.Headers.Add("Accept", "application/vnd.github+json");
                request.Headers.Add("User-Agent", UserAgent);

                HttpResponseMessage msg = await client.SendAsync(request);
                if (!msg.IsSuccessStatusCode) return null;

                var json = await msg.Content.ReadAsStringAsync();
                GithubRelease release = JsonUtility.FromJson<GithubRelease>(json);
                if (release?.assets == null) return null;

                foreach (GithubAsset asset in release.assets)
                {
                    if (asset.name != assetName) continue;
                    if (asset.digest == null || !asset.digest.StartsWith(DigestPrefix, StringComparison.Ordinal))
                        return null;

                    return asset.digest.Substring(DigestPrefix.Length).ToLowerInvariant();
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.Log($"[HashUtility] Could not read release metadata for {url}: {e.Message}");
                return null;
            }
        }

        // Handles .../releases/download/{tag}/{asset} and .../releases/latest/download/{asset}
        private static bool TryGetReleaseApiUrl(string url, out string apiUrl, out string assetName)
        {
            apiUrl = null;
            assetName = null;

            var queryStart = url.IndexOfAny(new[] { '?', '#' });
            var path = queryStart < 0 ? url : url.Substring(0, queryStart);

            var parts = path.Substring(GithubPrefix.Length).Split('/');
            if (parts.Length < 6 || parts[2] != "releases") return false;

            string release;
            if (parts[3] == "download")
                release = "tags/" + string.Join("/", parts, 4, parts.Length - 5);
            else if (parts[3] == "latest" && parts[4] == "download" && parts.Length == 6)
                release = "latest";
            else
                return false;

            assetName = Uri.UnescapeDataString(parts[^1]);
            apiUrl = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/{release}";
            return true;
        }

        private static async UniTask<string> GetSHA256_Fallback(string url)
        {
            HttpResponseMessage msg = await client.GetAsync(url);
            if (!msg.IsSuccessStatusCode) throw new Exception($"Failed to get hash for {url}");

            byte[] fileContent = await msg.Content.ReadAsByteArrayAsync();
            byte[] hash = hasher.ComputeHash(fileContent);

            return GetHexDigest(hash);
        }

        public static string GetHexDigest(byte[] bytes)
        {
            StringBuilder sb = new();
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(GetHexDigit(bytes[i] >> 4));
                sb.Append(GetHexDigit(bytes[i] & 0b1111));
            }

            return sb.ToString();
            
            char GetHexDigit(int value)
            {
                if (value < 10)
                    return (char)('0' + value);
                return (char)('a' + value - 10);
            }
        }
        
#pragma warning disable 649
        [Serializable]
        private class GithubRelease
        {
            public GithubAsset[] assets;
        }

        [Serializable]
        private class GithubAsset
        {
            public string name;
            public string digest;
        }
#pragma warning restore 649
    }
}
