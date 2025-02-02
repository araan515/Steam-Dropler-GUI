using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DroplerGUI.Services.Steam
{
    public static class SteamWeb
    {
        private static readonly HttpClient _httpClient;

        static SteamWeb()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; U; Android 4.1.1; en-us; Google Nexus 4 - 4.1.1 - API 16 - 768x1280 Build/JRO03S) AppleWebKit/534.30 (KHTML, like Gecko) Version/4.0 Mobile Safari/534.30");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/javascript, text/html, application/xml, text/xml, */*");
        }

        /// <summary>
        /// Perform a mobile login request
        /// </summary>
        /// <param name="url">API url</param>
        /// <param name="method">GET or POST</param>
        /// <param name="data">Name-data pairs</param>
        /// <param name="cookies">current cookie container</param>
        /// <returns>response body</returns>
        public static string MobileLoginRequest(string url, string method, NameValueCollection data = null, CookieContainer cookies = null, NameValueCollection headers = null)
        {
            return Request(url, method, data, cookies, headers, APIEndpoints.COMMUNITY_BASE + "/mobilelogin?oauth_client_id=DE45CD61&oauth_scope=read_profile%20write_profile%20read_client%20write_client");
        }

        public static string Request(string url, string method, NameValueCollection data = null, CookieContainer cookies = null, NameValueCollection headers = null, string referer = APIEndpoints.COMMUNITY_BASE)
        {
            var task = RequestAsync(url, method, data, cookies, headers, referer);
            return task.GetAwaiter().GetResult();
        }

        public static async Task<string> RequestAsync(string url, string method, NameValueCollection data = null, CookieContainer cookies = null, NameValueCollection headers = null, string referer = APIEndpoints.COMMUNITY_BASE)
        {
            try
            {
                using var request = new HttpRequestMessage();
                
                // Настраиваем базовые параметры запроса
                request.Method = new HttpMethod(method);
                request.RequestUri = new Uri(url);
                
                // Добавляем referer
                if (!string.IsNullOrEmpty(referer))
                {
                    request.Headers.Referrer = new Uri(referer);
                }

                // Добавляем дополнительные заголовки
                if (headers != null)
                {
                    foreach (string key in headers.Keys)
                    {
                        request.Headers.Add(key, headers[key]);
                    }
                }

                // Подготавливаем данные для отправки
                if (data != null)
                {
                    var query = string.Join("&", Array.ConvertAll(data.AllKeys, key => 
                        $"{WebUtility.UrlEncode(key)}={WebUtility.UrlEncode(data[key])}"));

                    if (method == "GET")
                    {
                        url += (url.Contains("?") ? "&" : "?") + query;
                        request.RequestUri = new Uri(url);
                    }
                    else if (method == "POST")
                    {
                        request.Content = new StringContent(query, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }
                }

                // Выполняем запрос
                using var response = await _httpClient.SendAsync(request);

                // Проверяем статус ответа
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    HandleFailedWebRequestResponse(response, url);
                    return null;
                }

                // Читаем ответ
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Ошибка при выполнении веб-запроса: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Raise exceptions relevant to this HttpWebResponse -- EG, to signal that our oauth token has expired.
        /// </summary>
        private static void HandleFailedWebRequestResponse(HttpResponseMessage response, string requestURL)
        {
            if (response == null) return;

            //Redirecting -- likely to a steammobile:// URI
            if (response.StatusCode == HttpStatusCode.Found)
            {
                var location = response.Headers.Location?.ToString();
                if (!string.IsNullOrEmpty(location))
                {
                    //Our OAuth token has expired. This is given both when we must refresh our session, or the entire OAuth Token cannot be refreshed anymore.
                    //Thus, we should only throw this exception when we're attempting to refresh our session.
                    if (location == "steammobile://lostauth" && requestURL == APIEndpoints.MOBILEAUTH_GETWGTOKEN)
                    {
                        throw new MobileAuth.WGTokenExpiredException();
                    }
                }
            }
        }
    }
} 