using System;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
namespace DroplerGUI.Services.Steam
{
	/// <summary>
	/// Class to help align system time with the Steam server time. Not super advanced; probably not taking some things into account that it should.
	/// Necessary to generate up-to-date codes. In general, this will have an error of less than a second, assuming Steam is operational.
	/// </summary>
	public static class TimeAligner
	{
		private static bool _aligned = false;
		private static long _timeDifference = 0;
		private static readonly HttpClient _httpClient = new HttpClient();
		private const string STEAM_TIME_API = "https://api.steampowered.com/IStoreService/GetStoreSalesPage/v1";

		public static long GetAlignedTime()
		{
			if (!_aligned)
			{
				AlignTime();
			}

			return ((long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds) + _timeDifference;
		}

		public static async Task<long> GetSteamTimeAsync()
		{
			if (!_aligned)
			{
				await AlignTimeAsync();
			}
			return ((long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds) + _timeDifference;
		}

		public static void AlignTime()
		{
			try
			{
				var task = AlignTimeAsync();
				task.Wait();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при синхронизации времени: {ex.Message}");
				_aligned = true;
				_timeDifference = 0;
			}
		}

		public static async Task AlignTimeAsync()
		{
			try
			{
				var localTime = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
				
				// Получаем время от Steam API
				var response = await _httpClient.GetStringAsync(STEAM_TIME_API);
				var timeData = JsonConvert.DeserializeObject<TimeQuery>(response);
				
				if (timeData?.Response != null)
				{
					var serverTime = timeData.Response.ServerTime;
					_timeDifference = serverTime - localTime;
					_aligned = true;
					
					Console.WriteLine($"Время успешно синхронизировано. Разница: {_timeDifference} секунд");
				}
				else
				{
					throw new Exception("Не удалось получить время от серверов Steam");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при синхронизации времени: {ex.Message}");
				_aligned = true;
				_timeDifference = 0;
			}
		}

		internal class TimeQuery
		{
			[JsonProperty("response")]
			internal TimeQueryResponse Response { get; set; }

			internal class TimeQueryResponse
			{
				[JsonProperty("server_time")]
				public long ServerTime { get; set; }
			}

		}
	}
}
