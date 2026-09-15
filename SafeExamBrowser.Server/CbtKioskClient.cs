/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 *
 * This client retrieves the quit/unlock password which is configured by the exam administrator on the CBT
 * panel. Expected response format of the configured endpoint:
 *
 *   { "success": true, "data": { "exit_password": "…", "password_expires_at": "2026-06-28T23:55", "is_expired": false } }
 */

using System;
using System.Globalization;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.Server.Contracts;
using SafeExamBrowser.Server.Contracts.Data;

namespace SafeExamBrowser.Server
{
	public class CbtKioskClient : ICbtKioskClient
	{
		private const int DEFAULT_TIMEOUT = 5000;
		private const string DATA = "data";
		private const string EXIT_PASSWORD = "exit_password";
		private const string IS_EXPIRED = "is_expired";
		private const string PASSWORD_EXPIRES_AT = "password_expires_at";
		private const string SUCCESS = "success";

		private readonly ILogger logger;

		public CbtKioskClient(ILogger logger)
		{
			this.logger = logger;
		}

		public KioskSettings GetSettings(string url, int timeout = DEFAULT_TIMEOUT, int attempts = 1, int attemptInterval = 0)
		{
			var settings = new KioskSettings();

			if (string.IsNullOrWhiteSpace(url))
			{
				settings.Message = "No CBT kiosk settings URL configured.";
				return settings;
			}

			if (attempts < 1)
			{
				attempts = 1;
			}

			for (var attempt = 1; attempt <= attempts; attempt++)
			{
				settings = TryGetSettings(url, timeout);

				if (settings.Success)
				{
					return settings;
				}

				if (attempt < attempts)
				{
					logger.Warn($"Attempt {attempt}/{attempts} to retrieve CBT kiosk settings from '{url}' failed, retrying in {attemptInterval}ms...");

					if (attemptInterval > 0)
					{
						System.Threading.Thread.Sleep(attemptInterval);
					}
				}
			}

			return settings;
		}

		private KioskSettings TryGetSettings(string url, int timeout)
		{
			var settings = new KioskSettings();

			try
			{
				using (var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeout) })
				{
					var response = client.GetAsync(url).GetAwaiter().GetResult();
					var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

					if (!response.IsSuccessStatusCode)
					{
						settings.Message = $"The endpoint returned {(int) response.StatusCode} {response.StatusCode}.";
						logger.Warn($"Failed to retrieve CBT kiosk settings from '{url}': {settings.Message}");
						return settings;
					}

					return Parse(content, settings);
				}
			}
			catch (Exception e)
			{
				settings.Message = e.Message;
				logger.Warn($"Failed to retrieve CBT kiosk settings from '{url}': {settings.Message}");
			}

			return settings;
		}

		private KioskSettings Parse(string content, KioskSettings settings)
		{
			try
			{
				var json = JObject.Parse(content);
				var success = json[SUCCESS]?.Value<bool>() ?? false;
				var data = json[DATA] as JObject;

				if (!success || data == default(JObject))
				{
					settings.Message = "The endpoint reported an unsuccessful response or is missing the data object.";
					logger.Warn($"Failed to parse CBT kiosk settings: {settings.Message}");
					return settings;
				}

				settings.ExitPassword = data[EXIT_PASSWORD]?.Value<string>();
				settings.IsExpired = data[IS_EXPIRED]?.Value<bool>() ?? false;
				settings.PasswordExpiresAt = TryParseExpiry(data[PASSWORD_EXPIRES_AT]?.Value<string>());
				settings.Success = true;

				logger.Debug($"Retrieved CBT kiosk settings: password {(string.IsNullOrEmpty(settings.ExitPassword) ? "not" : string.Empty)} present, " +
					$"expired: {settings.IsExpired}, expires at: {(settings.PasswordExpiresAt.HasValue ? settings.PasswordExpiresAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "n/a")}.");
			}
			catch (Exception e)
			{
				settings.Success = false;
				settings.Message = e.Message;
				logger.Warn($"Failed to parse CBT kiosk settings: {e.Message}");
			}

			return settings;
		}

		private static DateTime? TryParseExpiry(string value)
		{
			if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt))
			{
				return expiresAt;
			}

			return default(DateTime?);
		}
	}
}
