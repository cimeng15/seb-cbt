/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Modified for CBT integration (https://cbt.smkdata.sch.id).
 *
 * Offline fallback quit password, configurable directly on the exam client (no configuration file needed).
 *
 * An exam administrator may place a file named CbtFallback.txt in one of these locations:
 *
 *     %ProgramData%\SafeExamBrowser\CbtFallback.txt   (all users, needs administrator rights - preferred)
 *     %APPDATA%\SafeExamBrowser\CbtFallback.txt       (current user)
 *
 * The file may contain either:
 *
 *     password_hash=<Base16 SHA-256 hash of the password>
 *     password=<plain text password>
 *
 * The hash variant is preferred: the plain-text variant stores the emergency password in a readable file.
 * A line starting with '#' is treated as a comment. When both keys are present, 'password_hash' wins.
 */

using System;
using System.IO;
using SafeExamBrowser.Settings;

namespace SafeExamBrowser.Client.Responsibilities
{
	/// <summary>
	/// Reads the optional offline fallback quit password configured on the exam client.
	/// </summary>
	internal static class CbtOfflineFallback
	{
		private const string HASH_KEY = "password_hash=";
		private const string PASSWORD_KEY = "password=";

		/// <summary>
		/// Attempts to read the configured offline fallback password hash. Returns the Base16-encoded SHA-256 hash, or <c>default</c>
		/// when no fallback is configured. A plain-text password is hashed with <paramref name="hashAlgorithm"/> before being returned.
		/// </summary>
		internal static string TryReadPasswordHash(Func<string, string> hashAlgorithm)
		{
			foreach (var path in GetCandidatePaths())
			{
				try
				{
					if (!File.Exists(path))
					{
						continue;
					}

					var hash = default(string);
					var plainText = default(string);

					foreach (var rawLine in File.ReadAllLines(path))
					{
						var line = rawLine?.Trim();

						if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
						{
							continue;
						}

						if (line.StartsWith(HASH_KEY, StringComparison.OrdinalIgnoreCase))
						{
							hash = line.Substring(HASH_KEY.Length).Trim();
						}
						else if (line.StartsWith(PASSWORD_KEY, StringComparison.OrdinalIgnoreCase))
						{
							plainText = line.Substring(PASSWORD_KEY.Length).Trim();
						}
					}

					if (!string.IsNullOrEmpty(hash))
					{
						return hash;
					}

					if (!string.IsNullOrEmpty(plainText))
					{
						return hashAlgorithm(plainText);
					}
				}
				catch (Exception)
				{
					// An unreadable or malformed fallback file must never break termination of the exam; simply ignore it.
				}
			}

			return default;
		}

		/// <summary>
		/// Returns the locations in which an offline fallback file is searched for, in order of precedence.
		/// </summary>
		internal static string[] GetCandidatePaths()
		{
			var folder = nameof(SafeExamBrowser);

			return new[]
			{
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), folder, CbtDefaults.OfflineFallbackFileName),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folder, CbtDefaults.OfflineFallbackFileName)
			};
		}
	}
}
