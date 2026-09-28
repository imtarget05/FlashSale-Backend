using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FlashSale.Tests.Common
{
    public static class LiveInfraGuard
    {
        public static bool IsLiveTestsEnabled()
        {
            var val = Environment.GetEnvironmentVariable("LIVE_TESTS");
            return !string.IsNullOrEmpty(val) && (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<bool> IsHttpReachableAsync(string url, int timeoutMs = 1500)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
                var response = await client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
