using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Telegram.Bot;

namespace Shared.Logging
{
	public class LoggerInit
	{
		private FileInfo? _telegramTokenFile;
		private bool _logConsole = true;
		private string? _noConsoleLoggersPattern;

		private LoggerInit(FileInfo telegramTokenFile)
		{
			_telegramTokenFile = telegramTokenFile;
		}
		public LoggerInit() { }

		public static LoggerInit AsEmpty()
		{
			return new LoggerInit();
		}
		public static LoggerInit WithTelegramToken(FileInfo telegramTokenFile)
		{
			return new LoggerInit(telegramTokenFile);
		}
		public static LoggerInit WithTelegramToken(string telegramTokenFilePath)
		{
			return WithTelegramToken(new FileInfo(telegramTokenFilePath));
		}

		public LoggerInit SetConsoleLog(bool logConsole)
		{
			_logConsole = logConsole;
			return this;
		}

		/// <summary>
		/// Loggers whose name matches the pattern (NLog wildcards, e.g. "CCRParser-*") do not write to the console.
		/// They still write to the log file and to Telegram, if enabled.
		/// </summary>
		public LoggerInit SetNoConsoleLoggers(string loggerNamePattern)
		{
			_noConsoleLoggersPattern = loggerNamePattern;
			return this;
		}

		/// <param name="asyncFile">
		/// If true, file writes happen on a background thread: loggers only enqueue the event (the queue blocks the caller when full,
		/// so no event is lost). Useful with many threads logging to the same file. Call <see cref="LogManager.Shutdown"/> (or Flush)
		/// before exiting, otherwise events still in the queue are lost.
		/// </param>
		public NLogLoggerFactory InitLogger(FileInfo? outFile, bool deleteOldFile = false, bool logTelegram = true, bool asyncFile = false)
		{

			LoggingConfiguration lc = new();

			Target? fileTarget = null;
			if (outFile is not null)
			{

				if (deleteOldFile)
					outFile.Delete();
				else
				{
					using var fs = new StreamWriter(outFile.Open(FileMode.Append, FileAccess.Write));
					fs.WriteLine();
					fs.Close();
				}
				FileTarget ct1 = new();
				ct1.FileName = outFile.FullName;
				fileTarget = ct1;
				if (asyncFile)
				{
					fileTarget = new AsyncTargetWrapper(ct1)
					{
						QueueLimit = 100_000,
						OverflowAction = AsyncTargetWrapperOverflowAction.Block,
					};
				}
			}

			if (fileTarget is not null)
			{
				lc.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, fileTarget, "*");
			}

			if (logTelegram)
			{
				var telegramInfo = GetTelegramToken();
				var BotClient = new TelegramBotClient(telegramInfo.token);
				List<int> AdminTelegramIds = telegramInfo.adminIds;
				TelegramTarget telegramTarget = new()
				{
					BotClient = BotClient,
					ChatIds = AdminTelegramIds,
					Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}|${exception:format=ToString}",
				};

				lc.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, telegramTarget, "*");
			}

			// The console rule is the last one. Rules are evaluated in order, so a Final rule without targets for the
			// "no console" loggers placed right before it stops them from reaching the console, after they have been
			// written to the file and to Telegram by the rules above.
			if (_logConsole)
			{
				if (_noConsoleLoggersPattern is not null)
				{
					LoggingRule noConsole = new() { LoggerNamePattern = _noConsoleLoggersPattern, Final = true };
					noConsole.EnableLoggingForLevels(NLog.LogLevel.Info, NLog.LogLevel.Fatal);
					lc.LoggingRules.Add(noConsole);
				}

				ColoredConsoleTarget ct = new();
				lc.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, ct, "*");
			}

			LogManager.Configuration = lc;
			LogManager.ReconfigExistingLoggers();
			NLogLoggerProvider logLoggerProvider = new();
			NLogLoggerFactory logLoggerFactory = new(logLoggerProvider);

			return logLoggerFactory;
		}

		private (string token, List<int> adminIds) GetTelegramToken()
		{
			if (_telegramTokenFile is null)
				throw new Exception("Telegram token file is not set. Use WithTelegramToken method to set it.");
			using var sr = new StreamReader(_telegramTokenFile.OpenRead());
			string? token = sr.ReadLine();
			if (token is null)
				throw new Exception("Failed to read Telegram token from file.");
			List<int> adminIds;
			try
			{
				adminIds = sr.ReadToEnd().Split("\n").Select(int.Parse).ToList();
			}
			catch (Exception ex)
			{
				throw new Exception("Failed to read Telegram admin IDs from file.", ex);
			}
			if (adminIds.Count == 0)
				throw new Exception("Failed to read Telegram admin IDs from file.");

			return (token, adminIds);
		}
	}
}
