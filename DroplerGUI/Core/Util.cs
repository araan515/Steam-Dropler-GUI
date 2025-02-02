using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using Pastel;
using DroplerGUI.Models;
using DroplerGUI.Services.Steam;
using DroplerGUI.Core;
using DroplerGUI.Services;
using System.Linq;

namespace DroplerGUI.Core
{
	public static class Util
	{
		public static int unmax = 0;
		private static readonly object _dropLogLock = new object();

		public static long GetSystemUnixTime()
		{
			return (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
		}

		public static void LogDrop(string accountName, uint game, Models.DropResult result)
		{
			try
			{
				var taskNumber = int.Parse(accountName.Split('_').Last());
				var dropHistoryPath = Constants.GetTaskDropHistoryPath(taskNumber);
				var dropFile = Path.Combine(dropHistoryPath, $"{accountName}.txt");
				var dropInfo = $"{DateTime.Now}: {game} - Drop item: {result.ItemDefId} ({result.ItemId})";
				
				lock (_dropLogLock)
				{
					File.AppendAllText(dropFile, dropInfo + Environment.NewLine);
				}
			}
			catch (Exception)
			{
				// Игнорируем ошибки логирования
			}
		}

		public static string Unzip(byte[] bytes) {
		    using (var msi = new MemoryStream(bytes))
		    using (var mso = new MemoryStream()) {
		        using (var gs = new GZipStream(msi, CompressionMode.Decompress)) {
		            gs.CopyTo(mso);
		        }
		        return Encoding.UTF8.GetString(mso.ToArray());
		    }
		}
	}
}
